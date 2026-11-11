# AI Job Search Agent

AI Job Search Agent is an educational, portfolio-quality .NET application. Phase 1 collects public job vacancies from Arbeitnow and stores them in SQLite. Phase 2 asks a local Ollama model to extract vacancy requirements, compares them with a local candidate profile using deterministic C# rules, and preserves the evaluation history. Paid APIs, CV parsing, notifications, scheduling, authentication, a frontend, RAG, agent orchestration, and cloud deployment remain out of scope.

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
  AIJobSearchAgent.LinkedInSpike/   Isolated public LinkedIn page feasibility diagnostic
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

## Run the API and local web UI

```powershell
dotnet run --project src/AIJobSearchAgent.Api
```

The Development launch profile listens at `http://localhost:5091`. Open the local job listings UI at:

```text
http://localhost:5091/jobs
```

The page displays imported vacancies and their current evaluation state without starting new evaluations. Use the keyword field to search vacancy titles and descriptions, choose rating descending, rating ascending, or newest-first sorting, and move between pages with the Previous and Next links. Filter and sort values are retained while paging. Evaluation details expand in place, and original vacancy links open in a new browser tab.

Swagger UI remains available at:

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

The integration calls Ollama's `/api/chat` endpoint with streaming disabled, temperature 0, and a JSON Schema through the `format` field. Ollama receives only the untrusted vacancy title and bounded description. It extracts required and optional technologies, a small subset of role-defining core required technologies, whether the requirements are clear, an optional overall experience minimum, and mandatory languages. Before scoring, each extracted technology must also be mentioned in that bounded description; the title alone is not evidence. Unsupported extractions are excluded from required and optional skill comparison and named in the deterministic explanation. The candidate profile is never included in the LLM request; rating and skill comparison happen deterministically in C#. No candidate or vacancy data is sent to a paid or cloud API.

Matching version `job-match-simple-v4` uses these rules:

- all required and optional technical skills match: rating 5;
- all required skills match but an optional skill is missing: rating 4;
- some, but not all, required skills match: rating 3;
- no clear required technical stack is available: rating 2;
- none of the verified role-defining core required skills match: rating 1, even if generic supporting technologies match;
- none of a clear required technical stack matches: rating 1.

Explicit aliases cover common spellings of C#, EF Core, ASP.NET Core, and .NET for both candidate comparison and description verification. Description checks are token-aware: they do not broaden SQL into a specific database, Java into JavaScript, or a platform into all of its frameworks. A confirmed language-level shortfall caps the rating at 3. If a mandatory language is absent from the candidate profile, capability remains unknown rather than being called a mismatch, but the rating is also capped at 3 until that requirement is verified. An explicit overall-experience shortfall caps the rating at 4 and is never interpreted as technology-specific experience.

Technical classification is corrected only when the bounded description gives explicit evidence. Markers such as required, mandatory, or essential move an extracted technology to required; optional, preferred, useful, bonus, or nice-to-have move it to optional. Technologies explicitly described as irrelevant, not required, not used, or outside the role are discarded. Clear non-technology outputs such as year-count experience phrases and generic practices such as code review are also discarded. Without an explicit marker, the verifier preserves Ollama's classification rather than treating every mentioned technology as mandatory. Core-responsibility implications therefore remain model judgments, and the narrow non-technology exclusions are not a comprehensive professional-practice dictionary.

`coreRequiredSkills` identifies only technologies that define the role's main specialization or essential technical purpose, such as Python and FastAPI for a Python backend role. It is deterministically reduced to a normalized, duplicate-free subset of the verified required skills; an invented core skill or one supported only by the title is discarded. SQL and Docker are not considered role-defining by default. When no reliable core subset remains, the existing required/optional rating rules apply unchanged.

Mandatory languages are also checked against the bounded vacancy description before scoring. A language is retained only when the text connects it to an explicit requirement; background references such as an English-speaking team or English documentation are not enough. CEFR levels are retained only when the exact level is stated with the requirement. `native` is retained only when native proficiency is explicitly required. If the language is mandatory but Ollama returns an unsupported level—for example `native` for “professional English”—the language remains required with a `null` minimum level.

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

Browse vacancies with their latest evaluation for the current candidate profile and matching configuration:

```powershell
$ranked = Invoke-RestMethod `
  'http://localhost:5091/api/jobs/ranked?keyword=.NET&source=Arbeitnow&page=1&pageSize=20&sort=rating_desc'

$ranked.items | Format-Table `
  rating, evaluationStatus, title, company, location, publishedAt
```

`GET /api/jobs/ranked` supports these query parameters:

- `keyword`: optional title or description search using the same token matching as the existing vacancy list.
- `source`: optional case-insensitive provider filter.
- `page`: page number, default `1`.
- `pageSize`: items per page, default `20`, maximum `100`.
- `sort`: `rating_desc` (default), `rating_asc`, or `newest`.

Successfully rated vacancies appear before vacancies without a current rating for both rating sorts. Null ratings, including failed and timed-out evaluations, always appear last. Rating 1 vacancies remain visible.

Example ranked response:

