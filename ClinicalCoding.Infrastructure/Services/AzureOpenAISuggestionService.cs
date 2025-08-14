
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI; // DI signature compatibility only
using ClinicalCoding.Domain.Abstractions;
using ClinicalCoding.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicalCoding.Infrastructure.Services
{
    /// <summary>
    /// Azure OpenAI suggestion service via REST (SDK-agnostic).
    /// Constructs Diagnosis/Procedure using positional constructors:
    ///   Diagnosis(string code, string description, bool isPrimary)
    ///   Procedure(string code, string description, DateTime? performedOn)
    /// </summary>
    public class AzureOpenAISuggestionService : ICodingSuggestionService
    {
        private readonly ILogger<AzureOpenAISuggestionService> _logger;
        private readonly IConfiguration _cfg;
        private readonly TextAnalyticsClinicalExtractor _ta;

        public AzureOpenAISuggestionService(AzureOpenAIClient? _unusedClient,
            TextAnalyticsClinicalExtractor ta,
            ILogger<AzureOpenAISuggestionService> logger,
            IConfiguration cfg)
        {
            _ta = ta;
            _logger = logger;
            _cfg = cfg;
        }

        public async Task<(IEnumerable<Diagnosis> diagnoses, IEnumerable<Procedure> procedures)> SuggestAsync(Episode episode, CancellationToken ct = default)
        {
            var endpoint = _cfg["AzureOpenAI:Endpoint"];
            var key = _cfg["AzureOpenAI:ApiKey"];
            var deployment = _cfg["AzureOpenAI:Deployment"] ?? "gpt-4o-mini";
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                _logger.LogInformation("AzureOpenAI not configured; returning empty suggestions.");
                return (Enumerable.Empty<Diagnosis>(), Enumerable.Empty<Procedure>());
            }

            var phrases = await _ta.ExtractKeyPhrasesAsync(episode.SourceText ?? string.Empty, ct);
            var sb = new StringBuilder();
            sb.AppendLine("You are a UK clinical coding assistant. Map text to ICD-10 (diagnoses) and OPCS-4 (procedures).");
            sb.AppendLine("Return strict JSON: {\"diagnoses\":[{\"code\":\"\",\"description\":\"\",\"isPrimary\":true}],\"procedures\":[{\"code\":\"\",\"description\":\"\",\"performedOn\":\"YYYY-MM-DD or null\"}]}");
            sb.AppendLine();
            sb.AppendLine($"Patient: {episode.PatientName} | NHS: {episode.NHSNumber} | Specialty: {episode.Specialty}");
            sb.AppendLine($"Admission: {episode.AdmissionDate:yyyy-MM-dd} Discharge: {(episode.DischargeDate?.ToString("yyyy-MM-dd") ?? "")}");
            sb.AppendLine("Source text:");
            sb.AppendLine(episode.SourceText);
            if (phrases.Count > 0) sb.AppendLine($"Key phrases: {string.Join(", ", phrases)}");

            var body = new
            {
                messages = new object[]
                {
                    new { role = "system", content = "You output only JSON without commentary." },
                    new { role = "user", content = sb.ToString() }
                },
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };

            var apiVersion = _cfg["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";
            var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                http.DefaultRequestHeaders.Add("api-key", key);

                var req = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync(url, req, ct);
                var content = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AOAI REST call failed: {Status} {Body}", resp.StatusCode, content);
                    return (Enumerable.Empty<Diagnosis>(), Enumerable.Empty<Procedure>());
                }

                using var doc = JsonDocument.Parse(content);
                var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";

                using var parsed = JsonDocument.Parse(msg);
                var root = parsed.RootElement;

                var dx = new List<Diagnosis>();
                if (root.TryGetProperty("diagnoses", out var dxArr) && dxArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in dxArr.EnumerateArray())
                    {
                        var codeStr = d.TryGetProperty("code", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;
                        var descStr = d.TryGetProperty("description", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty;
                        var isPrimary = d.TryGetProperty("isPrimary", out var prim) && prim.ValueKind == JsonValueKind.True ? true : false;
                        dx.Add(new Diagnosis(codeStr, descStr, isPrimary));
                    }
                }

                var px = new List<Procedure>();
                if (root.TryGetProperty("procedures", out var pxArr) && pxArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pxArr.EnumerateArray())
                    {
                        var codeStr = p.TryGetProperty("code", out var pc) ? (pc.GetString() ?? string.Empty) : string.Empty;
                        var descStr = p.TryGetProperty("description", out var pd) ? (pd.GetString() ?? string.Empty) : string.Empty;
                        DateTime? on = null;
                        if (p.TryGetProperty("performedOn", out var po) && po.ValueKind == JsonValueKind.String && DateTime.TryParse(po.GetString(), out var parsedDate))
                            on = parsedDate;
                        px.Add(new Procedure(codeStr, descStr, on));
                    }
                }

                return (dx, px);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "AOAI REST suggestion unavailable — returning empty (fallback will kick in).");
                return (Enumerable.Empty<Diagnosis>(), Enumerable.Empty<Procedure>());
            }
        }
    }
}
