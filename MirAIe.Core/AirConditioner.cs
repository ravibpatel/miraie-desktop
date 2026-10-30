using System.Text.Json;

namespace MirAIe.Core;

/// <summary>
/// A MirAIe air conditioner. Commands are published over MQTT; <see cref="StatusChanged"/> fires when the
/// AC reports its state and, optimistically, right after a command is sent.
/// </summary>
public sealed class AirConditioner
{
    private readonly MirAIeBroker _broker;
    private readonly Lock _statusLock = new();

    internal AirConditioner(string id, string name, string topicBase, MirAIeBroker broker)
    {
        Id = id;
        Name = name;
        ControlTopic = topicBase + "/control";
        StatusTopic = topicBase + "/status";
        ConnectionStatusTopic = topicBase + "/connectionStatus";
        _broker = broker;
    }

    public string Id { get; }
    public string Name { get; }
    public AcDetails? Details { get; internal set; }
    public AcStatus Status { get; private set; } = new();

    internal string ControlTopic { get; }
    internal string StatusTopic { get; }
    internal string ConnectionStatusTopic { get; }

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<AcStatus>? StatusChanged;

    public Task SetPowerAsync(bool on, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Power(on), s => s with { IsOn = on }, cancellationToken);

    public Task SetTemperatureAsync(double temperature, CancellationToken cancellationToken = default)
    {
        temperature = Math.Clamp(temperature, AcStatus.MinTemperature, AcStatus.MaxTemperature);
        return SendAsync(ControlPayloads.Temperature(temperature), s => s with { TargetTemperature = temperature }, cancellationToken);
    }

    public Task SetModeAsync(HvacMode mode, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Mode(mode), s => s with { Mode = mode }, cancellationToken);

    public Task SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Fan(mode), s => s with { FanMode = mode }, cancellationToken);

    public Task SetPresetAsync(PresetMode preset, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Preset(preset), s => s with
        {
            Preset = preset,
            Converti = s.Converti == ConvertiMode.NotSupported ? s.Converti : ConvertiMode.Off,
            TargetTemperature = preset == PresetMode.Eco ? 26 : s.TargetTemperature,
        }, cancellationToken);

    public Task SetConvertiAsync(ConvertiMode mode, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Converti(mode), s => s with
        {
            Converti = mode,
            Preset = s.Preset is PresetMode.Eco or PresetMode.Boost ? PresetMode.None : s.Preset,
        }, cancellationToken);

    public Task SetVerticalSwingAsync(int position, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, 5);
        return SendAsync(ControlPayloads.VerticalSwing(position), s => s with { VerticalSwing = position }, cancellationToken);
    }

    public Task SetHorizontalSwingAsync(int position, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, 5);
        return SendAsync(ControlPayloads.HorizontalSwing(position), s => s with { HorizontalSwing = position }, cancellationToken);
    }

    public Task SetDisplayAsync(bool on, CancellationToken cancellationToken = default) =>
        SendAsync(ControlPayloads.Display(on), s => s with { DisplayOn = on }, cancellationToken);

    internal void ApplyCloudStatus(JsonElement payload) => UpdateStatus(s => StatusParser.FromCloudStatus(s, payload));

    internal void ApplyStatusMessage(JsonElement payload) => UpdateStatus(s => StatusParser.Apply(s, payload));

    internal void ApplyConnectionMessage(JsonElement payload) =>
        UpdateStatus(s => s with { IsOnline = payload.GetText("onlineStatus") == "true" });

    internal void MarkOffline() => UpdateStatus(s => s with { IsOnline = false });

    private async Task SendAsync(string payload, Func<AcStatus, AcStatus> expected, CancellationToken cancellationToken)
    {
        await _broker.PublishAsync(ControlTopic, payload, cancellationToken).ConfigureAwait(false);
        UpdateStatus(expected);
    }

    private void UpdateStatus(Func<AcStatus, AcStatus> update)
    {
        AcStatus status;
        lock (_statusLock)
        {
            status = update(Status);
            if (status == Status)
                return;
            Status = status;
        }

        StatusChanged?.Invoke(this, status);
    }
}
