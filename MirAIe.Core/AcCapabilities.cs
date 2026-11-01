namespace MirAIe.Core;

/// <summary>
/// The controls an AC model supports, from its MirAIe model entry.
/// </summary>
public sealed record AcCapabilities
{
    /// <summary>
    /// Every standard control. Used when the model's capabilities can't be read, so nothing the AC might
    /// support is hidden. Nanoe and the 50% and 60% Converti levels aren't included because most models
    /// don't have them.
    /// </summary>
    public static AcCapabilities Default { get; } = new()
    {
        Modes = Enum.GetValues<HvacMode>(),
        FanModes = Enum.GetValues<FanMode>(),
        Presets = Enum.GetValues<PresetMode>(),
        ConvertiModes =
        [
            ConvertiMode.Off, ConvertiMode.C40, ConvertiMode.C55, ConvertiMode.C70, ConvertiMode.C80,
            ConvertiMode.C90, ConvertiMode.FullCapacity, ConvertiMode.HighCapacity,
        ],
        VerticalSwingPositions = [0, 1, 2, 3, 4, 5],
        HorizontalSwingPositions = [0, 1, 2, 3, 4, 5],
        DisplayLight = true,
    };

    /// <summary>Lowest setpoint in °C.</summary>
    public double MinTemperature { get; init; } = 16;

    /// <summary>Highest setpoint in °C.</summary>
    public double MaxTemperature { get; init; } = 30;

    /// <summary>Setpoint increment in °C.</summary>
    public double TemperatureStep { get; init; } = 1;

    public IReadOnlyList<HvacMode> Modes { get; init; } = [];
    public IReadOnlyList<FanMode> FanModes { get; init; } = [];

    /// <summary>Always includes <see cref="PresetMode.None"/>.</summary>
    public IReadOnlyList<PresetMode> Presets { get; init; } = [];

    /// <summary>Capacity levels including <see cref="ConvertiMode.Off"/>; empty when the model has no Converti.</summary>
    public IReadOnlyList<ConvertiMode> ConvertiModes { get; init; } = [];

    /// <summary>Louver positions (0 = auto swing); empty when the louver can't be set.</summary>
    public IReadOnlyList<int> VerticalSwingPositions { get; init; } = [];

    /// <inheritdoc cref="VerticalSwingPositions"/>
    public IReadOnlyList<int> HorizontalSwingPositions { get; init; } = [];

    public bool DisplayLight { get; init; }

    /// <summary>The model's nanoe air purifier, or <c>null</c> when it has none.</summary>
    public NanoeFeature? Nanoe { get; init; }
}

/// <param name="Key">The status and control field for nanoe, usually <c>acng</c>.</param>
public sealed record NanoeFeature(NanoeType Type, string Key)
{
    public IReadOnlyList<NanoeMode> Modes =>
        Type == NanoeType.GA ? [NanoeMode.Off, NanoeMode.On, NanoeMode.Auto] : [NanoeMode.Off, NanoeMode.On];
}
