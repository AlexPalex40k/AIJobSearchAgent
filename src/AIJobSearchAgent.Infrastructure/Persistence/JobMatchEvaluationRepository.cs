using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class JobMatchEvaluationRepository(AppDbContext dbContext) : IJobMatchEvaluationRepository
{
    public async Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken)
    {
        dbContext.JobMatchEvaluations.Add(evaluation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
        Guid jobVacancyId,
        CancellationToken cancellationToken) => await dbContext.JobMatchEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.JobVacancyId == jobVacancyId)
            .OrderByDescending(evaluation => evaluation.EvaluatedAt)
            .ThenByDescending(evaluation => evaluation.Id)
            .ToListAsync(cancellationToken);

    public Task<JobMatchEvaluation?> GetSuccessfulAsync(
        Guid jobVacancyId,
        string profileHash,
        string modelName,
        string promptVersion,
        CancellationToken cancellationToken) => dbContext.JobMatchEvaluations
            .AsNoTracking()
            .Where(evaluation =>
                evaluation.JobVacancyId == jobVacancyId &&
                evaluation.ProfileHash == profileHash &&
                evaluation.ModelName == modelName &&
                evaluation.PromptVersion == promptVersion &&
                evaluation.EvaluationStatus == EvaluationStatus.Succeeded)
            .OrderByDescending(evaluation => evaluation.EvaluatedAt)
            .ThenByDescending(evaluation => evaluation.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
