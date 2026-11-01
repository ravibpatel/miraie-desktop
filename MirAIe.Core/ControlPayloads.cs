using System.Globalization;
using System.Text.Json.Nodes;

namespace MirAIe.Core;

/// <summary>
/// Builds the JSON commands published to a device's <c>/control</c> topic.
/// </summary>
internal static class ControlPayloads
{
    public static string Power(bool on) => Build(p => p["ps"] = WireValues.OnOff(on));

    public static string Temperature(double temperature) =>
        Build(p => p["actmp"] = temperature.ToString("0.0", CultureInfo.InvariantCulture));

    public static string Mode(HvacMode mode) => Build(p => p["acmd"] = WireValues.From(mode));

    public static string Fan(FanMode mode) => Build(p => p["acfs"] = WireValues.From(mode));

    public static string VerticalSwing(int position) => Build(p => p["acvs"] = position);

    public static string HorizontalSwing(int position) => Build(p => p["achs"] = position);

    public static string Display(bool on) => Build(p => p["acdc"] = WireValues.OnOff(on));

    // The official app sends the capability's key index here rather than 1, and none for nanoe GA.
    public static string Nanoe(NanoeFeature nanoe, NanoeMode mode) => Build(p =>
    {
        if (nanoe.Type == NanoeType.GA)
            p.Remove("ki");
        else
            p["ki"] = nanoe.Type == NanoeType.X ? 23 : 7;
        p[nanoe.Key] = WireValues.From(mode);
    });

    public static string Preset(PresetMode preset) => Build(p =>
    {
        p["acem"] = WireValues.OnOff(preset == PresetMode.Eco);
        p["acpm"] = WireValues.OnOff(preset == PresetMode.Boost);
        p["acec"] = WireValues.OnOff(preset == PresetMode.Clean);
        if (preset == PresetMode.Eco)
            p["actmp"] = 26.0m; // decimal keeps the ".0", matching the official payload
        p["cnv"] = 0;
    });

    public static string Converti(ConvertiMode mode) => Build(p =>
    {
        p["acem"] = "off";
        p["acpm"] = "off";
        p["cnv"] = (int)mode;
    });

    private static string Build(Action<JsonObject> fill)
    {
        var payload = new JsonObject
        {
            ["ki"] = 1,
            ["cnt"] = "an",
            ["sid"] = "1",
        };
        fill(payload);
        return payload.ToJsonString();
    }
}
