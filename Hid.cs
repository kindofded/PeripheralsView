using System.Runtime.InteropServices;

namespace PeripheralsView;

internal readonly record struct HidInfo(
    string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    int InputLength, int OutputLength, int FeatureLength);

/// <summary>
/// One HID top-level collection. Windows opens mouse and keyboard collections exclusively, but a query-only
/// handle (access 0) still allows feature reports; vendor-defined collections also allow read/write for
/// input/output reports. All I/O is overlapped so reads can time out instead of hanging on a sleeping device.
/// </summary>
internal sealed unsafe class HidDevice : IDisposable
{
    private nint _handle;
    private nint _event;

    private HidDevice(nint handle) => _handle = handle;

    public static List<string> EnumeratePaths()
    {
        var paths = new List<string>();
        Guid guid = Native.HidInterfaceGuid;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint length;
            if (Native.CM_Get_Device_Interface_List_SizeW(&length, &guid, null, Native.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != Native.CR_SUCCESS)
                return paths;

            var buffer = new char[length];
            uint result;
            fixed (char* p = buffer)
                result = Native.CM_Get_Device_Interface_ListW(&guid, null, p, length, Native.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (result == Native.CR_BUFFER_SMALL)
                continue; // a device arrived between the two calls
            if (result != Native.CR_SUCCESS)
                return paths;

            foreach (var range in buffer.AsSpan().Split('\0'))
            {
                var path = buffer.AsSpan(range);
                if (!path.IsEmpty)
                    paths.Add(path.ToString());
            }
            return paths;
        }
        return paths;
    }

    public static HidDevice? Open(string path, bool readWrite)
    {
        uint access = readWrite ? Native.GENERIC_READ | Native.GENERIC_WRITE : 0;
        nint handle = Native.CreateFileW(path, access, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0,
            Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, 0);
        return handle == Native.INVALID_HANDLE_VALUE ? null : new HidDevice(handle);
    }

    /// <summary>Vendor/product IDs and report sizes, using a query-only handle. Null if the device can't be opened.</summary>
    public static HidInfo? Query(string path)
    {
        using var device = Open(path, readWrite: false);
        if (device is null)
            return null;

        var attributes = new Native.HIDD_ATTRIBUTES { Size = (uint)sizeof(Native.HIDD_ATTRIBUTES) };
        if (!Native.HidD_GetAttributes(device._handle, &attributes))
            return null;

        nint preparsed;
        if (!Native.HidD_GetPreparsedData(device._handle, &preparsed))
            return null;
        Native.HIDP_CAPS caps;
        int status = Native.HidP_GetCaps(preparsed, &caps);
        Native.HidD_FreePreparsedData(preparsed);
        if (status != Native.HIDP_STATUS_SUCCESS)
            return null;

        return new HidInfo(path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
    }

    public string? GetProductString()
    {
        char* buffer = stackalloc char[128];
        if (!Native.HidD_GetProductString(_handle, buffer, 128 * sizeof(char)))
            return null;
        var text = new string(buffer).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary><paramref name="report"/> starts with the report ID and must be exactly the collection's feature report length.</summary>
    public bool SetFeature(Span<byte> report)
    {
        fixed (byte* p = report)
            return Native.HidD_SetFeature(_handle, p, (uint)report.Length);
    }

    /// <summary>Set <c>report[0]</c> to the report ID before calling.</summary>
    public bool GetFeature(Span<byte> report)
    {
        fixed (byte* p = report)
            return Native.HidD_GetFeature(_handle, p, (uint)report.Length);
    }

    /// <summary>Returns the bytes written, 0 on timeout, -1 on error.</summary>
    public int Write(Span<byte> report, int timeoutMs) => Transfer(write: true, report, timeoutMs);

    /// <summary>Returns the bytes read, 0 on timeout, -1 on error. <see cref="Timeout.Infinite"/> waits for the next report.</summary>
    public int Read(Span<byte> buffer, int timeoutMs) => Transfer(write: false, buffer, timeoutMs);

    private int Transfer(bool write, Span<byte> buffer, int timeoutMs)
    {
        if (_event == 0)
            _event = Native.CreateEventW(0, manualReset: true, initialState: false, null);

        var overlapped = new NativeOverlapped { EventHandle = _event };
        uint transferred;
        fixed (byte* p = buffer)
        {
            bool done = write
                ? Native.WriteFile(_handle, p, (uint)buffer.Length, null, &overlapped)
                : Native.ReadFile(_handle, p, (uint)buffer.Length, null, &overlapped);
            if (!done)
            {
                if (Marshal.GetLastPInvokeError() != Native.ERROR_IO_PENDING)
                    return -1;
                uint wait = timeoutMs == Timeout.Infinite ? Native.INFINITE : (uint)Math.Max(timeoutMs, 0);
                if (Native.WaitForSingleObject(_event, wait) != Native.WAIT_OBJECT_0)
                {
                    // The buffer and OVERLAPPED live on this stack frame: wait for the cancellation to finish.
                    Native.CancelIoEx(_handle, &overlapped);
                    Native.GetOverlappedResult(_handle, &overlapped, &transferred, wait: true);
                    return 0;
                }
            }
            return Native.GetOverlappedResult(_handle, &overlapped, &transferred, wait: false) ? (int)transferred : -1;
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            Native.CloseHandle(_handle);
            _handle = 0;
        }
        if (_event != 0)
        {
            Native.CloseHandle(_event);
            _event = 0;
        }
    }
}
