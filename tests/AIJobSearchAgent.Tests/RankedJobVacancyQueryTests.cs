using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Tests;

public sealed class RankedJobVacancyQueryTests
{
    private const string ProfileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ModelName = "qwen3:1.7b";
    private const string PromptVersion = "job-match-simple-v4";

    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyLatestEvaluationForCurrentIdentity()
    {
        await using var database = await TestDatabase.CreateAsync();
        var successful = CreateVacancy("successful");
        var latestFailed = CreateVacancy("latest-failed");
        var timedOut = CreateVacancy("timed-out");
        var differentProfile = CreateVacancy("different-profile");
        var differentModel = CreateVacancy("different-model");
        var differentPrompt = CreateVacancy("different-prompt");
        var withoutEvaluation = CreateVacancy("without-evaluation");
        database.Context.JobVacancies.AddRange(
            successful,
            latestFailed,
            timedOut,
            differentProfile,
            differentModel,
            differentPrompt,
            withoutEvaluation);

        var oldDate = DateTimeOffset.Parse("2026-11-01T10:00:00Z");
        var newDate = DateTimeOffset.Parse("2026-11-02T10:00:00Z");
        database.Context.JobMatchEvaluations.AddRange(
            CreateEvaluation(successful.Id, 5, EvaluationStatus.Succeeded, newDate, explanation: new string('x', 600)),
            CreateEvaluation(
                successful.Id,
                1,
                EvaluationStatus.Succeeded,
                newDate.AddDays(1),
                profileHash: new string('b', 64)),
            CreateEvaluation(latestFailed.Id, 4, EvaluationStatus.Succeeded, oldDate),
            CreateEvaluation(latestFailed.Id, null, EvaluationStatus.Failed, newDate),
            CreateEvaluation(timedOut.Id, null, EvaluationStatus.TimedOut, newDate),
            CreateEvaluation(
                differentProfile.Id,
                5,
                EvaluationStatus.Succeeded,
                newDate,
                profileHash: new string('b', 64)),
            CreateEvaluation(
                differentModel.Id,
                5,
                EvaluationStatus.Succeeded,
                newDate,
                modelName: "other-model"),
            CreateEvaluation(
                differentPrompt.Id,
                5,
                EvaluationStatus.Succeeded,
                newDate,
                promptVersion: "job-match-simple-v3"));
        await database.Context.SaveChangesAsync();

        var result = await database.Query.ExecuteAsync(
            new RankedJobQuery(null, null, 1, 20, RankedJobSort.Newest),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        Assert.Equal(7, result.TotalCount);
        var items = result.Items.ToDictionary(item => item.VacancyId);
        Assert.Equal(successful.Title, items[successful.Id].Title);
        Assert.Equal(successful.Company, items[successful.Id].Company);
        Assert.Equal(successful.Location, items[successful.Id].Location);
        Assert.Equal(successful.IsRemote, items[successful.Id].IsRemote);
        Assert.Equal(successful.Source, items[successful.Id].Source);
        Assert.Equal(successful.Url, items[successful.Id].Url);
        Assert.Equal(successful.PublishedAt, items[successful.Id].PublishedAt);
        Assert.Equal(5, items[successful.Id].Rating);
        Assert.Equal(EvaluationStatus.Succeeded, items[successful.Id].EvaluationStatus);
        Assert.Equal(500, items[successful.Id].Explanation?.Length);
        Assert.EndsWith("...", items[successful.Id].Explanation, StringComparison.Ordinal);
        Assert.Null(items[latestFailed.Id].Rating);
        Assert.Equal(EvaluationStatus.Failed, items[latestFailed.Id].EvaluationStatus);
        Assert.Equal(newDate, items[latestFailed.Id].EvaluatedAt);
        Assert.Null(items[timedOut.Id].Rating);
        Assert.Equal(EvaluationStatus.TimedOut, items[timedOut.Id].EvaluationStatus);
        AssertCurrentEvaluationIsMissing(items[differentProfile.Id]);
        AssertCurrentEvaluationIsMissing(items[differentModel.Id]);
        AssertCurrentEvaluationIsMissing(items[differentPrompt.Id]);
        AssertCurrentEvaluationIsMissing(items[withoutEvaluation.Id]);
    }

    [Fact]
    public async Task ExecuteAsync_UsesIdDescendingWhenEvaluationDatesAreEqual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vacancy = CreateVacancy("same-date");
        var evaluatedAt = DateTimeOffset.Parse("2026-11-02T10:00:00Z");
        database.Context.JobVacancies.Add(vacancy);
        database.Context.JobMatchEvaluations.AddRange(
            CreateEvaluation(
                vacancy.Id,
                5,
                EvaluationStatus.Succeeded,
                evaluatedAt,
                id: Guid.Parse("00000000-0000-0000-0000-000000000001")),
            CreateEvaluation(
                vacancy.Id,
                null,
                EvaluationStatus.TimedOut,
                evaluatedAt,
                id: Guid.Parse("00000000-0000-0000-0000-000000000002")));
        await database.Context.SaveChangesAsync();

        var result = await database.Query.ExecuteAsync(
            new RankedJobQuery(null, null, 1, 20, RankedJobSort.RatingDescending),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EvaluationStatus.TimedOut, item.EvaluationStatus);
        Assert.Null(item.Rating);
    }

