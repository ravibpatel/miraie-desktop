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

    [Fact]
    public void Nanoe_uses_model_key_and_capability_key_index()
    {
        Assert.Equal("""{"ki":7,"cnt":"an","sid":"1","acng":"on"}""", ControlPayloads.Nanoe(new NanoeFeature(NanoeType.G, "acng"), NanoeMode.On));
        Assert.Equal("""{"ki":23,"cnt":"an","sid":"1","model_key":"off"}""", ControlPayloads.Nanoe(new NanoeFeature(NanoeType.X, "model_key"), NanoeMode.Off));
    }

    [Fact]
    public void Nanoe_ga_is_sent_without_key_index()
    {
        Assert.Equal("""{"cnt":"an","sid":"1","model_key":"auto"}""", ControlPayloads.Nanoe(new NanoeFeature(NanoeType.GA, "model_key"), NanoeMode.Auto));
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

    [Fact]
    public void Nanoe_is_only_read_for_models_that_have_it()
    {
        var payload = Json("""{"acng":"on"}""");

        Assert.Equal(NanoeMode.Off, StatusParser.Apply(new AcStatus(), payload).Nanoe);
        Assert.Equal(NanoeMode.On, StatusParser.Apply(new AcStatus(), payload, "acng").Nanoe);
        Assert.Equal(NanoeMode.Auto, StatusParser.Apply(new AcStatus(), Json("""{"acng":"auto"}"""), "acng").Nanoe);
    }
}

public class CapabilityParserTests
{
    private static AcCapabilities Parse(string json) => CapabilityParser.Parse(JsonDocument.Parse(json).RootElement);

    // Model 130185, as returned by deviceModels/modelId (trimmed to the capabilities the app reads).
    private const string Model130185 = """
        {"modelId":"130185","controlCapabilities":[
         {"capability":"ACFanSpeed","attributes":[{"key":"acfs","values":["auto","quiet","low","medium","high"]}]},
         {"capability":"ACMode","attributes":[{"key":"acmd","values":["auto","cool","dry","fan"]}]},
         {"capability":"ACTemperature","attributes":[{"key":"actmp","range":{"increment":[{"unit":"celcius","value":1},{"unit":"fahrenheit","value":1}],"max":[{"unit":"celcius","value":30},{"unit":"fahrenheit","value":86}],"min":[{"unit":"celcius","value":16},{"unit":"fahrenheit","value":61}]}}]},
         {"capability":"Convertible","attributes":[{"intValues":[110,100,90,80,70,55,40,0],"key":"cnv"}]},
         {"capability":"DisplayControl","attributes":[{"key":"acdc","values":["on","off"]}]},
         {"capability":"ECOMode","attributes":[{"key":"acem","values":["on","off"]}]},
         {"capability":"EvaporatorClean","attributes":[{"key":"acec"}]},
         {"capability":"HorizontalSwing","attributes":[{"key":"achs","range":{"increment":[{"value":1}],"max":[{"value":5}],"min":[{"value":0}]}}]},
         {"capability":"PowerStatus","attributes":[{"key":"ps"}]},
         {"capability":"PowerfulMode","attributes":[{"key":"acpm"}]},
         {"capability":"VerticalSwing","attributes":[{"key":"acvs","range":{"increment":[{"value":1}],"max":[{"value":5}],"min":[{"value":0}]}}]}]}
        """;

    [Fact]
    public void Model_lists_its_controls()
    {
        var capabilities = Parse(Model130185);

        Assert.Equal((16.0, 30.0, 1.0), (capabilities.MinTemperature, capabilities.MaxTemperature, capabilities.TemperatureStep));
        Assert.Equal([HvacMode.Auto, HvacMode.Cool, HvacMode.Dry, HvacMode.Fan], capabilities.Modes);
        Assert.Equal([FanMode.Auto, FanMode.Quiet, FanMode.Low, FanMode.Medium, FanMode.High], capabilities.FanModes);
        Assert.Equal([PresetMode.None, PresetMode.Eco, PresetMode.Boost, PresetMode.Clean], capabilities.Presets);
        Assert.Equal(
            [ConvertiMode.Off, ConvertiMode.HighCapacity, ConvertiMode.FullCapacity, ConvertiMode.C90, ConvertiMode.C80, ConvertiMode.C70, ConvertiMode.C55, ConvertiMode.C40],
            capabilities.ConvertiModes);
        Assert.Equal([0, 1, 2, 3, 4, 5], capabilities.VerticalSwingPositions);
        Assert.Equal([0, 1, 2, 3, 4, 5], capabilities.HorizontalSwingPositions);
        Assert.True(capabilities.DisplayLight);
        Assert.Null(capabilities.Nanoe);
    }

