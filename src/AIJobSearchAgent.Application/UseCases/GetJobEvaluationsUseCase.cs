using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class GetJobEvaluationsUseCase(
    IJobVacancyRepository vacancyRepository,
    IJobMatchEvaluationRepository evaluationRepository)
{
    public async Task<IReadOnlyList<JobMatchEvaluationDto>?> ExecuteAsync(
        Guid vacancyId,
        CancellationToken cancellationToken)
    {
        if (await vacancyRepository.GetByIdAsync(vacancyId, cancellationToken) is null)
        {
            return null;
        }

        var evaluations = await evaluationRepository.GetByVacancyIdAsync(vacancyId, cancellationToken);
        return evaluations.Select(JobMatchEvaluationDto.FromEntity).ToArray();
    }
}
