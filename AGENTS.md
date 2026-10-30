# Development Guide

## Architecture

- Keep the dependency direction `Domain <- Application <- Infrastructure <- Api`.
- Domain contains entities and domain behavior and must not depend on infrastructure packages.
- Application contains use cases, DTOs, filtering, pagination, and interfaces for persistence and providers.
- Infrastructure contains EF Core, SQLite, HTTP clients, and all external API integrations.
- Api is the composition root and contains transport-specific validation and HTTP behavior.
- Do not introduce MediatR, CQRS frameworks, generic repositories, event buses, or other frameworks unless a concrete requirement justifies them.

## Coding conventions

- Use English for source code, names, comments, logs, tests, and documentation.
- Keep nullable reference types enabled and resolve compiler warnings.
- Prefer small, purpose-specific types and straightforward code over speculative abstractions.
- Preserve the `(Source, ExternalId)` identity rule for vacancies; never deduplicate by title.
- Avoid changes unrelated to the current task.

## Async and cancellation

- Use asynchronous APIs for I/O.
- Accept and propagate `CancellationToken` through controllers, use cases, repositories, and HTTP integrations.
- Do not block on tasks with `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.

## Dependency injection and configuration

- Register application and infrastructure services through their layer-specific `DependencyInjection` classes.
- Use constructor injection. Do not use service location in business code.
- Use typed `HttpClient` instances created by `IHttpClientFactory` for external providers.
- Put non-sensitive settings in `appsettings.json`; never commit credentials or machine-specific paths.

## Persistence

- Use EF Core migrations. Never replace migrations with `EnsureCreated`, and never drop or recreate the database during normal startup.
- Keep the unique database index on `(Source, ExternalId)`.
- The local database belongs in `Data/jobs.db` and is not committed.

## Testing

- Add or update tests with every behavior change.
- Mock external HTTP responses; automated tests must not require a live service.
- Use SQLite, not the EF in-memory provider, for persistence integration tests.
- Before finishing, run:

  ```powershell
  dotnet restore AIJobSearchAgent.sln
  dotnet build AIJobSearchAgent.sln --no-restore
  dotnet test AIJobSearchAgent.sln --no-build --no-restore
  ```

## Running locally

```powershell
dotnet run --project src/AIJobSearchAgent.Api
```

In Development, Swagger is available at `http://localhost:5091/swagger`. The API applies pending migrations at startup without deleting existing data.
