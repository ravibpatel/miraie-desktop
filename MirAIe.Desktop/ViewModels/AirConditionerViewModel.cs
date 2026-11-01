using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirAIe.Core;

namespace MirAIe.Desktop.ViewModels;

/// <summary>
/// Controls one AC. Property changes made by the user are sent to the AC; changes reported by the AC are
/// applied without echoing them back.
/// </summary>
public partial class AirConditionerViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan TemperatureDebounce = TimeSpan.FromMilliseconds(600);

    private readonly MirAIeHub _hub;
    private readonly AirConditioner _device;
    private bool _applyingStatus;
    private double? _pendingTemperature;
    private CancellationTokenSource? _temperatureDelay;
    private bool _energyLoaded;

    private static readonly IReadOnlyList<Option<HvacMode>> AllModes =
    [
        new(HvacMode.Cool, "Cool"),
        new(HvacMode.Auto, "Auto"),
        new(HvacMode.Dry, "Dry"),
        new(HvacMode.Fan, "Fan"),
        new(HvacMode.Heat, "Heat"),
    ];

    private static readonly IReadOnlyList<Option<FanMode>> AllFanModes =
    [
        new(FanMode.Auto, "Auto"),
        new(FanMode.Quiet, "Quiet"),
        new(FanMode.Low, "Low"),
        new(FanMode.Medium, "Medium"),
        new(FanMode.High, "High"),
    ];

    private static readonly IReadOnlyList<Option<PresetMode>> AllPresets =
    [
        new(PresetMode.None, "Normal"),
        new(PresetMode.Eco, "Eco"),
        new(PresetMode.Boost, "Boost"),
        new(PresetMode.Clean, "Clean"),
    ];

    private static readonly IReadOnlyList<Option<ConvertiMode>> AllConvertiModes =
    [
        new(ConvertiMode.Off, "Off"),
        new(ConvertiMode.HighCapacity, "110%"),
        new(ConvertiMode.FullCapacity, "100%"),
        new(ConvertiMode.C90, "90%"),
        new(ConvertiMode.C80, "80%"),
        new(ConvertiMode.C70, "70%"),
        new(ConvertiMode.C60, "60%"),
        new(ConvertiMode.C55, "55%"),
        new(ConvertiMode.C50, "50%"),
        new(ConvertiMode.C40, "40%"),
    ];

    private static readonly IReadOnlyList<Option<int>> AllSwingPositions =
        [new(0, "Auto"), .. Enumerable.Range(1, 5).Select(i => new Option<int>(i, i.ToString()))];

    private static readonly IReadOnlyList<Option<NanoeMode>> AllNanoeModes =
    [
        new(NanoeMode.Off, "Off"),
        new(NanoeMode.On, "On"),
        new(NanoeMode.Auto, "Auto"),
    ];

    public AirConditionerViewModel(MirAIeHub hub, AirConditioner device)
    {
        _hub = hub;
        _device = device;

        var capabilities = device.Capabilities;
        Modes = Supported(AllModes, capabilities.Modes);
        FanModes = Supported(AllFanModes, capabilities.FanModes);
        Presets = Supported(AllPresets, capabilities.Presets);
        ConvertiModes = Supported(AllConvertiModes, capabilities.ConvertiModes);
        VerticalSwingPositions = Supported(AllSwingPositions, capabilities.VerticalSwingPositions);
        HorizontalSwingPositions = Supported(AllSwingPositions, capabilities.HorizontalSwingPositions);
        NanoeModes = Supported(AllNanoeModes, capabilities.Nanoe?.Modes ?? []);

        ApplyStatus(device.Status);
        device.StatusChanged += OnStatusChanged;
    }

    // Only the options the AC's model supports.
    public IReadOnlyList<Option<HvacMode>> Modes { get; }
    public IReadOnlyList<Option<FanMode>> FanModes { get; }
    public IReadOnlyList<Option<PresetMode>> Presets { get; }
    public IReadOnlyList<Option<ConvertiMode>> ConvertiModes { get; }
    public IReadOnlyList<Option<int>> VerticalSwingPositions { get; }
    public IReadOnlyList<Option<int>> HorizontalSwingPositions { get; }
    public IReadOnlyList<Option<NanoeMode>> NanoeModes { get; }

    // A list with a single option ("Normal", "Off" or "Auto") offers no choice, so it's hidden.
    public bool SupportsPresets => Presets.Count > 1;
    public bool SupportsVerticalSwing => VerticalSwingPositions.Count > 1;
    public bool SupportsHorizontalSwing => HorizontalSwingPositions.Count > 1;
    public bool SupportsDisplayLight => _device.Capabilities.DisplayLight;

    // nanoe G and X are on or off, so they get a switch; nanoe GA also has Auto, so it gets a choice.
    public bool SupportsNanoeSwitch => NanoeModes.Count == 2;
    public bool SupportsNanoeModes => NanoeModes.Count > 2;

    public string NanoeLabel => _device.Capabilities.Nanoe?.Type switch
    {
        NanoeType.X => "nanoe™ X",
        NanoeType.GA => "nanoe™ GA",
        _ => "nanoe™ G",
    };

    public string Name => _device.Name;

    public string Description
    {
        get
        {
            var details = _device.Details;
            var model = string.Join(" ", new[] { details?.Brand, details?.ModelNumber ?? details?.ModelName }.Where(s => !string.IsNullOrEmpty(s)));
            var firmware = string.IsNullOrEmpty(details?.FirmwareVersion) ? null : $"Firmware {details.FirmwareVersion}";
            return string.Join(" · ", new[] { model, firmware }.Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControl), nameof(Summary))]
    public partial bool IsOnline { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControl), nameof(Summary))]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoomTemperatureText))]
    public partial double? RoomTemperature { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetTemperatureText), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(IncreaseTemperatureCommand), nameof(DecreaseTemperatureCommand))]
    public partial double TargetTemperature { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial Option<HvacMode>? Mode { get; set; }

    [ObservableProperty]
    public partial Option<FanMode>? Fan { get; set; }

    [ObservableProperty]
    public partial Option<PresetMode>? Preset { get; set; }

    [ObservableProperty]
    public partial Option<ConvertiMode>? Converti { get; set; }

    [ObservableProperty]
    public partial bool SupportsConverti { get; set; }

    [ObservableProperty]
    public partial Option<int>? VerticalSwing { get; set; }

    [ObservableProperty]
    public partial Option<int>? HorizontalSwing { get; set; }

    [ObservableProperty]
    public partial bool DisplayOn { get; set; }

    [ObservableProperty]
    public partial bool NanoeOn { get; set; }

    [ObservableProperty]
    public partial Option<NanoeMode>? Nanoe { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string DailyEnergy { get; set; } = "—";

    [ObservableProperty]
    public partial string WeeklyEnergy { get; set; } = "—";

    [ObservableProperty]
    public partial string MonthlyEnergy { get; set; } = "—";

    [ObservableProperty]
    public partial string WeeklyEnergyLabel { get; set; } = "This week";

    [ObservableProperty]
    public partial string MonthlyEnergyLabel { get; set; } = "This month";

    public bool CanControl => IsOnline && IsOn;

    public string RoomTemperatureText => RoomTemperature is { } t ? $"{t:0.#}°C" : "--";

    public string TargetTemperatureText => $"{TargetTemperature:0.#}°C";

    public string Summary =>
        !IsOnline ? "Offline"
        : !IsOn ? "Off"
        : $"{Mode?.Label} · {TargetTemperature:0.#}°C";

    public void LoadEnergyIfNeeded()
    {
        if (!_energyLoaded)
            RefreshEnergyCommand.Execute(null);
    }

    public void Dispose()
    {
        _device.StatusChanged -= OnStatusChanged;
        _temperatureDelay?.Cancel();
    }

    partial void OnIsOnChanged(bool value) => Send(d => d.SetPowerAsync(value));

    partial void OnModeChanged(Option<HvacMode>? value)
    {
        if (value is not null)
            Send(d => d.SetModeAsync(value.Value));
    }

    partial void OnFanChanged(Option<FanMode>? value)
    {
        if (value is not null)
            Send(d => d.SetFanModeAsync(value.Value));
    }

    partial void OnPresetChanged(Option<PresetMode>? value)
    {
        if (value is not null)
            Send(d => d.SetPresetAsync(value.Value));
    }

    partial void OnConvertiChanged(Option<ConvertiMode>? value)
    {
        if (value is not null)
            Send(d => d.SetConvertiAsync(value.Value));
    }

    partial void OnVerticalSwingChanged(Option<int>? value)
    {
        if (value is not null)
            Send(d => d.SetVerticalSwingAsync(value.Value));
    }

    partial void OnHorizontalSwingChanged(Option<int>? value)
    {
        if (value is not null)
            Send(d => d.SetHorizontalSwingAsync(value.Value));
    }

    partial void OnDisplayOnChanged(bool value) => Send(d => d.SetDisplayAsync(value));

    partial void OnNanoeOnChanged(bool value) => Send(d => d.SetNanoeAsync(value ? NanoeMode.On : NanoeMode.Off));

    partial void OnNanoeChanged(Option<NanoeMode>? value)
    {
        if (value is not null)
            Send(d => d.SetNanoeAsync(value.Value));
    }

    [RelayCommand(CanExecute = nameof(CanIncreaseTemperature))]
    private void IncreaseTemperature() => ChangeTemperature(_device.Capabilities.TemperatureStep);

    private bool CanIncreaseTemperature() => TargetTemperature < _device.Capabilities.MaxTemperature;

    [RelayCommand(CanExecute = nameof(CanDecreaseTemperature))]
    private void DecreaseTemperature() => ChangeTemperature(-_device.Capabilities.TemperatureStep);

    private bool CanDecreaseTemperature() => TargetTemperature > _device.Capabilities.MinTemperature;

    [RelayCommand]
    private async Task RefreshEnergyAsync()
    {
        _energyLoaded = true;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dayStart = EnergyPeriods.LatestStart(ConsumptionPeriod.Daily, today);
        var weekStart = EnergyPeriods.LatestStart(ConsumptionPeriod.Weekly, today);
        var monthStart = EnergyPeriods.LatestStart(ConsumptionPeriod.Monthly, today);

        WeeklyEnergyLabel = $"Week from {weekStart:d MMM}";
        MonthlyEnergyLabel = $"{monthStart:MMMM yyyy}";

        var daily = LoadEnergyAsync(ConsumptionPeriod.Daily, dayStart);
        var weekly = LoadEnergyAsync(ConsumptionPeriod.Weekly, weekStart);
        var monthly = LoadEnergyAsync(ConsumptionPeriod.Monthly, monthStart);

        DailyEnergy = await daily;
        WeeklyEnergy = await weekly;
        MonthlyEnergy = await monthly;
    }

    private async Task<string> LoadEnergyAsync(ConsumptionPeriod period, DateOnly start)
    {
        try
        {
            return await _hub.GetEnergyConsumptionAsync(_device, period, start) is { } kwh
                ? $"{kwh:0.00} kWh"
                : "No data yet";
        }
        catch (Exception)
        {
            return "Unavailable";
        }
    }

    private void ChangeTemperature(double delta)
    {
        var capabilities = _device.Capabilities;
        TargetTemperature = Math.Clamp(TargetTemperature + delta, capabilities.MinTemperature, capabilities.MaxTemperature);

        // Wait for the user to stop clicking before sending, so the AC gets one command.
        _pendingTemperature = TargetTemperature;
        _temperatureDelay?.Cancel();
        _temperatureDelay = new CancellationTokenSource();
        _ = CommitTemperatureAsync(_temperatureDelay.Token);
    }

    private async Task CommitTemperatureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TemperatureDebounce, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var target = _pendingTemperature!.Value;
        _pendingTemperature = null;
        await SendAsync(d => d.SetTemperatureAsync(target));
    }

    private void Send(Func<AirConditioner, Task> command)
    {
        if (!_applyingStatus)
            _ = SendAsync(command);
    }

    private async Task SendAsync(Func<AirConditioner, Task> command)
    {
        ErrorMessage = null;
        try
        {
            await command(_device);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            // Go back to what the AC last reported.
            ApplyStatus(_device.Status);
        }
    }

    private void OnStatusChanged(object? sender, AcStatus status) =>
        Dispatcher.UIThread.Post(() => ApplyStatus(status));

    private void ApplyStatus(AcStatus status)
    {
        _applyingStatus = true;
        try
        {
            IsOnline = status.IsOnline;
            IsOn = status.IsOn;
            RoomTemperature = status.RoomTemperature;
            if (_pendingTemperature is null)
                TargetTemperature = status.TargetTemperature;
            Mode = Find(Modes, status.Mode);
            Fan = Find(FanModes, status.FanMode);
            Preset = Find(Presets, status.Preset);
            SupportsConverti = ConvertiModes.Count > 1 && status.Converti != ConvertiMode.NotSupported;
            Converti = Find(ConvertiModes, status.Converti);
            VerticalSwing = Find(VerticalSwingPositions, status.VerticalSwing);
            HorizontalSwing = Find(HorizontalSwingPositions, status.HorizontalSwing);
            DisplayOn = status.DisplayOn;
            NanoeOn = status.Nanoe != NanoeMode.Off;
            Nanoe = Find(NanoeModes, status.Nanoe);
        }
        finally
        {
            _applyingStatus = false;
        }
    }

    private static Option<T>? Find<T>(IReadOnlyList<Option<T>> options, T value) =>
        options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value));

    private static List<Option<T>> Supported<T>(IReadOnlyList<Option<T>> options, IReadOnlyList<T> supported) =>
        [.. options.Where(o => supported.Contains(o.Value))];
}