    [Fact]
    public void Unlisted_optional_controls_are_unsupported()
    {
        var capabilities = Parse("""
            {"controlCapabilities":[
             {"capability":"PowerStatus","attributes":[{"key":"ps"}]},
             {"capability":"NanoeG","attributes":[{"key":"acng","values":["on","off"]}]},
             {"capability":"VerticalSwing","attributes":[{"key":"acvs","range":{"max":[{"value":3}],"min":[{"value":0}]}}]}]}
            """);

        Assert.Equal((16.0, 30.0, 1.0), (capabilities.MinTemperature, capabilities.MaxTemperature, capabilities.TemperatureStep));
        Assert.Equal(AcCapabilities.Default.Modes, capabilities.Modes);
        Assert.Equal([PresetMode.None], capabilities.Presets);
        Assert.Empty(capabilities.ConvertiModes);
        Assert.Equal([0, 1, 2, 3], capabilities.VerticalSwingPositions);
        Assert.Empty(capabilities.HorizontalSwingPositions);
        Assert.False(capabilities.DisplayLight);
        Assert.Equal(new NanoeFeature(NanoeType.G, "acng"), capabilities.Nanoe);
    }

    [Fact]
    public void Temperature_range_uses_celsius_entries()
    {
        var capabilities = Parse("""
            {"controlCapabilities":[{"capability":"ACTemperature","attributes":[{"key":"actmp","range":{
             "increment":[{"unit":"fahrenheit","value":1},{"unit":"celcius","value":0.5}],
             "max":[{"unit":"fahrenheit","value":88},{"unit":"celcius","value":31}],
             "min":[{"unit":"fahrenheit","value":62},{"unit":"celcius","value":17}]}}]}]}
            """);

        Assert.Equal((17.0, 31.0, 0.5), (capabilities.MinTemperature, capabilities.MaxTemperature, capabilities.TemperatureStep));
    }

    [Fact]
    public void Nanoe_x_is_recognised()
    {
        var capabilities = Parse("""{"controlCapabilities":[{"capability":"NanoeX","attributes":[{"key":"model_key"}]}]}""");

        Assert.Equal(new NanoeFeature(NanoeType.X, "model_key"), capabilities.Nanoe);
        Assert.Equal([NanoeMode.Off, NanoeMode.On], capabilities.Nanoe!.Modes);
    }

    [Fact]
    public void Nanoe_ga_has_auto()
    {
        var capabilities = Parse("""{"controlCapabilities":[{"capability":"NanoeGA","attributes":[{"key":"model_key","values":["off","on","auto"]}]}]}""");

        Assert.Equal(new NanoeFeature(NanoeType.GA, "model_key"), capabilities.Nanoe);
        Assert.Equal([NanoeMode.Off, NanoeMode.On, NanoeMode.Auto], capabilities.Nanoe!.Modes);
    }

    [Fact]
    public void Converti_50_and_60_are_offered_only_when_listed()
    {
        var capabilities = Parse("""{"controlCapabilities":[{"capability":"Convertible","attributes":[{"key":"cnv","values":["110","60","50","0"]}]}]}""");

        Assert.Equal([ConvertiMode.Off, ConvertiMode.HighCapacity, ConvertiMode.C60, ConvertiMode.C50], capabilities.ConvertiModes);
        Assert.DoesNotContain(ConvertiMode.C50, AcCapabilities.Default.ConvertiModes);
        Assert.DoesNotContain(ConvertiMode.C60, AcCapabilities.Default.ConvertiModes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"controlCapabilities":[]}""")]
    [InlineData("[]")]
    public void Unknown_model_gets_every_standard_control(string json)
    {
        Assert.Same(AcCapabilities.Default, Parse(json));
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
