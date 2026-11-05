namespace PeripheralsView;

internal sealed record RazerModel(string Name, bool ReportsCharging, params ushort[] ProductIds);

/// <summary>
/// Razer mouse over its control feature report (protocol from openrazer): a 90-byte report, sent with
/// HidD_SetFeature and answered via HidD_GetFeature. Works directly with the receiver/cable – no Synapse.
/// </summary>
internal sealed class RazerDevice : IBatteryDevice
{
    public const ushort VendorId = 0x1532;
    public const int FeatureReportLength = 91; // report ID 0 + 90-byte Razer report

    private const byte ClassPower = 0x07;
    private const byte CommandBatteryLevel = 0x80;
    private const byte CommandChargingStatus = 0x84;

    private enum Status : byte
    {
        New = 0x00,
        Busy = 0x01,
        Success = 0x02,
        Failure = 0x03,
        Timeout = 0x04, // the receiver got no answer from the mouse: it's asleep or off
        NotSupported = 0x05,
        Error = 0xFF, // HID call failed (not a Razer status)
    }

    // Wired and receiver product IDs (from openrazer). Models marked false don't report charging.
    public static readonly RazerModel[] Models =
    [
        new("Razer Atheris", false, 0x0062),
        new("Razer Basilisk Mobile", false, 0x00D3, 0x00D4),
        new("Razer Basilisk Ultimate", true, 0x0086, 0x0088),
        new("Razer Basilisk V3 Pro", true, 0x00AA, 0x00AB),
        new("Razer Basilisk V3 Pro 35K", true, 0x00CC, 0x00CD),
        new("Razer Basilisk V3 Pro 35K Phantom Green Edition", true, 0x00D6, 0x00D7),
        new("Razer Basilisk V3 X HyperSpeed", false, 0x00B9),
        new("Razer Basilisk V4 Pro", true, 0x00E2, 0x00E3),
        new("Razer Basilisk X HyperSpeed", false, 0x0083),
        new("Razer Cobra HyperSpeed", true, 0x00DA, 0x00DB),
        new("Razer Cobra Pro", true, 0x00AF, 0x00B0),
        new("Razer DeathAdder V2 Pro", true, 0x007C, 0x007D),
        new("Razer DeathAdder V2 X HyperSpeed", false, 0x009C),
        new("Razer DeathAdder V3 HyperSpeed", true, 0x00C4, 0x00C5),
        new("Razer DeathAdder V3 Pro", true, 0x00B6, 0x00C2, 0x00B7, 0x00C3),
        new("Razer DeathAdder V4 Pro", true, 0x00BE, 0x00BF),
        new("Razer HyperPolling Wireless Dongle", true, 0x00B3),
        new("Razer Lancehead", true, 0x0059, 0x005A),
        new("Razer Lancehead Wireless", true, 0x0070, 0x006F),
        new("Razer Mamba 2012", true, 0x0024, 0x0025),
        new("Razer Mamba Chroma", true, 0x0044, 0x0045),
        new("Razer Mamba Wireless", true, 0x0073, 0x0072),
        new("Razer Naga Epic", true, 0x001F),
        new("Razer Naga Epic Chroma", true, 0x003E, 0x003F),
        new("Razer Naga Pro", true, 0x008F, 0x0090),
        new("Razer Naga V2 HyperSpeed", false, 0x00B4),
        new("Razer Naga V2 Pro", true, 0x00A7, 0x00A8),
        new("Razer Orochi V2", false, 0x0094, 0x0095),
        new("Razer Ouroboros", true, 0x0032),
        new("Razer Pro Click", true, 0x0080, 0x0077),
        new("Razer Pro Click Mini", true, 0x009A),
        new("Razer Pro Click V2", true, 0x00D0, 0x00D1),
        new("Razer Pro Click V2 Vertical Edition", true, 0x00C7, 0x00C8),
        new("Razer Viper Mini SE", true, 0x009E, 0x009F),
        new("Razer Viper Ultimate", true, 0x007A, 0x007B),
        new("Razer Viper V2 Pro", true, 0x00A5, 0x00A6),
        new("Razer Viper V3 HyperSpeed", false, 0x00B8),
        new("Razer Viper V3 Pro", true, 0x00C0, 0x00C1),
        new("Razer Viper V3 Pro SE", true, 0x00DE, 0x00DF),
        new("Razer Viper V4 Pro", true, 0x00E5, 0x00E6),
    ];

    // Models differ in which transaction ID they answer to; try each and remember the one that works.
    private static readonly byte[] TransactionIds = [0x1F, 0x3F, 0xFF];

    private readonly RazerModel _model;
    private readonly List<string> _paths;
    private int _pathIndex;
    private int _transactionIndex;

    public RazerDevice(RazerModel model, List<string> paths)
    {
        _model = model;
        _paths = paths;
    }

    public static RazerModel? FindModel(ushort productId) =>
        Array.Find(Models, model => Array.IndexOf(model.ProductIds, productId) >= 0);

    public string Details => $"transaction ID 0x{TransactionIds[_transactionIndex]:X2} via {_paths[_pathIndex]}";

    public DeviceStatus? Read()
    {
        bool asleep = false;
        // Start with the collection and transaction ID that worked last time.
        for (int p = 0; p < _paths.Count; p++)
        {
            int pathIndex = (_pathIndex + p) % _paths.Count;
            using var hid = HidDevice.Open(_paths[pathIndex], readWrite: false);
            if (hid is null)
                continue;

            for (int t = 0; t < TransactionIds.Length; t++)
            {
                int transactionIndex = (_transactionIndex + t) % TransactionIds.Length;
                byte transactionId = TransactionIds[transactionIndex];
                var status = Query(hid, transactionId, CommandBatteryLevel, out byte rawLevel);
                if (status == Status.Timeout)
                    asleep = true;
                if (status != Status.Success)
                    continue;

                _pathIndex = pathIndex;
                _transactionIndex = transactionIndex;
                int level = (rawLevel * 100 + 127) / 255;
                bool charging = _model.ReportsCharging
                    && Query(hid, transactionId, CommandChargingStatus, out byte isCharging) == Status.Success
                    && isCharging != 0;
                return Result(DeviceState.Online, level, charging);
            }
        }
        return Result(asleep ? DeviceState.Asleep : DeviceState.Offline, -1, false);
    }

    private DeviceStatus Result(DeviceState state, int level, bool charging) =>
        new("razer:" + _model.Name, _model.Name, DeviceKind.Mouse, state, level, charging, charging && level >= 100);

    private static Status Query(HidDevice hid, byte transactionId, byte command, out byte value)
    {
        value = 0;
        // Offsets are +1 from openrazer's struct because byte 0 is the HID report ID.
        Span<byte> report = stackalloc byte[FeatureReportLength];
        report.Clear();
        report[2] = transactionId;
        report[6] = 0x02; // data size
        report[7] = ClassPower;
        report[8] = command;
        report[89] = Checksum(report);
        if (!hid.SetFeature(report))
            return Status.Error;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 10 : 25);
            report.Clear();
            if (!hid.GetFeature(report))
                return Status.Error;

            var status = (Status)report[1];
            if (status is Status.New or Status.Busy || report[2] != transactionId || report[7] != ClassPower || report[8] != command)
                continue; // not processed yet (or someone else's answer)
            value = report[10]; // arguments[1]
            return status;
        }
        return Status.Busy;
    }

    /// <summary>XOR of the 90-byte report's bytes 2..87.</summary>
    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 3; i < 89; i++)
            crc ^= report[i];
        return crc;
    }
}
