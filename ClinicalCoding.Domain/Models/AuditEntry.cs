namespace ClinicalCoding.Domain.Models;

public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string PerformedBy { get; set; } = "system";
    public string Action { get; set; } = string.Empty; // e.g., EpisodeCreated
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}
