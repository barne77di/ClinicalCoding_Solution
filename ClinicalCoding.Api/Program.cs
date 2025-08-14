using Azure;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.Identity;
using ClinicalCoding.Domain.Abstractions;
using ClinicalCoding.Domain.Models;
using ClinicalCoding.Infrastructure.DLQ;
using ClinicalCoding.Infrastructure.Graph;
using ClinicalCoding.Infrastructure.Persistence;
using ClinicalCoding.Infrastructure.PowerBI;
using ClinicalCoding.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();
builder.Services.AddLogging();
builder.Services.AddApplicationInsightsTelemetry();

// ---- EF Core SQL Server ----
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<CodingDbContext>(opt => opt.UseSqlServer(conn));
builder.Services.AddScoped<EpisodeRepository>();

// ---- Azure Text Analytics ----
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var endpoint = new Uri(cfg["TextAnalytics:Endpoint"] ?? "https://example.cognitiveservices.azure.com/");
    var key = cfg["TextAnalytics:ApiKey"];
    if (!string.IsNullOrEmpty(key))
        return new TextAnalyticsClient(endpoint, new AzureKeyCredential(key));
    return new TextAnalyticsClient(endpoint, new DefaultAzureCredential());
});
builder.Services.AddSingleton<TextAnalyticsClinicalExtractor>();

// ---- Azure OpenAI ----
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var endpoint = new Uri(cfg["AzureOpenAI:Endpoint"] ?? "https://example.openai.azure.com/");
    var key = cfg["AzureOpenAI:ApiKey"];
    if (!string.IsNullOrEmpty(key))
        return new AzureOpenAIClient(endpoint, new AzureKeyCredential(key));
    return new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
});

// ---- Suggestion services (primary AOAI, fallback rules) ----
builder.Services.AddSingleton<RuleBasedSuggestionService>();
builder.Services.AddSingleton<ICodingSuggestionService>(sp =>
{
    var aoai = new AzureOpenAISuggestionService(
        sp.GetRequiredService<AzureOpenAIClient>(),
        sp.GetRequiredService<TextAnalyticsClinicalExtractor>(),
        sp.GetRequiredService<ILogger<AzureOpenAISuggestionService>>(),
        sp.GetRequiredService<IConfiguration>());
    var rule = sp.GetRequiredService<RuleBasedSuggestionService>();
    return new CompositeSuggestionService(aoai, rule, sp.GetRequiredService<ILogger<CompositeSuggestionService>>());
});

// ---- AuthN/AuthZ (Entra ID) ----
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddSingleton<GraphTeamsSender>();

builder.Services.AddSingleton<PbiPushService>();

// DLQ provider (Storage or ServiceBus)
builder.Services.AddSingleton<IDeadLetterQueue>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var provider = cfg["DLQ:Provider"] ?? "Storage";
    if (string.Equals(provider, "ServiceBus", StringComparison.OrdinalIgnoreCase))
    {
        var cs = cfg["DLQ:ServiceBus:ConnectionString"] ?? "";
        var name = cfg["DLQ:ServiceBus:QueueName"] ?? "deadletters";
        return new ServiceBusDeadLetter(cs, name);
    }
    else
    {
        var cs = cfg["DLQ:Storage:ConnectionString"] ?? "";
        var name = cfg["DLQ:Storage:QueueName"] ?? "deadletters";
        return new StorageQueueDeadLetter(cs, name);
    }
});


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Coder", p => p.RequireClaim("roles", "Coder"));
    options.AddPolicy("Reviewer", p => p.RequireClaim("roles", "Reviewer"));
});

