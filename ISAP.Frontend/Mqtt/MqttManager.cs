using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;

namespace ISAP.Frontend.Pages_Production
{
    public sealed class MqttManager : IDisposable
    {
        private static readonly Lazy<MqttManager> _instance =
            new Lazy<MqttManager>(() => new MqttManager(), isThreadSafe: true);

        public static MqttManager Instance => _instance.Value;

        private readonly object _lock = new object();

        private IMqttClient _mqttClient;
        private MqttClientOptions _clientOptions;
        private System.Timers.Timer _heartbeatTimer;
        private volatile bool _initialized;
        private volatile bool _disposed;
        private volatile bool _isConnected;

        public bool IsConnected
        {
            get
            {
                if (_disposed) return false;
                var client = _mqttClient;
                return _isConnected && client != null && client.IsConnected;
            }
        }

#if DEBUG
        private const string BROKER_ADDRESS = "broker.mqtt.cool";
        private const int BROKER_PORT = 1883;
#else
        private const string BROKER_ADDRESS = "broker.emqx.io";
        private const int BROKER_PORT = 1883;
#endif

        private const string TOPIC_DPD_ERROR = "sns/order/error";
        private const string TOPIC_DPD_ORDER_LOAD = "sns/order/load";
        private const string TOPIC_DPD_ORDER_ACTIVE = "sns/order/active";
        private const string TOPIC_DPD_ORDER_UNKNOWN = "sns/order/unknown";
        private const string TOPIC_DPD_ORDER_STATUS_PC = "sns/order/status/pc";
        private const string TOPIC_DPD_ORDER_CANCEL = "sns/order/cancel";
        private const string TOPIC_DPD_ORDER_FINALIZE = "sns/order/finalize";
        private const string TOPIC_DPD_ORDER_PACKAGE = "sns/order/package";
        private const string TOPIC_DPD_ORDER_PACKAGE_ADD = "sns/order/packageadd";
        private const string TOPIC_DPD_ORDER_PRINT_LABEL = "sns/order/print/label";
        private const string TOPIC_DPD_ORDER_PRINT_DELNOTE = "sns/order/print/delnote";
        private const string TOPIC_HEARTBEAT = "sns/heartbeat/isap";

        private const int HEARTBEAT_INTERVAL_MS = 1000;
        private const int RECONNECT_DELAY_MS = 5000;

        private static readonly string[] SubscriptionTopics = new[]
        {
            TOPIC_DPD_ORDER_LOAD,
            TOPIC_DPD_ORDER_CANCEL,
            TOPIC_DPD_ORDER_FINALIZE,
            TOPIC_DPD_ORDER_PACKAGE,
            TOPIC_DPD_ORDER_PACKAGE_ADD,
        };

        private MqttManager()
        {
            Logger.AddLogEntry(Logger.LogEntryCategories.Info, "Broker: " + BROKER_ADDRESS, null, "MqttManager");
        }

        /// <summary>
        /// Call once at application startup (e.g. Application_Start or OWIN Startup).
        /// Safe to call multiple times; only the first call has an effect.
        /// </summary>
        public void Initialize()
        {
            ThrowIfDisposed();

            if (_initialized)
                return;

            _initialized = true;

            // Fire-and-forget is intentional here; ConnectAsync handles its own errors.
            // ConfigureAwait(false) avoids deadlocking on the ASP.NET sync context.
            Task.Run(() => ConnectAsync());
        }