```json
{
  "items": [
    {
      "vacancyId": "00000000-0000-0000-0000-000000000001",
      "title": ".NET Backend Engineer",
      "company": "Example Company",
      "location": "Budapest or remote",
      "isRemote": true,
      "source": "Arbeitnow",
      "url": "https://example.test/jobs/1",
      "publishedAt": "2026-11-08T09:00:00Z",
      "rating": 4,
      "evaluationStatus": "succeeded",
      "evaluatedAt": "2026-11-10T10:30:00Z",
      "explanation": "The candidate matches the required backend skills but lacks one optional technology."
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1
}
```

The ranked list treats an evaluation as current only when its candidate-profile hash, model name, and prompt version all match the current configuration. It selects the newest matching attempt using evaluation time and evaluation ID, so a latest failed or timed-out attempt is shown with its status and a null rating. Older results and results from other profiles, models, or prompt versions remain in history but are not presented as the current result. Reading this endpoint never invokes Ollama or creates an evaluation.

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
  "promptVersion": "job-match-simple-v4",
  "rating": 4,
  "matchedSkills": ["C#", "ASP.NET Core"],
  "missingRequiredSkills": [],
  "missingOptionalSkills": ["Kubernetes"],
  "explanation": "The candidate matches the core backend requirements but lacks the optional Kubernetes experience.",
  "evaluationStatus": "succeeded",
  "evaluatedAt": "2026-10-30T18:30:00Z"
}
```

Evaluate 5–10 stored vacancies sequentially with one request. This example selects the first eight vacancies from local storage:

```powershell
$jobs = Invoke-RestMethod 'http://localhost:5091/api/jobs?page=1&pageSize=8'
$jobIds = @($jobs.items | Select-Object -First 8 | ForEach-Object { $_.id })
$body = @{ jobIds = $jobIds } | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:5091/api/jobs/evaluate-batch' `
  -ContentType 'application/json' `
  -Body $body
```

The batch accepts 1–20 IDs. Duplicate IDs are safe: only the first occurrence can trigger evaluation and later occurrences are reported as skipped. An individual missing vacancy or Ollama failure is reported in the summary without stopping later vacancies.

Example batch response:

```json
{
  "totalRequestedVacancies": 3,
  "successfullyEvaluatedCount": 1,
  "skippedCount": 1,
  "failedCount": 1,
  "results": [
    {
      "vacancyId": "00000000-0000-0000-0000-000000000001",
      "status": "succeeded",
      "rating": 4,
      "explanation": "The candidate matches the required backend skills."
    },
    {
      "vacancyId": "00000000-0000-0000-0000-000000000002",
      "status": "skipped",
      "rating": 5,
      "explanation": "Skipped because a successful evaluation already exists for this profile, model, and prompt version."
    },
    {
      "vacancyId": "00000000-0000-0000-0000-000000000003",
      "status": "failed",
      "rating": null,
      "explanation": "Evaluation failed."
    }
  ]
}
```

By default, batch evaluation skips a vacancy only when its history contains a successful result with the exact current candidate-profile hash, configured model name, and prompt version. A changed profile, model, or matching version triggers a new evaluation. Failed and timed-out attempts are never reused. The single-vacancy endpoint always starts a new evaluation, which allows an explicit manual retry while preserving all prior history.

Retrieve all evaluation attempts for that vacancy, newest first:

```powershell
Invoke-RestMethod "http://localhost:5091/api/jobs/$jobId/evaluations"
```

Failed and timed-out LLM attempts are recorded with a `null` rating and a non-success status. The single-vacancy API returns an error response instead of fabricating a successful rating. Imports and list requests never trigger matching automatically.

## Local LLM benchmarking

The benchmark runner exercises the same `IJobMatchingService`, requirement extraction, deterministic calculator, and candidate-profile loader used by the API. It runs sequentially by default and needs neither the Web API, SQLite, nor Docker.

The committed inputs are intentionally synthetic:

- `benchmarks/dataset.v1.json` preserves the 18 fictional vacancies and approved labels from the earlier rating system. It is retained unchanged for historical reproducibility.
- `benchmarks/dataset.simple.v3.json` contains the same 18 vacancy identities and descriptions with independently reviewed, approved reference labels for `job-match-simple-v3`.
- `benchmarks/candidate-profile.synthetic.json` is a benchmark-only profile and does not read or contain the private `candidate-profile.json`.

The 18 approved ratings in `dataset.v1.json` remain historical labels and are not authoritative for simplified matching. The independently reviewed v3 dataset declares `"datasetVersion": "2.0"` and `"matchingVersion": "job-match-simple-v3"`; all 18 cases have approved ratings and reviewer metadata. It remains unchanged for v3 reproducibility. Because the current prompt is `job-match-simple-v4`, runs against this v3 dataset are marked reference-label incompatible and do not report accuracy or MAE; they still report predictions, failures, distributions, and latency. A future v4 reference dataset requires independent review rather than copying model predictions.

