using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class GetJobsUseCase(IJobVacancyRepository repository)
{
    public async Task<PagedResult<JobVacancyDto>> ExecuteAsync(
        JobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
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

        var result = await repository.GetAsync(query, cancellationToken);
        return new PagedResult<JobVacancyDto>(
            result.Items.Select(JobVacancyDto.FromEntity).ToArray(),
            result.TotalCount,
            result.Page,
            result.PageSize);
    }

    public async Task<JobVacancyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var vacancy = await repository.GetByIdAsync(id, cancellationToken);
        return vacancy is null ? null : JobVacancyDto.FromEntity(vacancy);
    }
}
