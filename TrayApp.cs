using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PeripheralsView;

/// <summary>
/// A hidden window plus one tray icon per device. Everything here runs on the UI thread; battery reads happen
/// on <see cref="DeviceMonitor"/>'s thread, which posts <see cref="WM_DEVICES"/> when it has new results.
/// </summary>
internal sealed unsafe class TrayApp : IDisposable
{
    private const uint WM_TRAY = Native.WM_APP + 1;
    private const uint WM_DEVICES = Native.WM_APP + 2;
    private const nuint RescanTimer = 1;
    private const string PlaceholderKey = "";
    private const string ClassName = "PeripheralsView.Tray";

    private static TrayApp? s_app;

    private readonly Settings _settings = Settings.Load();
    private readonly DeviceMonitor _monitor;
    private readonly Dictionary<string, TrayIcon> _icons = [];
    private readonly Dictionary<string, AlertState> _alerts = [];
    private IReadOnlyList<DeviceStatus> _devices = [];
    private bool _scanned;
    private nint _hwnd;
    private nint _deviceNotification;
    private uint _taskbarCreated;
    private bool _lightTaskbar;
    private int _iconSize;

    private sealed class TrayIcon
    {
        public uint Id;
        public nint Handle;
        public IconSpec Spec;
        public string Tip = "";
        public bool Added;
    }

    private sealed class AlertState
    {
        public bool Low, Critical, Full;
    }

    public TrayApp()
    {
        s_app = this;
        _monitor = new DeviceMonitor(() => Native.PostMessageW(_hwnd, WM_DEVICES, 0, 0));
    }