Synthetic cases deliberately omit `vacancyUrl`: their companies and vacancies do not exist, so the dataset must not invent links that appear to identify external listings. A future benchmark case based on a real vacancy may set `vacancyUrl` to its actual absolute HTTP or HTTPS source URL. Dataset validation rejects relative, malformed, and non-HTTP(S) URLs. Because external listings can change or disappear, the dataset remains a reviewed snapshot rather than fetching live vacancy content during a run.

Generated files are written under the ignored `benchmarks/results/` directory. Each run creates a new JSON file containing dataset and profile hashes, model and prompt versions, aggregate metrics, individual predictions, latency, and safe failure information. Every new case result includes its stable case ID plus the evaluated vacancy title and description, and includes the source URL when the dataset supplied one. This makes the evaluated vacancy identifiable even when a real URL is absent or later expires. Raw prompts and personal data are not stored.

### Approved simplified reference labels

The approved labels in `dataset.simple.v3.json` were reviewed against the synthetic profile using this simplified rating rubric:

- **5:** All mandatory and optional technical skills match, with no known major incompatibilities.
- **4:** Mandatory technical skills match, but some optional skills are missing.
- **3:** The candidate matches part of the central required technical stack, but lacks other mandatory skills.
- **2:** The professional title is relevant, but the description does not contain enough information for reliable technical matching.
- **1:** The main required technology stack is incompatible with the candidate's skills.

Explicit overall experience and mandatory language requirements also affect the judgment under the currently documented matching rules. In particular, an unknown mandatory language capability is not a confirmed mismatch, but caps the rating at 3; an explicitly lower CEFR level remains a confirmed mismatch. Do not infer requirements absent from the vacancy.

Future label changes must be reviewed independently of model predictions. Update the rating, rationale, and expected skill lists together, retain non-empty `reviewedBy` and `reviewedAt` metadata, and prefer a second human review for disputed cases. Dataset validation rejects approved labels without reviewer metadata and ratings outside 1-5. Any edit changes the dataset hash, preventing silent comparison with runs that used earlier judgments.

### Run an experiment

Start Ollama, pull the model, and run the preserved v3 dataset as a v4 regression experiment. The report will mark its approved labels incompatible with the current matching version:

```powershell
ollama pull qwen3:4b
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --model qwen3:4b --dataset benchmarks/dataset.simple.v3.json
```

Available options are:

```text
--dataset <path>  Dataset path
--profile <path>  Synthetic benchmark profile path
--model <name>    Ollama model name
--output <path>   Generated result directory
--case <id>       Run one exact stable case ID
```

For a quick connectivity smoke test that does not establish accuracy:

```powershell
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --dataset benchmarks/dataset.simple.v3.json --case dotnet-backend-excellent
```

Run another model with the same dataset and profile, then compare any two or more saved reports without invoking Ollama:

```powershell
dotnet run --project tools/AIJobSearchAgent.Benchmarks -- --model qwen3:4b --dataset benchmarks/dataset.simple.v3.json
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

Coverage includes Phase 1 collection behavior plus candidate profile validation, stable profile hashing, deterministic ratings, core-stack handling and aliases, conservative experience and language handling, Ollama structured-output deserialization and validation, timeout/error handling, cancellation propagation, untrusted vacancy content boundaries, additive migration safety, and evaluation persistence. Tests use mocked Ollama responses and never require a running local model.

## LinkedIn feasibility spike

The isolated diagnostic under `tools/AIJobSearchAgent.LinkedInSpike` compares direct public HTML inspection with a visible Selenium browser. It is not wired into `IJobProvider` and never writes to SQLite or calls Ollama. See the [spike README](tools/AIJobSearchAgent.LinkedInSpike/README.md) for commands, browser setup, safety restrictions, and interpretation guidance.

## Provider behavior and limitations

The implementation uses Arbeitnow's documented free endpoint: `https://www.arbeitnow.com/api/job-board-api?page=1`. According to the [official Arbeitnow API documentation](https://blog.arbeitnow.com/blog/job-board-api), it requires no API key. The public documentation does not describe a keyword query parameter, so the client fetches only the requested/configured number of pages and filters title and description locally. This means a matching vacancy beyond that page window is not discovered.

Additional limitations:

- Only Arbeitnow is configured.
- Keyword matching is literal substring matching, not stemming or semantic search.
- SQLite's local database is intended for a single application instance; the unique constraint still protects against duplicate imports.
- Imported data is only as complete and current as the upstream response.
- Swagger is enabled only in the Development environment.
- `qwen3:1.7b` is a small local model. Its requirement extraction can be inconsistent or overly literal and must be treated as decision support, not fact.
- Structured output enforces response shape, while the local evidence check rejects extracted technologies that are not mentioned in the supplied description. It does not understand every semantic phrasing, so review the deterministic explanation and original vacancy before acting.
- Results can change after model updates, model changes, profile edits, or prompt revisions; model name, profile hash, and prompt version are stored for traceability.
- Long vacancy descriptions are truncated at the configured character limit before evaluation.
- The prompt explicitly treats vacancy text as untrusted data, but prompt injection defenses are not an absolute security boundary.
- Rating 1 evaluations remain in technical history and are included in ranked browsing. There is no recommended-vacancy endpoint in this iteration.