        private async Task ConnectAsync()
        {
            if (_disposed) return;

            try
            {
                var factory = new MqttFactory();
                var client = factory.CreateMqttClient();

                _clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(BROKER_ADDRESS, BROKER_PORT)
                    .WithClientId("DPDPackageManagement_" + Guid.NewGuid().ToString())
                    .WithCleanSession()
                    .Build();

                client.UseConnectedHandler(async e =>
                {
                    await SubscribeAllTopicsAsync(client).ConfigureAwait(false);
                });

                client.UseDisconnectedHandler(async e =>
                {
                    _isConnected = false;
                    StopHeartbeat();

                    if (_disposed) return;

                    Logger.AddLogEntry(
                        Logger.LogEntryCategories.Info,
                        "Disconnected. Attempting reconnect in " + RECONNECT_DELAY_MS + "ms...",
                        null,
                        "MqttManager");

                    await Task.Delay(RECONNECT_DELAY_MS).ConfigureAwait(false);

                    if (_disposed) return;

                    try
                    {
                        await client.ConnectAsync(_clientOptions, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Client was disposed during reconnect delay — expected during shutdown
                    }
                    catch (Exception ex)
                    {
                        Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Reconnection failed: " + ex.Message, null, "MqttManager");
                    }
                });

                client.UseApplicationMessageReceivedHandler(e =>
                {
                    // Route to an async handler via Task.Run so the MQTTnet receive
                    // pipeline is not blocked by downstream processing, and exceptions
                    // in user code cannot tear down the connection.
                    Task.Run(() => HandleMessageReceivedAsync(e));
                });

                _mqttClient = client;

                await client.ConnectAsync(_clientOptions, CancellationToken.None).ConfigureAwait(false);
                _isConnected = true;

                StartHeartbeat();
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Connection failed: " + ex.Message, null, "MqttManager");
                _isConnected = false;
            }
        }

        private async Task SubscribeAllTopicsAsync(IMqttClient client)
        {
            try
            {
                foreach (var topic in SubscriptionTopics)
                {
                    await client.SubscribeAsync(
                        new MqttTopicFilterBuilder().WithTopic(topic).Build()
                    ).ConfigureAwait(false);
                }

                Logger.AddLogEntry(Logger.LogEntryCategories.Info, "Subscribed to all order topics", null, "MqttManager");
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Subscription failed: " + ex.Message, null, "MqttManager");
            }
        }

        #region Message Handling

        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = e.ApplicationMessage.Payload != null
                    ? Encoding.UTF8.GetString(e.ApplicationMessage.Payload)
                    : string.Empty;

                Logger.AddLogEntry(Logger.LogEntryCategories.Info, "Message received: " + topic + ", " + payload, null, "MqttManager");

                var dpd = Classes.MQTT.MqttDPDManager.Instance;

                if (topic == TOPIC_DPD_ORDER_LOAD)
                {
                    await dpd.ProcessOrderLoadMessage(payload).ConfigureAwait(false);
                }
                else if (topic == TOPIC_DPD_ORDER_CANCEL)
                {
                    dpd.ProcessOrderCancelMessage(payload);
                }
                else if (topic == TOPIC_DPD_ORDER_FINALIZE)
                {
                    dpd.ProcessOrderFinalizeMessage(payload);
                }
                else if (topic == TOPIC_DPD_ORDER_PACKAGE)
                {
                    await dpd.ProcessOrderPackageMessage(payload).ConfigureAwait(false);
                }
                else if (topic == TOPIC_DPD_ORDER_PACKAGE_ADD)
                {
                    await dpd.ProcessOrderPackageAddMessage(payload).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Handle Message failed: " + ex.Message, null, "MqttManager");
            }
        }

        #endregion

        #region Heartbeat

        private void StartHeartbeat()
        {
            lock (_lock)
            {
                StopHeartbeatUnsafe();

                var timer = new System.Timers.Timer(HEARTBEAT_INTERVAL_MS);
                timer.Elapsed += HeartbeatTimer_Elapsed;
                timer.AutoReset = true;
                timer.Start();
                _heartbeatTimer = timer;
            }
        }

        private async void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // async void is unavoidable here because System.Timers.Timer.Elapsed
            // requires an EventHandler<ElapsedEventArgs> delegate. We guard against
            // unobserved exceptions with the outer try/catch.
            try
            {
                if (!IsConnected) return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                await PublishMessageAsync(TOPIC_HEARTBEAT, timestamp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Sending Heartbeat failed: " + ex.Message, null, "MqttManager");
            }
        }

        private void StopHeartbeat()
        {
            lock (_lock)
            {
                StopHeartbeatUnsafe();
            }
        }

        private void StopHeartbeatUnsafe()
        {
            var timer = _heartbeatTimer;
            if (timer != null)
            {
                timer.Stop();
                timer.Elapsed -= HeartbeatTimer_Elapsed;
                timer.Dispose();
                _heartbeatTimer = null;
            }
        }

        #endregion

        #region Public Publish API

        public enum MessageTypes { DPDOrderLoad, DPDOrderLoadActive, DPDDeliveryNote, DPDLabel }
        public enum ErrorTypes { DPDGeneric, DPDOrderNumberFormat, DPDOrderNumberUnknown, DPDAdress }

