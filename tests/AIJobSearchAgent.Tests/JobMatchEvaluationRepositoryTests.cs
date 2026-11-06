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

        await migrator.MigrateAsync();

        Assert.Equal(1, await dbContext.JobVacancies.CountAsync());
        var repository = new JobMatchEvaluationRepository(dbContext);
        var older = CreateEvaluation(vacancy.Id, 3, DateTimeOffset.UtcNow.AddMinutes(-1));
        var newer = CreateEvaluation(vacancy.Id, 4, DateTimeOffset.UtcNow);
        await repository.AddAsync(older, CancellationToken.None);
        await repository.AddAsync(newer, CancellationToken.None);

        var history = await repository.GetByVacancyIdAsync(vacancy.Id, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(4, history[0].Rating);
        Assert.Equal(["C#"], history[0].MatchedSkills);
        Assert.Equal(EvaluationStatus.Succeeded, history[0].EvaluationStatus);
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
        int rating,
        DateTimeOffset evaluatedAt) => new()
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
            EvaluationStatus = EvaluationStatus.Succeeded,
            EvaluatedAt = evaluatedAt
        };
}
