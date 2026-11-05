namespace PeripheralsView;

internal enum DeviceKind
{
    Mouse,
    Keyboard,
    Other,
}

/// <summary>Ordered best-first: when the same device shows up twice (e.g. cable and receiver) the lower value wins.</summary>
internal enum DeviceState
{
    Online,
    Asleep,
    Offline,
}

/// <param name="Level">Battery percentage 0–100, or -1 when unknown.</param>
internal sealed record DeviceStatus(string Id, string Name, DeviceKind Kind, DeviceState State, int Level, bool Charging, bool Full)
{
    public string Describe() => State switch
    {
        DeviceState.Offline => "Offline",
        DeviceState.Asleep => Level >= 0 ? $"Asleep · {Level}%" : "Asleep",
        _ when Level < 0 => "Connected",
        _ when Full => $"{Level}% · fully charged",
        _ when Charging => $"{Level}% · charging",
        _ => $"{Level}%",
    };
}

internal interface IBatteryDevice
{
    /// <summary>Reads the battery. Never throws; returns null if this isn't (or isn't yet known to be) a battery device.</summary>
    DeviceStatus? Read();

    /// <summary>Protocol details for <c>diagnose</c>.</summary>
    string Details { get; }
}
