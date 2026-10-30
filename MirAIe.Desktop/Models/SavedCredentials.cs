namespace MirAIe.Desktop.Models;

/// <param name="Username">The international mobile number sent to MirAIe, e.g. +919876543210.</param>
/// <param name="CountryCode">ISO code of the country picked at sign-in.</param>
public sealed record SavedCredentials(string Username, string? CountryCode, string? Password);
