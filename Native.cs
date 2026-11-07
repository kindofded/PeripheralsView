using System.Runtime.InteropServices;

namespace PeripheralsView;

/// <summary>P/Invoke declarations. Everything is pointer-based / blittable so no runtime marshalling is needed (AOT-safe).</summary>
internal static unsafe partial class Native
{
    // ---------------------------------------------------------------- windows & messages

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_DEVICECHANGE = 0x0219;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;

    public const nint DBT_DEVICEARRIVAL = 0x8000;
    public const nint DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const uint DBT_DEVTYP_DEVICEINTERFACE = 5;
    public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    public const nint PBT_APMRESUMEAUTOMATIC = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public uint dbcc_size;
        public uint dbcc_devicetype;
        public uint dbcc_reserved;
        public Guid dbcc_classguid;
        public char dbcc_name;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* message);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string message);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowW(string className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial nint RegisterDeviceNotificationW(nint recipient, void* filter, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterDeviceNotification(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    public const int SM_CXSMICON = 49;

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    // ---------------------------------------------------------------- menus

    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_POPUP = 0x0010;
    public const uint MIIM_STATE = 0x0001;
    public const uint MIIM_ID = 0x0002;
    public const uint MIIM_STRING = 0x0040;
    public const uint MIIM_FTYPE = 0x0100;
    public const uint MFT_RADIOCHECK = 0x0200;
    public const uint MFS_DISABLED = 0x0003;
    public const uint MFS_CHECKED = 0x0008;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_VERTICAL = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct TPMPARAMS
    {
        public uint cbSize;
        public RECT rcExclude;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public char* dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InsertMenuItemW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, MENUITEMINFOW* info);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, TPMPARAMS* parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    // Undocumented uxtheme exports (stable since Windows 10 1903) that switch native menus to dark mode.
    [LibraryImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowDarkModeForWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    public static partial int SetPreferredAppMode(int mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    public static partial void FlushMenuThemes();

    // ---------------------------------------------------------------- tray icons

    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_INFO = 0x10;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x01;
    public const uint NIIF_WARNING = 0x02;
    public const uint NIIF_RESPECT_QUIET_TIME = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    /// <summary>Copies <paramref name="text"/> into a fixed-size, null-terminated struct buffer (truncating).</summary>
    public static void CopyString(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint hwnd, string? operation, string file, string? parameters, string? directory, int showCommand);

    // ---------------------------------------------------------------- icons & GDI

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconIndirect(ICONINFO* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, void* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint handle);

    public const int FW_SEMIBOLD = 600;
    public const uint ANTIALIASED_QUALITY = 4;
    public const int TRANSPARENT = 1;
    public const uint DT_NOPREFIX = 0x0800;
    public const uint DT_SINGLELINE = 0x0020;
    public const uint DT_NOCLIP = 0x0100;

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("user32.dll")]
    public static partial int DrawTextW(nint hdc, char* text, int length, RECT* rect, uint format);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    // ---------------------------------------------------------------- files, events, console

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int ERROR_IO_PENDING = 997;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint INFINITE = 0xFFFFFFFF;
    public static readonly nint INVALID_HANDLE_VALUE = -1;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(nint handle, void* buffer, uint length, uint* read, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(nint handle, void* buffer, uint length, uint* written, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOverlappedResult(nint handle, NativeOverlapped* overlapped, uint* transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelIoEx(nint handle, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll")]
    public static partial nint CreateEventW(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, char* name);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    // ---------------------------------------------------------------- HID

    public static readonly Guid HidInterfaceGuid = new(0x4D1E55B2, 0xF16F, 0x11CF, 0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);
    public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const uint CR_SUCCESS = 0;
    public const uint CR_BUFFER_SMALL = 0x1A;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Rest[27];
    }

    // ---------------------------------------------------------------- device nodes (PnP properties)

    public const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1;
    public const uint CM_GETIDLIST_FILTER_PRESENT = 0x100;
    public const uint DEVPROP_TYPE_BYTE = 0x03;
    public const uint DEVPROP_TYPE_UINT32 = 0x07;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x11;
    public const uint DEVPROP_TYPE_STRING = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;

        public DEVPROPKEY(string fmtid, uint pid)
        {
            this.fmtid = new Guid(fmtid);
            this.pid = pid;
        }
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_ID_List_SizeW(uint* length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_ID_ListW(string? filter, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Locate_DevNodeW(uint* devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Parent(uint* parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_DevNode_PropertyW(uint devInst, DEVPROPKEY* key, uint* type, void* buffer, uint* size, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Device_Interface_List_SizeW(uint* length, Guid* interfaceClass, char* deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Device_Interface_ListW(Guid* interfaceClass, char* deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetAttributes(nint handle, HIDD_ATTRIBUTES* attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetPreparsedData(nint handle, nint* preparsedData);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_FreePreparsedData(nint preparsedData);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsedData, HIDP_CAPS* caps);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetFeature(nint handle, void* buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetFeature(nint handle, void* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(nint handle, void* buffer, uint length);
}
