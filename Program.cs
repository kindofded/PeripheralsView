namespace PeripheralsView;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error(e.ExceptionObject as Exception);
        if (args.Length > 0)
            return Cli.Run(args);

        using var mutex = new Mutex(true, @"Local\PeripheralsView.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
            return 0;

        using var app = new TrayApp();
        app.Run();
        return 0;
    }
}
