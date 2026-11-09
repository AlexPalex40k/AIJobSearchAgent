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

The integration calls Ollama's `/api/chat` endpoint with streaming disabled, temperature 0, and a JSON Schema through the `format` field. Ollama receives only the untrusted vacancy title and bounded description. It extracts required and optional technologies, whether the requirements are clear, an optional overall experience minimum, and mandatory languages. Before scoring, each extracted technology must also be mentioned in that bounded description; the title alone is not evidence. Unsupported extractions are excluded from required and optional skill comparison and named in the deterministic explanation. The candidate profile is never included in the LLM request; rating and skill comparison happen deterministically in C#. No candidate or vacancy data is sent to a paid or cloud API.

Matching version `job-match-simple-v3` uses these rules:

- all required and optional technical skills match: rating 5;
- all required skills match but an optional skill is missing: rating 4;
- some, but not all, required skills match: rating 3;
- no clear required technical stack is available: rating 2;
- none of a clear required technical stack matches: rating 1.

Explicit aliases cover common spellings of C#, EF Core, ASP.NET Core, and .NET for both candidate comparison and description verification. Description checks are token-aware: they do not broaden SQL into a specific database, Java into JavaScript, or a platform into all of its frameworks. A confirmed language-level shortfall caps the rating at 3. If a mandatory language is absent from the candidate profile, capability remains unknown rather than being called a mismatch, but the rating is also capped at 3 until that requirement is verified. An explicit overall-experience shortfall caps the rating at 4 and is never interpreted as technology-specific experience.

Technical classification is corrected only when the bounded description gives explicit evidence. Markers such as required, mandatory, or essential move an extracted technology to required; optional, preferred, useful, bonus, or nice-to-have move it to optional. Technologies explicitly described as irrelevant, not required, not used, or outside the role are discarded. Clear non-technology outputs such as year-count experience phrases and generic practices such as code review are also discarded. Without an explicit marker, the verifier preserves Ollama's classification rather than treating every mentioned technology as mandatory. Core-responsibility implications therefore remain model judgments, and the narrow non-technology exclusions are not a comprehensive professional-practice dictionary.

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
  "promptVersion": "job-match-simple-v3",
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

The benchmark runner exercises the same `IJobMatchingService`, requirement extraction, deterministic calculator, and candidate-profile loader used by the API. It runs sequentially by default and needs neither the Web API, SQLite, nor Docker.

The committed inputs are intentionally synthetic:

- `benchmarks/dataset.v1.json` preserves the 18 fictional vacancies and approved labels from the earlier rating system. It is retained unchanged for historical reproducibility.
- `benchmarks/dataset.simple.v3.json` copies the same vacancy IDs, titles, descriptions, locations, tags, and other vacancy context for independent review under `job-match-simple-v3`. Its reference ratings and expected skill lists are intentionally empty.
- `benchmarks/candidate-profile.synthetic.json` is a benchmark-only profile and does not read or contain the private `candidate-profile.json`.

The 18 approved ratings in `dataset.v1.json` are not authoritative ground truth for `job-match-simple-v3`. The new dataset declares `"datasetVersion": "2.0"` and `"matchingVersion": "job-match-simple-v3"`, but every case starts with `expectedRating: null` and `labelStatus: "unlabeled"`. Runs can therefore record predictions, failures, distributions, and latency without presenting model output as reference truth. Accuracy and MAE become available only after human-reviewed labels are approved.

Synthetic cases deliberately omit `vacancyUrl`: their companies and vacancies do not exist, so the dataset must not invent links that appear to identify external listings. A future benchmark case based on a real vacancy may set `vacancyUrl` to its actual absolute HTTP or HTTPS source URL. Dataset validation rejects relative, malformed, and non-HTTP(S) URLs. Because external listings can change or disappear, the dataset remains a reviewed snapshot rather than fetching live vacancy content during a run.

Generated files are written under the ignored `benchmarks/results/` directory. Each run creates a new JSON file containing dataset and profile hashes, model and prompt versions, aggregate metrics, individual predictions, latency, and safe failure information. Every new case result includes its stable case ID plus the evaluated vacancy title and description, and includes the source URL when the dataset supplied one. This makes the evaluated vacancy identifiable even when a real URL is absent or later expires. Raw prompts and personal data are not stored.

### Review and approve reference labels

Review `dataset.simple.v3.json` against the synthetic profile using this simplified rating rubric:

- **5:** All mandatory and optional technical skills match, with no known major incompatibilities.
- **4:** Mandatory technical skills match, but some optional skills are missing.
- **3:** The candidate matches part of the central required technical stack, but lacks other mandatory skills.
- **2:** The professional title is relevant, but the description does not contain enough information for reliable technical matching.
- **1:** The main required technology stack is incompatible with the candidate's skills.

Explicit overall experience and mandatory language requirements also affect the judgment under the currently documented matching rules. In particular, an unknown mandatory language capability is not a confirmed mismatch, but caps the rating at 3; an explicitly lower CEFR level remains a confirmed mismatch. Do not infer requirements absent from the vacancy.

Approve labels case by case using this workflow:

1. Read the synthetic profile and vacancy without consulting the model's prediction or a previous benchmark result.
2. Identify the vacancy's verified mandatory and optional technologies, explicit overall experience, and mandatory language requirements, then apply the 1-5 rubric consistently.
3. Set `expectedRating` to the independently reviewed value, replace the pending-review rationale with a concise justification, and populate expected skill lists only when the review supports them.
4. Set `labelStatus` to `approved`, add a non-empty `reviewedBy` value, and add `reviewedAt` as an ISO 8601 timestamp such as `2026-11-07T12:00:00Z`.
5. Prefer a second human review for disputed or ambiguous cases. Record the agreed judgment rather than copying an evaluated model's answer.

Dataset validation rejects approved labels without reviewer metadata and rejects ratings outside 1-5. Editing or approving a label changes the dataset hash, preventing silent comparison with runs that used earlier judgments. The runner calculates accuracy and MAE only from successfully evaluated, approved cases in a dataset whose `matchingVersion` matches the current prompt version; remaining unlabeled cases stay outside the denominator.

### Run an experiment

Start Ollama and pull the model first:

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

Coverage includes Phase 1 collection behavior plus candidate profile validation, stable profile hashing, deterministic ratings and aliases, conservative experience and language handling, Ollama structured-output deserialization and validation, timeout/error handling, cancellation propagation, untrusted vacancy content boundaries, additive migration safety, and evaluation persistence. Tests use mocked Ollama responses and never require a running local model.

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
- Rating 1 evaluations remain in technical history. There is no recommended-vacancy endpoint in this iteration; any future recommendation query must exclude rating 1 without deleting vacancies or evaluation history.
