using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobVacancyRepository
{
    Task<SaveJobsResult> AddNewAsync(
        IReadOnlyCollection<JobVacancy> vacancies,
        CancellationToken cancellationToken);

    Task<PagedResult<JobVacancy>> GetAsync(JobQuery query, CancellationToken cancellationToken);

    Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
