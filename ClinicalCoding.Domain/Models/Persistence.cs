using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicalCoding.Domain.Models;

public class EpisodeEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(20)]
    public string NHSNumber { get; set; } = string.Empty;
    [MaxLength(200)]
    public string PatientName { get; set; } = string.Empty;
    public DateTime AdmissionDate { get; set; }
    public DateTime? DischargeDate { get; set; }
    [MaxLength(100)]
    public string Specialty { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;

    public EpisodeStatus Status { get; set; } = EpisodeStatus.Draft;
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedOn { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedOn { get; set; }
    public string? ReviewNotes { get; set; }

    public List<DiagnosisEntity> Diagnoses { get; set; } = new();
    public List<ProcedureEntity> Procedures { get; set; } = new();
}

public class DiagnosisEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(10)] public string Code { get; set; } = string.Empty;
    [MaxLength(300)] public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public Guid EpisodeId { get; set; }
    public EpisodeEntity Episode { get; set; } = null!;
}

public class ProcedureEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(10)] public string Code { get; set; } = string.Empty;
    [MaxLength(300)] public string Description { get; set; } = string.Empty;
    public DateTime? PerformedOn { get; set; }
    public Guid EpisodeId { get; set; }
    public EpisodeEntity Episode { get; set; } = null!;
}

public class ClinicianQueryEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    [MaxLength(200)] public string ToClinician { get; set; } = string.Empty;
    [MaxLength(200)] public string Subject { get; set; } = "Clinical Coding Query";
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = "system";
    [MaxLength(200)] public string? ExternalReference { get; set; }

    public string? ResponseText { get; set; }
    public DateTimeOffset? RespondedOn { get; set; }
    [MaxLength(200)] public string? RespondedBy { get; set; }
}
