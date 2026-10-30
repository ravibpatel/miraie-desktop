using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MirAIe.Core;
using MirAIe.Desktop.Models;
using MirAIe.Desktop.Services;

namespace MirAIe.Desktop.ViewModels;

/// <summary>
/// Sign-in with country, mobile number and password, like the MirAIe Android app,
/// or with an email address for accounts registered that way.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly CredentialStore _credentials;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Action<MirAIeHub> _onSignedIn;

    public LoginViewModel(CredentialStore credentials, ILoggerFactory loggerFactory, Action<MirAIeHub> onSignedIn)
    {
        _credentials = credentials;
        _loggerFactory = loggerFactory;
        _onSignedIn = onSignedIn;
    }

    public static IReadOnlyList<Country> Countries => Country.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchSignInMethodText))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool UseEmail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial Country? SelectedCountry { get; set; } = Country.Default;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string MobileNumber { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool RememberMe { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool CanRemember => _credentials.CanRemember;

    public string SwitchSignInMethodText => UseEmail ? "Use mobile number instead" : "Use email instead";

    public async Task SignInWithSavedCredentialsAsync()
    {
        if (_credentials.Load() is not { } saved)
            return;

        if (saved.Username.Contains('@'))
        {
            UseEmail = true;
            Email = saved.Username;
        }
        else if (Country.Find(saved.CountryCode, saved.Username) is { } country
                 && saved.Username.StartsWith(country.DialCode, StringComparison.Ordinal))
        {
            SelectedCountry = country;
            MobileNumber = saved.Username[country.DialCode.Length..];
        }
        else
        {
            MobileNumber = saved.Username;
        }

        if (saved.Password is null)
            return;

        Password = saved.Password;
        await SignInCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void SwitchSignInMethod()
    {
        UseEmail = !UseEmail;
        ErrorMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        if (GetUsername() is not { } username)
        {
            ErrorMessage = UseEmail ? "Enter your email address." : "Enter your mobile number.";
            return;
        }

        try
        {
            var hub = await MirAIeHub.ConnectAsync(username, Password, _loggerFactory);
            if (RememberMe)
                _credentials.Save(username, UseEmail ? null : SelectedCountry?.IsoCode, Password);
            else
                _credentials.Clear();
            Password = "";
            _onSignedIn(hub);
        }
        catch (MirAIeAuthenticationException)
        {
            ErrorMessage = UseEmail
                ? "Sign-in failed. Check your email address and password."
                : "Sign-in failed. Check your country, mobile number and password.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't connect to MirAIe: {ex.Message}";
        }
    }

    private bool CanSignIn() => GetUsername() is not null && !string.IsNullOrEmpty(Password);

    /// <summary>
    /// The value MirAIe expects as the login: the email address, or the mobile number in international format.
    /// </summary>
    private string? GetUsername() => UseEmail ? GetEmail() : GetInternationalNumber();

    private string? GetEmail()
    {
        var email = Email.Trim();
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.Any(char.IsWhiteSpace)
            ? email
            : null;
    }

    /// <summary>
    /// Combines the dial code and the typed number, e.g. "India" + "098765 43210" → "+919876543210".
    /// Returns <c>null</c> until the number looks valid.
    /// </summary>
    private string? GetInternationalNumber()
    {
        if (SelectedCountry is not { } country)
            return null;

        var number = string.Concat(MobileNumber.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')' or '.')));
        if (number.StartsWith(country.DialCode, StringComparison.Ordinal))
            number = number[country.DialCode.Length..];
        number = number.TrimStart('0');

        return number.Length is >= 6 and <= 14 && number.All(char.IsAsciiDigit)
            ? country.DialCode + number
            : null;
    }
}
