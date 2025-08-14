using ClinicalCoding.Domain.Models;

namespace ClinicalCoding.Domain.Abstractions;

public interface ICodingSuggestionService
{
    Task<(IEnumerable<Diagnosis> diagnoses, IEnumerable<Procedure> procedures)> SuggestAsync(Episode episode, CancellationToken ct = default);
}
