namespace ClinicalCoding.Domain.Models;

public class DeadLetterEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "FlowQueryResponse";
    public string PayloadJson { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; } = 0;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastTriedOn { get; set; }
}
