# AI Job Search Agent

AI Job Search Agent is an educational, portfolio-quality .NET application. Phase 1 collects public job vacancies from Arbeitnow and stores them in SQLite. Phase 2 evaluates individual saved vacancies against a local candidate profile using a local Ollama model and preserves the evaluation history. Paid APIs, CV parsing, notifications, scheduling, authentication, a frontend, RAG, agent orchestration, and cloud deployment remain out of scope.

## Architecture

The solution follows a small Clean Architecture layout:

```text
src/
  AIJobSearchAgent.Domain/          Vacancies, evaluations, and candidate profile models
  AIJobSearchAgent.Application/     Use cases, contracts, DTOs, matching prompt
  AIJobSearchAgent.Infrastructure/  EF Core, SQLite, Arbeitnow and Ollama clients
  AIJobSearchAgent.Api/             Controllers and composition root
tests/
  AIJobSearchAgent.Tests/           Unit and SQLite integration tests
```

Dependencies point inward: Domain has no project dependencies; Application depends on Domain; Infrastructure depends on Application and Domain; Api composes the application.

## Prerequisites

- .NET 9 SDK
- Windows, macOS, or Linux; the documented commands use PowerShell
- Internet access only for package restore and real Arbeitnow imports
- [Ollama](https://docs.ollama.com/quickstart) and a local model for real job evaluations

No Docker, API key, paid service, or external database server is needed. Automated tests mock both external HTTP integrations and do not require Ollama.

## Restore and build

From the repository root:

```powershell
dotnet restore AIJobSearchAgent.sln
dotnet build AIJobSearchAgent.sln --no-restore
```

The repository's `global.json` selects .NET 9 and permits later stable .NET 9 feature bands.

## Database and migrations

The API stores data in `Data/jobs.db`. It runs pending EF Core migrations at startup with `MigrateAsync`; it does not drop or recreate the database.

To apply migrations explicitly, install the matching EF tool once and run:

```powershell
dotnet tool install --global dotnet-ef --version 9.0.10
dotnet ef database update --project src/AIJobSearchAgent.Infrastructure --startup-project src/AIJobSearchAgent.Api
```

To create a future migration:

```powershell
dotnet ef migrations add MigrationName --project src/AIJobSearchAgent.Infrastructure --startup-project src/AIJobSearchAgent.Api
```

## Run the API

```powershell
dotnet run --project src/AIJobSearchAgent.Api
```

The Development launch profile listens at `http://localhost:5091`. Open Swagger UI at:

```text
http://localhost:5091/swagger
```

Provider URL, timeout, and the configured page ceiling are in `src/AIJobSearchAgent.Api/appsettings.json`.

## Configure local job matching

### 1. Install Ollama and download the model

Install Ollama using the [official download and setup instructions](https://docs.ollama.com/quickstart). On Windows, the native application runs in the background and serves its local API at `http://localhost:11434`; see the [Windows-specific instructions](https://docs.ollama.com/windows).

Download the configured default model:

```powershell
ollama pull qwen3:1.7b
```

Confirm that Ollama can see it:

```powershell
ollama list
```

The endpoint, model, timeout, and maximum vacancy-description size are configurable in `src/AIJobSearchAgent.Api/appsettings.json`:

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "Model": "qwen3:1.7b",
    "TimeoutSeconds": 120,
    "MaxDescriptionCharacters": 30000
  }
}
```

The integration calls Ollama's `/api/chat` endpoint with streaming disabled and supplies a JSON Schema through the `format` field. No candidate or vacancy data is sent to a paid or cloud API.

### 2. Create the private candidate profile

Copy the safe example file and edit the copy:

```powershell
Copy-Item candidate-profile.example.json candidate-profile.json
notepad candidate-profile.json
```

`candidate-profile.json` is intentionally ignored by Git. Never put a private CV or personal profile in `candidate-profile.example.json`. The local file supports:

- optional name and required professional title;
- total professional experience;
- technical skills with optional years and level;
- preferred titles and locations;
- `noPreference`, `remoteOnly`, `remotePreferred`, `hybridPreferred`, or `onSiteAccepted` remote preferences;
- spoken languages and proficiency;
- additional professional experience or preferences.

Each evaluation stores a SHA-256 hash of the normalized profile, not the profile itself. Editing the profile therefore creates a distinguishable history without persisting its private contents in the database.

## Example requests

Import up to two Arbeitnow pages. Keywords use case-insensitive **OR** semantics across title and description:

```powershell
$body = @{
  keywords = @('.NET', 'C#', 'ASP.NET Core')
  maxPages = 2
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:5091/api/jobs/import' `
  -ContentType 'application/json' `
  -Body $body
```

List stored vacancies:

```powershell
Invoke-RestMethod 'http://localhost:5091/api/jobs?keyword=.NET&source=Arbeitnow&page=1&pageSize=20'
```

Get a vacancy by its local ID:

```powershell
Invoke-RestMethod 'http://localhost:5091/api/jobs/00000000-0000-0000-0000-000000000000'
```

Evaluate one stored vacancy using the local profile and Ollama:

```powershell
$jobId = '00000000-0000-0000-0000-000000000000'
Invoke-RestMethod -Method Post "http://localhost:5091/api/jobs/$jobId/evaluate"
```

Example successful response:

```json
{
  "id": "d83232f1-e573-4658-9d4e-f87fd983e6fc",
  "jobVacancyId": "00000000-0000-0000-0000-000000000000",
  "profileHash": "4b7c...64-lowercase-hex-characters...",
  "modelName": "qwen3:1.7b",
  "promptVersion": "job-match-v1",
  "rating": 4,
  "matchedSkills": ["C#", "ASP.NET Core"],
  "missingRequiredSkills": [],
  "missingOptionalSkills": ["Kubernetes"],
  "explanation": "The candidate matches the core backend requirements but lacks the optional Kubernetes experience.",
  "evaluationStatus": "succeeded",
  "evaluatedAt": "2026-10-30T18:30:00Z"
}
```

Retrieve all evaluation attempts for that vacancy, newest first:

```powershell
Invoke-RestMethod "http://localhost:5091/api/jobs/$jobId/evaluations"
```

Failed and timed-out LLM attempts are recorded with a `null` rating and a non-success status. The API returns an error response instead of fabricating a successful rating. Evaluations are only started by the single-vacancy POST endpoint; imports and list requests never trigger matching automatically.

The import summary counters mean:

- `totalFetched`: vacancies returned by the provider after its bounded page fetch and local provider keyword filter.
- `totalMatched`: fetched vacancies that remain valid and match the normalized keyword list during application validation.
- `inserted`: new rows committed to SQLite.
- `skippedDuplicates`: valid matches skipped because the same `(source, externalId)` already exists or appeared twice in the batch.
- `rejectedInvalid`: provider results missing required identity/content fields or containing invalid values.

## Tests

Tests use mocked HTTP responses and an in-memory SQLite connection; they never call the live API:

```powershell
dotnet test AIJobSearchAgent.sln
```

Coverage includes Phase 1 collection behavior plus candidate profile validation, stable profile hashing, Ollama structured-output deserialization, rating and required-field validation, timeout/error handling, cancellation propagation, untrusted vacancy content boundaries, additive migration safety, and evaluation persistence. Tests use mocked Ollama responses and never require a running local model.

## Provider behavior and limitations

The implementation uses Arbeitnow's documented free endpoint: `https://www.arbeitnow.com/api/job-board-api?page=1`. According to the [official Arbeitnow API documentation](https://blog.arbeitnow.com/blog/job-board-api), it requires no API key. The public documentation does not describe a keyword query parameter, so the client fetches only the requested/configured number of pages and filters title and description locally. This means a matching vacancy beyond that page window is not discovered.

Additional limitations:

- Only Arbeitnow is configured.
- Keyword matching is literal substring matching, not stemming or semantic search.
- SQLite's local database is intended for a single application instance; the unique constraint still protects against duplicate imports.
- Imported data is only as complete and current as the upstream response.
- Swagger is enabled only in the Development environment.
- `qwen3:1.7b` is a small local model. Its ratings and skill classification can be inconsistent or overly literal and must be treated as decision support, not fact.
- Structured output enforces response shape, not correctness. Review the explanation and original vacancy before acting.
- Results can change after model updates, model changes, profile edits, or prompt revisions; model name, profile hash, and prompt version are stored for traceability.
- Long vacancy descriptions are truncated at the configured character limit before evaluation.
- The prompt explicitly treats vacancy text as untrusted data, but prompt injection defenses are not an absolute security boundary.
