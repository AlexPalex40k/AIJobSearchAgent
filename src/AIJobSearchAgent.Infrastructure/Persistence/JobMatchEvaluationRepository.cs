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
}
