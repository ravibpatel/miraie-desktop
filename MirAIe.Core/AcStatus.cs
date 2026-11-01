namespace MirAIe.Core;

/// <summary>
/// Last known state of an air conditioner.
/// </summary>
public sealed record AcStatus
{
    public bool IsOnline { get; init; }
    public bool IsOn { get; init; }
    public double TargetTemperature { get; init; } = 24;

    /// <summary>Room temperature in °C, or <c>null</c> when the AC has not reported one.</summary>
    public double? RoomTemperature { get; init; }

    public HvacMode Mode { get; init; } = HvacMode.Auto;
    public FanMode FanMode { get; init; } = FanMode.Auto;
    public PresetMode Preset { get; init; } = PresetMode.None;
    public ConvertiMode Converti { get; init; } = ConvertiMode.Off;

    /// <summary>Vertical louver position: 0 = auto swing, 1–5 = fixed positions.</summary>
    public int VerticalSwing { get; init; }

    /// <summary>Horizontal louver position: 0 = auto swing, 1–5 = fixed positions.</summary>
    public int HorizontalSwing { get; init; }

    public bool DisplayOn { get; init; } = true;

    /// <summary>The nanoe air purifier's setting. Only tracked for models that have one.</summary>
    public NanoeMode Nanoe { get; init; }
}

public sealed record AcDetails(
    string? Brand,
    string? ModelName,
    string? ModelNumber,
    string? FirmwareVersion,
    string? SerialNumber,
    string? MacAddress);