// ---- CORS for SPA ----
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("spa",
        p => p.WithOrigins("http://localhost:5173", "https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var minIntervalMinutes = builder.Configuration.GetValue<int>("Resuggest:MinIntervalMinutes", 5);
var app = builder.Build();

// Apply EF migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CodingDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    // app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseCors("spa");
app.UseAuthentication();

var disableAuth = builder.Configuration.GetValue<bool>("Auth:Disable", false);
if (disableAuth)
{
    app.Use(async (ctx, next) =>
    {
        // If unauthenticated, inject a dev identity with Coder & Reviewer roles
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            var id = new ClaimsIdentity("DevBypass");
            id.AddClaim(new Claim("roles", "Coder"));
            id.AddClaim(new Claim("roles", "Reviewer"));
            id.AddClaim(new Claim(ClaimTypes.Upn, "dev@local"));
            ctx.User = new ClaimsPrincipal(id);
        }
        await next();
    });
}

app.UseAuthorization();

// ---- Upload + Compare (no DB write) ----
app.MapPost("/episodes/compare-upload",
    async (HttpRequest req, ICodingSuggestionService sugg, CancellationToken ct) =>
    {
        if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data required");
        var form = await req.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null) return Results.BadRequest("form-data field 'file' is required");

        string narrativeText;
        using (var sr = new StreamReader(file.OpenReadStream()))
            narrativeText = await sr.ReadToEndAsync(ct);

        var codesText = form.TryGetValue("codes", out var codesVal) ? codesVal.ToString() : null;

        // Parse supplied codes (JSON or CSV). If none, try to regex-skim from narrative.
        var (oldDx, oldPx) = TryParseCodes(codesText) ?? GuessCodesFromText(narrativeText);

        // Run suggestion on the narrative
        var ep = new Episode
        {
            NHSNumber = "0000000000",
            PatientName = "Uploaded Case",
            AdmissionDate = DateTimeOffset.UtcNow.UtcDateTime,
            Specialty = "Unknown",
            SourceText = narrativeText
        };
        var (newDx, newPx) = await sugg.SuggestAsync(ep, ct);

        // Compute deltas
        var oldDxCodes = oldDx.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newDxCodes = newDx.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldPxCodes = oldPx.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPxCodes = newPx.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dxAdded = newDxCodes.Except(oldDxCodes).ToArray();
        var dxRemoved = oldDxCodes.Except(newDxCodes).ToArray();
        var pxAdded = newPxCodes.Except(oldPxCodes).ToArray();
        var pxRemoved = oldPxCodes.Except(newPxCodes).ToArray();

        var result = new
        {
            narrativePreview = narrativeText.Length > 800 ? narrativeText[..800] + "…" : narrativeText,
            dx = new { old = oldDx, @new = newDx },
            px = new { old = oldPx, @new = newPx },
            deltas = new { dxAdded, dxRemoved, pxAdded, pxRemoved }
        };
        return Results.Json(result);
    })
// keep secured in real use; allow anonymous if you’re using dev-bypass
.RequireAuthorization();

