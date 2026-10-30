using System.Globalization;
using System.Text.Json;

namespace MirAIe.Core;

/// <summary>
/// Lenient readers for MirAIe JSON, which mixes strings and numbers for the same fields.
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetText(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static decimal? GetDecimal(this JsonElement element, string propertyName) =>
        decimal.TryParse(element.GetText(propertyName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement element, string? propertyName = null)
    {
        if (propertyName is not null && !(element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out element)))
            return [];

        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
    }
}
