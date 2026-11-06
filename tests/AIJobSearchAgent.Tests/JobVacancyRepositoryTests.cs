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

        var result = await repository.AddNewAsync(
            [
                CreateVacancy("duplicate", "Arbeitnow"),
                CreateVacancy("duplicate", "Arbeitnow"),
                CreateVacancy("unique", "Arbeitnow")
            ],
            CancellationToken.None);

        Assert.Equal(new SaveJobsResult(2, 1), result);
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

        var firstResult = await repository.AddNewAsync(
            [CreateVacancy("same-id", "Arbeitnow")],
            CancellationToken.None);
        var secondResult = await repository.AddNewAsync(
            [CreateVacancy("same-id", "Arbeitnow")],
            CancellationToken.None);

        Assert.Equal(new SaveJobsResult(1, 0), firstResult);
        Assert.Equal(new SaveJobsResult(0, 1), secondResult);
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

        var result = await repository.AddNewAsync(
            [
                CreateVacancy("batch-1", "Arbeitnow"),
                CreateVacancy("batch-2", "Arbeitnow"),
                CreateVacancy("batch-3", "Other")
            ],
            CancellationToken.None);

        Assert.Equal(new SaveJobsResult(3, 0), result);
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

    private static JobVacancy CreateVacancy(string externalId, string source) => new()
    {
        ExternalId = externalId,
        Source = source,
        Title = ".NET Developer",
        Company = "Example",
        Location = "Remote",
        IsRemote = true,
        Description = "Backend developer role",
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