// ---- helpers ----
static (IEnumerable<Diagnosis> dx, IEnumerable<Procedure> px)? TryParseCodes(string? codesText)
{
    if (string.IsNullOrWhiteSpace(codesText)) return null;

    // JSON format:
    // { "diagnoses":[{"code":"A41.9","description":"Sepsis","isPrimary":true}], "procedures":[{"code":"H33.8","description":"...","performedOn":"2025-08-01"}] }
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(codesText);
        var root = doc.RootElement;
        if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
            (root.TryGetProperty("diagnoses", out _) || root.TryGetProperty("procedures", out _)))
        {
            var dx = root.TryGetProperty("diagnoses", out var dxe)
    ? dxe.EnumerateArray().Select(e => new Diagnosis(
        e.GetProperty("code").GetString() ?? "",
        e.TryGetProperty("description", out var dd) ? dd.GetString() ?? "" : "",
        e.TryGetProperty("isPrimary", out var ip) && ip.GetBoolean()
      ))
    : Enumerable.Empty<Diagnosis>();

            var px = root.TryGetProperty("procedures", out var pxe)
                ? pxe.EnumerateArray().Select(e => new Procedure(
                    e.GetProperty("code").GetString() ?? "",
                    e.TryGetProperty("description", out var pd) ? pd.GetString() ?? "" : "",
                    e.TryGetProperty("performedOn", out var po) && po.ValueKind != System.Text.Json.JsonValueKind.Null
                        ? po.GetDateTime()
                        : (DateTime?)null
                  ))
                : Enumerable.Empty<Procedure>();

            return (dx, px);
        }
    }
    catch { /* not JSON, continue */ }

    // CSV format (two sections separated by a blank line). Headers optional.
    // Diagnoses:
    // Code,Description,IsPrimary
    // A41.9,Sepsis,TRUE
    //
    // Procedures:
    // Code,Description,PerformedOn
    // H33.8,Bronchoscopy,2025-08-01
    try
    {
        var parts = Regex.Split(codesText.Trim(), @"\r?\n\r?\n+"); // split on blank line
        IEnumerable<Diagnosis> dx = Enumerable.Empty<Diagnosis>();
        IEnumerable<Procedure> px = Enumerable.Empty<Procedure>();

        foreach (var block in parts)
        {
            var lines = Regex.Split(block.Trim(), @"\r?\n").Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) continue;

            // detect diagnoses header
            if (Regex.IsMatch(lines[0], @"^\s*Code\s*,\s*Description\s*,\s*IsPrimary", RegexOptions.IgnoreCase))
            {
                dx = lines.Skip(1).Select(l =>
                {
                    var cells = SplitCsv(l);
                    var code = cells.ElementAtOrDefault(0) ?? "";
                    var desc = cells.ElementAtOrDefault(1) ?? "";
                    var ip = (cells.ElementAtOrDefault(2) ?? "").Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    return new Diagnosis(code, desc, ip);
                }).ToList();
                continue;
            }

            // detect procedures header
            if (Regex.IsMatch(lines[0], @"^\s*Code\s*,\s*Description\s*,\s*PerformedOn", RegexOptions.IgnoreCase))
            {
                px = lines.Skip(1).Select(l =>
                {
                    var cells = SplitCsv(l);
                    var code = cells.ElementAtOrDefault(0) ?? "";
                    var desc = cells.ElementAtOrDefault(1) ?? "";
                    var po = DateTime.TryParse(cells.ElementAtOrDefault(2), out var dt) ? dt : (DateTime?)null;
                    return new Procedure(code, desc, po);
                }).ToList();
                continue;
            }
        }

        if (dx.Any() || px.Any()) return (dx, px);
    }
    catch { /* ignore */ }

    return null;
}

static (IEnumerable<Diagnosis> dx, IEnumerable<Procedure> px) GuessCodesFromText(string narrative)
{
    // Very simple regex pickers:
    // ICD-10 pattern: e.g., A41.9, J13, J44.1 (letter + 2 digits + optional .digit+)
    var icd = Regex.Matches(narrative, @"\b[A-TV-Z][0-9]{2}(?:\.[0-9A-Za-z]+)?\b")
                   .Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // OPCS-4 pattern: e.g., H33.8, K35.1 (letter + 2 digits + . + digit)
    var opcs = Regex.Matches(narrative, @"\b[A-Z][0-9]{2}\.[0-9A-Z]\b")
                    .Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    var dx = icd.Select((c, i) => new Diagnosis(c, "", i == 0)).ToList();
    var px = opcs.Select(c => new Procedure(c, "", null)).ToList(); 
    return (dx, px);
}

static List<string> SplitCsv(string line)
{
    // minimal CSV splitter: handles quotes and commas inside quotes
    var res = new List<string>();
    var sb = new System.Text.StringBuilder();
    bool inQ = false;
    for (int i = 0; i < line.Length; i++)
    {
        var ch = line[i];
        if (ch == '"')
        {
            if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } // escaped "
            else inQ = !inQ;
        }
        else if (ch == ',' && !inQ)
        {
            res.Add(sb.ToString()); sb.Clear();
        }
        else sb.Append(ch);
    }
    res.Add(sb.ToString());
    return res;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapGet("/episodes-open", async (CodingDbContext db, CancellationToken ct) =>
{
    var items = await db.Episodes
       // .OrderByDescending(e => e.CreatedOn)
        .Take(50)
        .Select(e => new {
            e.Id,
            e.PatientName,
            e.AdmissionDate,
            e.DischargeDate,
            e.Specialty,
            e.Status
        })
        .ToListAsync(ct);

    return Results.Ok(items);
})
.AllowAnonymous();

