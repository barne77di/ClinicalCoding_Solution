using Azure.Messaging.ServiceBus;

namespace ClinicalCoding.Infrastructure.DLQ;

public class ServiceBusDeadLetter : IDeadLetterQueue, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    public ServiceBusDeadLetter(string connectionString, string queueName)
    {
        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(queueName);
    }

    public async Task EnqueueAsync(string payload, CancellationToken ct = default)
    {
        var msg = new ServiceBusMessage(payload)
        {
            TimeToLive = TimeSpan.FromDays(7)
        };
        await _sender.SendMessageAsync(msg, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
