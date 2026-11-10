using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobMatchEvaluationRepository
{
    Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
        Guid jobVacancyId,
        CancellationToken cancellationToken);

    Task<JobMatchEvaluation?> GetSuccessfulAsync(
        Guid jobVacancyId,
        string profileHash,
        string modelName,
        string promptVersion,
        CancellationToken cancellationToken);
}
