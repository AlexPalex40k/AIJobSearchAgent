using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobMatchEvaluationRepository
{
    Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
        Guid jobVacancyId,
        CancellationToken cancellationToken);
}
