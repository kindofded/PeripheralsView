# Peripherals View

Shows the battery level of your wireless Razer mice and Logitech keyboards/mice in the Windows system tray – one icon per device. It talks to the devices directly: no Razer Synapse or Logitech G HUB needed.

Built to be as light as possible: plain Win32 (no WinForms/WPF), one background thread that sleeps between polls, ~7 MB private memory and effectively 0% CPU (≈30 ms of CPU time per minute-long poll cycle). The exe is ~240 KB.

![Tray icons](docs/tray.png) ![Percentage mode](docs/tray-percent.png)

## Icons

| Icon | Meaning |
| --- | --- |
| Mouse / keyboard / battery outline, filled | The fill is the battery level. Hover for the exact percentage. |
| Yellow | At or below your low-battery level (default 15%). |
| Red | 5% or less. |
| Lightning bolt | Charging. |
| Moon | The mouse is asleep and isn't answering (last known level shown). |
| Crossed out | Offline, or no supported device found. |

**Show percentage on icons** replaces the glyph with the number (green while charging).

New tray icons start in the overflow (^). Drag them onto the taskbar, or turn them on in *Settings > Personalization > Taskbar > Other system tray icons*. Each device keeps the same icon ID, so Windows remembers your choice.

## Menu

Right- or left-click any icon: device list with levels, **Show percentage on icons**, **Battery alerts** (low, critical and fully-charged notifications), **Low battery level** (10–25%), **Start with Windows**, **Refresh now**, **Exit**.

## Supported devices

- **Razer** – the wireless mice from openrazer's list (see `RazerModel` table in `Razer.cs`), on cable or receiver. Uses the 90-byte Razer feature report (class 0x07, commands 0x80 battery / 0x84 charging).
- **Logitech** – any HID++ 2.0 device with a battery feature (0x1004 unified battery, 0x1000 battery status or 0x1001 battery voltage), over Bluetooth, USB cable or a Lightspeed/Unifying/Bolt receiver. Tested with a **G915** over Bluetooth (voltage-only: the percentage is estimated from the voltage with Solaar's discharge curve).

## Command line

```powershell
.\PeripheralsView.exe status   | Out-Host   # battery of each device
.\PeripheralsView.exe diagnose | Out-Host   # every Razer/Logitech HID collection + protocol details
```

It's a GUI exe, so PowerShell doesn't wait for it – pipe to `Out-Host` to get the output in order.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build, the .NET 10 Desktop or base runtime to run.

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
.\publish\PeripheralsView.exe
```

The code is NativeAOT-compatible: with the Visual Studio C++ build tools installed, `-p:PublishAot=true` gives a self-contained native exe with no .NET runtime dependency.

Only one instance runs at a time. Settings are stored in `HKCU\Software\PeripheralsView`; errors are logged to `%LOCALAPPDATA%\PeripheralsView\errors.log`.

## Credits

- [openrazer](https://github.com/openrazer/openrazer) – Razer protocol and device IDs.
- [Solaar](https://github.com/pwr-Solaar/Solaar) – Logitech HID++ protocol and the voltage curve.
- Inspired by [razer-battery-report](https://github.com/xzeldon/razer-battery-report).
