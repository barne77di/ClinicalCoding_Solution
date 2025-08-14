**Clinical Coding – AI-Assisted Coding Demo (NET 9)**
An end-to-end sample solution that:

ingests a clinical episode,

suggests ICD-10 (UK) diagnoses and OPCS-4 procedures (Azure OpenAI + Text Analytics with rule-based fallback),

supports coder/reviewer workflow (submit/approve/reject),

sends clinician queries via Teams (Power Automate or Graph),

auto re-suggests on clinician reply, computes old vs new code diffs, and supports revert (with optional 2-person approval),

pushes deltas to Power BI,

includes DLQ (Storage Queue or Service Bus), Application Insights, and a React admin SPA.

⚠️ This is a demo. Do not use real patient data.

**Solution layout**
	ClinicalCoding.Domain/            # Models (Episode, Diagnosis, Procedure, etc.)
	ClinicalCoding.Infrastructure/    # EF Core DbContext, Repositories, Services (AOAI, TextAnalytics, DLQ, Graph, PowerBI)
	ClinicalCoding.Api/               # Minimal API (.NET 9) + endpoints + OpenAPI JSON + static assets (diff.html)
	ClinicalCoding.Worker/            # (optional) background worker examples (DLQ processing)
	ClinicalCoding.Web/               # React (Vite) SPA admin UI
	docs/                             # Adaptive Card examples, etc.

**Prerequisites**

.NET 9 SDK

Node 18+ and npm

SQL Server (LocalDB or full)

(Optional) Azure: OpenAI, Cognitive Services Text Analytics, Storage/Service Bus, App Insights, Power BI Workspace
(Optional) Entra ID (Azure AD) app registrations (API + SPA)

**Configure**
**
****1) Database ******

ClinicalCoding.Api/appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ClinicalCoding;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "AzureOpenAI": { "Endpoint": "", "ApiKey": "" },
  "TextAnalytics": { "Endpoint": "", "ApiKey": "" },
  "DLQ": {
    "Provider": "Storage",
    "Storage": { "ConnectionString": "UseDevelopmentStorage=true", "QueueName": "deadletters" }
  },
  "Resuggest": { "MinIntervalMinutes": 5 },
  "Webhooks": { "FlowSecret": "devsecret" },
  "Auth": { "Disable": true }  // DEV BYPASS, see below
}

On first run the API applies EF migrations automatically.
If you prefer manual DB creation, run the solution once (it will create schema), or run your generated SQL scripts.

**2) Dev auth bypass (for local testing)**


If you don’t want to sign in during dev:

Set "Auth": { "Disable": true } (as above).

Ensure Program.cs contains the middleware between UseAuthentication() and UseAuthorization():

app.UseCors("spa");
app.UseAuthentication();

