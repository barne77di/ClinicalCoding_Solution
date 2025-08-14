
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Configuration; // <-- Added

namespace ClinicalCoding.Infrastructure.Graph;

public class GraphTeamsSender
{
    private readonly ILogger<GraphTeamsSender> _logger;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string[] _scopes = new[] { "https://graph.microsoft.com/.default" };

    public GraphTeamsSender(IConfiguration cfg, ILogger<GraphTeamsSender> logger)
    {
        _logger = logger;
        _tenantId = cfg["Graph:TenantId"] ?? "";
        _clientId = cfg["Graph:ClientId"] ?? "";
        _clientSecret = cfg["Graph:ClientSecret"] ?? "";
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var app = ConfidentialClientApplicationBuilder
            .Create(_clientId)
            .WithClientSecret(_clientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
            .Build();

        var res = await app.AcquireTokenForClient(_scopes).ExecuteAsync(ct);
        return res.AccessToken;
    }

    public async Task SendAdaptiveCardToUserAsync(string upn, object adaptiveCard, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        using var http = new HttpClient { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var userRes = await http.GetAsync($"users/{Uri.EscapeDataString(upn)}", ct);
        userRes.EnsureSuccessStatusCode();
        var userJson = JsonDocument.Parse(await userRes.Content.ReadAsStringAsync(ct));
        var userId = userJson.RootElement.GetProperty("id").GetString();

        var chatsRes = await http.GetAsync($"users/{userId}/chats?$filter=chatType eq 'oneOnOne'&$top=1", ct);
        chatsRes.EnsureSuccessStatusCode();
        var chatsJson = JsonDocument.Parse(await chatsRes.Content.ReadAsStringAsync(ct));
        var chatId = chatsJson.RootElement.GetProperty("value")[0].GetProperty("id").GetString();

        var message = new
        {
            body = new { contentType = "html", content = "Clinical Coding Query" },
            attachments = new[]
            {
                new {
                    id = Guid.NewGuid().ToString(),
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = adaptiveCard,
                    name = "ClinicalQueryCard"
                }
            }
        };

        var resp = await http.PostAsync($"chats/{chatId}/messages",
            new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"), ct);

        resp.EnsureSuccessStatusCode();
    }
}
