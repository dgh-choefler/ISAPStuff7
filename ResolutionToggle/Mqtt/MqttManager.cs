using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ISAP.Frontend.Pages_Production;

public sealed class MqttManager : IAsyncDisposable
{
    private static readonly Lazy<MqttManager> _instance =
        new(() => new MqttManager(), isThreadSafe: true);

    public static MqttManager Instance => _instance.Value;

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private IMqttClient? _mqttClient;
    private MqttClientOptions? _clientOptions;
    private PeriodicTimer? _heartbeatTimer;
    private CancellationTokenSource? _heartbeatCts;
    private CancellationTokenSource? _reconnectCts;
    private bool _initialized;
    private volatile bool _disposed;

    public bool IsConnected =>
        !_disposed && _mqttClient is { IsConnected: true };

#if DEBUG
    private const string BrokerAddress = "broker.mqtt.cool";
    private const int BrokerPort = 1883;
#else
    private const string BrokerAddress = "broker.emqx.io";
    private const int BrokerPort = 1883;
#endif

    private const string TopicOrderError = "sns/order/error";
    private const string TopicOrderLoad = "sns/order/load";
    private const string TopicOrderActive = "sns/order/active";
    private const string TopicOrderUnknown = "sns/order/unknown";
    private const string TopicOrderStatusPc = "sns/order/status/pc";
    private const string TopicOrderCancel = "sns/order/cancel";
    private const string TopicOrderFinalize = "sns/order/finalize";
    private const string TopicOrderPackage = "sns/order/package";
    private const string TopicOrderPackageAdd = "sns/order/packageadd";
    private const string TopicOrderPrintLabel = "sns/order/print/label";
    private const string TopicOrderPrintDelNote = "sns/order/print/delnote";
    private const string TopicHeartbeat = "sns/heartbeat/isap";

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private static readonly string[] SubscriptionTopics =
    [
        TopicOrderLoad,
        TopicOrderCancel,
        TopicOrderFinalize,
        TopicOrderPackage,
        TopicOrderPackageAdd,
    ];

