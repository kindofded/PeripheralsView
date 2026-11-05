namespace PeripheralsView;

/// <summary>
/// Polls every device on one background thread that sleeps between polls (no thread pool, no timers).
/// The device list is only rebuilt when asked (HID arrival/removal, resume, "Refresh now").
/// </summary>
internal sealed class DeviceMonitor
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly AutoResetEvent _wake = new(false);
    private readonly Action _updated;
    private readonly Dictionary<string, int> _lastLevels = [];
    private List<IBatteryDevice> _devices = [];
    private IReadOnlyList<DeviceStatus> _snapshot = [];
    private volatile bool _rescanRequested = true;

    /// <param name="updated">Called on the monitor thread after each poll.</param>
    public DeviceMonitor(Action updated) => _updated = updated;

    public IReadOnlyList<DeviceStatus> Snapshot => Volatile.Read(ref _snapshot);

    public void Start() => new Thread(Run) { IsBackground = true, Name = "Device monitor" }.Start();

    public void Refresh(bool rescan = false)
    {
        if (rescan)
            _rescanRequested = true;
        _wake.Set();
    }

    private void Run()
    {
        while (true)
        {
            try
            {
                if (_rescanRequested)
                {
                    _rescanRequested = false;
                    _devices = Scan();
                }
                Volatile.Write(ref _snapshot, Merge(ReadAll(_devices), _lastLevels));
                _updated();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            _wake.WaitOne(PollInterval);
        }
    }

    /// <summary>Finds every supported device. Only Razer and Logitech collections are opened beyond a query.</summary>
    public static List<IBatteryDevice> Scan()
    {
        var devices = new List<IBatteryDevice>();
        var razerPaths = new Dictionary<RazerModel, List<string>>();

        foreach (string path in HidDevice.EnumeratePaths())
        {
            if (HidDevice.Query(path) is not { } info)
                continue;

            if (info.VendorId == RazerDevice.VendorId && info.FeatureLength == RazerDevice.FeatureReportLength
                && RazerDevice.FindModel(info.ProductId) is { } model)
            {
                if (!razerPaths.TryGetValue(model, out var paths))
                    razerPaths[model] = paths = [];
                paths.Add(path);
            }
            else if (LogitechDevice.IsHidppCollection(info))
            {
                string? product;
                using (var hid = HidDevice.Open(path, readWrite: false))
                    product = hid?.GetProductString();
                devices.AddRange(LogitechDevice.Create(info, product));
            }
        }

        foreach (var (model, paths) in razerPaths)
        {
            // Interface 0 is the one openrazer talks to; keep the others as fallbacks.
            paths.Sort((a, b) => IsInterface0(b).CompareTo(IsInterface0(a)));
            devices.Add(new RazerDevice(model, paths));
        }
        return devices;
    }

    /// <summary>Reads the scanned HID devices plus the Windows-reported Bluetooth batteries (looked up fresh each time).</summary>
    public static IEnumerable<DeviceStatus?> ReadAll(List<IBatteryDevice> devices) =>
        devices.Select(device => device.Read()).Concat(BluetoothBattery.Scan().Select(device => device.Read()));

    private static bool IsInterface0(string path) => path.Contains("&mi_00", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One entry per device (a mouse on cable and receiver at once shows once, best state wins), sorted by kind and name.
    /// Asleep devices keep showing their last known level.
    /// </summary>
    public static List<DeviceStatus> Merge(IEnumerable<DeviceStatus?> statuses, Dictionary<string, int>? lastLevels = null)
    {
        var best = new Dictionary<string, DeviceStatus>();
        foreach (var status in statuses)
        {
            if (status is null)
                continue;
            var current = status;
            if (lastLevels is not null)
            {
                if (current.State == DeviceState.Online && current.Level >= 0)
                    lastLevels[current.Id] = current.Level;
                else if (current.State == DeviceState.Asleep && current.Level < 0 && lastLevels.TryGetValue(current.Id, out int last))
                    current = current with { Level = last };
            }
            if (!best.TryGetValue(current.Id, out var existing) || current.State < existing.State)
                best[current.Id] = current;
        }
        var list = best.Values.ToList();
        list.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
