using System;
using System.Collections.Generic;
using System.Linq;

namespace MirAIe.Desktop.Models;

/// <summary>
/// A country in the sign-in picker. MirAIe identifies accounts by the mobile number in
/// international format, e.g. +919876543210.
/// </summary>
public sealed record Country(string IsoCode, string Name, string DialCode)
{
    public static IReadOnlyList<Country> All { get; } =
    [
        new("AU", "Australia", "+61"),
        new("BH", "Bahrain", "+973"),
        new("BD", "Bangladesh", "+880"),
        new("BT", "Bhutan", "+975"),
        new("CA", "Canada", "+1"),
        new("CN", "China", "+86"),
        new("FR", "France", "+33"),
        new("DE", "Germany", "+49"),
        new("HK", "Hong Kong", "+852"),
        new("IN", "India", "+91"),
        new("ID", "Indonesia", "+62"),
        new("IE", "Ireland", "+353"),
        new("IT", "Italy", "+39"),
        new("JP", "Japan", "+81"),
        new("KE", "Kenya", "+254"),
        new("KW", "Kuwait", "+965"),
        new("MY", "Malaysia", "+60"),
        new("MV", "Maldives", "+960"),
        new("MU", "Mauritius", "+230"),
        new("MM", "Myanmar", "+95"),
        new("NP", "Nepal", "+977"),
        new("NL", "Netherlands", "+31"),
        new("NZ", "New Zealand", "+64"),
        new("NG", "Nigeria", "+234"),
        new("OM", "Oman", "+968"),
        new("PK", "Pakistan", "+92"),
        new("PH", "Philippines", "+63"),
        new("QA", "Qatar", "+974"),
        new("SA", "Saudi Arabia", "+966"),
        new("SG", "Singapore", "+65"),
        new("ZA", "South Africa", "+27"),
        new("KR", "South Korea", "+82"),
        new("ES", "Spain", "+34"),
        new("LK", "Sri Lanka", "+94"),
        new("CH", "Switzerland", "+41"),
        new("TW", "Taiwan", "+886"),
        new("TZ", "Tanzania", "+255"),
        new("TH", "Thailand", "+66"),
        new("AE", "United Arab Emirates", "+971"),
        new("GB", "United Kingdom", "+44"),
        new("US", "United States", "+1"),
        new("VN", "Vietnam", "+84"),
    ];

    public static Country Default { get; } = All.First(c => c.IsoCode == "IN");

    /// <summary>
    /// Finds the country of a saved number, preferring the remembered ISO code because some
    /// countries share a dial code (e.g. +1).
    /// </summary>
    public static Country? Find(string? isoCode, string internationalNumber) =>
        All.FirstOrDefault(c => c.IsoCode == isoCode)
        ?? All.Where(c => internationalNumber.StartsWith(c.DialCode, StringComparison.Ordinal))
              .MaxBy(c => c.DialCode.Length);

    // Shown by the ComboBox.
    public override string ToString() => $"{Name} ({DialCode})";
}
