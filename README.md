
# ClinicalCoding (.NET 9) – AI-Assisted Clinical Coding (Starter Solution)

This Visual Studio solution provides a clean starting point for building an AI-assisted clinical coding system on Microsoft Cloud.

## Projects

- **ClinicalCoding.Api** (ASP.NET Core Minimal API, Swagger enabled)
- **ClinicalCoding.Domain** (Episode/Diagnosis/Procedure models + abstractions)
- **ClinicalCoding.Infrastructure** (Rule-based suggestion service; swap with Azure services later)
- **ClinicalCoding.Worker** (Background worker for queued episode processing)
- **ClinicalCoding.Tests** (xUnit tests)

## Run locally

1. Open `ClinicalCoding.sln` in Visual Studio 2022 (17.10+) or VS 2025 Preview.
2. Set **ClinicalCoding.Api** as Startup Project and press F5.
3. Browse to Swagger UI and try **POST /episodes/suggest** with sample body:

```json
{
  "nhsNumber": "9999999999",
  "patientName": "John Smith",
  "admissionDate": "2025-08-01T10:00:00Z",
  "dischargeDate": "2025-08-06T15:00:00Z",
  "specialty": "Respiratory Medicine",
  "sourceText": "Admitted with community-acquired pneumonia (right lower lobe). Background COPD. CXR performed, nebulisation and oxygen given."
}
```

## Next steps (swap rule-based for Azure AI)

- Replace `RuleBasedSuggestionService` with integrations to:
  - Azure AI Text Analytics for Health
  - Azure OpenAI (prompt with coding rules)
  - Azure Cognitive Search (evidence retrieval)
- Add persistence (EF Core to Azure SQL) and identity (Entra ID).
- Add queue ingestion (Azure Storage Queues / Service Bus) to the Worker.
- Add Power BI dataset refresh via Web API.

> This repo is intentionally dependency-light so it builds without external services; wire up cloud dependencies as you go.


---

## Entra ID (Azure AD) Authentication

1. **Register API app** in Entra ID:
   - Name: `ClinicalCoding.Api`
   - App ID URI: `api://<API_CLIENT_ID>`
   - Expose an API → add scope `access_as_user` for user delegated access.
2. **Register SPA app**:
   - Name: `ClinicalCoding.Web`
   - Platform: Single-page application
   - Redirect URI: `http://localhost:5173`
   - Add permission to your API: scope `access_as_user` (grant admin consent).
3. **Configure** `appsettings.json` → `AzureAd` with TenantId, ClientId (API), Audience (`api://<API_CLIENT_ID>`).
4. **Configure** `ClinicalCoding.Web/.env` based on `.env.example`:
   - `VITE_TENANT_ID`, `VITE_AAD_CLIENT_ID` (SPA), `VITE_API_SCOPE=api://<API_CLIENT_ID>/access_as_user`.

> The API endpoints require authorization and CORS allows the SPA at `http://localhost:5173`.

### Running the SPA

```bash
cd ClinicalCoding.Web
npm install
npm run dev
```

### EF Core migrations

Create proper schema with migrations (recommended for non-dev):

```bash
dotnet tool install --global dotnet-ef
dotnet add ClinicalCoding.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add InitialCreate -p ClinicalCoding.Infrastructure -s ClinicalCoding.Api
dotnet ef database update -p ClinicalCoding.Infrastructure -s ClinicalCoding.Api
```



---

## Docker Compose (API + SQL + Web)

```bash
docker compose up --build
```
- API at `http://localhost:8080`
- SPA at `http://localhost:5173`
- SQL Server at `localhost,1433` (sa / YourStrong(!)Password)

Set environment variables (e.g. via `.env`) for Azure keys and Entra config used in `docker-compose.yml`.

## Power BI

Use **Web** connector:
- JSON: `http://localhost:8080/export/episodes.json`
- CSV:  `http://localhost:8080/export/episodes.csv`

You can schedule refresh after publishing to Power BI Service.



---

## RBAC & Approvals

- Define **App roles** on the API app registration (Entra ID → App roles):
  - `Coder` (value: `Coder`)
  - `Reviewer` (value: `Reviewer`)
