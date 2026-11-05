namespace PeripheralsView;

/// <summary>
/// Logitech device over HID++ 2.0 (protocol as documented by Solaar). Works over Bluetooth, USB cable and
/// Lightspeed/Unifying/Bolt receivers. Everything is discovered on the first successful read: device name and
/// type (feature 0x0005) and whichever battery feature the device has (0x1004, 0x1000 or 0x1001).
/// </summary>
internal sealed class LogitechDevice : IBatteryDevice
{
    public const ushort VendorId = 0x046D;

    private const byte LongReport = 0x11;
    private const byte SoftwareId = 0x0E; // tags our requests so we skip G HUB's traffic and device notifications
    private const byte DirectIndex = 0xFF; // device index for cable / Bluetooth
    private const ushort FeatureDeviceName = 0x0005;
    private const ushort FeatureBatteryStatus = 0x1000;
    private const ushort FeatureBatteryVoltage = 0x1001;
    private const ushort FeatureUnifiedBattery = 0x1004;

    private static readonly ushort[] Receivers =
    [
        0xC52B, 0xC532, 0xC548, // Unifying, Bolt
        0xC52F, 0xC534, 0xC537, // nano
        0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC54D, // Lightspeed
    ];

    private readonly HidInfo _info;
    private readonly byte _index;
    private readonly int _timeoutMs;
    private string _name;
    private DeviceKind _kind = DeviceKind.Other;
    private bool _discovered;
    private bool _noBattery;
    private ushort _batteryFeature;
    private byte _batteryIndex;
    private int _lastMillivolts;

    private LogitechDevice(HidInfo info, byte index, string fallbackName)
    {
        _info = info;
        _index = index;
        _name = fallbackName;
        // Bluetooth round trips are slower, and a sleeping keyboard has to reconnect first.
        _timeoutMs = info.UsagePage == 0xFF43 ? 1500 : 400;
    }

    /// <summary>The HID++ long-report collection: 0xFF00/0x0002 over USB, 0xFF43/0x0202 over Bluetooth.</summary>
    public static bool IsHidppCollection(in HidInfo info) =>
        info.VendorId == VendorId
        && (info.ProductId >> 12) is 0xA or 0xB or 0xC // headsets, Bluetooth, USB/receivers – not webcams or conference gear
        && ((info.UsagePage == 0xFF00 && info.Usage == 0x0002) || (info.UsagePage == 0xFF43 && info.Usage == 0x0202))
        && info.OutputLength >= 20 && info.InputLength >= 20;

    /// <summary>A direct device has index 0xFF; behind a receiver each paired device has its own index (1–6).</summary>
    public static IEnumerable<LogitechDevice> Create(HidInfo info, string? productName)
    {
        if (Array.IndexOf(Receivers, info.ProductId) < 0)
        {
            yield return new LogitechDevice(info, DirectIndex, productName ?? "Logitech device");
            yield break;
        }
        for (byte index = 1; index <= 6; index++)
            yield return new LogitechDevice(info, index, $"{productName ?? "Receiver"} device {index}");
    }

    public string Details =>
        $"index 0x{_index:X2}, battery feature " + (_batteryFeature == 0 ? "none" : $"0x{_batteryFeature:X4}")
        + (_batteryFeature == FeatureBatteryVoltage ? $" ({_lastMillivolts} mV)" : "") + $" via {_info.Path}";

    public DeviceStatus? Read()
    {
        if (_noBattery)
            return null;

        using var hid = HidDevice.Open(_info.Path, readWrite: true);
        if (hid is not null && (_discovered || Discover(hid)) && ReadBattery(hid, out int level, out bool charging, out bool full))
            return new DeviceStatus(Id, _name, _kind, DeviceState.Online, level, charging, full);

        // Never seen online: stay hidden (e.g. an empty receiver slot) until it answers.
        return _discovered && !_noBattery ? new DeviceStatus(Id, _name, _kind, DeviceState.Offline, -1, false, false) : null;
    }

    private string Id => "logitech:" + _name;

