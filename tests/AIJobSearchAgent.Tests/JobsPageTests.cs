using AIJobSearchAgent.Api.Pages.Jobs;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace AIJobSearchAgent.Tests;

public sealed class JobsPageTests
{
    [Fact]
    public async Task OnGetAsync_LoadsRankedJobsAndPreservesFilterSortAndPageState()
    {
        var rankedQuery = new RecordingRankedJobVacancyQuery(totalCount: 45);
        var model = CreatePageModel(rankedQuery);
        using var cancellationSource = new CancellationTokenSource();

        var result = await model.OnGetAsync(
            ".NET backend",
            "rating_asc",
            2,
            cancellationSource.Token);

        Assert.IsType<PageResult>(result);
        Assert.Equal(".NET backend", model.Keyword);
        Assert.Equal("rating_asc", model.Sort);
        Assert.Equal(2, model.PageNumber);
        Assert.True(model.HasPreviousPage);
        Assert.True(model.HasNextPage);
        Assert.Equal(45, model.Jobs.TotalCount);
        Assert.NotNull(rankedQuery.ReceivedQuery);
        Assert.Equal(".NET backend", rankedQuery.ReceivedQuery.Keyword);
        Assert.Null(rankedQuery.ReceivedQuery.Source);
        Assert.Equal(2, rankedQuery.ReceivedQuery.Page);
        Assert.Equal(IndexModel.PageSize, rankedQuery.ReceivedQuery.PageSize);
        Assert.Equal(RankedJobSort.RatingAscending, rankedQuery.ReceivedQuery.Sort);
        Assert.Equal(cancellationSource.Token, rankedQuery.ReceivedCancellationToken);
    }

    [Fact]
    public async Task OnGetAsync_NormalizesInvalidPageAndSortWithoutLosingResults()
    {
        var rankedQuery = new RecordingRankedJobVacancyQuery(totalCount: 1);
        var model = CreatePageModel(rankedQuery);

        await model.OnGetAsync(null, "unsupported", 0, CancellationToken.None);

        Assert.Equal(1, model.PageNumber);
        Assert.Equal(RankedJobSortParser.DefaultValue, model.Sort);
        Assert.False(model.ModelState.IsValid);
        Assert.Equal(RankedJobSort.RatingDescending, rankedQuery.ReceivedQuery?.Sort);
        Assert.Single(model.Jobs.Items);
    }

    [Fact]
    public async Task OnGetAsync_DoesNotQueryWhenKeywordIsInvalid()
    {
        var rankedQuery = new RecordingRankedJobVacancyQuery(totalCount: 1);
        var model = CreatePageModel(rankedQuery);
        var keyword = string.Join(
            ' ',
            Enumerable.Range(1, KeywordMatcher.MaximumSearchTokenCount + 1).Select(index => $"k{index}"));

        await model.OnGetAsync(keyword, null, 1, CancellationToken.None);

        Assert.False(model.ModelState.IsValid);
        Assert.Null(rankedQuery.ReceivedQuery);
        Assert.Empty(model.Jobs.Items);
        Assert.Equal(0, model.Jobs.TotalCount);
    }

    [Theory]
    [InlineData("https://example.test/jobs/1", true)]
    [InlineData("http://example.test/jobs/1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/candidate-profile.json", false)]
    [InlineData("/api/jobs", false)]
    [InlineData(null, false)]
    public void IsSafeExternalUrl_AllowsOnlyAbsoluteHttpUrls(string? url, bool expected)
    {
        Assert.Equal(expected, IndexModel.IsSafeExternalUrl(url));
    }

    private static IndexModel CreatePageModel(RecordingRankedJobVacancyQuery rankedQuery)
    {
        var useCase = new GetRankedJobsUseCase(
            rankedQuery,
            new StubProfileProvider(),
            new MetadataOnlyMatchingService());
        return new IndexModel(useCase)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
                ViewData = new ViewDataDictionary(
                    new EmptyModelMetadataProvider(),
                    new ModelStateDictionary())
            }
        };
    }

    private sealed class RecordingRankedJobVacancyQuery(int totalCount) : IRankedJobVacancyQuery
    {
        public RankedJobQuery? ReceivedQuery { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
            RankedJobQuery query,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken)
        {
            ReceivedQuery = query;
            ReceivedCancellationToken = cancellationToken;
            IReadOnlyList<RankedJobVacancyDto> items =
            [
                new(
                    Guid.NewGuid(),
                    ".NET Engineer",
                    "Example",
                    "Remote",
                    true,
                    "Test",
                    "https://example.test/jobs/1",
                    DateTimeOffset.Parse("2026-11-10T10:00:00Z"),
                    4,
                    EvaluationStatus.Succeeded,
                    DateTimeOffset.Parse("2026-11-10T11:00:00Z"),
                    "Good match.")
            ];
            return Task.FromResult(new PagedResult<RankedJobVacancyDto>(
                items,
                totalCount,
                query.Page,
                query.PageSize));
        }
    }

    private sealed class StubProfileProvider : ICandidateProfileProvider
    {
        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(
            new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                new string('a', 64)));
    }

    private sealed class MetadataOnlyMatchingService : IJobMatchingService
    {
        public string ModelName => "qwen3:1.7b";
        public string PromptVersion => "job-match-simple-v4";

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The jobs page must not trigger evaluation.");
    }
}
