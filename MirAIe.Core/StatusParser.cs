using System.Globalization;
using System.Text.Json;

namespace MirAIe.Core;

/// <summary>
/// Turns MirAIe status payloads (REST and MQTT) into <see cref="AcStatus"/> values.
/// </summary>
internal static class StatusParser
{
    /// <summary>
    /// Parses the REST status response. Anything other than an AC payload means the device is unreachable.
    /// </summary>
    public static AcStatus FromCloudStatus(AcStatus current, JsonElement payload, string? nanoeKey = null)
    {
        if (payload.GetText("ty") != "AC")
            return current with { IsOnline = false };

        return Apply(current, payload, nanoeKey) with { IsOnline = payload.GetText("onlineStatus") == "true" };
    }

    /// <summary>
    /// Applies the fields present in <paramref name="payload"/> on top of <paramref name="current"/>.
    /// </summary>
    /// <param name="nanoeKey">
    /// The model's nanoe field. Models without nanoe report one too, so it's only read when given.
    /// </param>
    public static AcStatus Apply(AcStatus current, JsonElement payload, string? nanoeKey = null)
    {
        var status = current;

        if (payload.GetDecimal("actmp") is { } target)
            status = status with { TargetTemperature = (double)target };

        if (payload.GetText("rmtmp") is { } room)
            status = status with { RoomTemperature = DecodeRoomTemperature(room) };

        if (payload.GetText("ps") is { } power)
            status = status with { IsOn = power == "on" };

        if (WireValues.ToHvacMode(payload.GetText("acmd")) is { } mode)
            status = status with { Mode = mode };

        if (WireValues.ToFanMode(payload.GetText("acfs")) is { } fan)
            status = status with { FanMode = fan };

        if (payload.GetDecimal("acvs") is { } vertical)
            status = status with { VerticalSwing = (int)vertical };

        if (payload.GetDecimal("achs") is { } horizontal)
            status = status with { HorizontalSwing = (int)horizontal };

        if (payload.GetText("acdc") is { } display)
            status = status with { DisplayOn = display == "on" };

        if (nanoeKey is not null && WireValues.ToNanoeMode(payload.GetText(nanoeKey)) is { } nanoe)
            status = status with { Nanoe = nanoe };

        var boost = payload.GetText("acpm");
        var eco = payload.GetText("acem");
        var clean = payload.GetText("acec");
        if (boost is not null || eco is not null || clean is not null)
        {
            status = status with
            {
                Preset = boost == "on" ? PresetMode.Boost
                    : eco == "on" ? PresetMode.Eco
                    : clean == "on" ? PresetMode.Clean
                    : PresetMode.None,
            };
        }

        if (payload.GetDecimal("cnv") is { } converti && Enum.IsDefined((ConvertiMode)(int)converti))
            status = status with { Converti = (ConvertiMode)(int)converti };

        if (payload.GetText("onlineStatus") is { } online)
            status = status with { IsOnline = online == "true" };

        return status;
    }

    /// <summary>
    /// Panasonic ACs report room temperature in 0.5° steps. Some newer firmware prefixes the value with
    /// unrelated data (e.g. 150.27, 2.29, 61.26); in that case the two decimals are the temperature.
    /// </summary>
    public static double? DecodeRoomTemperature(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature))
            return null;

        var fraction = temperature - decimal.Truncate(temperature);
        if (fraction != 0m && fraction != 0.5m)
            return (double)Math.Round(fraction * 100m);

        return (double)temperature;
    }
}
