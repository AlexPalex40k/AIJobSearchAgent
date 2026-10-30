using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class GetJobsUseCase(IJobVacancyRepository repository)
{
    public async Task<PagedResult<JobVacancyDto>> ExecuteAsync(
        JobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

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
