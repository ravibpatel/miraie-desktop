using System.Text.Json;

namespace MirAIe.Core;

/// <summary>
/// Turns a MirAIe <c>deviceModels</c> response into <see cref="AcCapabilities"/>. Each entry in its
/// <c>controlCapabilities</c> names a control; the first attribute holds the field key and allowed values.
/// </summary>
internal static class CapabilityParser
{
    public static AcCapabilities Parse(JsonElement model)
    {
        var controls = new Dictionary<string, JsonElement>();
        foreach (var capability in model.EnumerateArrayOrEmpty("controlCapabilities"))
        {
            if (capability.GetText("capability") is { } name)
                controls.TryAdd(name, capability.EnumerateArrayOrEmpty("attributes").FirstOrDefault());
        }

        // Without a capability list nothing is known about the model.
        if (controls.Count == 0)
            return AcCapabilities.Default;

        var presets = new List<PresetMode> { PresetMode.None };
        if (controls.ContainsKey("ECOMode"))
            presets.Add(PresetMode.Eco);
        if (controls.ContainsKey("PowerfulMode"))
            presets.Add(PresetMode.Boost);
        if (controls.ContainsKey("EvaporatorClean"))
            presets.Add(PresetMode.Clean);

        var (minTemperature, maxTemperature, temperatureStep) = TemperatureRange(controls.GetValueOrDefault("ACTemperature"));

        return new AcCapabilities
        {
            MinTemperature = minTemperature,
            MaxTemperature = maxTemperature,
            TemperatureStep = temperatureStep,
            // Mode and fan speed are core controls, so a model that doesn't list their values gets them all.
            Modes = Values(controls.GetValueOrDefault("ACMode"), WireValues.ToHvacMode, AcCapabilities.Default.Modes),
            FanModes = Values(controls.GetValueOrDefault("ACFanSpeed"), WireValues.ToFanMode, AcCapabilities.Default.FanModes),
            Presets = presets,
            ConvertiModes = controls.TryGetValue("Convertible", out var converti) ? ConvertiLevels(converti) : [],
            VerticalSwingPositions = controls.TryGetValue("VerticalSwing", out var vertical) ? SwingPositions(vertical) : [],
            HorizontalSwingPositions = controls.TryGetValue("HorizontalSwing", out var horizontal) ? SwingPositions(horizontal) : [],
            DisplayLight = controls.ContainsKey("DisplayControl"),
            Nanoe = controls.TryGetValue("NanoeG", out var nanoeG) ? new NanoeFeature(NanoeType.G, NanoeKey(nanoeG))
                : controls.TryGetValue("NanoeX", out var nanoeX) ? new NanoeFeature(NanoeType.X, NanoeKey(nanoeX))
                : controls.TryGetValue("NanoeGA", out var nanoeGA) ? new NanoeFeature(NanoeType.GA, NanoeKey(nanoeGA))
                : null,
        };
    }

    /// <summary>The listed values the app knows, or <paramref name="all"/> when the model lists none.</summary>
    private static IReadOnlyList<T> Values<T>(JsonElement attribute, Func<string?, T?> parse, IReadOnlyList<T> all)
        where T : struct
    {
        var listed = attribute.EnumerateArrayOrEmpty("values").Select(v => v.AsText()).ToList();
        return listed.Count == 0 ? all : [.. listed.Select(parse).OfType<T>().Distinct()];
    }

    /// <summary>The listed capacity levels the app knows, plus Off; every level when the model lists none.</summary>
    private static IReadOnlyList<ConvertiMode> ConvertiLevels(JsonElement attribute)
    {
        // Levels arrive as numbers in "intValues" or as strings in "values".
        var listed = attribute.EnumerateArrayOrEmpty("intValues")
            .Concat(attribute.EnumerateArrayOrEmpty("values"))
            .Select(v => v.AsDecimal())
            .OfType<decimal>()
            .Select(v => (ConvertiMode)(int)v)
            .ToList();
        if (listed.Count == 0)
            return AcCapabilities.Default.ConvertiModes;

        return [ConvertiMode.Off, .. listed.Where(m => m is not (ConvertiMode.Off or ConvertiMode.NotSupported) && Enum.IsDefined(m)).Distinct()];
    }

    /// <summary>The model's setpoint range, or the standard one when the model gives none.</summary>
    private static (double Min, double Max, double Step) TemperatureRange(JsonElement attribute)
    {
        var defaults = AcCapabilities.Default;
        if (RangeBound(attribute, "min") is not { } min || RangeBound(attribute, "max") is not { } max || min >= max)
            return (defaults.MinTemperature, defaults.MaxTemperature, defaults.TemperatureStep);

        return (min, max, RangeBound(attribute, "increment") is double step and > 0 ? step : defaults.TemperatureStep);
    }

    /// <summary>Positions within the model's range, limited to the 0–5 the app supports.</summary>
    private static IReadOnlyList<int> SwingPositions(JsonElement attribute)
    {
        var min = RangeBound(attribute, "min") ?? 0;
        var max = RangeBound(attribute, "max") ?? 5;
        return [.. AcCapabilities.Default.VerticalSwingPositions.Where(p => p >= min && p <= max)];
    }

    /// <summary>
    /// Reads <c>range.{bound}</c>. Temperatures are listed per unit, so the Celsius entry is preferred
    /// (MirAIe spells it "celcius").
    /// </summary>
    private static double? RangeBound(JsonElement attribute, string bound)
    {
        if (attribute.ValueKind != JsonValueKind.Object || !attribute.TryGetProperty("range", out var range))
            return null;

        var entries = range.EnumerateArrayOrEmpty(bound).ToList();
        var entry = entries
            .Where(e => e.GetText("unit")?.StartsWith("cel", StringComparison.OrdinalIgnoreCase) == true)
            .Concat(entries)
            .FirstOrDefault();
        return (double?)entry.GetDecimal("value");
    }

    private static string NanoeKey(JsonElement attribute) =>
        attribute.GetText("key") is { Length: > 0 } key ? key : "acng";
}
