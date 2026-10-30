using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using MirAIe.Core;
using MirAIe.Desktop.Services;

namespace MirAIe.Desktop.ViewModels;

/// <summary>
/// Shell view model: shows the sign-in page until a MirAIe session exists, then the dashboard.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly CredentialStore _credentials = new();
    private readonly ILoggerFactory _loggerFactory =
        LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
    private readonly LoginViewModel _login;

    public MainViewModel()
    {
        _login = new LoginViewModel(_credentials, _loggerFactory, ShowDashboard);
        CurrentPage = _login;
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>
    /// Signs in automatically when a password was remembered.
    /// </summary>
    public Task InitializeAsync() => _login.SignInWithSavedCredentialsAsync();

    public void Dispose()
    {
        (CurrentPage as IDisposable)?.Dispose();
        _loggerFactory.Dispose();
    }

    private void ShowDashboard(MirAIeHub hub) => CurrentPage = new DashboardViewModel(hub, SignOut);

    private void SignOut()
    {
        (CurrentPage as IDisposable)?.Dispose();
        _credentials.ForgetPassword();
        CurrentPage = _login;
    }
}
