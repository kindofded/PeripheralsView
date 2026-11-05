namespace PeripheralsView;

internal static class Log
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralsView");

    /// <summary>Appends to %LOCALAPPDATA%\PeripheralsView\errors.log (reset once it passes 256 KB).</summary>
    public static void Error(Exception? error)
    {
        if (error is null)
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