- Assign roles to users or groups. The role arrives in the `roles` claim.
- Policies:
  - `Coder` can create episodes, request suggestions, submit drafts, and create clinician queries.
  - `Reviewer` can approve or reject submitted episodes.

**Workflow states**: Draft → Submitted → Approved/Rejected

**Endpoints:**
- `POST /episodes/{id}/submit` (Coder)
- `POST /episodes/{id}/approve?notes=...` (Reviewer)
- `POST /episodes/{id}/reject?notes=...` (Reviewer)
- `POST /episodes/{id}/queries` (Coder) → sends payload to Power Automate webhook

## Power Automate (Teams query)

Create an **Instant cloud flow** with a **When an HTTP request is received** trigger. Add steps to:
- Post an Adaptive Card to a Teams channel or chat for the clinician
- Optionally collect a response and PATCH back to your API

Copy the trigger URL into `PowerAutomate:WebhookUrl` (appsettings or environment variable). The API will POST:
```json
{
  "episodeId": "guid",
  "to": "dr.smith@hospital.nhs.uk",
  "subject": "Clinical Coding Query",
  "body": "Please clarify ...",
  "createdBy": "user@domain",
  "createdOn": "2025-08-12T14:00:00Z"
}
```



---

## Clinician queries – Teams options

### A) Power Automate (recommended for citizen dev)
- Import `docs/PowerAutomate_SampleFlow_ClinicalQuery.json` into a new Flow.
- It exposes an HTTP trigger and posts the Adaptive Card in Teams ("Post adaptive card and wait for a response").
- Put the trigger URL into `PowerAutomate:WebhookUrl`.

### B) Graph API (application permissions)
- Use **application permissions**: `Chat.ReadWrite.All` and `User.Read.All` (admin consent).
- Create a client secret and set values in `appsettings.json` under `Graph`.
- Endpoint (Coder role): `POST /teams/sendCard` will send the sample `docs/AdaptiveCard_ClinicalQuery.json` card to `Graph:TestUserUpn` in a 1:1 chat (sample logic).
  > Note: For production, prefer sending to a channel or use **sendActivityNotification**. Direct 1:1 app-only posting has constraints and may require proactive installation of your Teams app.

### Adaptive Card
- Template stored at `docs/AdaptiveCard_ClinicalQuery.json` (v1.5). Replace `${...}` tokens before sending or bind using your Flow/template step.

---

## Reviewer dashboard
- SPA now shows filters (Status, From/To date placeholders) and provides **Export CSV/JSON** links.
- Buttons appear based on the user's role claims (`Coder`/`Reviewer`).



---

## Adaptive Card response wiring

Two options to record clinician responses:

1) **Authenticated API** (internal tools):  
   `POST /queries/{id}/response` with JSON or form params `responder`, `responseText`.

2) **Flow webhook** (from Power Automate card submit):  
   `POST /webhooks/flow/queries/{id}/response`  
   - Header: `x-shared-secret: <your secret>` (set `Webhooks:FlowSecret`)
   - Body JSON example:
     ```json
     { "responder": "dr.smith@nhs.net", "responseText": "Bacterial RLL pneumonia confirmed" }
     ```

## Filtering & pagination

- `GET /episodes?status=1&from=2025-08-01T00:00:00Z&to=2025-08-31T23:59:59Z&page=1&pageSize=50`  
  Returns `{ page, pageSize, total, items: [...] }`.

- Exports accept the same filters:
  - `/export/episodes.csv?status=2&from=...&to=...`
  - `/export/episodes.json?status=2&from=...&to=...`

## Deploy

- **Quick infra (az CLI)**: `deploy/scripts/provision.sh` (creates RG, SQL, App Service for API, Storage static site).
- **GitHub Actions**:
  - `deploy-api.yml` (needs `AZURE_WEBAPP_PUBLISH_PROFILE` + `AZURE_WEB_APP_NAME` secrets)
  - `deploy-web.yml` (needs `AZURE_STORAGE_ACCOUNT` secret; configure Storage static website first).



---

## Secure Flow webhook (HMAC)

- Set `Webhooks:FlowSecret` in API config.
- Compute signature in Flow (Expression action) as `sha256(body, secret)` equivalent:
  - In Power Automate, use **"Compose"** with `base64ToString(hmacSha256(triggerBody(), '<secret>'))` then convert to hex,
    or call an Azure Function/Custom Connector to add the `x-signature` header.
