using ClinicalCoding.Domain.Abstractions;
using ClinicalCoding.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ClinicalCoding.Infrastructure.Services;

// Simple rule-based fallback to demonstrate flow without external dependencies.
public class RuleBasedSuggestionService : ICodingSuggestionService
{
    private readonly ILogger<RuleBasedSuggestionService> _logger;
    public RuleBasedSuggestionService(ILogger<RuleBasedSuggestionService> logger) => _logger = logger;

    public Task<(IEnumerable<Diagnosis> diagnoses, IEnumerable<Procedure> procedures)> SuggestAsync(Episode episode, CancellationToken ct = default)
    {
        var diagnoses = new List<Diagnosis>();
        var procedures = new List<Procedure>();

        string text = episode.SourceText.ToLowerInvariant();

        if (text.Contains("pneumonia"))
            diagnoses.Add(new Diagnosis("J18.1", "Lobar pneumonia, unspecified", true));

        if (text.Contains("copd") || text.Contains("chronic obstructive"))
            diagnoses.Add(new Diagnosis("J44.9", "Chronic obstructive pulmonary disease, unspecified"));

        if (text.Contains("chest x-ray") || text.Contains("cxr"))
            procedures.Add(new Procedure("U20.1", "Diagnostic X-ray of chest", DateTime.UtcNow));

        if (text.Contains("nebulis"))
            procedures.Add(new Procedure("E85.3", "Nebulisation therapy", DateTime.UtcNow));

        if (text.Contains("oxygen"))
            procedures.Add(new Procedure("E85.2", "Administration of oxygen therapy", DateTime.UtcNow));

        _logger.LogInformation("Rule-based suggestion produced {Dx} diagnoses and {Px} procedures.", diagnoses.Count, procedures.Count);
        return Task.FromResult<(IEnumerable<Diagnosis>, IEnumerable<Procedure>)>((diagnoses, procedures));
    }
}
