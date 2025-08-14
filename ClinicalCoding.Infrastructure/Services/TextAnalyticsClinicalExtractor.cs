
using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Logging;

namespace ClinicalCoding.Infrastructure.Services
{
    /// <summary>
    /// Thin helper around Azure Text Analytics to pull useful context
    /// (key phrases + entities). If you don't configure TA, methods
    /// gracefully return empty results.
    /// </summary>
    public class TextAnalyticsClinicalExtractor
    {
        private readonly TextAnalyticsClient _client;
        private readonly ILogger<TextAnalyticsClinicalExtractor> _logger;

        public TextAnalyticsClinicalExtractor(TextAnalyticsClient client, ILogger<TextAnalyticsClinicalExtractor> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> ExtractKeyPhrasesAsync(string text, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
                // Use named parameter to avoid the (text, language) overload
                var resp = await _client.ExtractKeyPhrasesAsync(text, cancellationToken: ct);
                return resp.Value;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Key phrase extraction skipped");
                return Array.Empty<string>();
            }
        }

        public async Task<IReadOnlyList<(string Category, string Text)>> ExtractEntitiesAsync(string text, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return Array.Empty<(string, string)>();
                // Use named parameter to avoid the (text, language) overload
                var resp = await _client.RecognizeEntitiesAsync(text, cancellationToken: ct);
                var list = new List<(string, string)>();
                foreach (var e in resp.Value)
                    list.Add((e.Category.ToString(), e.Text));
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "NER extraction skipped");
                return Array.Empty<(string, string)>();
            }
        }
    }
}
