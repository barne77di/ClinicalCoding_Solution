using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Configuration;

namespace ClinicalCoding.Infrastructure.PowerBI;

public class PbiPushService
{
    private readonly ILogger<PbiPushService> _logger;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _workspaceId;
    private readonly string _datasetId;

    private static readonly string[] Scopes = new []{ "https://analysis.windows.net/powerbi/api/.default" };

    public PbiPushService(IConfiguration cfg, ILogger<PbiPushService> logger)
    {
        _logger = logger;
        _tenantId = cfg["PowerBI:TenantId"] ?? "";
        _clientId = cfg["PowerBI:ClientId"] ?? "";
        _clientSecret = cfg["PowerBI:ClientSecret"] ?? "";
        _workspaceId = cfg["PowerBI:WorkspaceId"] ?? "";
        _datasetId = cfg["PowerBI:DatasetId"] ?? "";
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_tenantId) || string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            return null;

        var app = ConfidentialClientApplicationBuilder
            .Create(_clientId)
            .WithClientSecret(_clientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
            .Build();

        var res = await app.AcquireTokenForClient(Scopes).ExecuteAsync(ct);
        return res.AccessToken;
    }

    public async Task<bool> PushRowsAsync(string tableName, IEnumerable<object> rows, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_workspaceId) || string.IsNullOrWhiteSpace(_datasetId))
            return false;

        using var http = new HttpClient { BaseAddress = new Uri("https://api.powerbi.com/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var url = $"v1.0/myorg/groups/{_workspaceId}/datasets/{_datasetId}/tables/{tableName}/rows";
        var payload = new { rows = rows.ToArray() };
        var resp = await http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
        _logger.LogInformation("PBI push rows to {Table}: {Status}", tableName, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }
}
