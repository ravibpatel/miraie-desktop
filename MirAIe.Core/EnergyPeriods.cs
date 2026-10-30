using System.Globalization;

namespace MirAIe.Core;

/// <summary>
/// Date rules for the MirAIe energy consumption API. Figures for a day are published the following morning.
/// </summary>
public static class EnergyPeriods
{
    /// <summary>
    /// Start of the most recent period that has data: yesterday, the last Sunday before today,
    /// or the month containing yesterday.
    /// </summary>
    public static DateOnly LatestStart(ConsumptionPeriod period, DateOnly today) => period switch
    {
        ConsumptionPeriod.Daily => today.AddDays(-1),
        ConsumptionPeriod.Weekly => today.AddDays(today.DayOfWeek == DayOfWeek.Sunday ? -7 : -(int)today.DayOfWeek),
        ConsumptionPeriod.Monthly => new DateOnly(today.AddDays(-1).Year, today.AddDays(-1).Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    internal static string FormatKey(ConsumptionPeriod period, DateOnly start) =>
        start.ToString(period == ConsumptionPeriod.Monthly ? "MMyyyy" : "ddMMyyyy", CultureInfo.InvariantCulture);
}
