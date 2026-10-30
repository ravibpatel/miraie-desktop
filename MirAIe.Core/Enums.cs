namespace MirAIe.Core;

public enum HvacMode
{
    Cool,
    Auto,
    Dry,
    Fan,
    Heat,
}

public enum FanMode
{
    Auto,
    Low,
    Medium,
    High,
    Quiet,
}

public enum PresetMode
{
    None,
    Eco,
    Boost,
    Clean,
}

/// <summary>
/// Converti (capacity) mode. The numeric value is the capacity percentage sent to the AC.
/// </summary>
public enum ConvertiMode
{
    Off = 0,
    NotSupported = 1,
    C40 = 40,
    C55 = 55,
    C70 = 70,
    C80 = 80,
    C90 = 90,
    FullCapacity = 100,
    HighCapacity = 110,
}

public enum ConsumptionPeriod
{
    Daily,
    Weekly,
    Monthly,
}

public enum BrokerState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// Maps enums to and from the string values used in MirAIe payloads.
/// </summary>
internal static class WireValues
{
    public static string From(HvacMode mode) => mode switch
    {
        HvacMode.Cool => "cool",
        HvacMode.Auto => "auto",
        HvacMode.Dry => "dry",
        HvacMode.Fan => "fan",
        HvacMode.Heat => "heat",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string From(FanMode mode) => mode switch
    {
        FanMode.Auto => "auto",
        FanMode.Low => "low",
        FanMode.Medium => "medium",
        FanMode.High => "high",
        FanMode.Quiet => "quiet",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string OnOff(bool on) => on ? "on" : "off";

    public static HvacMode? ToHvacMode(string? value) => value switch
    {
        "cool" => HvacMode.Cool,
        "auto" => HvacMode.Auto,
        "dry" => HvacMode.Dry,
        "fan" => HvacMode.Fan,
        "heat" => HvacMode.Heat,
        _ => null,
    };

    public static FanMode? ToFanMode(string? value) => value switch
    {
        "auto" => FanMode.Auto,
        "low" => FanMode.Low,
        "medium" => FanMode.Medium,
        "high" => FanMode.High,
        "quiet" => FanMode.Quiet,
        _ => null,
    };

    public static string ResponseKey(ConsumptionPeriod period) => period switch
    {
        ConsumptionPeriod.Daily => "day",
        ConsumptionPeriod.Weekly => "week",
        ConsumptionPeriod.Monthly => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };
}