static string GetUser(HttpContext ctx)
    => ctx.User?.FindFirstValue("preferred_username")
       ?? ctx.User?.FindFirstValue(ClaimTypes.Upn)
       ?? ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier)
       ?? "anonymous";

var authGroup = app.MapGroup("/").RequireAuthorization();

// Common endpoints (any authenticated user)

authGroup.MapGet("episodes/{id:guid}/code-diff", async (Guid id, EpisodeRepository repo, CancellationToken ct) =>
{
    var last = await repo.GetLastResuggestAuditForEpisodeAsync(id, ct);
    if (last is null) return Results.NotFound();
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(last.PayloadJson);
        var root = doc.RootElement;
        var oldDx = root.GetProperty("oldDx");
        var newDx = root.GetProperty("newDx");
        var oldPx = root.GetProperty("oldPx");
        var newPx = root.GetProperty("newPx");
        return Results.Json(new {
            auditId = last.Id,
            dx = new { old = oldDx, @new = newDx },
            px = new { old = oldPx, @new = newPx }
        });
    }
    catch { return Results.Problem("Invalid diff payload"); }
});

authGroup.MapGet("episodes/{id:guid}", async (Guid id, EpisodeRepository repo, CancellationToken ct) =>

{
    var e = await repo.GetAsync(id, ct);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

authGroup.MapGet("episodes",
    async (EpisodeRepository repo, CancellationToken ct) =>
    {
        var items = await repo.ListAsync(200, ct); // existing method
        return Results.Ok(new { items, total = items.Count, page = 1, pageSize = items.Count });
    });


authGroup.MapPost("deadletter/{id:guid}/retry", async (Guid id, EpisodeRepository repo, CancellationToken ct) =>
{
    var dl = await repo.GetDeadLetterAsync(id, ct);
    if (dl is null) return Results.NotFound();
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(dl.PayloadJson);
        var responder = doc.RootElement.TryGetProperty("responder", out var r) ? r.GetString() : null;
        var text = doc.RootElement.TryGetProperty("responseText", out var t) ? t.GetString() : dl.PayloadJson;
        var queryId = doc.RootElement.TryGetProperty("queryId", out var q) ? q.GetString() : null;
        if (Guid.TryParse(queryId, out var qid))
        {
            await repo.UpdateQueryResponseAsync(qid, responder, text ?? "", ct);
            await repo.UpdateDeadLetterAsync(dl, ct);
            return Results.NoContent();
        }
        return Results.BadRequest();
    }
    catch { return Results.BadRequest(); }
});

authGroup.MapGet("audit", 
 async (EpisodeRepository repo, CancellationToken ct) =>
{
    return Results.Ok(await repo.ListAuditAsync(200, ct));
});

authGroup.MapGet("export/episodes.csv", async (EpisodeRepository repo, CancellationToken ct) =>
{
    var list = await repo.ListAsync(500, ct);
    var sb = new StringBuilder();
    sb.AppendLine("Id,NHSNumber,PatientName,AdmissionDate,DischargeDate,Specialty,Status");
    foreach (var e in list)
    {
        sb.AppendLine($"{e.Id},{e.NHSNumber},\"{e.PatientName}\",{e.AdmissionDate:o},{e.DischargeDate:o},{e.Specialty},{e.Status}");
    }
    return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
});

authGroup.MapGet("export/episodes.json", async (EpisodeRepository repo, CancellationToken ct) =>
{
    var list = await repo.ListAsync(500, ct);
    return Results.Json(list);
});

// Coder-only endpoints
var coder = authGroup.MapGroup("/").RequireAuthorization("Coder");

coder.MapPost("episodes/suggest", async (Episode episode, ICodingSuggestionService svc, CancellationToken ct) =>
{
    var (dx, px) = await svc.SuggestAsync(episode, ct);
    episode.Diagnoses = dx.ToList();
    episode.Procedures = px.ToList();
    return Results.Ok(episode);
});

coder.MapPost("episodes", async (HttpContext ctx, Episode episode, ICodingSuggestionService svc, EpisodeRepository repo, CancellationToken ct) =>
{
    var (dx, px) = await svc.SuggestAsync(episode, ct);
    var entity = new EpisodeEntity
    {
        NHSNumber = episode.NHSNumber,
        PatientName = episode.PatientName,
        AdmissionDate = episode.AdmissionDate,
        DischargeDate = episode.DischargeDate,
        Specialty = episode.Specialty,
        SourceText = episode.SourceText,
        Diagnoses = dx.Select(d => new DiagnosisEntity { Code = d.Code, Description = d.Description, IsPrimary = d.IsPrimary }).ToList(),
        Procedures = px.Select(p => new ProcedureEntity { Code = p.Code, Description = p.Description, PerformedOn = p.PerformedOn }).ToList()
    };
    entity = await repo.AddAsync(entity, ct);

    await repo.AddAuditAsync(new AuditEntry
    {
        PerformedBy = GetUser(ctx),
        Action = "EpisodeCreated",
        EntityType = nameof(EpisodeEntity),
        EntityId = entity.Id.ToString(),
        PayloadJson = JsonSerializer.Serialize(new { episode, dx, px })
    }, ct);

    return Results.Created($"/episodes/{entity.Id}", new { entity.Id });
});

coder.MapPost("episodes/{id:guid}/submit", async (HttpContext ctx, Guid id, EpisodeRepository repo, CancellationToken ct) =>
{
    await repo.SubmitAsync(id, GetUser(ctx), ct);
    await repo.AddAuditAsync(new AuditEntry
    {
        PerformedBy = GetUser(ctx),
        Action = "EpisodeSubmitted",
        EntityType = nameof(EpisodeEntity),
        EntityId = id.ToString(),
        PayloadJson = "{}"
    }, ct);
    return Results.NoContent();
});

// Reviewer-only endpoints
var reviewer = authGroup.MapGroup("/").RequireAuthorization("Reviewer");

reviewer.MapPost("episodes/{id:guid}/approve", async (HttpContext ctx, Guid id, string? notes, EpisodeRepository repo, CancellationToken ct) =>
{
    await repo.ApproveAsync(id, GetUser(ctx), notes, ct);
    await repo.AddAuditAsync(new AuditEntry
    {
        PerformedBy = GetUser(ctx),
        Action = "EpisodeApproved",
        EntityType = nameof(EpisodeEntity),
        EntityId = id.ToString(),
        PayloadJson = JsonSerializer.Serialize(new { notes })
    }, ct);
    return Results.NoContent();
});



reviewer.MapPost("episodes/{id:guid}/revert-request", async (HttpContext ctx, Guid id, Guid auditId, EpisodeRepository repo, CancellationToken ct) =>
{
    var req = await repo.CreateRevertRequestAsync(id, auditId, ctx.User?.Identity?.Name ?? "reviewer", ct);
    await repo.AddAuditAsync(new AuditEntry { PerformedBy = req.RequestedBy, Action = "RevertRequested", EntityType = nameof(EpisodeEntity), EntityId = id.ToString(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { req.Id, auditId }) }, ct);
    return Results.Created($"/reverts/{req.Id}", new { req.Id });
});

reviewer.MapPost("episodes/{id:guid}/revert-approve", async (HttpContext ctx, Guid id, Guid requestId, EpisodeRepository repo, CancellationToken ct) =>
{
    var rr = await repo.GetRevertRequestAsync(requestId, ct);
    if (rr is null || rr.EpisodeId != id || rr.Status != RevertStatus.Pending) return Results.BadRequest();
    var approver = ctx.User?.Identity?.Name ?? "reviewer2";
    if (approver == rr.RequestedBy) return Results.Forbid(); // must be a different reviewer
    await repo.ApproveRevertAsync(requestId, approver, ct);

    // Apply the revert using the stored audit
    var audit = await repo.GetAuditByIdAsync(rr.AuditId, ct);
    if (audit is null) return Results.NotFound();
    using var doc = System.Text.Json.JsonDocument.Parse(audit.PayloadJson);
    var oldDx = doc.RootElement.GetProperty("oldDx").EnumerateArray().Select(e => new DiagnosisEntity { Code = e.GetProperty("Code").GetString() ?? "", Description = e.GetProperty("Description").GetString() ?? "", IsPrimary = e.GetProperty("IsPrimary").GetBoolean() });
    var oldPx = doc.RootElement.GetProperty("oldPx").EnumerateArray().Select(e => new ProcedureEntity { Code = e.GetProperty("Code").GetString() ?? "", Description = e.GetProperty("Description").GetString() ?? "", PerformedOn = e.TryGetProperty("PerformedOn", out var po) && po.ValueKind!=System.Text.Json.JsonValueKind.Null ? po.GetDateTime() : (DateTime?)null });
    await repo.ReplaceEpisodeCodesAsync(id, oldDx, oldPx, ct);
    await repo.AddAuditAsync(new AuditEntry { PerformedBy = approver, Action = "ReSuggestionReverted", EntityType = nameof(EpisodeEntity), EntityId = id.ToString(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { requestId }) }, ct);
    return Results.NoContent();
});

reviewer.MapPost("episodes/{id:guid}/revert-reject", async (HttpContext ctx, Guid id, Guid requestId, EpisodeRepository repo, CancellationToken ct) =>
{
    var rr = await repo.GetRevertRequestAsync(requestId, ct);
    if (rr is null || rr.EpisodeId != id || rr.Status != RevertStatus.Pending) return Results.BadRequest();
    var approver = ctx.User?.Identity?.Name ?? "reviewer2";
    if (approver == rr.RequestedBy) return Results.Forbid();
    await repo.RejectRevertAsync(requestId, approver, ct);
    await repo.AddAuditAsync(new AuditEntry { PerformedBy = approver, Action = "RevertRejected", EntityType = nameof(EpisodeEntity), EntityId = id.ToString(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { requestId }) }, ct);
    return Results.NoContent();
});

reviewer.MapPost("episodes/{id:guid}/revert",
 async (Guid id, Guid auditId, EpisodeRepository repo, CancellationToken ct) =>
{
    var audit = await repo.GetAuditByIdAsync(auditId, ct);
    if (audit is null) return Results.NotFound();
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(audit.PayloadJson);
        var oldDx = doc.RootElement.GetProperty("oldDx").EnumerateArray().Select(e => new DiagnosisEntity { Code = e.GetProperty("Code").GetString() ?? "", Description = e.GetProperty("Description").GetString() ?? "", IsPrimary = e.GetProperty("IsPrimary").GetBoolean() });
        var oldPx = doc.RootElement.GetProperty("oldPx").EnumerateArray().Select(e => new ProcedureEntity { Code = e.GetProperty("Code").GetString() ?? "", Description = e.GetProperty("Description").GetString() ?? "", PerformedOn = e.TryGetProperty("PerformedOn", out var po) && po.ValueKind!=System.Text.Json.JsonValueKind.Null ? po.GetDateTime() : (DateTime?)null });
        await repo.ReplaceEpisodeCodesAsync(id, oldDx, oldPx, ct);
        await repo.AddAuditAsync(new AuditEntry { PerformedBy = "reviewer", Action = "ReSuggestionReverted", EntityType = nameof(EpisodeEntity), EntityId = id.ToString(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { auditId }) }, ct);
        return Results.NoContent();
    }
    catch { return Results.Problem("Invalid audit payload"); }
});

