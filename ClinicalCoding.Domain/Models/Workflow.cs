namespace ClinicalCoding.Domain.Models;

public enum EpisodeStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3
}

public class ClinicianQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public string ToClinician { get; set; } = string.Empty; // name/email
    public string Subject { get; set; } = "Clinical Coding Query";
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string? ExternalReference { get; set; } // e.g., Teams message/Flow run id
}
