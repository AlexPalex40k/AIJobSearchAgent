# AI Job Search Agent

AI Job Search Agent is an educational, portfolio-quality .NET application. Phase 1 collects public job vacancies from Arbeitnow, normalizes and stores them in SQLite, and exposes them through a REST API. LLM evaluation, CV processing, notifications, scheduling, authentication, a frontend, and cloud deployment are intentionally out of scope.

## Architecture

The solution follows a small Clean Architecture layout:

```text
src/
  AIJobSearchAgent.Domain/          Entities and domain behavior
  AIJobSearchAgent.Application/     Use cases, contracts, DTOs, filtering
  AIJobSearchAgent.Infrastructure/  EF Core, SQLite, Arbeitnow HTTP client
  AIJobSearchAgent.Api/             Controllers and composition root
tests/
  AIJobSearchAgent.Tests/           Unit and SQLite integration tests
```

Dependencies point inward: Domain has no project dependencies; Application depends on Domain; Infrastructure depends on Application and Domain; Api composes the application.

## Prerequisites

- .NET 9 SDK
- Windows, macOS, or Linux; the documented commands use PowerShell
- Internet access only for package restore and real Arbeitnow imports

No Docker, API key, paid service, or external database server is needed.

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

Coverage includes case-insensitive keyword matching, OR semantics, empty keywords, provider mapping with missing fields, import summary calculations, duplicate handling, persistence, and the unique database index.

## Provider behavior and limitations

The implementation uses Arbeitnow's documented free endpoint: `https://www.arbeitnow.com/api/job-board-api?page=1`. According to the [official Arbeitnow API documentation](https://blog.arbeitnow.com/blog/job-board-api), it requires no API key. The public documentation does not describe a keyword query parameter, so the client fetches only the requested/configured number of pages and filters title and description locally. This means a matching vacancy beyond that page window is not discovered.

Additional Phase 1 limitations:

- Only Arbeitnow is configured.
- Keyword matching is literal substring matching, not stemming or semantic search.
- SQLite's local database is intended for a single application instance; the unique constraint still protects against duplicate imports.
- Imported data is only as complete and current as the upstream response.
- Swagger is enabled only in the Development environment.
