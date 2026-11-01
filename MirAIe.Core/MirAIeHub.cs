using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MirAIe.Core;

/// <summary>
/// Entry point: signs in, discovers the ACs in the account's first home, and keeps them in sync over MQTT.
/// </summary>
public sealed class MirAIeHub : IDisposable
{
    private readonly MirAIeApiClient _api;
    private readonly MirAIeBroker _broker;
    private readonly ILogger _logger;
    private readonly Dictionary<string, (AirConditioner Device, bool IsConnectionTopic)> _topics = [];

    private MirAIeHub(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MirAIeHub>();
        _api = new MirAIeApiClient(loggerFactory.CreateLogger<MirAIeApiClient>());
        _broker = new MirAIeBroker(loggerFactory.CreateLogger<MirAIeBroker>());
        _broker.MessageReceived += OnMessageReceived;
        _broker.StateChanged += (_, state) => BrokerStateChanged?.Invoke(this, state);
    }

    public string HomeId { get; private set; } = "";
    public IReadOnlyList<AirConditioner> Devices { get; private set; } = [];
    public BrokerState BrokerState => _broker.State;

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<BrokerState>? BrokerStateChanged;

    /// <exception cref="MirAIeAuthenticationException">The username or password was rejected.</exception>
    public static async Task<MirAIeHub> ConnectAsync(
        string username,
        string password,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var hub = new MirAIeHub(loggerFactory ?? NullLoggerFactory.Instance);
        try
        {
            await hub._api.LoginAsync(username, password, cancellationToken).ConfigureAwait(false);
            await hub.LoadHomeAsync(cancellationToken).ConfigureAwait(false);
            await hub.RefreshStatusesAsync(cancellationToken).ConfigureAwait(false);
            hub._broker.Start(hub.HomeId, hub._api.AccessToken!, hub._api.RenewTokenAsync, hub._topics.Keys.ToList());
            return hub;
        }
        catch
        {
            hub.Dispose();
            throw;
        }
    }

    /// <summary>Re-reads every AC's state from the cloud.</summary>
    public Task RefreshStatusesAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(Devices.Select(async device =>
        {
            try
            {
                using var status = await _api.GetJsonAsync(MirAIeEndpoints.DeviceStatus(device.Id), cancellationToken).ConfigureAwait(false);
                device.ApplyCloudStatus(status.RootElement);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not read status of {Device}", device.Name);
                device.MarkOffline();
            }
        }));

    /// <summary>
    /// Energy used (kWh) during the period starting at <paramref name="periodStart"/>, or <c>null</c> when
    /// MirAIe has no figure yet. See <see cref="EnergyPeriods.LatestStart"/> for valid start dates.
    /// </summary>
    public async Task<double?> GetEnergyConsumptionAsync(
        AirConditioner device,
        ConsumptionPeriod period,
        DateOnly periodStart,
        CancellationToken cancellationToken = default)
    {
        var key = EnergyPeriods.FormatKey(period, periodStart);
        var url = MirAIeEndpoints.EnergyConsumption(device.Id, period, key, key);
        using var response = await _api.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

        var responseKey = WireValues.ResponseKey(period);
        foreach (var entry in response.RootElement.EnumerateArrayOrEmpty())
        {
            if (entry.GetText(responseKey) == key && entry.GetDecimal("power") is { } power)
                return (double)power;
        }

        return null;
    }

    public void Dispose()
    {
        _broker.Dispose();
        _api.Dispose();
    }

    private async Task LoadHomeAsync(CancellationToken cancellationToken)
    {
        using var homes = await _api.GetJsonAsync(MirAIeEndpoints.Homes, cancellationToken).ConfigureAwait(false);
        var home = homes.RootElement.EnumerateArrayOrEmpty().FirstOrDefault();
        HomeId = home.GetText("homeId") ?? throw new MirAIeException("No home was found in this MirAIe account.");

        var devices = new List<AirConditioner>();
        var modelIds = new Dictionary<AirConditioner, string>();
        foreach (var space in home.EnumerateArrayOrEmpty("spaces"))
        {
            foreach (var entry in space.EnumerateArrayOrEmpty("devices"))
            {
                var id = entry.GetText("deviceId");
                var topic = entry.EnumerateArrayOrEmpty("topic").Select(t => t.GetString()).FirstOrDefault();
                if (id is null || topic is null)
                    continue;

                var device = new AirConditioner(id, entry.GetText("deviceName") ?? id, topic, _broker);
                devices.Add(device);
                _topics[device.StatusTopic] = (device, false);
                _topics[device.ConnectionStatusTopic] = (device, true);
                if (entry.GetText("modelId") is { } modelId)
                    modelIds[device] = modelId;
            }
        }

        Devices = devices;
        if (devices.Count == 0)
            return;

        using var details = await _api.GetJsonAsync(MirAIeEndpoints.DeviceDetails(devices.Select(d => d.Id)), cancellationToken).ConfigureAwait(false);
        foreach (var entry in details.RootElement.EnumerateArrayOrEmpty())
        {
            var device = devices.FirstOrDefault(d => d.Id == entry.GetText("deviceId"));
            if (device is null)
                continue;

            device.Details = new AcDetails(
                Brand: entry.GetText("brand"),
                ModelName: entry.GetText("modelName"),
                ModelNumber: entry.GetText("modelNumber"),
                FirmwareVersion: entry.GetText("firmwareVersion"),
                SerialNumber: entry.GetText("serialNumber"),
                MacAddress: entry.GetText("macAddress"));
            if (entry.GetText("modelId") is { } modelId)
                modelIds.TryAdd(device, modelId);
        }

        await LoadCapabilitiesAsync(modelIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads each model's capabilities once. ACs whose model can't be read keep <see cref="AcCapabilities.Default"/>.
    /// </summary>
    private Task LoadCapabilitiesAsync(Dictionary<AirConditioner, string> modelIds, CancellationToken cancellationToken) =>
        Task.WhenAll(modelIds.GroupBy(pair => pair.Value).Select(async model =>
        {
            try
            {
                using var response = await _api.GetJsonAsync(MirAIeEndpoints.DeviceModel(model.Key), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Model {Model}: {Capabilities}", model.Key, response.RootElement.GetRawText());

                var capabilities = CapabilityParser.Parse(response.RootElement);
                foreach (var (device, _) in model)
                    device.Capabilities = capabilities;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not read capabilities of model {Model}", model.Key);
            }
        }));

    private void OnMessageReceived(string topic, JsonElement payload)
    {
        if (!_topics.TryGetValue(topic, out var target))
            return;

        if (target.IsConnectionTopic)
            target.Device.ApplyConnectionMessage(payload);
        else
            target.Device.ApplyStatusMessage(payload);
    }
}
