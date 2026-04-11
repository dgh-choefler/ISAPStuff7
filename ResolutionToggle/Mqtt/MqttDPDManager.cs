namespace ISAP.Frontend.Pages_Production.Classes.MQTT;

/// <summary>
/// Handles DPD-specific order message processing.
/// Replace this stub with your actual implementation.
/// </summary>
public sealed class MqttDPDManager
{
    private static readonly Lazy<MqttDPDManager> _instance =
        new(() => new MqttDPDManager(), isThreadSafe: true);

    public static MqttDPDManager Instance => _instance.Value;

    private MqttDPDManager() { }

    public Task ProcessOrderLoadMessage(string payload)
    {
        return Task.CompletedTask;
    }

    public void ProcessOrderCancelMessage(string payload)
    {
    }

    public void ProcessOrderFinalizeMessage(string payload)
    {
    }

    public Task ProcessOrderPackageMessage(string payload)
    {
        return Task.CompletedTask;
    }

    public Task ProcessOrderPackageAddMessage(string payload)
    {
        return Task.CompletedTask;
    }
}
