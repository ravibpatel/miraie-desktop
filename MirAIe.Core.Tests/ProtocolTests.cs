using System.Text.Json;
using MirAIe.Core;

namespace MirAIe.Core.Tests;

public class ControlPayloadTests
{
    [Fact]
    public void Power_includes_base_fields()
    {
        Assert.Equal("""{"ki":1,"cnt":"an","sid":"1","ps":"on"}""", ControlPayloads.Power(true));
    }

    [Theory]
    [InlineData(24, "24.0")]
    [InlineData(24.5, "24.5")]
    public void Temperature_is_sent_as_string_with_one_decimal(double temperature, string expected)
    {
        Assert.Equal($$"""{"ki":1,"cnt":"an","sid":"1","actmp":"{{expected}}"}""", ControlPayloads.Temperature(temperature));
    }

    [Fact]
    public void Eco_preset_sets_flags_and_26_degrees()
    {
        Assert.Equal(
            """{"ki":1,"cnt":"an","sid":"1","acem":"on","acpm":"off","acec":"off","actmp":26.0,"cnv":0}""",
            ControlPayloads.Preset(PresetMode.Eco));
    }

    [Fact]
    public void Swing_and_converti_are_numbers()
    {
        Assert.Equal("""{"ki":1,"cnt":"an","sid":"1","acvs":3}""", ControlPayloads.VerticalSwing(3));
        Assert.Equal("""{"ki":1,"cnt":"an","sid":"1","acem":"off","acpm":"off","cnv":90}""", ControlPayloads.Converti(ConvertiMode.C90));
    }

    [Fact]
    public void Mode_and_fan_use_lowercase_names()
    {
        Assert.Equal("""{"ki":1,"cnt":"an","sid":"1","acmd":"fan"}""", ControlPayloads.Mode(HvacMode.Fan));
        Assert.Equal("""{"ki":1,"cnt":"an","sid":"1","acfs":"quiet"}""", ControlPayloads.Fan(FanMode.Quiet));
    }
}

public class StatusParserTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("27.0", 27.0)]
    [InlineData("26.5", 26.5)]
    [InlineData("150.27", 27.0)]
    [InlineData("2.29", 29.0)]
    [InlineData("61.26", 26.0)]
    public void Room_temperature_is_decoded(string raw, double expected)
    {
        Assert.Equal(expected, StatusParser.DecodeRoomTemperature(raw));
    }

    [Fact]
    public void Unreadable_room_temperature_is_null()
    {
        Assert.Null(StatusParser.DecodeRoomTemperature("n/a"));
    }

    [Fact]
    public void Cloud_status_is_parsed()
    {
        var status = StatusParser.FromCloudStatus(new AcStatus(), Json("""
            {"ty":"AC","onlineStatus":"true","actmp":"23.0","rmtmp":"28.5","ps":"on","acfs":"high",
             "acvs":2,"achs":0,"acdc":"off","acmd":"cool","acpm":"on","acem":"off","acec":"off","cnv":90}
            """));

        Assert.Equal(new AcStatus
        {
            IsOnline = true,
            IsOn = true,
            TargetTemperature = 23,
            RoomTemperature = 28.5,
            Mode = HvacMode.Cool,
            FanMode = FanMode.High,
            Preset = PresetMode.Boost,
            Converti = ConvertiMode.C90,
            VerticalSwing = 2,
            HorizontalSwing = 0,
            DisplayOn = false,
        }, status);
    }

    [Fact]
    public void Non_ac_cloud_status_means_offline()
    {
        var current = new AcStatus { IsOnline = true, TargetTemperature = 21 };

        var status = StatusParser.FromCloudStatus(current, Json("""{"onlineStatus":"false"}"""));

        Assert.Equal(current with { IsOnline = false }, status);
    }

    [Fact]
    public void Partial_message_keeps_other_fields()
    {
        var current = new AcStatus { IsOnline = true, IsOn = true, Mode = HvacMode.Dry, TargetTemperature = 25 };

        var status = StatusParser.Apply(current, Json("""{"actmp":"22.0","acec":"on","acem":"off","acpm":"off"}"""));

        Assert.Equal(current with { TargetTemperature = 22, Preset = PresetMode.Clean }, status);
    }

    [Fact]
    public void Unknown_values_are_ignored()
    {
        var current = new AcStatus { Mode = HvacMode.Heat, Converti = ConvertiMode.C55 };

        var status = StatusParser.Apply(current, Json("""{"acmd":"turbo","cnv":12}"""));

        Assert.Equal(current, status);
    }
}

public class EnergyPeriodTests
{
    [Theory]
    [InlineData("2026-10-29", "2026-10-25")] // Thursday -> previous Sunday
    [InlineData("2026-10-26", "2026-10-25")] // Monday -> yesterday
    [InlineData("2026-10-25", "2026-10-18")] // Sunday -> the Sunday before
    public void Weekly_period_starts_on_last_sunday_before_today(string today, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), EnergyPeriods.LatestStart(ConsumptionPeriod.Weekly, DateOnly.Parse(today)));
    }

    [Fact]
    public void Monthly_period_is_month_of_yesterday()
    {
        var start = EnergyPeriods.LatestStart(ConsumptionPeriod.Monthly, new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 10, 1), start);
        Assert.Equal("102026", EnergyPeriods.FormatKey(ConsumptionPeriod.Monthly, start));
    }

    [Fact]
    public void Daily_period_is_yesterday_formatted_ddMMyyyy()
    {
        var start = EnergyPeriods.LatestStart(ConsumptionPeriod.Daily, new DateOnly(2026, 10, 1));

        Assert.Equal("30092026", EnergyPeriods.FormatKey(ConsumptionPeriod.Daily, start));
    }
}
