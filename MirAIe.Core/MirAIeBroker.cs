using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;

namespace MirAIe.Core;

/// <summary>
/// Keeps a TLS MQTT connection to the MirAIe broker open, reconnecting with a fresh token when it drops.
/// The broker authenticates with the home id as username and the access token as password.
/// </summary>
internal sealed class MirAIeBroker : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IMqttClient _client;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource? _disconnected;

    public MirAIeBroker(ILogger logger)
    {
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += e =>
        {
            if (e.ClientWasConnected)
            {
                _logger.LogWarning(e.Exception, "MQTT connection lost: {Reason}", e.ReasonString ?? e.Reason.ToString());
                _disconnected?.TrySetResult();
            }
            return Task.CompletedTask;
        };
    }

    public BrokerState State { get; private set; }

    public event EventHandler<BrokerState>? StateChanged;

    /// <summary>
    /// Raised on a background thread for every message. The payload element is a detached copy.
    /// </summary>
    public event Action<string, JsonElement>? MessageReceived;

    public void Start(string homeId, string accessToken, Func<CancellationToken, Task<string>> renewToken, IReadOnlyCollection<string> topics)
    {
        _ = Task.Run(() => RunAsync(homeId, accessToken, renewToken, topics, _shutdown.Token));
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
            throw new MirAIeException("Not connected to MirAIe. Reconnecting…");

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        _logger.LogDebug("Publish {Topic}: {Payload}", topic, payload);
        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _client.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunAsync(
        string homeId,
        string token,
        Func<CancellationToken, Task<string>> renewToken,
        IReadOnlyCollection<string> topics,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(BrokerState.Connecting);
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disconnected = disconnected;

                await _client.ConnectAsync(BuildOptions(homeId, token), cancellationToken).ConfigureAwait(false);

                var subscribe = new MqttClientSubscribeOptionsBuilder();
                foreach (var topic in topics)
                    subscribe.WithTopicFilter(topic);
                await _client.SubscribeAsync(subscribe.Build(), cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Connected to MirAIe broker, subscribed to {Count} topics", topics.Count);
                SetState(BrokerState.Connected);

                await disconnected.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT connection attempt failed");
            }

            SetState(BrokerState.Reconnecting);
            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                token = await renewToken(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(BrokerState.Disconnected);
    }

    private static MqttClientOptions BuildOptions(string homeId, string token) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(MirAIeEndpoints.MqttHost, MirAIeEndpoints.MqttPort)
            .WithTlsOptions(o => o.UseTls())
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"miraie{Guid.NewGuid():N}"[..22])
            .WithCredentials(homeId, token)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(15))
            .Build();

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        try
        {
            using var document = JsonDocument.Parse(e.ApplicationMessage.Payload);
            _logger.LogDebug("Message {Topic}: {Payload}", topic, document.RootElement.GetRawText());
            MessageReceived?.Invoke(topic, document.RootElement.Clone());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignoring unreadable message on {Topic}", topic);
        }

        return Task.CompletedTask;
    }

    private void SetState(BrokerState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(this, state);
    }
}