reviewer.MapPost("episodes/{id:guid}/reject",
 async (HttpContext ctx, Guid id, string? notes, EpisodeRepository repo, CancellationToken ct) =>
{
    await repo.RejectAsync(id, GetUser(ctx), notes, ct);
    await repo.AddAuditAsync(new AuditEntry
    {
        PerformedBy = GetUser(ctx),
        Action = "EpisodeRejected",
        EntityType = nameof(EpisodeEntity),
        EntityId = id.ToString(),
        PayloadJson = JsonSerializer.Serialize(new { notes })
    }, ct);
    return Results.NoContent();
});

// Clinician query draft -> Power Automate webhook
coder.MapPost("episodes/{id:guid}/queries", async (HttpContext ctx, Guid id, ClinicianQuery q, EpisodeRepository repo, IConfiguration cfg, CancellationToken ct) =>
{
    var entry = new ClinicianQueryEntity
    {
        EpisodeId = id,
        ToClinician = q.ToClinician,
        Subject = string.IsNullOrWhiteSpace(q.Subject) ? "Clinical Coding Query" : q.Subject,
        Body = q.Body,
        CreatedBy = GetUser(ctx)
    };
    entry = await repo.CreateQueryAsync(entry, ct);

    var hook = cfg["PowerAutomate:WebhookUrl"];
    if (!string.IsNullOrWhiteSpace(hook))
    {
        try
        {
            using var http = new HttpClient();
            var payload = new
            {
                episodeId = id,
                to = entry.ToClinician,
                subject = entry.Subject,
                body = entry.Body,
                createdBy = entry.CreatedBy,
                createdOn = entry.CreatedOn
            };
            var res = await http.PostAsync(hook,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
            entry.ExternalReference = $"HTTP {(int)res.StatusCode}";
            await repo.CreateQueryAsync(entry, ct); // update ext ref quickly
        }
        catch
        {
            // swallow; audit below
        }
    }

    await repo.AddAuditAsync(new AuditEntry
    {
        PerformedBy = GetUser(ctx),
        Action = "ClinicianQueryCreated",
        EntityType = nameof(ClinicianQueryEntity),
        EntityId = entry.Id.ToString(),
        PayloadJson = JsonSerializer.Serialize(new { entry })
    }, ct);

    return Results.Created($"/queries/{entry.Id}", new { entry.Id });
});


coder.MapPost("teams/sendCard", async (GraphTeamsSender graph, IConfiguration cfg, CancellationToken ct) =>
{
    var card = System.Text.Json.JsonSerializer.Deserialize<object>(System.IO.File.ReadAllText("docs/AdaptiveCard_ClinicalQuery.json"));
    var to = cfg["Graph:TestUserUpn"] ?? "user@contoso.com";
    await graph.SendAdaptiveCardToUserAsync(to, card!, ct);
    return Results.Ok(new { sentTo = to });
});


// Authenticated response (internal)
authGroup.MapPost("queries/{id:guid}/response", async (Guid id, string? responder, string responseText, EpisodeRepository repo, CancellationToken ct) =>
{
    var ok = await repo.UpdateQueryResponseAsync(id, responder, responseText, ct);
    return ok ? Results.NoContent() : Results.NotFound();
})
.WithSummary("Record clinician query response (authenticated)");
// Webhook for Flow/Adaptive Card (shared secret)

app.MapPost("/webhooks/flow/queries/{id:guid}/response", async (Guid id, HttpRequest req, EpisodeRepository repo, IConfiguration cfg, ICodingSuggestionService sugg, PbiPushService pbi, CancellationToken ct) =>
{
    var secret = cfg["Webhooks:FlowSecret"];
    string ComputeHmac(string secretKey, string body)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var hash = h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body));
        return "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync(ct);
    if (string.IsNullOrEmpty(secret)) return Results.Unauthorized();
    if (!req.Headers.TryGetValue("x-signature", out var sig)) return Results.Unauthorized();
    var expected = ComputeHmac(secret, body);
    if (!string.Equals(sig.ToString(), expected, StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();

    using var doc = System.Text.Json.JsonDocument.Parse(body);
    var responder = doc.RootElement.TryGetProperty("responder", out var r) ? r.GetString() : null;
    var text = doc.RootElement.TryGetProperty("responseText", out var t) ? t.GetString() : body;

    // Persist the response
    var ok = await repo.UpdateQueryResponseAsync(id, responder, text ?? "", ct);
    if (!ok) return Results.NotFound();

    // Find the episode via the query
    var query = await repo.GetQueryAsync(id, ct);
    // Debounce: skip if a resuggest was applied very recently
    if (query is not null && minIntervalMinutes > 0)
    {
        var last = await repo.GetLastResuggestAuditForEpisodeAsync(query.EpisodeId, ct);
        if (last is not null && (DateTimeOffset.UtcNow - last.Timestamp).TotalMinutes < minIntervalMinutes)
        {
            await repo.AddAuditAsync(new AuditEntry { PerformedBy = responder ?? "flow", Action = "ReSuggestionSkipped_Debounce", EntityType = nameof(EpisodeEntity), EntityId = query.EpisodeId.ToString(), PayloadJson = body }, ct);
            return Results.Accepted();
        }
    }
    if (query is not null)
    {
        var ep = await repo.GetEpisodeWithDetailsAsync(query.EpisodeId, ct);
        if (ep is not null)
        {
            // Append the clinician response into the source text
            ep.SourceText += "\n\nClinician response (" + (responder ?? "unknown") + " on " + DateTimeOffset.UtcNow.ToString("u") + "):\n" + (text ?? "");
            // Run suggestions again
            var episodeModel = new Episode
            {
                NHSNumber = ep.NHSNumber,
                PatientName = ep.PatientName,
                AdmissionDate = ep.AdmissionDate,
                DischargeDate = ep.DischargeDate,
                Specialty = ep.Specialty,
                SourceText = ep.SourceText
            };
            var (dxNew, pxNew) = await sugg.SuggestAsync(episodeModel, ct);

            // Compute deltas
            var dxOldCodes = ep.Diagnoses.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pxOldCodes = ep.Procedures.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dxNewCodes = dxNew.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pxNewCodes = pxNew.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dxAdded = dxNewCodes.Except(dxOldCodes).ToArray();
            var dxRemoved = dxOldCodes.Except(dxNewCodes).ToArray();
            var pxAdded = pxNewCodes.Except(pxOldCodes).ToArray();
            var pxRemoved = pxOldCodes.Except(pxNewCodes).ToArray();

            // Persist replacement
            await repo.ReplaceEpisodeCodesAsync(ep.Id,
                dxNew.Select(d => new DiagnosisEntity { Code = d.Code, Description = d.Description, IsPrimary = d.IsPrimary }),
                pxNew.Select(p => new ProcedureEntity { Code = p.Code, Description = p.Description, PerformedOn = p.PerformedOn }),
                ct);

            // Audit with full old/new sets for diff & revert
            var oldDx = ep.Diagnoses.Select(d => new { d.Code, d.Description, d.IsPrimary }).ToArray();
            var oldPx = ep.Procedures.Select(p => new { p.Code, p.Description, p.PerformedOn }).ToArray();
            var newDx = dxNew.Select(d => new { d.Code, d.Description, d.IsPrimary }).ToArray();
            var newPx = pxNew.Select(p => new { p.Code, p.Description, p.PerformedOn }).ToArray();

            await repo.AddAuditAsync(new AuditEntry
            {
                PerformedBy = responder ?? "flow",
                Action = "ReSuggestionApplied",
                EntityType = nameof(EpisodeEntity),
                EntityId = ep.Id.ToString(),
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { dxAdded, dxRemoved, pxAdded, pxRemoved, oldDx, oldPx, newDx, newPx })
            }, ct);

            // Push deltas to Power BI
            await pbi.PushRowsAsync("SuggestionDeltas", new [] {
                new {
                    EpisodeId = ep.Id.ToString(),
                    EventUtc = DateTimeOffset.UtcNow,
                    DxAdded = string.Join("|", dxAdded),
                    DxRemoved = string.Join("|", dxRemoved),
                    PxAdded = string.Join("|", pxAdded),
                    PxRemoved = string.Join("|", pxRemoved)
                }
            }, ct);
        }
    }

    return Results.NoContent();
})
.WithSummary("Flow webhook to record clinician response (HMAC) and auto re-suggest + update");

app.Run();



public class CompositeSuggestionService(ICodingSuggestionService primary, ICodingSuggestionService fallback, ILogger<CompositeSuggestionService> logger) : ICodingSuggestionService
{
    public async Task<(IEnumerable<Diagnosis> diagnoses, IEnumerable<Procedure> procedures)> SuggestAsync(Episode episode, CancellationToken ct = default)
    {
        var (dx, px) = await primary.SuggestAsync(episode, ct);
        if (!dx.Any() && !px.Any())
        {
            logger.LogInformation("Primary suggestion engine returned empty; using fallback.");
            return await fallback.SuggestAsync(episode, ct);
        }
        return (dx, px);
    }
}