    private MqttManager()
    {
        Logger.AddLogEntry(
            Logger.LogEntryCategories.Info,
            $"Broker: {BrokerAddress}",
            null,
            "MqttManager");
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _initialized = true;
        _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_disposed) return;

        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_mqttClient is { IsConnected: true })
                return;

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            _clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(BrokerAddress, BrokerPort)
                .WithClientId($"DPDPackageManagement_{Guid.NewGuid()}")
                .WithCleanSession()
                .Build();

            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            await _mqttClient.ConnectAsync(_clientOptions, CancellationToken.None)
                .ConfigureAwait(false);

            StartHeartbeat();
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Connection failed: {ex.Message}",
                null,
                "MqttManager");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        try
        {
            var factory = new MqttFactory();
            var builder = factory.CreateSubscribeOptionsBuilder();

            foreach (var topic in SubscriptionTopics)
                builder.WithTopicFilter(f => f.WithTopic(topic));

            await _mqttClient!.SubscribeAsync(builder.Build(), CancellationToken.None)
                .ConfigureAwait(false);

            Logger.AddLogEntry(
                Logger.LogEntryCategories.Info,
                "Subscribed to all order topics",
                null,
                "MqttManager");
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Subscription failed: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        StopHeartbeat();

        if (_disposed) return;

        Logger.AddLogEntry(
            Logger.LogEntryCategories.Info,
            $"Disconnected (reason: {e.Reason}). Scheduling reconnect...",
            null,
            "MqttManager");

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        try
        {
            await Task.Delay(ReconnectDelay, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested && _clientOptions is not null)
            {
                await _mqttClient!.ConnectAsync(_clientOptions, token)
                    .ConfigureAwait(false);
                StartHeartbeat();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed or new reconnect superseded this one
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Reconnection failed: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.PayloadSegment.Count > 0
                ? Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)
                : string.Empty;

            Logger.AddLogEntry(
                Logger.LogEntryCategories.Info,
                $"Message received: {topic}, {payload}",
                null,
                "MqttManager");

            var dpd = Classes.MQTT.MqttDPDManager.Instance;

            if (topic == TopicOrderLoad)
                await dpd.ProcessOrderLoadMessage(payload).ConfigureAwait(false);
            else if (topic == TopicOrderCancel)
                dpd.ProcessOrderCancelMessage(payload);
            else if (topic == TopicOrderFinalize)
                dpd.ProcessOrderFinalizeMessage(payload);
            else if (topic == TopicOrderPackage)
                await dpd.ProcessOrderPackageMessage(payload).ConfigureAwait(false);
            else if (topic == TopicOrderPackageAdd)
                await dpd.ProcessOrderPackageAddMessage(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Handle Message failed: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    #region Heartbeat

    private void StartHeartbeat()
    {
        StopHeartbeat();

        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTimer = new PeriodicTimer(HeartbeatInterval);

        _ = RunHeartbeatLoopAsync(_heartbeatTimer, _heartbeatCts.Token);
    }

    private async Task RunHeartbeatLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!IsConnected) continue;

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                await PublishCoreAsync(TopicHeartbeat, timestamp).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Heartbeat loop failed: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    private void StopHeartbeat()
    {
        try
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Stopping Heartbeat failed: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    #endregion

    #region Public API

    public enum MessageTypes { DPDOrderLoad, DPDOrderLoadActive, DPDDeliveryNote, DPDLabel }
    public enum ErrorTypes { DPDGeneric, DPDOrderNumberFormat, DPDOrderNumberUnknown, DPDAdress }

    public Task PublishMessageAsync(MessageTypes messageType, string payload)
    {
        return messageType switch
        {
            MessageTypes.DPDOrderLoad => PublishCoreAsync(TopicOrderStatusPc, payload),
            MessageTypes.DPDOrderLoadActive => PublishCoreAsync(TopicOrderActive, payload),
            _ => Task.CompletedTask,
        };
    }

    public Task PublishMessageAsync(MessageTypes messageType, byte[] payload)
    {
        return messageType switch
        {
            MessageTypes.DPDDeliveryNote => PublishBinaryAsync(TopicOrderPrintDelNote, payload),
            MessageTypes.DPDLabel => PublishBinaryAsync(TopicOrderPrintLabel, payload),
            _ => Task.CompletedTask,
        };
    }

    public Task PublishErrorMessageAsync(ErrorTypes errorType, string errorMessage)
    {
        return errorType switch
        {
            ErrorTypes.DPDGeneric or ErrorTypes.DPDAdress =>
                PublishBinaryAsync(TopicOrderError, Encoding.UTF8.GetBytes(errorMessage)),
            ErrorTypes.DPDOrderNumberFormat or ErrorTypes.DPDOrderNumberUnknown =>
                PublishCoreAsync(TopicOrderUnknown, errorMessage),
            _ => Task.CompletedTask,
        };
    }

    public async Task DisconnectAsync()
    {
        if (_disposed) return;

        try
        {
            StopHeartbeat();

            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;

            if (_mqttClient is { IsConnected: true })
            {
                await _mqttClient.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder()
                        .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                        .Build(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Error disconnecting: {ex.Message}",
                null,
                "MqttManager");
        }
    }

    #endregion

    #region Core Publish

    private async Task PublishCoreAsync(string topic, string payload)
    {
        await _publishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_mqttClient is not { IsConnected: true })
            {
                Logger.AddLogEntry(
                    Logger.LogEntryCategories.Error,
                    $"MQTT client not connected. Cannot publish to {topic}.",
                    null,
                    "MqttManager");
                return;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(message, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Error publishing to {topic}: {ex.Message}",
                null,
                "MqttManager");
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task PublishBinaryAsync(string topic, byte[] payload)
    {
        Logger.AddLogEntry(
            Logger.LogEntryCategories.Info,
            $"PublishBinaryAsync: {topic} - size: {payload.Length}",
            null,
            "MqttManager");

        await _publishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_mqttClient is not { IsConnected: true })
            {
                Logger.AddLogEntry(
                    Logger.LogEntryCategories.Error,
                    $"MQTT client not connected. Cannot publish binary to {topic}.",
                    null,
                    "MqttManager");
                return;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.AddLogEntry(
                Logger.LogEntryCategories.Error,
                $"Error publishing binary to {topic}: {ex.Message}",
                null,
                "MqttManager");
        }
        finally
        {
            _publishLock.Release();
        }
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DisconnectAsync().ConfigureAwait(false);

        _mqttClient?.Dispose();
        _mqttClient = null;

        _connectLock.Dispose();
        _publishLock.Dispose();
    }

    #endregion
}