    private bool Discover(HidDevice hid)
    {
        Span<byte> response = stackalloc byte[16];
        // Root feature, getProtocolVersion: answers only if a HID++ 2.0 device is there.
        if (!Request(hid, 0x00, 1, [0, 0, 0x5A], response) || response[0] < 2)
            return false;

        byte nameIndex = GetFeatureIndex(hid, FeatureDeviceName);
        if (nameIndex != 0)
        {
            if (Request(hid, nameIndex, 0, [], response))
            {
                int length = response[0];
                var name = new char[length];
                for (int offset = 0; offset < length; offset += 16)
                {
                    if (!Request(hid, nameIndex, 1, [(byte)offset], response))
                        break;
                    for (int i = 0; i < 16 && offset + i < length; i++)
                        name[offset + i] = (char)response[i];
                }
                var text = new string(name).TrimEnd('\0', ' ');
                if (text.Length > 0)
                    _name = text;
            }
            if (Request(hid, nameIndex, 2, [], response))
            {
                _kind = response[0] switch
                {
                    0 or 2 => DeviceKind.Keyboard, // keyboard, numpad
                    3 or 4 or 5 => DeviceKind.Mouse, // mouse, touchpad, trackball
                    8 => DeviceKind.Headset,
                    _ => DeviceKind.Other,
                };
            }
        }

        foreach (ushort feature in (ReadOnlySpan<ushort>)[FeatureUnifiedBattery, FeatureBatteryStatus, FeatureBatteryVoltage])
        {
            byte index = GetFeatureIndex(hid, feature);
            if (index != 0)
            {
                _batteryFeature = feature;
                _batteryIndex = index;
                break;
            }
        }
        _discovered = true;
        _noBattery = _batteryFeature == 0;
        return !_noBattery;
    }

    private bool ReadBattery(HidDevice hid, out int level, out bool charging, out bool full)
    {
        level = -1;
        charging = full = false;
        Span<byte> r = stackalloc byte[16];
        switch (_batteryFeature)
        {
            case FeatureUnifiedBattery when Request(hid, _batteryIndex, 1, [], r): // getStatus
                // r[0] = %, r[1] = level flags (critical/low/good/full), r[2] = 0 discharging, 1/2 charging, 3 complete
                level = r[0] != 0 ? r[0] : (r[1] & 8) != 0 ? 100 : (r[1] & 4) != 0 ? 60 : (r[1] & 2) != 0 ? 20 : (r[1] & 1) != 0 ? 5 : -1;
                charging = r[2] is 1 or 2 or 3;
                full = r[2] == 3;
                return true;

            case FeatureBatteryStatus when Request(hid, _batteryIndex, 0, [], r): // getBatteryLevelStatus
                // r[0] = %, r[2] = 0 discharging, 1 recharging, 2 almost full, 3 charged, 4 slow recharge
                level = r[0];
                charging = r[2] is 1 or 2 or 3 or 4;
                full = r[2] == 3;
                return true;

            case FeatureBatteryVoltage when Request(hid, _batteryIndex, 0, [], r): // getBatteryInfo
                // r[0..1] = millivolts, r[2] bit 7 = external power, bits 0-2 = 0 charging, 1 full
                _lastMillivolts = r[0] << 8 | r[1];
                level = EstimateLevel(_lastMillivolts);
                charging = (r[2] & 0x80) != 0;
                full = charging && (r[2] & 0x07) == 1;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Li-ion discharge curve used by Solaar for devices that only report voltage (e.g. G915, G Pro Wireless).</summary>
    public static int EstimateLevel(int millivolts)
    {
        ReadOnlySpan<(int Millivolts, int Percent)> curve =
        [
            (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
            (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
        ];
        if (millivolts >= curve[0].Millivolts)
            return 100;
        for (int i = 1; i < curve.Length; i++)
        {
            if (millivolts >= curve[i].Millivolts)
            {
                var (hiV, hiP) = curve[i - 1];
                var (loV, loP) = curve[i];
                return loP + (millivolts - loV) * (hiP - loP) / (hiV - loV);
            }
        }
        return 0;
    }

    private byte GetFeatureIndex(HidDevice hid, ushort feature)
    {
        Span<byte> response = stackalloc byte[16];
        return Request(hid, 0x00, 0, [(byte)(feature >> 8), (byte)feature], response) ? response[0] : (byte)0;
    }

    /// <summary>Sends a long HID++ 2.0 request and waits for the matching answer. False on error or timeout.</summary>
    private bool Request(HidDevice hid, byte featureIndex, int function, ReadOnlySpan<byte> parameters, Span<byte> response)
    {
        Span<byte> report = stackalloc byte[_info.OutputLength];
        report.Clear();
        report[0] = LongReport;
        report[1] = _index;
        report[2] = featureIndex;
        report[3] = (byte)(function << 4 | SoftwareId);
        parameters.CopyTo(report[4..]);
        if (hid.Write(report, _timeoutMs) <= 0)
            return false;

        Span<byte> input = stackalloc byte[_info.InputLength];
        long deadline = Environment.TickCount64 + _timeoutMs;
        while (true)
        {
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
                return false;
            int read = hid.Read(input, remaining);
            if (read <= 0)
                return false;
            if (read < 20 || input[1] != _index)
                continue;
            if (input[2] == featureIndex && input[3] == report[3])
            {
                input.Slice(4, 16).CopyTo(response);
                return true;
            }
            // HID++ 2.0 error (0xFF) or HID++ 1.0 error (0x8F) for this request
            if (input[2] is 0xFF or 0x8F && input[3] == featureIndex && input[4] == report[3])
                return false;
        }
    }
}