- API expects header: `x-signature: sha256=<hex>`

## Application Insights

- Package is added and auto-collects requests, dependencies, traces, and exceptions.
- Set `ApplicationInsights:ConnectionString` in API settings (or `APPINSIGHTS_CONNECTIONSTRING` env var).

## Power BI Push Dataset

- Configure `PowerBI:{TenantId, ClientId, ClientSecret, WorkspaceId, DatasetId}`.
- Service: `PbiPushService` pushes rows to a table (e.g. `ClinicianResponses`).
- The webhook now pushes a response record to Power BI after storing the response.
- Create your dataset with a table schema:
  - `ClinicianResponses(QueryId:string, EpisodeId:string, Responder:string, RespondedOnUtc:datetime, ResponseText:string)`

## Re-suggestion upon clinician response

- The webhook records an audit and pushes to Power BI.
- You can extend it to fetch the `Episode` by id and append the response to `SourceText`, then call the suggestion service again and update codes.
- If you prefer this behaviour now, tell me and I’ll wire an `EpisodeNotes` field + re-run suggestion atomically.



### Auto re-suggestion on clinician response
When the Flow webhook posts a response:
1. The API verifies HMAC and saves the reply.
2. The corresponding episode is loaded; the response text is appended to `Episode.SourceText`.
3. Azure OpenAI/Text Analytics re-run suggestions.
4. Diagnoses/Procedures are replaced with the new sets.
5. An audit entry `ReSuggestionApplied` is written with the code deltas.
6. A `SuggestionDeltas` row is pushed to Power BI.

Power BI tables to create:
- `SuggestionDeltas(EpisodeId:string, EventUtc:datetime, DxAdded:string, DxRemoved:string, PxAdded:string, PxRemoved:string)`


---

## Reviewer code-diff & revert
- `GET /episodes/{id}/code-diff` returns the latest re-suggestion audit with **old/new** code sets.
- The SPA shows a two-column diff and a **Revert to old codes** button (Reviewer-only).
- `POST /episodes/{id}/revert?auditId=<audit>` restores the previous codes and logs `ReSuggestionReverted`.

## Debounce (throttle re-suggestions)
- Configure `Resuggest:MinIntervalMinutes` (default 5). If another response lands within this window, the webhook logs `ReSuggestionSkipped_Debounce` and returns 202 Accepted.

## Retry & Dead-letter
- On webhook processing errors, the payload is stored in `DeadLetters` with kind `FlowQueryResponse`.
- Retry via `POST /deadletter/{id}/retry`. (Demo replays normalized fields from the payload.)



---

## Side-by-side diff in Swagger
- Static page at `/diff.html`. Paste a bearer token into `sessionStorage.setItem('bearer','<token>')` and enter an Episode ID to view the latest code diff.

## Azure Queue DLQ with scheduled retries
- Set `Queues:ConnectionString` and `Queues:DeadLetterName` in API/Worker.
- On webhook exceptions, payloads are also enqueued to Azure Storage Queue (`timeToLive: 7 days`).
- The **ClinicalCoding.Worker** reads from the queue and retries with a backoff (using visibility timeouts).

## Two-step revert approvals
- Endpoints (Reviewer policy required):
  - `POST /episodes/{id}/revert-request?auditId=...` → creates a pending request.
  - `POST /episodes/{id}/revert-approve?requestId=...` → **must be a different reviewer**; applies the revert.
  - `POST /episodes/{id}/revert-reject?requestId=...` → declines the revert.
- Audited: `RevertRequested`, `ReSuggestionReverted`, `RevertRejected`.


---

## Pluggable DLQ backend (Storage Queue or Service Bus)

- Configure in API/Worker settings under `DLQ`:
  ```json
  {
    "DLQ": {
      "Provider": "Storage", // or "ServiceBus"
      "Storage": { "ConnectionString": "...", "QueueName": "deadletters" },
      "ServiceBus": { "ConnectionString": "...", "QueueName": "deadletters" }
    }
  }
  ```
- The API enqueues failures using `IDeadLetterQueue`.
- The Worker reads from **either** backend based on `DLQ:Provider` (environment variables supported as `DLQ__Storage__ConnectionString` etc.).
