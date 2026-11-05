namespace PeripheralsView;

/// <summary>
/// Native Win32 popup menu. Windows 11 draws these itself (rounded corners, Fluent styling, dark mode),
/// and it's destroyed as soon as it closes, so nothing stays in memory.
/// </summary>
internal sealed unsafe class PopupMenu : IDisposable
{
    private readonly nint _handle = Native.CreatePopupMenu();
    private readonly List<Action> _commands;

    public PopupMenu() : this([]) { }

    private PopupMenu(List<Action> commands) => _commands = commands;

    /// <remarks>A tab in <paramref name="text"/> right-aligns what follows it.</remarks>
    public void Add(string text, Action action, bool isChecked = false, bool radio = false, bool enabled = true)
    {
        _commands.Add(action);
        fixed (char* p = text)
        {
            var item = new Native.MENUITEMINFOW
            {
                cbSize = (uint)sizeof(Native.MENUITEMINFOW),
                fMask = Native.MIIM_ID | Native.MIIM_STRING | Native.MIIM_STATE | Native.MIIM_FTYPE,
                fType = radio ? Native.MFT_RADIOCHECK : 0,
                fState = (isChecked ? Native.MFS_CHECKED : 0) | (enabled ? 0 : Native.MFS_DISABLED),
                wID = (uint)_commands.Count,
                dwTypeData = p,
            };
            Native.InsertMenuItemW(_handle, uint.MaxValue, true, &item);
        }
    }

    public void AddSeparator() => Native.AppendMenuW(_handle, Native.MF_SEPARATOR, 0, null);

    public PopupMenu AddSubmenu(string text)
    {
        var child = new PopupMenu(_commands);
        Native.AppendMenuW(_handle, Native.MF_POPUP, (nuint)child._handle, text);
        return child;
    }

    /// <summary>Shows the menu at (x, y), outside the taskbar, and runs the chosen command after it closes.</summary>
    public void Show(nint owner, int x, int y)
    {
        var parameters = new Native.TPMPARAMS { cbSize = (uint)sizeof(Native.TPMPARAMS) };
        nint taskbar = Native.FindWindowW("Shell_TrayWnd", null);
        if (taskbar != 0)
            Native.GetWindowRect(taskbar, out parameters.rcExclude);

        // Without the foreground, the menu won't dismiss when clicking elsewhere.
        Native.SetForegroundWindow(owner);
        int command = Native.TrackPopupMenuEx(_handle,
            Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_NONOTIFY | Native.TPM_BOTTOMALIGN | Native.TPM_VERTICAL,
            x, y, owner, &parameters);
        Native.PostMessageW(owner, 0 /* WM_NULL */, 0, 0);

        if (command > 0 && command <= _commands.Count)
            _commands[command - 1]();
    }

    public void Dispose()
    {
        // Destroying the root menu also destroys its submenus.
        Native.DestroyMenu(_handle);
    }
}