    [Fact]
    public async Task ExecuteAsync_SortsRatingsBothDirectionsWithNullsLast()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ratingFive = CreateVacancy("rating-5", publishedAt: "2026-11-01T10:00:00Z");
        var ratingThree = CreateVacancy("rating-3", publishedAt: "2026-11-02T10:00:00Z");
        var ratingOne = CreateVacancy("rating-1", publishedAt: "2026-11-03T10:00:00Z");
        var failed = CreateVacancy("failed", publishedAt: "2026-11-05T10:00:00Z");
        var unrated = CreateVacancy("unrated", publishedAt: "2026-11-04T10:00:00Z");
        database.Context.JobVacancies.AddRange(ratingFive, ratingThree, ratingOne, failed, unrated);
        database.Context.JobMatchEvaluations.AddRange(
            CreateEvaluation(ratingFive.Id, 5, EvaluationStatus.Succeeded),
            CreateEvaluation(ratingThree.Id, 3, EvaluationStatus.Succeeded),
            CreateEvaluation(ratingOne.Id, 1, EvaluationStatus.Succeeded),
            CreateEvaluation(failed.Id, null, EvaluationStatus.Failed));
        await database.Context.SaveChangesAsync();

        var descending = await database.Query.ExecuteAsync(
            new RankedJobQuery(null, null, 1, 20, RankedJobSort.RatingDescending),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);
        var ascending = await database.Query.ExecuteAsync(
            new RankedJobQuery(null, null, 1, 20, RankedJobSort.RatingAscending),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        Assert.Equal([5, 3, 1, null, null], descending.Items.Select(item => item.Rating));
        Assert.Equal([1, 3, 5, null, null], ascending.Items.Select(item => item.Rating));
        Assert.Equal([failed.Id, unrated.Id], descending.Items.Skip(3).Select(item => item.VacancyId));
    }

    [Fact]
    public async Task ExecuteAsync_SortsBeforePagination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ratingThree = CreateVacancy("rating-3");
        var ratingFive = CreateVacancy("rating-5");
        var ratingFour = CreateVacancy("rating-4");
        database.Context.JobVacancies.AddRange(ratingThree, ratingFive, ratingFour);
        database.Context.JobMatchEvaluations.AddRange(
            CreateEvaluation(ratingThree.Id, 3, EvaluationStatus.Succeeded),
            CreateEvaluation(ratingFive.Id, 5, EvaluationStatus.Succeeded),
            CreateEvaluation(ratingFour.Id, 4, EvaluationStatus.Succeeded));
        await database.Context.SaveChangesAsync();

        var result = await database.Query.ExecuteAsync(
            new RankedJobQuery(null, null, 2, 1, RankedJobSort.RatingDescending),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(4, Assert.Single(result.Items).Rating);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesNewestKeywordAndSourceSortingAndFiltering()
    {
        await using var database = await TestDatabase.CreateAsync();
        var olderMatch = CreateVacancy(
            "older-match",
            source: "Arbeitnow",
            title: ".NET Engineer",
            description: "Build distributed services.",
            publishedAt: "2026-11-01T10:00:00Z");
        var newerMatch = CreateVacancy(
            "newer-match",
            source: "Arbeitnow",
            title: "Platform Engineer",
            description: "Build distributed .NET services.",
            publishedAt: "2026-11-03T10:00:00Z");
        var otherSource = CreateVacancy(
            "other-source",
            source: "Other",
            title: ".NET Engineer",
            description: "Build distributed services.",
            publishedAt: "2026-11-04T10:00:00Z");
        var missingToken = CreateVacancy(
            "missing-token",
            source: "Arbeitnow",
            title: ".NET Developer",
            description: "Build services.",
            publishedAt: "2026-11-05T10:00:00Z");
        database.Context.JobVacancies.AddRange(olderMatch, newerMatch, otherSource, missingToken);
        await database.Context.SaveChangesAsync();

        var result = await database.Query.ExecuteAsync(
            new RankedJobQuery(".net engineer", "ARBEITNOW", 1, 20, RankedJobSort.Newest),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([newerMatch.Id, olderMatch.Id], result.Items.Select(item => item.VacancyId));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyPageWhenNothingMatches()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Query.ExecuteAsync(
            new RankedJobQuery("missing", null, 1, 20, RankedJobSort.RatingDescending),
            ProfileHash,
            ModelName,
            PromptVersion,
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    private static void AssertCurrentEvaluationIsMissing(RankedJobVacancyDto item)
    {
        Assert.Null(item.Rating);
        Assert.Null(item.EvaluationStatus);
        Assert.Null(item.EvaluatedAt);
        Assert.Null(item.Explanation);
    }

    private static JobVacancy CreateVacancy(
        string externalId,
        string source = "Arbeitnow",
        string title = ".NET Engineer",
        string description = "Build services.",
        string publishedAt = "2026-11-01T10:00:00Z") => new()
        {
            ExternalId = externalId,
            Source = source,
            Title = title,
            Company = "Example",
            Location = "Remote",
            IsRemote = true,
            Description = description,
            Url = $"https://example.test/jobs/{externalId}",
            PublishedAt = DateTimeOffset.Parse(publishedAt),
            CreatedAt = DateTimeOffset.Parse(publishedAt).AddMinutes(1)
        };

    private static JobMatchEvaluation CreateEvaluation(
        Guid vacancyId,
        int? rating,
        EvaluationStatus status,
        DateTimeOffset? evaluatedAt = null,
        string? profileHash = null,
        string? modelName = null,
        string? promptVersion = null,
        string explanation = "Evaluation explanation.",
        Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            JobVacancyId = vacancyId,
            ProfileHash = profileHash ?? ProfileHash,
            ModelName = modelName ?? ModelName,
            PromptVersion = promptVersion ?? PromptVersion,
            Rating = rating,
            Explanation = explanation,
            EvaluationStatus = status,
            EvaluatedAt = evaluatedAt ?? DateTimeOffset.Parse("2026-11-02T10:00:00Z")
        };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            this.connection = connection;
            Context = context;
            Query = new RankedJobVacancyQuery(context);
        }

        public AppDbContext Context { get; }
        public RankedJobVacancyQuery Query { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.MigrateAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
