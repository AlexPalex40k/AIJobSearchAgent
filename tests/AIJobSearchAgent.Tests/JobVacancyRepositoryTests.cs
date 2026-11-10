using System.Data.Common;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AIJobSearchAgent.Tests;

public sealed class JobVacancyRepositoryTests
{
    [Fact]
    public async Task AddNewAsync_CountsDuplicatesWithinOneImport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var repository = new JobVacancyRepository(dbContext);

        var firstDuplicate = CreateVacancy("duplicate", "Arbeitnow");
        var ignoredDuplicate = CreateVacancy("duplicate", "Arbeitnow");
        var unique = CreateVacancy("unique", "Arbeitnow");
        var result = await repository.AddNewAsync(
            [firstDuplicate, ignoredDuplicate, unique],
            CancellationToken.None);

        Assert.Equal(2, result.Inserted);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.True(new HashSet<Guid> { firstDuplicate.Id, unique.Id }.SetEquals(result.PersistedVacancyIds));
        Assert.DoesNotContain(ignoredDuplicate.Id, result.PersistedVacancyIds);
        Assert.Equal(2, await dbContext.JobVacancies.CountAsync());
    }

    [Fact]
    public async Task AddNewAsync_CountsDuplicatesAlreadyStoredInDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var repository = new JobVacancyRepository(dbContext);

        var insertedVacancy = CreateVacancy("same-id", "Arbeitnow");
        var duplicateWithGeneratedId = CreateVacancy("same-id", "Arbeitnow");
        var firstResult = await repository.AddNewAsync(
            [insertedVacancy],
            CancellationToken.None);
        var secondResult = await repository.AddNewAsync(
            [duplicateWithGeneratedId],
            CancellationToken.None);

        Assert.Equal(1, firstResult.Inserted);
        Assert.Equal(0, firstResult.SkippedDuplicates);
        Assert.Equal([insertedVacancy.Id], firstResult.PersistedVacancyIds);
        Assert.Equal(0, secondResult.Inserted);
        Assert.Equal(1, secondResult.SkippedDuplicates);
        Assert.Equal([insertedVacancy.Id], secondResult.PersistedVacancyIds);
        Assert.DoesNotContain(duplicateWithGeneratedId.Id, secondResult.PersistedVacancyIds);
        Assert.Equal(1, await dbContext.JobVacancies.CountAsync());

        var page = await repository.GetAsync(
            new JobQuery("developer", "ARBEITNOW", 1, 10),
            CancellationToken.None);
        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task AddNewAsync_BatchesMultipleVacanciesIntoOneSaveChangesCall()
    {
        var interceptor = new SaveChangesCallInterceptor();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var repository = new JobVacancyRepository(dbContext);

        JobVacancy[] vacancies =
        [
            CreateVacancy("batch-1", "Arbeitnow"),
            CreateVacancy("batch-2", "Arbeitnow"),
            CreateVacancy("batch-3", "Other")
        ];
        var result = await repository.AddNewAsync(
            vacancies,
            CancellationToken.None);

        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.True(vacancies.Select(vacancy => vacancy.Id).ToHashSet().SetEquals(result.PersistedVacancyIds));
        Assert.Equal(1, interceptor.AsyncSavingChangesCalls);
        Assert.Equal(3, await dbContext.JobVacancies.CountAsync());
    }

    [Fact]
    public async Task AddNewAsync_PropagatesNonUniqueSqliteConstraintErrors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var repository = new JobVacancyRepository(dbContext);
        var invalidVacancy = CreateVacancy("invalid", "Arbeitnow");
        invalidVacancy.Title = null!;

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => repository.AddNewAsync([invalidVacancy], CancellationToken.None));

        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(19, sqliteException.SqliteErrorCode);
        Assert.NotEqual(2067, sqliteException.SqliteExtendedErrorCode);
        Assert.Equal(0, await dbContext.JobVacancies.CountAsync());
    }

    [Fact]
    public async Task AddNewAsync_ConcurrentImportsInsertOneAndCountOneDuplicate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AIJobSearchAgent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "concurrent.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 10,
            Pooling = false
        }.ToString();

        try
        {
            var setupOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var setupContext = new AppDbContext(setupOptions))
            {
                await setupContext.Database.MigrateAsync();
                await setupContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            }

            var queryBarrier = new ExistingVacanciesQueryBarrierInterceptor();
            var concurrentOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(queryBarrier)
                .Options;
            await using var firstContext = new AppDbContext(concurrentOptions);
            await using var secondContext = new AppDbContext(concurrentOptions);
            var firstRepository = new JobVacancyRepository(firstContext);
            var secondRepository = new JobVacancyRepository(secondContext);

            var results = await Task.WhenAll(
                firstRepository.AddNewAsync(
                    [CreateVacancy("concurrent", "Arbeitnow")],
                    CancellationToken.None),
                secondRepository.AddNewAsync(
                    [CreateVacancy("concurrent", "Arbeitnow")],
                    CancellationToken.None));

            Assert.Equal(1, results.Sum(result => result.Inserted));
            Assert.Equal(1, results.Sum(result => result.SkippedDuplicates));
            await using var verificationContext = new AppDbContext(setupOptions);
            Assert.Equal(1, await verificationContext.JobVacancies.CountAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_MatchesAllTokensInAnyOrderAcrossTitleAndDescription()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var titleMatch = CreateVacancy(
            "title-match",
            "Arbeitnow",
            "Senior Software Engineer (.NET/C#)",
            "Build reliable services.");
        var distributedMatch = CreateVacancy(
            "distributed-match",
            "Arbeitnow",
            "Platform Engineer",
            "Design and build .NET services.");
        var missingToken = CreateVacancy(
            "missing-token",
            "Arbeitnow",
            ".NET Developer",
            "Build backend services.");
        dbContext.JobVacancies.AddRange(titleMatch, distributedMatch, missingToken);
        await dbContext.SaveChangesAsync();
        var repository = new JobVacancyRepository(dbContext);

        var result = await repository.GetAsync(
            new JobQuery(".net engineer", null, 1, 20),
            CancellationToken.None);
        var reversedCaseResult = await repository.GetAsync(
            new JobQuery("EnGiNeEr .NeT", null, 1, 20),
            CancellationToken.None);
        var normalizedWhitespaceResult = await repository.GetAsync(
            new JobQuery("  \t.net\r\n engineer  ", null, 1, 20),
            CancellationToken.None);
        var missingRequiredTokenResult = await repository.GetAsync(
            new JobQuery(".net engineer python", null, 1, 20),
            CancellationToken.None);

        var expectedIds = new HashSet<Guid> { titleMatch.Id, distributedMatch.Id };
        Assert.Equal(2, result.TotalCount);
        Assert.True(expectedIds.SetEquals(result.Items.Select(vacancy => vacancy.Id)));
        Assert.True(expectedIds.SetEquals(reversedCaseResult.Items.Select(vacancy => vacancy.Id)));
        Assert.True(expectedIds.SetEquals(normalizedWhitespaceResult.Items.Select(vacancy => vacancy.Id)));
        Assert.Empty(missingRequiredTokenResult.Items);
        Assert.Equal(0, missingRequiredTokenResult.TotalCount);
    }

    [Fact]
    public async Task GetAsync_PreservesTechnicalKeywordPunctuation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        var vacancy = CreateVacancy(
            "technical-keywords",
            "Arbeitnow",
            "C# and C++ Engineer",
            "Develop services on .NET.");
        dbContext.JobVacancies.Add(vacancy);
        await dbContext.SaveChangesAsync();
        var repository = new JobVacancyRepository(dbContext);

        var result = await repository.GetAsync(
            new JobQuery("C# C++ .NET", null, 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(vacancy.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetAsync_AppliesPaginationAfterTokenFilteringAndKeepsAccurateTotalCount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        dbContext.JobVacancies.AddRange(
            CreateVacancy("match-1", "Arbeitnow", ".NET Engineer", "Backend services."),
            CreateVacancy("match-2", "Arbeitnow", "Platform Engineer", ".NET services."),
            CreateVacancy("match-3", "Other", "Lead .NET Engineer", "Architecture."),
            CreateVacancy("not-a-match", "Arbeitnow", "Python Engineer", "Data services."));
        await dbContext.SaveChangesAsync();
        var repository = new JobVacancyRepository(dbContext);

        var firstPage = await repository.GetAsync(
            new JobQuery(".net engineer", null, 1, 2),
            CancellationToken.None);
        var secondPage = await repository.GetAsync(
            new JobQuery(".net engineer", null, 2, 2),
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(3, secondPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Empty(firstPage.Items.Select(vacancy => vacancy.Id).Intersect(
            secondPage.Items.Select(vacancy => vacancy.Id)));
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicatePairWhenBypassingRepository()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();

        dbContext.JobVacancies.AddRange(
            CreateVacancy("duplicate", "Arbeitnow"),
            CreateVacancy("duplicate", "Arbeitnow"));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static JobVacancy CreateVacancy(
        string externalId,
        string source,
        string title = ".NET Developer",
        string description = "Backend developer role") =>
        new()
        {
            ExternalId = externalId,
            Source = source,
            Title = title,
            Company = "Example",
            Location = "Remote",
            IsRemote = true,
            Description = description,
            Url = $"https://example.test/jobs/{externalId}",
            PublishedAt = DateTimeOffset.UtcNow
        };

    private sealed class SaveChangesCallInterceptor : SaveChangesInterceptor
    {
        public int AsyncSavingChangesCalls { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            AsyncSavingChangesCalls++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ExistingVacanciesQueryBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothQueriesStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _queryCount;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"JobVacancies\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("ExternalId", StringComparison.Ordinal) &&
                command.CommandText.Contains("Source", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _queryCount) >= 2)
                {
                    _bothQueriesStarted.TrySetResult();
                }

                await _bothQueriesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
