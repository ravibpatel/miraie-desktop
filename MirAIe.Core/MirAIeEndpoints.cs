namespace MirAIe.Core;

/// <summary>
/// Cloud endpoints and client identifiers used by the Panasonic MirAIe mobile app.
/// </summary>
internal static class MirAIeEndpoints
{
    public const string ClientId = "PBcMcfG19njNCL8AOgvRzIC8AjQa";
    public const string Scope = "an_14214235325";

    public const string Login = "https://auth.miraie.in/simplifi/v1/userManagement/login";
    public const string Homes = "https://app.miraie.in/simplifi/v1/homeManagement/homes";

    public const string MqttHost = "mqtt.miraie.in";
    public const int MqttPort = 8883;

    // The literal "deviceId" path segment is part of the real URL; the ids follow it.
    public static string DeviceDetails(IEnumerable<string> deviceIds) =>
        "https://app.miraie.in/simplifi/v1/deviceManagement/devices/deviceId/"
        + string.Join(",", deviceIds.Select(Uri.EscapeDataString));

    // Likewise "modelId" is a literal segment.
    public static string DeviceModel(string modelId) =>
        $"https://app.miraie.in/simplifi/v1/deviceManagement/deviceModels/modelId/{Uri.EscapeDataString(modelId)}";

    public static string DeviceStatus(string deviceId) =>
        $"https://app.miraie.in/simplifi/v1/deviceManagement/devices/{Uri.EscapeDataString(deviceId)}/mobile/status";

    public static string EnergyConsumption(string deviceId, ConsumptionPeriod period, string fromDate, string toDate) =>
        $"https://app.miraie.in/simplifi/v1/powerConsumption/devices/{Uri.EscapeDataString(deviceId)}"
        + $"?grain={period}&startDate={fromDate}&endDate={toDate}";
}
