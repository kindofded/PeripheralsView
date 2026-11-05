using Microsoft.Win32;

namespace PeripheralsView;

internal sealed class Settings
{
    private const string KeyPath = @"Software\PeripheralsView";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "PeripheralsView";

    public static readonly int[] LowLevels = [10, 15, 20, 25];

    public bool ShowPercent { get; set; }
    public bool Alerts { get; set; } = true;
    public int LowLevel { get; set; } = 15;

    public static Settings Load()
    {
        var settings = new Settings();
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null)
            return settings;

        settings.ShowPercent = GetBool(key, nameof(ShowPercent), settings.ShowPercent);
        settings.Alerts = GetBool(key, nameof(Alerts), settings.Alerts);
        settings.LowLevel = GetInt(key, nameof(LowLevel), settings.LowLevel);
        return settings;
    }

    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(nameof(ShowPercent), ShowPercent ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(Alerts), Alerts ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(LowLevel), LowLevel, RegistryValueKind.DWord);
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (value)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, false);
        }
    }

    private static bool GetBool(RegistryKey key, string name, bool fallback) =>
        key.GetValue(name) is int value ? value != 0 : fallback;

    private static int GetInt(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) is int value ? value : fallback;
}
