using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirAIe.Core;

namespace MirAIe.Desktop.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly MirAIeHub _hub;
    private readonly Action _onSignOut;

    public DashboardViewModel(MirAIeHub hub, Action onSignOut)
    {
        _hub = hub;
        _onSignOut = onSignOut;
        Devices = hub.Devices.Select(device => new AirConditionerViewModel(hub, device)).ToList();
        SelectedDevice = Devices.FirstOrDefault();

        hub.BrokerStateChanged += OnBrokerStateChanged;
        UpdateConnection(hub.BrokerState);
    }

    public IReadOnlyList<AirConditionerViewModel> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    [ObservableProperty]
    public partial AirConditionerViewModel? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "";

    public void Dispose()
    {
        _hub.BrokerStateChanged -= OnBrokerStateChanged;
        foreach (var device in Devices)
            device.Dispose();
        _hub.Dispose();
    }

    partial void OnSelectedDeviceChanged(AirConditionerViewModel? value) => value?.LoadEnergyIfNeeded();

    [RelayCommand]
    private Task RefreshAsync() => _hub.RefreshStatusesAsync();

    [RelayCommand]
    private void SignOut() => _onSignOut();

    private void OnBrokerStateChanged(object? sender, BrokerState state) =>
        Dispatcher.UIThread.Post(() => UpdateConnection(state));

    private void UpdateConnection(BrokerState state)
    {
        IsConnected = state == BrokerState.Connected;
        ConnectionText = state switch
        {
            BrokerState.Connected => "Live updates on",
            BrokerState.Connecting => "Connecting…",
            BrokerState.Reconnecting => "Connection lost, retrying…",
            _ => "Disconnected",
        };
    }
}