        /// <summary>
        /// Publishes a text message for the given message type.
        /// Prefer <c>await</c>ing the returned Task. If you cannot await, the method
        /// still logs any internal errors rather than throwing.
        /// </summary>
        public Task PublishMessageAsync(MessageTypes messageType, string payload)
        {
            switch (messageType)
            {
                case MessageTypes.DPDOrderLoad:
                    return PublishMessageAsync(TOPIC_DPD_ORDER_STATUS_PC, payload);
                case MessageTypes.DPDOrderLoadActive:
                    return PublishMessageAsync(TOPIC_DPD_ORDER_ACTIVE, payload);
                default:
                    return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Publishes a binary (e.g. PDF) message for the given message type.
        /// </summary>
        public Task PublishBinaryMessageAsync(MessageTypes messageType, byte[] payload)
        {
            switch (messageType)
            {
                case MessageTypes.DPDDeliveryNote:
                    return PublishBinaryAsync(TOPIC_DPD_ORDER_PRINT_DELNOTE, payload);
                case MessageTypes.DPDLabel:
                    return PublishBinaryAsync(TOPIC_DPD_ORDER_PRINT_LABEL, payload);
                default:
                    return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Publishes an error message for the given error type.
        /// </summary>
        public Task PublishErrorMessageAsync(ErrorTypes errorType, string errorMessage)
        {
            switch (errorType)
            {
                case ErrorTypes.DPDGeneric:
                case ErrorTypes.DPDAdress:
                    return PublishBinaryAsync(TOPIC_DPD_ERROR, Encoding.UTF8.GetBytes(errorMessage));
                case ErrorTypes.DPDOrderNumberFormat:
                case ErrorTypes.DPDOrderNumberUnknown:
                    return PublishMessageAsync(TOPIC_DPD_ORDER_UNKNOWN, errorMessage);
                default:
                    return Task.CompletedTask;
            }
        }

        #endregion

        #region Core Publish

        private async Task PublishMessageAsync(string topic, string payload)
        {
            try
            {
                var client = _mqttClient;
                if (client == null || !client.IsConnected)
                {
                    Logger.AddLogEntry(Logger.LogEntryCategories.Error, "MQTT client not connected. Cannot publish to " + topic, null, "MqttManager");
                    return;
                }

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await client.PublishAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Error publishing to " + topic + ": " + ex.Message, null, "MqttManager");
            }
        }

        private async Task PublishBinaryAsync(string topic, byte[] payload)
        {
            Logger.AddLogEntry(Logger.LogEntryCategories.Info, "PublishBinaryAsync: " + topic + " - size: " + payload.Length, null, "MqttManager");

            try
            {
                var client = _mqttClient;
                if (client == null || !client.IsConnected)
                {
                    Logger.AddLogEntry(Logger.LogEntryCategories.Error, "MQTT client not connected. Cannot publish binary to " + topic, null, "MqttManager");
                    return;
                }

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await client.PublishAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Error publishing binary to " + topic + ": " + ex.Message, null, "MqttManager");
            }
        }

        #endregion

        #region Disconnect / Dispose

        /// <summary>
        /// Gracefully disconnects from the broker. Safe to call from synchronous code
        /// (e.g. Application_End). Does not throw.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            try
            {
                StopHeartbeat();

                var client = _mqttClient;
                if (client != null && client.IsConnected)
                {
                    await client.DisconnectAsync().ConfigureAwait(false);
                }

                _isConnected = false;
            }
            catch (Exception ex)
            {
                Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Error disconnecting: " + ex.Message, null, "MqttManager");
            }
        }

        /// <summary>
        /// Call from Application_End or when the IIS app pool recycles.
        /// Stops heartbeat, disconnects the client, and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopHeartbeat();

            var client = _mqttClient;
            _mqttClient = null;
            _isConnected = false;

            if (client != null)
            {
                try
                {
                    if (client.IsConnected)
                    {
                        // Use a short timeout so IIS shutdown is not blocked indefinitely
                        var disconnectTask = client.DisconnectAsync();
                        disconnectTask.Wait(TimeSpan.FromSeconds(3));
                    }
                }
                catch (Exception ex)
                {
                    Logger.AddLogEntry(Logger.LogEntryCategories.Error, "Error during dispose disconnect: " + ex.Message, null, "MqttManager");
                }
                finally
                {
                    client.Dispose();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MqttManager));
        }

        #endregion
    }
}
