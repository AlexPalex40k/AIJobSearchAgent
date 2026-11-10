using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIJobSearchAgent.Tests;

public sealed class JobMatchEvaluationRepositoryTests
{
    [Fact]
    public async Task MigrationPreservesVacanciesAndRepositoryPersistsEvaluationHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync("20261030000000_InitialCreate");

        var vacancy = CreateVacancy();
        dbContext.JobVacancies.Add(vacancy);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await migrator.MigrateAsync("20261030190000_AddJobMatchEvaluations");
        var repository = new JobMatchEvaluationRepository(dbContext);
        var older = CreateEvaluation(vacancy.Id, 3, DateTimeOffset.UtcNow.AddMinutes(-1));
        await repository.AddAsync(older, CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        Assert.Equal(1, await dbContext.JobVacancies.CountAsync());
        Assert.Equal(1, await dbContext.JobMatchEvaluations.CountAsync());
        var newer = CreateEvaluation(vacancy.Id, 4, DateTimeOffset.UtcNow);
        await repository.AddAsync(newer, CancellationToken.None);

        var history = await repository.GetByVacancyIdAsync(vacancy.Id, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(4, history[0].Rating);
        Assert.Equal(["C#"], history[0].MatchedSkills);
        Assert.Equal(EvaluationStatus.Succeeded, history[0].EvaluationStatus);

        var matchingEvaluation = await repository.GetSuccessfulAsync(
            vacancy.Id,
            newer.ProfileHash,
            newer.ModelName,
            newer.PromptVersion,
            CancellationToken.None);
        var differentProfileEvaluation = await repository.GetSuccessfulAsync(
            vacancy.Id,
            new string('b', 64),
            newer.ModelName,
            newer.PromptVersion,
            CancellationToken.None);

        Assert.Equal(newer.Id, matchingEvaluation?.Id);
        Assert.Null(differentProfileEvaluation);
    }

    [Fact]
    public async Task DatabaseEnforcesEvaluationStatusAndRatingConsistency()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var vacancy = CreateVacancy();

        await using (var setupContext = new AppDbContext(options))
        {
            await setupContext.Database.MigrateAsync();
            setupContext.JobVacancies.Add(vacancy);
            await setupContext.SaveChangesAsync();
        }

        await using (var validContext = new AppDbContext(options))
        {
            validContext.JobMatchEvaluations.AddRange(
                CreateEvaluation(vacancy.Id, 5, DateTimeOffset.UtcNow, EvaluationStatus.Succeeded),
                CreateEvaluation(vacancy.Id, null, DateTimeOffset.UtcNow, EvaluationStatus.Failed),
                CreateEvaluation(vacancy.Id, null, DateTimeOffset.UtcNow, EvaluationStatus.TimedOut));
            await validContext.SaveChangesAsync();
        }

        await AssertRejectedAsync(options, CreateEvaluation(
            vacancy.Id,
            null,
            DateTimeOffset.UtcNow,
            EvaluationStatus.Succeeded));
        await AssertRejectedAsync(options, CreateEvaluation(
            vacancy.Id,
            3,
            DateTimeOffset.UtcNow,
            EvaluationStatus.Failed));
        await AssertRejectedAsync(options, CreateEvaluation(
            vacancy.Id,
            3,
            DateTimeOffset.UtcNow,
            EvaluationStatus.TimedOut));
        await AssertRejectedAsync(options, CreateEvaluation(
            vacancy.Id,
            6,
            DateTimeOffset.UtcNow,
            EvaluationStatus.Succeeded));
    }

    [Fact]
    public async Task CurrentModelMatchesMigrationSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    private static JobVacancy CreateVacancy() => new()
    {
        ExternalId = "persisted-job",
        Source = "Test",
        Title = ".NET Engineer",
        Company = "Example",
        Location = "Remote",
        Description = "Build APIs.",
        Url = "https://example.test/jobs/persisted-job"
    };

    private static JobMatchEvaluation CreateEvaluation(
        Guid vacancyId,
        int? rating,
        DateTimeOffset evaluatedAt,
        EvaluationStatus status = EvaluationStatus.Succeeded) => new()
        {
            JobVacancyId = vacancyId,
            ProfileHash = new string('a', 64),
            ModelName = "qwen3:1.7b",
            PromptVersion = "job-match-v1",
            Rating = rating,
            MatchedSkills = ["C#"],
            MissingRequiredSkills = [],
            MissingOptionalSkills = ["Kubernetes"],
            Explanation = "Test evaluation.",
            EvaluationStatus = status,
            EvaluatedAt = evaluatedAt
        };

    private static async Task AssertRejectedAsync(
        DbContextOptions<AppDbContext> options,
        JobMatchEvaluation evaluation)
    {
        await using var dbContext = new AppDbContext(options);
        dbContext.JobMatchEvaluations.Add(evaluation);
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }
}
