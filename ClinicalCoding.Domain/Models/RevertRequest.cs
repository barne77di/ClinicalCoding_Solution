namespace ClinicalCoding.Domain.Models;

public enum RevertStatus { Pending = 0, Approved = 1, Rejected = 2 }

public class RevertRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public Guid AuditId { get; set; }
    public string RequestedBy { get; set; } = "reviewer";
    public DateTimeOffset RequestedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedOn { get; set; }
    public string? RejectedBy { get; set; }
    public DateTimeOffset? RejectedOn { get; set; }
    public RevertStatus Status { get; set; } = RevertStatus.Pending;
}