if (builder.Configuration.GetValue<bool>("Auth:Disable", false))
{
    app.Use(async (ctx, next) =>
    {
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

**3) SPA environment**
Create ClinicalCoding.Web/.env.local:

VITE_API_BASE=https://localhost:7249
VITE_BYPASS_AUTH=true
# If you want real auth instead of bypass:
# VITE_BYPASS_AUTH=false
# VITE_AAD_CLIENT_ID=<SPA App (client) ID>
# VITE_AAD_TENANT_ID=<Tenant ID>
# VITE_REDIRECT_URI=http://localhost:5173
# VITE_API_SCOPE=api://<API App (client) ID>/user_impersonation

If using real auth: create two Entra apps (API + SPA). Expose user_impersonation on API; assign SPA the delegated scope; add redirect http://localhost:5173; grant admin consent.

**Run locally**

dotnet dev-certs https --trust
cd ClinicalCoding.Api
dotnet run

Look for:
Now listening on: https://localhost:7249

**Check:**

https://localhost:7249/health → { status: "ok" }

https://localhost:7249/openapi/v1.json → OpenAPI JSON

https://localhost:7249/diff.html → diff demo page

cd ClinicalCoding.Web
npm install
npm run dev

Open http://localhost:5173
If you change VITE_API_BASE, restart npm run dev

**What it does (happy path)**


Suggest (no DB write)
POST /episodes/suggest → AOAI/Text Analytics → ICD-10/OPCS-4 suggestions.

Create Episode
POST /episodes → persists Episode + codes, writes EpisodeCreated Audit.

List Episodes
GET /episodes?status&from&to&page&pageSize (secured)
UI shows table with workflow buttons; exports at /export/episodes.csv and /export/episodes.json.

Submit / Approve / Reject

POST /episodes/{id}/submit (Coder)

POST /episodes/{id}/approve?notes=... (Reviewer)

POST /episodes/{id}/reject?notes=... (Reviewer)

Clinician query via Teams

POST /episodes/{id}/queries (Coder) → creates ClinicianQuery + calls Power Automate webhook (if configured).

Or send Adaptive Card directly using Graph (see GraphTeamsSender).

Clinician reply (Power Automate webhook → HMAC)
Flow posts to:

bash
Copy
Edit
POST /webhooks/flow/queries/{id}/response
Headers: x-signature: sha256=<HMAC body with Webhooks:FlowSecret>
Body: { "responder":"Dr X", "responseText":"..." }
Pipeline:

Verify HMAC → store response → find episode

Debounce (skip if a recent re-suggest occurred)

Append reply to Episode.SourceText → run SuggestAsync

Compute deltas (added/removed) vs. current codes

Replace codes → write ReSuggestionApplied Audit with old/new sets

Push a row to Power BI push dataset (SuggestionDeltas)

Review code diff & Revert

GET /episodes/{id}/code-diff → latest “old vs new” from audit

Revert options:

POST /episodes/{id}/revert?auditId=... (direct)

Two-person flow:

POST /episodes/{id}/revert-request (Reviewer A)

POST /episodes/{id}/revert-approve (Reviewer B) or .../revert-reject

Extra features
Upload & Compare (no DB write)
Upload a narrative (and optionally original codes) and get side-by-side comparison.

Endpoint: POST /episodes/compare-upload (multipart/form-data)
Fields:

file (required): narrative (TXT/CSV/JSON)

codes (optional): coder codes JSON or CSV

Returns:
{
  "narrativePreview":"...",
  "dx": { "old":[...], "new":[...] },
  "px": { "old":[...], "new":[...] },
  "deltas": { "dxAdded":["J13"], "dxRemoved":["A41.9"], "pxAdded":[], "pxRemoved":["H33.8","X29.9"] }
}

SPA section Upload & Compare lets you pick a file, paste codes, and view the diff immediately.

DLQ + retry
Choose Storage Queue or Service Bus via config.

Failed webhook or downstream operations are dead-lettered.

Admin retry: POST /deadletter/{id}/retry.

Application Insights
builder.Services.AddApplicationInsightsTelemetry();

Distributed tracing & logs across API/services.

Power BI push dataset
PbiPushService.PushRowsAsync("SuggestionDeltas", ...) per resuggest.

Teams (Adaptive Cards)
Flow path uses webhook + HMAC to return replies.

Graph path can send cards directly with app-only permissions (see GraphTeamsSender).

Roles & RBAC
JWT roles claim: Coder, Reviewer

Policies:

Coder endpoints: create, suggest, submit, clinician query

Reviewer endpoints: approve, reject, revert (+ two-person revert when enabled)

In dev bypass, you get both roles.

Key endpoints (API)
Public health & assets

GET /health

GET /openapi/v1.json

GET /diff.html

Episodes

POST /episodes/suggest

POST /episodes

GET /episodes?status&from&to&page&pageSize

GET /episodes/{id}

POST /episodes/{id}/submit (Coder)

POST /episodes/{id}/approve?notes=... (Reviewer)

POST /episodes/{id}/reject?notes=... (Reviewer)

Diff & revert

GET /episodes/{id}/code-diff

POST /episodes/{id}/revert?auditId=... (Reviewer)

POST /episodes/{id}/revert-request → POST /episodes/{id}/revert-approve|revert-reject

Clinician queries

POST /episodes/{id}/queries (Coder)

POST /queries/{id}/response (internal)

POST /webhooks/flow/queries/{id}/response (HMAC webhook)

Export & audit

GET /export/episodes.csv

GET /export/episodes.json

GET /audit

Upload & compare

POST /episodes/compare-upload (multipart)

DLQ

POST /deadletter/{id}/retry

Optional dev helpers you may have added locally:

POST /episodes/{id}/resuggest-dev (simulate clinician reply + re-suggest)

POST /episodes/{id}/set-codes-dev (set baseline codes for comparison)

SPA tips
Buttons:

Get Suggestions (stateless)

Create Episode

List Episodes (filters + export)

Submit/Approve/Reject

Draft Clinician Query

Diff (opens old vs new panel) + Revert

Upload & Compare (file + optional codes → instant diff)

If List Episodes shows nothing:

Check DevTools → Network for /episodes (401 vs 200).

Use dev bypass or sign in with a token (set VITE_* auth vars).

Ensure VITE_API_BASE points to the port your API printed.

Power Automate webhook (HMAC) test
PowerShell:
$QueryId = "<GUID from dbo.ClinicianQueries>"
$Body = '{"responder":"Dr Smith","responseText":"Confirmed Strep pneumoniae; no sepsis on admission."}'
$Secret = "devsecret"

$hmac = New-Object System.Security.Cryptography.HMACSHA256 ([Text.Encoding]::UTF8.GetBytes($Secret))
$sig  = "sha256=" + (($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Body)) | ForEach-Object { $_.ToString("x2") }) -join "")

Invoke-RestMethod -Method POST "https://localhost:7249/webhooks/flow/queries/$QueryId/response" `
  -Headers @{ "x-signature" = $sig } -Body $Body -ContentType "application/json"

Then in SPA click Diff next to that Episode.

Troubleshooting
ERR_CONNECTION_REFUSED from SPA
API not running on that port. Start API and set VITE_API_BASE to the exact URL printed by dotnet run.

401 on /episodes
Endpoint is secured. Enable dev bypass or sign in from SPA (set VITE_AAD_* + VITE_API_SCOPE).

OpenAPI/Swagger issues
This uses built-in OpenAPI JSON at /openapi/v1.json. If you previously added Swashbuckle and saw TypeLoadException, remove it or align versions. The system runs fine with built-in OpenAPI only.

EF migration failures on startup
Wrap db.Database.Migrate() in try/catch (already in template) or fix connection string.

Re-suggest seems to do nothing
Debounce may skip. Set "Resuggest": { "MinIntervalMinutes": 0 } for testing. Check /audit for ReSuggestionApplied.

Vite says env var missing
Place .env.local in ClinicalCoding.Web folder, restart npm run dev. Use .env.development.local if needed.

Security notes
All write endpoints are behind Entra ID auth & role policies (except dev bypass).

Webhook is HMAC-signed (x-signature: sha256=<HMAC(body)>).

No real PHI—sample texts only.

Next ideas
Add DOCX/PDF text extraction on server (e.g., DocX, PdfPig) in compare-upload.

Add per-user queues / worklists and episode locking.

Add SNOMED CT mapping view.

Add cost/DRG impact preview for diffs.

License
Sample code for demo/education. Review and harden before any real use.
