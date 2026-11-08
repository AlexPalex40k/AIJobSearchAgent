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
tools/
  AIJobSearchAgent.Benchmarks/      Sequential local-LLM benchmark and offline comparison runner
benchmarks/                         Versioned synthetic dataset and benchmark-only candidate profile
```

Dependencies point inward: Domain has no project dependencies; Application depends on Domain; Infrastructure depends on Application and Domain; Api composes the application.

## Prerequisites

- .NET 10 SDK (the repository's `global.json` selects stable SDK 10.0.302)
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

The repository targets .NET 10 LTS. Its `global.json` selects SDK 10.0.302 and permits later stable .NET 10 feature bands. Direct Microsoft runtime and EF Core packages use the stable 10.0.14 patch release; Swashbuckle uses 10.3.1.

The development package baseline is:

- target framework `net10.0` with C# 12;
- EF Core, SQLite, and Microsoft.Extensions packages 10.0.14;
- Microsoft.NET.Test.Sdk 18.11.0;
- Swashbuckle.AspNetCore 10.3.1;
- xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, and coverlet.collector 6.0.4.

## Database and migrations

The API stores data in `Data/jobs.db`. It runs pending EF Core migrations at startup with `MigrateAsync`; it does not drop or recreate the database.

The existing migrations retain their original identifiers and EF Core 9 metadata. EF Core 10 recognizes and applies them unchanged, so databases created by the .NET 9 application remain compatible and preserve vacancy and evaluation history. No schema migration is required solely for the framework upgrade.

To apply migrations explicitly, install the matching EF tool once and run:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
dotnet ef database update --project src/AIJobSearchAgent.Infrastructure --startup-project src/AIJobSearchAgent.Api
```

If `dotnet-ef` is already installed from the .NET 9 development environment, update it instead:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.14
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
- technical skills with an optional level;
- preferred titles and locations;
- `noPreference`, `remoteOnly`, `remotePreferred`, `hybridPreferred`, or `onSiteAccepted` remote preferences;
- spoken languages with a required `A1`, `A2`, `B1`, `B2`, `C1`, `C2`, or `native` level;
- additional professional experience or preferences.

For example:

```json
{
  "professionalTitle": "Backend Software Engineer",
  "yearsProfessionalExperience": 6,
  "technicalSkills": [
    { "name": "C#", "level": "advanced" },
    { "name": "Docker", "level": "intermediate" }
  ],
  "spokenLanguages": [
    { "language": "English", "level": "B2" },
    { "language": "Hungarian", "level": "native" }
  ]
}
```

`yearsProfessionalExperience` is the candidate's total professional experience. It is not copied to individual skills and does not mean that every listed technology has been used for that number of years. The `native` language level represents native proficiency; it is not a formal CEFR certification.

Profiles created with the previous format must be migrated manually because unknown JSON properties are rejected:

1. Remove every `technicalSkills[].yearsExperience` property.
2. Rename `spokenLanguages[].proficiency` to `spokenLanguages[].level`.
3. Replace descriptive language proficiency text with `A1`, `A2`, `B1`, `B2`, `C1`, `C2`, or `native`.

The loader reports these deprecated fields explicitly instead of silently ignoring them.

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

## Local LLM benchmarking

The benchmark runner measures the existing Ollama matching implementation without changing its prompt or matching algorithm. It references the existing `IJobMatchingService`, `OllamaJobMatchingService`, `JobMatchingPrompt`, and candidate-profile loader. It runs sequentially by default and needs neither the Web API, SQLite, nor Docker.

The committed inputs are intentionally synthetic:

- `benchmarks/dataset.v1.json` contains 18 fictional vacancies covering strong and incomplete .NET matches, frontend and desktop gaps, seniority mismatches, Python and Java roles, DevOps-heavy work, language and location restrictions, and incomplete or ambiguous descriptions.
- `benchmarks/candidate-profile.synthetic.json` is a benchmark-only profile and does not read or contain the private `candidate-profile.json`.

Synthetic cases deliberately omit `vacancyUrl`: their companies and vacancies do not exist, so the dataset must not invent links that appear to identify external listings. A future benchmark case based on a real vacancy may set `vacancyUrl` to its actual absolute HTTP or HTTPS source URL. Dataset validation rejects relative, malformed, and non-HTTP(S) URLs. Because external listings can change or disappear, the dataset remains a reviewed snapshot rather than fetching live vacancy content during a run.

Generated files are written under the ignored `benchmarks/results/` directory. Each run creates a new JSON file containing dataset and profile hashes, model and prompt versions, aggregate metrics, individual predictions, latency, and safe failure information. Every new case result includes its stable case ID plus the evaluated vacancy title and description, and includes the source URL when the dataset supplied one. This makes the evaluated vacancy identifiable even when a real URL is absent or later expires. Raw prompts and personal data are not stored.

### Review and approve reference labels

All ratings in `dataset.v1.json` initially have `labelStatus: "provisional"`. They are proposed starting points, not validated ground truth. The runner records provisional expectations for inspection but refuses to include them in accuracy or MAE calculations.

Approve labels case by case using this workflow:

