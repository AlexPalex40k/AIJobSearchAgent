using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Tests;

public sealed class JobVacancyRepositoryTests
{
    [Fact]
    public async Task AddNewAsync_PersistsJobsAndSkipsDuplicateSourceExternalIdPairs()
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
}
