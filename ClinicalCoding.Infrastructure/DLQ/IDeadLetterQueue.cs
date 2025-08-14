namespace ClinicalCoding.Infrastructure.DLQ;

public interface IDeadLetterQueue
{
    Task EnqueueAsync(string payload, CancellationToken ct = default);
}
