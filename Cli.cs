namespace PeripheralsView;

/// <summary>
/// Command-line mode. The exe is a GUI app, so it attaches to the parent console; PowerShell doesn't wait for
/// GUI apps, so pipe the output (<c>.\PeripheralsView.exe status | Out-Host</c>) to see it in order.
/// </summary>
internal static class Cli
{
    public static int Run(string[] args)
    {
        Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        switch (args[0].ToLowerInvariant())
        {
            case "status":
                var statuses = DeviceMonitor.Merge(DeviceMonitor.ReadAll(DeviceMonitor.Scan()));
                if (statuses.Count == 0)
                    Console.WriteLine("No supported devices found.");
                foreach (var status in statuses)
                    Console.WriteLine($"{status.Name,-36} {status.Describe()}");
                return 0;

            case "diagnose":
                Diagnose();
                return 0;

            default:
                Console.WriteLine("Usage: PeripheralsView [status | diagnose]");
                return 1;
        }
    }

    private static void Diagnose()
    {
        Console.WriteLine($"Peripherals View {typeof(Cli).Assembly.GetName().Version}, {Environment.OSVersion}, .NET {Environment.Version}");
        Console.WriteLine($"Tray icon size {TrayApp.QueryIconSize()} px, settings HKCU\\Software\\PeripheralsView, log {Log.Folder}");
        Console.WriteLine();
        Console.WriteLine("HID collections (Razer 1532, Logitech 046D, Dell 413C):");
        foreach (string path in HidDevice.EnumeratePaths())
        {
            if (HidDevice.Query(path) is not { } info || info.VendorId is not (RazerDevice.VendorId or LogitechDevice.VendorId or DellReceiver.VendorId))
                continue;
            string? product;
            using (var hid = HidDevice.Open(path, readWrite: false))
                product = hid?.GetProductString();
            Console.WriteLine($"  {info.VendorId:X4}:{info.ProductId:X4}  usage {info.UsagePage:X4}/{info.Usage:X4}  " +
                $"in {info.InputLength,2} out {info.OutputLength,2} feature {info.FeatureLength,2}  {product}");
            Console.WriteLine($"      {path}");
        }

        Console.WriteLine();
        Console.WriteLine("Devices:");
        foreach (var device in DeviceMonitor.Scan().Concat<IBatteryDevice>(BluetoothBattery.Scan()))
        {
            var started = Environment.TickCount64;
            var status = device.Read();
            long elapsed = Environment.TickCount64 - started;
            Console.WriteLine(status is null
                ? $"  (no battery / not answering)  [{elapsed} ms]"
                : $"  {status.Name}: {status.State}, {status.Describe()}, kind {status.Kind}  [{elapsed} ms]");
            Console.WriteLine($"      {device.Details}");
        }
    }
}
