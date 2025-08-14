using Azure.Storage.Queues;

namespace ClinicalCoding.Infrastructure.DLQ;

public class StorageQueueDeadLetter : IDeadLetterQueue
{
    private readonly QueueClient _queue;

    public StorageQueueDeadLetter(string connectionString, string queueName)
    {
        _queue = new QueueClient(connectionString, queueName);
        _queue.CreateIfNotExists();
    }

    public async Task EnqueueAsync(string payload, CancellationToken ct = default)
    {
        await _queue.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)),
            timeToLive: TimeSpan.FromDays(7), cancellationToken: ct);
    }
}
