using ClinicalCoding.Domain.Models;
using ClinicalCoding.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class RuleBasedSuggestionServiceTests
{
    [Fact]
    public async Task SuggestAsync_FindsExpectedCodes()
    {
        var svc = new RuleBasedSuggestionService(new NullLogger<RuleBasedSuggestionService>());
        var episode = new Episode
        {
            PatientName = "John Smith",
            SourceText = "Admitted with community-acquired pneumonia. Background of COPD. CXR performed with nebulisation and oxygen therapy."
        };

        var (dx, px) = await svc.SuggestAsync(episode);
        Assert.Contains(dx, d => d.Code == "J18.1");
        Assert.Contains(dx, d => d.Code == "J44.9");
        Assert.Contains(px, p => p.Code == "U20.1");
        Assert.Contains(px, p => p.Code == "E85.3");
        Assert.Contains(px, p => p.Code == "E85.2");
    }
}