    public void Run()
    {
        CreateWindow();
        _lightTaskbar = IsTaskbarLight();
        _iconSize = QueryIconSize();
        ApplyMenuTheme();
        RegisterForHidChanges();
        _monitor.Start();

        Native.MSG message;
        while (Native.GetMessageW(&message, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(&message);
            Native.DispatchMessageW(&message);
        }
    }

    private void CreateWindow()
    {
        nint instance = Native.GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = className,
            };
            Native.RegisterClassExW(&windowClass);
        }
        // A real (never shown) top-level window rather than a message-only one: only top-level windows get
        // the TaskbarCreated and WM_SETTINGCHANGE broadcasts.
        _hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, ClassName, "Peripherals View", Native.WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
    }

    private void RegisterForHidChanges()
    {
        var filter = new Native.DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = (uint)sizeof(Native.DEV_BROADCAST_DEVICEINTERFACE),
            dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = Native.HidInterfaceGuid,
        };
        _deviceNotification = Native.RegisterDeviceNotificationW(_hwnd, &filter, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (s_app is { } app && app.HandleMessage(hwnd, message, wParam, lParam) is { } result)
                return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    /// <summary>Null = let DefWindowProc handle it.</summary>
    private nint? HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_TRAY:
                // NOTIFYICON_VERSION_4: event in LOWORD(lParam), anchor point in wParam.
                uint trayEvent = (uint)(lParam & 0xFFFF);
                if (trayEvent is Native.WM_CONTEXTMENU or Native.NIN_SELECT or Native.NIN_KEYSELECT)
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return 0;

            case WM_DEVICES:
                _scanned = true;
                _devices = _monitor.Snapshot;
                UpdateIcons();
                return 0;

            case Native.WM_DEVICECHANGE:
                // Debounced: one plug-in produces several interface arrivals.
                if (wParam is Native.DBT_DEVICEARRIVAL or Native.DBT_DEVICEREMOVECOMPLETE)
                    Native.SetTimer(hwnd, RescanTimer, 1500, 0);
                return 1;

            case Native.WM_POWERBROADCAST:
                if (wParam == Native.PBT_APMRESUMEAUTOMATIC)
                    Native.SetTimer(hwnd, RescanTimer, 5000, 0);
                return 1;

            case Native.WM_TIMER when wParam == (nint)RescanTimer:
                Native.KillTimer(hwnd, RescanTimer);
                _monitor.Refresh(rescan: true);
                return 0;

            case Native.WM_SETTINGCHANGE or Native.WM_DISPLAYCHANGE or Native.WM_DPICHANGED:
                RefreshAppearance();
                return null;

            case Native.WM_DESTROY:
                RemoveAllIcons();
                _hwnd = 0;
                Native.PostQuitMessage(0);
                return 0;
        }

        if (message == _taskbarCreated && message != 0)
        {
            // Explorer restarted: the old icons are gone.
            foreach (var icon in _icons.Values)
                icon.Added = false;
            _iconSize = QueryIconSize();
            UpdateIcons();
            return 0;
        }
        return null;
    }

    // ---------------------------------------------------------------- icons

    private void UpdateIcons()
    {
        if (!_scanned)
            return;

        var wanted = new HashSet<string>();
        if (_devices.Count == 0)
        {
            wanted.Add(PlaceholderKey);
            SetIcon(PlaceholderKey, new IconSpec(DeviceKind.Other, -1, Badge.Offline, NormalColor, false, _iconSize),
                "Peripherals View\nNo supported devices found");
        }
        foreach (var device in _devices)
        {
            wanted.Add(device.Id);
            SetIcon(device.Id, SpecFor(device), $"{device.Name}\n{device.Describe()}");
            CheckAlerts(device);
        }

        foreach (var key in _icons.Keys.Where(key => !wanted.Contains(key)).ToList())
        {
            RemoveIcon(_icons[key]);
            _icons.Remove(key);
        }
    }

    private IconSpec SpecFor(DeviceStatus device)
    {
        var badge = device.State switch
        {
            DeviceState.Offline => Badge.Offline,
            DeviceState.Asleep => Badge.Asleep,
            _ => device.Charging ? Badge.Charging : Badge.None,
        };
        uint color = NormalColor;
        bool known = device.State != DeviceState.Offline && device.Level >= 0;
        if (known && _settings.ColorByLevel)
            color = Palette.ForLevel(device.Level, _lightTaskbar);
        else if (known && !device.Charging)
        {
            if (device.Level <= 5)
                color = Palette.Critical(_lightTaskbar);
            else if (device.Level <= _settings.LowLevel)
                color = Palette.Low(_lightTaskbar);
        }
        else if (known && _settings.ShowPercent)
            color = Palette.Success(_lightTaskbar);
        return new IconSpec(device.Kind, device.Level, badge, color, _settings.ShowPercent, _iconSize);
    }

    private uint NormalColor => Palette.Normal(_lightTaskbar);

    private void SetIcon(string key, in IconSpec spec, string tip)
    {
        if (!_icons.TryGetValue(key, out var icon))
            _icons[key] = icon = new TrayIcon { Id = StableId(key) };

        bool iconChanged = icon.Handle == 0 || icon.Spec != spec;
        if (!iconChanged && icon.Tip == tip && icon.Added)
            return;

        nint oldHandle = 0;
        if (iconChanged)
        {
            oldHandle = icon.Handle;
            icon.Handle = IconRenderer.CreateIcon(spec);
            icon.Spec = spec;
        }
        icon.Tip = tip;

        var data = NewIconData(icon.Id);
        data.uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = icon.Handle;
        Native.CopyString(tip, data.szTip, 128);
        if (icon.Added)
        {
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, &data);
        }
        else if (Native.Shell_NotifyIconW(Native.NIM_ADD, &data))
        {
            data.uVersion = Native.NOTIFYICON_VERSION_4;
            Native.Shell_NotifyIconW(Native.NIM_SETVERSION, &data);
            icon.Added = true;
        }

        if (oldHandle != 0)
            Native.DestroyIcon(oldHandle);
    }

    private void RemoveIcon(TrayIcon icon)
    {
        var data = NewIconData(icon.Id);
        Native.Shell_NotifyIconW(Native.NIM_DELETE, &data);
        if (icon.Handle != 0)
            Native.DestroyIcon(icon.Handle);
        icon.Handle = 0;
        icon.Added = false;
    }

    private Native.NOTIFYICONDATAW NewIconData(uint id) =>
        new() { cbSize = (uint)sizeof(Native.NOTIFYICONDATAW), hWnd = _hwnd, uID = id };

    /// <summary>Same device, same icon ID across restarts, so Windows remembers whether it's shown on the taskbar.</summary>
    private static uint StableId(string key)
    {
        uint hash = 2166136261;
        foreach (char c in key)
            hash = (hash ^ c) * 16777619;
        return key.Length == 0 ? 1 : hash | 0x100;
    }

    // ---------------------------------------------------------------- alerts

    private void CheckAlerts(DeviceStatus device)
    {
        if (device.State != DeviceState.Online || device.Level < 0)
            return;
        if (!_alerts.TryGetValue(device.Id, out var state))
            _alerts[device.Id] = state = new AlertState { Full = device.Full }; // "fully charged" only when charging finishes

        if (device.Charging)
        {
            state.Low = state.Critical = false;
            if (device.Full && !state.Full)
            {
                state.Full = true;
                Notify(device.Id, $"{device.Name} is fully charged", "You can unplug it.", Native.NIIF_INFO);
            }
            return;
        }

        state.Full = false;
        if (device.Level <= 5 && !state.Critical)
        {
            state.Critical = state.Low = true;
            Notify(device.Id, $"{device.Name}: {device.Level}%", "Battery critically low. Charge it now.", Native.NIIF_WARNING);
        }
        else if (device.Level <= _settings.LowLevel && !state.Low)
        {
            state.Low = true;
            Notify(device.Id, $"{device.Name}: {device.Level}%", "Battery low.", Native.NIIF_WARNING);
        }
        else if (device.Level > _settings.LowLevel + 5)
        {
            state.Low = state.Critical = false;
        }
    }

    private void Notify(string key, string title, string text, uint flags)
    {
        if (!_settings.Alerts || !_icons.TryGetValue(key, out var icon) || !icon.Added)
            return;
        var data = NewIconData(icon.Id);
        data.uFlags = Native.NIF_INFO;
        data.dwInfoFlags = flags | Native.NIIF_RESPECT_QUIET_TIME;
        Native.CopyString(title, data.szInfoTitle, 64);
        Native.CopyString(text, data.szInfo, 256);
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, &data);
    }

    // ---------------------------------------------------------------- menu

    private void ShowMenu(int x, int y)
    {
        using var menu = new PopupMenu();
        if (!_scanned)
            menu.Add("Looking for devices…", () => { }, enabled: false);
        else if (_devices.Count == 0)
            menu.Add("No supported devices found", () => { }, enabled: false);
        foreach (var device in _devices)
            menu.Add($"{device.Name}\t{device.Describe()}", () => _monitor.Refresh());
        menu.AddSeparator();

        menu.Add("Show percentage on icons", () =>
        {
            _settings.ShowPercent = !_settings.ShowPercent;
            _settings.Save();
            UpdateIcons();
        }, _settings.ShowPercent);
        menu.Add("Color by battery level", () =>
        {
            _settings.ColorByLevel = !_settings.ColorByLevel;
            _settings.Save();
            UpdateIcons();
        }, _settings.ColorByLevel);
        menu.Add("Battery alerts", () =>
        {
            _settings.Alerts = !_settings.Alerts;
            _settings.Save();
        }, _settings.Alerts);
        var low = menu.AddSubmenu("Low battery level");
        foreach (int level in Settings.LowLevels)
        {
            low.Add($"{level}%", () =>
            {
                _settings.LowLevel = level;
                _settings.Save();
                _alerts.Clear();
                UpdateIcons();
            }, _settings.LowLevel == level, radio: true);
        }
        menu.Add("Start with Windows", () => Settings.StartWithWindows = !Settings.StartWithWindows, Settings.StartWithWindows);
        menu.AddSeparator();
        menu.Add("Refresh now", () => _monitor.Refresh(rescan: true));
        menu.Add("Exit", () => Native.DestroyWindow(_hwnd));

        if (x == 0 && y == 0 && Native.GetCursorPos(out var cursor))
            (x, y) = (cursor.X, cursor.Y);
        menu.Show(_hwnd, x, y);
    }

    // ---------------------------------------------------------------- theme & size

    private void RefreshAppearance()
    {
        bool light = IsTaskbarLight();
        int size = QueryIconSize();
        if (light == _lightTaskbar && size == _iconSize)
            return;
        _lightTaskbar = light;
        _iconSize = size;
        ApplyMenuTheme();
        UpdateIcons();
    }

    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    private void ApplyMenuTheme()
    {
        try
        {
            Native.SetPreferredAppMode(_lightTaskbar ? 3 /* ForceLight */ : 2 /* ForceDark */);
            Native.AllowDarkModeForWindow(_hwnd, true);
            Native.FlushMenuThemes();
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Small-icon size at the taskbar's DPI (it follows the primary display's scale, not ours).</summary>
    public static int QueryIconSize()
    {
        nint taskbar = Native.FindWindowW("Shell_TrayWnd", null);
        uint dpi = taskbar != 0 ? Native.GetDpiForWindow(taskbar) : 0;
        if (dpi == 0)
            dpi = Native.GetDpiForSystem();
        return Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);
    }

    private void RemoveAllIcons()
    {
        foreach (var icon in _icons.Values)
            RemoveIcon(icon);
        _icons.Clear();
    }

    public void Dispose()
    {
        if (_deviceNotification != 0)
            Native.UnregisterDeviceNotification(_deviceNotification);
        _deviceNotification = 0;
        if (_hwnd != 0)
            Native.DestroyWindow(_hwnd);
    }
}
