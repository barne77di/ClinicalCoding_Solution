namespace ClinicalCoding.Domain.Models;

public record Diagnosis(string Code, string Description, bool IsPrimary = false);
public record Procedure(string Code, string Description, DateTime? PerformedOn = null);

public class Episode
{
    public string EpisodeId { get; set; } = Guid.NewGuid().ToString();
    public string NHSNumber { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public DateTime AdmissionDate { get; set; }
    public DateTime? DischargeDate { get; set; }
    public string Specialty { get; set; } = "Respiratory Medicine";
    public List<Diagnosis> Diagnoses { get; set; } = new();
    public List<Procedure> Procedures { get; set; } = new();
    public string SourceText { get; set; } = string.Empty;
}
