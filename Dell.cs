namespace PeripheralsView;

/// <summary>
/// Dell Universal Receiver (413C:4503, e.g. the KM5221W keyboard + mouse set). Its command collection (MI_02 Col02) is
/// opened exclusively by Dell's own software, but the receiver also sends notifications on the input-only collection
/// 0xFF00/0x0000 (32 bytes), which anyone can read: <c>01 08 slot percent</c> is a battery level, sent when a device
/// wakes up; <c>01 06 slot 0|1</c> is the radio link going idle/active (every few seconds, ignored).
/// Nothing is ever sent to the receiver, so one listener thread per receiver waits for those reports.
/// </summary>
internal sealed class DellReceiver
{
    public const ushort VendorId = 0x413C;
    private const ushort ProductId = 0x4503;
    private const int Slots = 6;

    private static readonly Dictionary<string, DellReceiver> Listening = [];

    /// <summary>Called on a listener thread when a battery level changes.</summary>
    public static Action? Changed;

    private readonly string _path;
    private readonly int[] _levels = new int[Slots + 1];
    private volatile bool _stopped;

    private DellReceiver(string path)
    {
        _path = path;
        Array.Fill(_levels, -1);
    }

    public static bool IsNotificationCollection(in HidInfo info) =>
        info.VendorId == VendorId && info.ProductId == ProductId && info.UsagePage == 0xFF00 && info.Usage == 0x0000 && info.InputLength >= 4;

    /// <summary>The devices behind the receiver; reuses the running listener so rescans don't lose the levels heard so far.</summary>
    public static IEnumerable<IBatteryDevice> Create(in HidInfo info)
    {
        DellReceiver? receiver;
        lock (Listening)
        {
            if (!Listening.TryGetValue(info.Path, out receiver) || receiver._stopped)
            {
                receiver = new DellReceiver(info.Path);
                if (!receiver.Start(info.InputLength))
                    return [];
                Listening[info.Path] = receiver;
            }
        }
        return Enumerable.Range(1, Slots).Select(slot => (IBatteryDevice)new DellDevice(receiver, slot));
    }

    private bool Start(int reportLength)
    {
        var hid = HidDevice.Open(_path, readWrite: true);
        if (hid is null)
            return false;
        new Thread(() => Listen(hid, reportLength), maxStackSize: 64 * 1024) { IsBackground = true, Name = "Dell receiver" }.Start();
        return true;
    }

    private void Listen(HidDevice hid, int reportLength)
    {
        try
        {
            Span<byte> report = stackalloc byte[reportLength];
            while (true)
            {
                int length = hid.Read(report, Timeout.Infinite);
                if (length < 0)
                    break; // receiver unplugged
                if (length >= 4 && report[0] == 0x01 && report[1] == 0x08 && report[2] is >= 1 and <= Slots && report[3] <= 100
                    && Interlocked.Exchange(ref _levels[report[2]], report[3]) != report[3])
                    Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        finally
        {
            _stopped = true;
            hid.Dispose();
            lock (Listening)
                Listening.Remove(_path);
        }
    }

    public int Level(int slot) => Volatile.Read(ref _levels[slot]);

    public string Path => _path;
}

/// <summary>One paired slot. Hidden until the receiver has reported its battery.</summary>
internal sealed class DellDevice(DellReceiver receiver, int slot) : IBatteryDevice
{
    // The KM5221W pairs the keyboard as slot 1 and the mouse as slot 2.
    private readonly (string Name, DeviceKind Kind) _identity = slot switch
    {
        1 => ("Dell Wireless Keyboard", DeviceKind.Keyboard),
        2 => ("Dell Wireless Mouse", DeviceKind.Mouse),
        _ => ($"Dell device {slot}", DeviceKind.Other),
    };

    public string Details => $"slot {slot}, " + (receiver.Level(slot) < 0 ? "no battery report yet (sent when the device wakes)" : "battery report heard")
        + $" via {receiver.Path}";

    public DeviceStatus? Read()
    {
        int level = receiver.Level(slot);
        return level < 0 ? null : new DeviceStatus($"dell:{slot}", _identity.Name, _identity.Kind, DeviceState.Online, level, false, false);
    }
}
