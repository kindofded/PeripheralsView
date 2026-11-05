namespace PeripheralsView;

/// <summary>
/// Classic Bluetooth devices whose battery level Windows already knows – headsets report it over the Hands-Free
/// profile (it's what Settings > Bluetooth shows). Read straight from PnP properties without opening a connection,
/// so it works alongside vendor apps such as Samsung's Galaxy Buds app, which keeps the earbuds' own channel busy.
/// </summary>
internal sealed unsafe class BluetoothBattery : IBatteryDevice
{
    private static readonly Native.DEVPROPKEY BatteryKey = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
    private static readonly Native.DEVPROPKEY ConnectedKey = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);
    private static readonly Native.DEVPROPKEY ClassOfDeviceKey = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 4);
    private static readonly Native.DEVPROPKEY FriendlyNameKey = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);

    // Paired-but-never-connected devices (an old speaker) stay hidden; once seen connected they show as offline.
    private static readonly HashSet<string> SeenConnected = [];

    private readonly string _serviceNodeId;
    private readonly uint _serviceNode;
    private readonly uint _deviceNode;
    private readonly string _name;
    private readonly DeviceKind _kind;

    private BluetoothBattery(string serviceNodeId, uint serviceNode, uint deviceNode, string name, DeviceKind kind)
    {
        _serviceNodeId = serviceNodeId;
        _serviceNode = serviceNode;
        _deviceNode = deviceNode;
        _name = name;
        _kind = kind;
    }

    public string Details => $"Windows battery property on {_serviceNodeId}";

    private string Id => "bluetooth:" + _name;

    /// <summary>
    /// Every paired classic Bluetooth device with a battery property. This is an in-memory PnP query (well under a
    /// millisecond), so it runs every poll and newly paired headsets show up without a rescan.
    /// </summary>
    public static List<BluetoothBattery> Scan()
    {
        var found = new List<BluetoothBattery>();
        var ids = ListDeviceIds("BTHENUM");
        var addresses = new HashSet<string>();
        foreach (string id in ids)
        {
            uint node;
            if (Native.CM_Locate_DevNodeW(&node, id, 0) != Native.CR_SUCCESS || !TryGet(node, BatteryKey, Native.DEVPROP_TYPE_BYTE, out byte _))
                continue;
            // The battery lives on a service node (e.g. Hands-Free, "...&58A63904F9A5_C00000000"); name, class and
            // connection on the device node "BTHENUM\DEV_58A63904F9A5\...". That isn't its PnP parent, so match the address.
            if (AddressOf(id) is not { } address || !addresses.Add(address))
                continue;
            string? deviceId = ids.Find(other => other.StartsWith(@"BTHENUM\DEV_" + address, StringComparison.OrdinalIgnoreCase));
            uint device;
            if (deviceId is null || Native.CM_Locate_DevNodeW(&device, deviceId, 0) != Native.CR_SUCCESS)
                continue;
            string name = GetString(device, FriendlyNameKey) ?? GetString(node, FriendlyNameKey) ?? "Bluetooth device";
            uint classOfDevice = TryGet(device, ClassOfDeviceKey, Native.DEVPROP_TYPE_UINT32, out uint cod) ? cod : 0;
            var kind = KindFromClass(classOfDevice);
            // Bluetooth's class of device can't tell earbuds from over-ear headphones; the name usually can.
            if (kind == DeviceKind.Headset && (name.Contains("bud", StringComparison.OrdinalIgnoreCase) || name.Contains("pod", StringComparison.OrdinalIgnoreCase)))
                kind = DeviceKind.Earbuds;
            found.Add(new BluetoothBattery(id, node, device, name, kind));
        }
        return found;
    }

    /// <summary>The 12-hex-digit address in a service node ID's last segment, e.g. "7&amp;10448D10&amp;0&amp;58A63904F9A5_C00000000".</summary>
    private static string? AddressOf(string serviceNodeId)
    {
        var instance = serviceNodeId.AsSpan(serviceNodeId.LastIndexOf('\\') + 1);
        var last = instance[(instance.LastIndexOf('&') + 1)..];
        int underscore = last.IndexOf('_');
        var address = underscore >= 0 ? last[..underscore] : last;
        return address.Length == 12 ? address.ToString() : null;
    }

    public DeviceStatus? Read()
    {
        bool connected = TryGet(_deviceNode, ConnectedKey, Native.DEVPROP_TYPE_BOOLEAN, out byte isConnected) && isConnected != 0;
        if (connected && TryGet(_serviceNode, BatteryKey, Native.DEVPROP_TYPE_BYTE, out byte level))
        {
            SeenConnected.Add(Id);
            return new DeviceStatus(Id, _name, _kind, DeviceState.Online, Math.Min((int)level, 100), false, false);
        }
        return SeenConnected.Contains(Id) ? new DeviceStatus(Id, _name, _kind, DeviceState.Offline, -1, false, false) : null;
    }

    /// <summary>Bluetooth class of device: major class 4 = audio/video, 5 = peripheral (minor bits: keyboard / pointing).</summary>
    private static DeviceKind KindFromClass(uint classOfDevice) => ((classOfDevice >> 8) & 0x1F) switch
    {
        0x04 => DeviceKind.Headset,
        0x05 => ((classOfDevice >> 6) & 0x3) switch
        {
            2 => DeviceKind.Mouse,
            1 or 3 => DeviceKind.Keyboard,
            _ => DeviceKind.Other,
        },
        _ => DeviceKind.Other,
    };

    private static List<string> ListDeviceIds(string enumerator)
    {
        const uint flags = Native.CM_GETIDLIST_FILTER_ENUMERATOR | Native.CM_GETIDLIST_FILTER_PRESENT;
        var ids = new List<string>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint length;
            if (Native.CM_Get_Device_ID_List_SizeW(&length, enumerator, flags) != Native.CR_SUCCESS)
                return ids;
            var buffer = new char[length];
            uint result;
            fixed (char* p = buffer)
                result = Native.CM_Get_Device_ID_ListW(enumerator, p, length, flags);
            if (result == Native.CR_BUFFER_SMALL)
                continue;
            if (result != Native.CR_SUCCESS)
                return ids;
            foreach (var range in buffer.AsSpan().Split('\0'))
            {
                if (range.End.Value > range.Start.Value)
                    ids.Add(new string(buffer.AsSpan(range)));
            }
            return ids;
        }
        return ids;
    }

    private static bool TryGet<T>(uint node, Native.DEVPROPKEY key, uint expectedType, out T value) where T : unmanaged
    {
        value = default;
        uint type, size = (uint)sizeof(T);
        T result;
        if (Native.CM_Get_DevNode_PropertyW(node, &key, &type, &result, &size, 0) != Native.CR_SUCCESS || type != expectedType)
            return false;
        value = result;
        return true;
    }

    private static string? GetString(uint node, Native.DEVPROPKEY key)
    {
        char* buffer = stackalloc char[256];
        uint type, size = 256 * sizeof(char);
        if (Native.CM_Get_DevNode_PropertyW(node, &key, &type, buffer, &size, 0) != Native.CR_SUCCESS || type != Native.DEVPROP_TYPE_STRING)
            return null;
        var text = new string(buffer).Trim();
        return text.Length == 0 ? null : text;
    }
}
