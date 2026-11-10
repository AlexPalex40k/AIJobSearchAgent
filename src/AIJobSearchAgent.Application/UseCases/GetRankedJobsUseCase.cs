using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class GetRankedJobsUseCase(
    IRankedJobVacancyQuery rankedJobVacancyQuery,
    ICandidateProfileProvider profileProvider,
    IJobMatchingService matchingService)
{
    public const int MaximumPageSize = 100;
    public const int MaximumSourceLength = 100;

    public async Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
        RankedJobQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        var profileSnapshot = await profileProvider.LoadAsync(cancellationToken);

        return await rankedJobVacancyQuery.ExecuteAsync(
            query,
            profileSnapshot.ProfileHash,
            matchingService.ModelName,
            matchingService.PromptVersion,
            cancellationToken);
    }

    private static void Validate(RankedJobQuery query)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, MaximumPageSize);

        if (query.Keyword?.Length > KeywordMatcher.MaximumSearchQueryLength)
        {
            throw new ArgumentException(
                $"Search query must not exceed {KeywordMatcher.MaximumSearchQueryLength} characters.",
                nameof(query));
        }

        if (KeywordMatcher.TokenizeSearchQuery(query.Keyword).Count > KeywordMatcher.MaximumSearchTokenCount)
        {
            throw new ArgumentException(
                $"Search query must not contain more than {KeywordMatcher.MaximumSearchTokenCount} tokens.",
                nameof(query));
        }

        if (query.Source?.Length > MaximumSourceLength)
        {
            throw new ArgumentException(
                $"Source must not exceed {MaximumSourceLength} characters.",
                nameof(query));
        }

        if (!Enum.IsDefined(query.Sort))
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }
    }
}
