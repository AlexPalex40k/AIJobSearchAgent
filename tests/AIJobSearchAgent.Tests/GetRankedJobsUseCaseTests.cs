using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Tests;

public sealed class GetRankedJobsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_UsesCurrentProfileModelAndPromptWithoutEvaluating()
    {
        var query = new RecordingRankedJobVacancyQuery();
        var matchingService = new MetadataOnlyMatchingService();
        var useCase = new GetRankedJobsUseCase(
            query,
            new StubProfileProvider(),
            matchingService);
        var request = new RankedJobQuery(".net", "Arbeitnow", 2, 10, RankedJobSort.RatingDescending);

        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(request, query.ReceivedQuery);
        Assert.Equal(StubProfileProvider.ProfileHash, query.ReceivedProfileHash);
        Assert.Equal(matchingService.ModelName, query.ReceivedModelName);
        Assert.Equal(matchingService.PromptVersion, query.ReceivedPromptVersion);
        Assert.Equal(0, matchingService.EvaluateCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidQueryParametersBeforeLoadingProfile()
    {
        var rankedQuery = new RecordingRankedJobVacancyQuery();
        var profileProvider = new StubProfileProvider();
        var useCase = new GetRankedJobsUseCase(
            rankedQuery,
            profileProvider,
            new MetadataOnlyMatchingService());
        var tooManyTokens = string.Join(
            ' ',
            Enumerable.Range(1, KeywordMatcher.MaximumSearchTokenCount + 1).Select(index => $"k{index}"));
        RankedJobQuery[] invalidQueries =
        [
            new(null, null, 0, 20, RankedJobSort.RatingDescending),
            new(null, null, 1, 0, RankedJobSort.RatingDescending),
            new(null, null, 1, GetRankedJobsUseCase.MaximumPageSize + 1, RankedJobSort.RatingDescending),
            new(new string('a', KeywordMatcher.MaximumSearchQueryLength + 1), null, 1, 20, RankedJobSort.RatingDescending),
            new(tooManyTokens, null, 1, 20, RankedJobSort.RatingDescending),
            new(null, new string('a', GetRankedJobsUseCase.MaximumSourceLength + 1), 1, 20, RankedJobSort.RatingDescending),
            new(null, null, 1, 20, (RankedJobSort)999)
        ];

        foreach (var invalidQuery in invalidQueries)
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                useCase.ExecuteAsync(invalidQuery, CancellationToken.None));
        }

        Assert.Equal(0, profileProvider.CallCount);
        Assert.Null(rankedQuery.ReceivedQuery);
    }

    [Theory]
    [InlineData("rating_desc", RankedJobSort.RatingDescending)]
    [InlineData("RATING_ASC", RankedJobSort.RatingAscending)]
    [InlineData(" newest ", RankedJobSort.Newest)]
    public void SortParser_AcceptsSupportedValues(string value, RankedJobSort expected)
    {
        Assert.True(RankedJobSortParser.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rating")]
    [InlineData("oldest")]
    public void SortParser_RejectsUnsupportedValues(string? value)
    {
        Assert.False(RankedJobSortParser.TryParse(value, out _));
    }

    private sealed class RecordingRankedJobVacancyQuery : IRankedJobVacancyQuery
    {
        public RankedJobQuery? ReceivedQuery { get; private set; }
        public string? ReceivedProfileHash { get; private set; }
        public string? ReceivedModelName { get; private set; }
        public string? ReceivedPromptVersion { get; private set; }

        public Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
            RankedJobQuery query,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken)
        {
            ReceivedQuery = query;
            ReceivedProfileHash = profileHash;
            ReceivedModelName = modelName;
            ReceivedPromptVersion = promptVersion;
            return Task.FromResult(new PagedResult<RankedJobVacancyDto>([], 0, query.Page, query.PageSize));
        }
    }

    private sealed class StubProfileProvider : ICandidateProfileProvider
    {
        public const string ProfileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        public int CallCount { get; private set; }

        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                ProfileHash));
        }
    }

    private sealed class MetadataOnlyMatchingService : IJobMatchingService
    {
        public string ModelName => "qwen3:1.7b";
        public string PromptVersion => "job-match-simple-v4";
        public int EvaluateCallCount { get; private set; }

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken)
        {
            EvaluateCallCount++;
            throw new InvalidOperationException("Ranked reads must not evaluate vacancies.");
        }
    }
}