1. Read the synthetic profile, vacancy, proposed rating, and rationale without consulting a model result.
2. Apply the documented 1–5 rating scale consistently, checking skills, commercial experience, seniority, responsibilities, location, remote compatibility, and languages.
3. Correct the expected rating, rationale, and optional expected skill lists when needed.
4. Set `labelStatus` to `approved`, add a non-empty `reviewedBy` value, and add `reviewedAt` as an ISO 8601 timestamp such as `2026-11-07T12:00:00Z`.
5. Prefer a second human review for disputed or ambiguous cases. Record the agreed judgment in the dataset rather than copying an evaluated model's answer.

Dataset validation rejects approved labels without reviewer metadata and rejects ratings outside 1–5. Editing or approving a label changes the dataset hash, preventing silent comparison with runs that used the earlier judgments. Cases may instead use `labelStatus: "unlabeled"` with a null expected rating.

### Run an experiment

Start Ollama and pull the model first:

```powershell
ollama pull qwen3:1.7b
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --model qwen3:1.7b --dataset benchmarks/dataset.v1.json
```

Available options are:

```text
--dataset <path>  Dataset path
--profile <path>  Synthetic benchmark profile path
--model <name>    Ollama model name
--output <path>   Generated result directory
--case <id>       Run one exact stable case ID
```

### Inspect structured vacancy requirements

The development benchmark tool can run the isolated `vacancy-requirements-v1` extractor against one existing benchmark vacancy. It prints the structured requirements and exact supporting quotations as JSON; it does not load the candidate profile, calculate a match rating, save a benchmark result, or change the production API matching path.

Start Ollama, then run:

```powershell
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- extract --case dotnet-backend-excellent
```

Use `--model <name>` or `--dataset <path>` to override the defaults. Unsupported quotations, malformed output, Ollama errors, and timeouts fail the command instead of producing an apparently valid extraction. An empty description produces an explicit `isDescriptionInsufficient` result without calling Ollama.

Human-reviewed extraction labels are intentionally separate from the rating ground truth. The initial empty label set is `benchmarks/extraction-labels.v1.json`; do not populate it from model output. A future reviewed entry has this shape:

```json
{
  "caseId": "stable-benchmark-case-id",
  "reviewStatus": "reviewed",
  "reviewedBy": "reviewer identifier",
  "reviewedAt": "2026-11-09T12:00:00Z",
  "requirements": {
    "mandatoryTechnicalSkills": [
      { "name": "C#", "evidenceQuote": "exact text copied from the vacancy" }
    ],
    "optionalTechnicalSkills": [],
    "contextualTechnologyMentions": [],
    "overallExperienceRequirements": [],
    "technologyExperienceRequirements": [],
    "languageRequirements": [],
    "locationRequirements": [],
    "workArrangementConditions": [],
    "relocationConditions": [],
    "ambiguousRequirements": [],
    "isDescriptionInsufficient": false,
    "insufficientInformation": []
  },
  "notes": "Independent human annotation notes."
}
```

No extraction accuracy is reported until this separate label set contains independently reviewed annotations and an evaluation procedure is approved.

For a quick connectivity smoke test that does not establish accuracy:

```powershell
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --case dotnet-backend-excellent
```

Run another model with the same dataset and profile, then compare any two or more saved reports without invoking Ollama:

```powershell
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --model qwen3:4b
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- compare benchmarks/results/run-a.json benchmarks/results/run-b.json
```

Comparison requires identical dataset and profile hashes. It reports prompt-version differences, aggregate deltas relative to the first run, failure and latency differences, and cases with different successful predictions. It deliberately does not declare a winning model from one metric.

### Interpret benchmark metrics

- Exact and within-one-point accuracy report both the number correct and the number of successfully scored approved labels. Provisional, unlabeled, failed, and timed-out cases are excluded from this denominator.
- MAE is calculated only over successfully scored approved labels and reports its evaluation count. Lower is better, but it must be read alongside coverage.
- Failure rate uses every attempted case as its denominator and includes both failed and timed-out evaluations. Successful, failed, and timed-out counts are always reported separately.
- Rating distribution uses successful evaluations, whether or not their reference labels are approved.
- Mean and median latency include every completed attempt, including failures and timeouts. P95 is reported only for at least 20 attempts; smaller samples are not considered meaningful for that percentile.

The committed synthetic dataset is useful for prompt regressions, model comparisons, and initial reliability experiments. Eighteen curated fictional cases cannot establish real-world model accuracy, represent production vacancy diversity, or remove subjective disagreement between human reviewers. Future versions should add independently reviewed cases sampled from the intended job market, repeat runs to measure nondeterminism, and analyze errors by category while keeping private candidate data out of Git.

The import summary counters mean:

- `totalFetched`: vacancies returned by the provider after its bounded page fetch and local provider keyword filter.
- `totalMatched`: fetched vacancies that remain valid and match the normalized keyword list during application validation.
- `inserted`: new rows committed to SQLite.
- `skippedDuplicates`: valid matches skipped because the same `(source, externalId)` already exists or appeared twice in the batch.
- `rejectedInvalid`: provider results missing required identity/content fields or containing invalid values.

## Tests

Tests use mocked HTTP responses and disposable in-memory or temporary-file SQLite databases; they never call the live API:

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
