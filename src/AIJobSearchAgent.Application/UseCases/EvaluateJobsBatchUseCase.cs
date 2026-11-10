using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class EvaluateJobsBatchUseCase(
    IJobVacancyRepository vacancyRepository,
    IJobMatchEvaluationRepository evaluationRepository,
    ICandidateProfileProvider profileProvider,
    IJobMatchingService matchingService,
    EvaluateJobUseCase evaluateJob,
    ILogger<EvaluateJobsBatchUseCase> logger)
{
    public const int MaximumBatchSize = 20;
    private const int MaximumExplanationLength = 500;

    public async Task<BatchJobEvaluationSummary> ExecuteAsync(
        IReadOnlyList<Guid> vacancyIds,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vacancyIds.Count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(vacancyIds.Count, MaximumBatchSize);

        var profileSnapshot = await profileProvider.LoadAsync(cancellationToken);
        var seenVacancyIds = new HashSet<Guid>();
        var results = new List<BatchJobEvaluationResult>(vacancyIds.Count);

        foreach (var vacancyId in vacancyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (vacancyId == Guid.Empty)
            {
                results.Add(Failed(vacancyId, "Vacancy ID is invalid."));
                continue;
            }

            if (!seenVacancyIds.Add(vacancyId))
            {
                var previousResult = results.Last(result => result.VacancyId == vacancyId);
                results.Add(new BatchJobEvaluationResult(
                    vacancyId,
                    BatchJobEvaluationStatus.Skipped,
                    previousResult.Rating,
                    "Skipped duplicate vacancy ID in this request."));
                continue;
            }

            try
            {
                var vacancy = await vacancyRepository.GetByIdAsync(vacancyId, cancellationToken);
                if (vacancy is null)
                {
                    results.Add(Failed(vacancyId, "Vacancy was not found."));
                    continue;
                }

                var existingEvaluation = await evaluationRepository.GetSuccessfulAsync(
                    vacancyId,
                    profileSnapshot.ProfileHash,
                    matchingService.ModelName,
                    matchingService.PromptVersion,
                    cancellationToken);
                if (existingEvaluation is not null)
                {
                    results.Add(new BatchJobEvaluationResult(
                        vacancyId,
                        BatchJobEvaluationStatus.Skipped,
                        existingEvaluation.Rating,
                        "Skipped because a successful evaluation already exists for this profile, model, and prompt version."));
                    continue;
                }

                var evaluation = await evaluateJob.ExecuteAsync(vacancy, profileSnapshot, cancellationToken);
                results.Add(new BatchJobEvaluationResult(
                    vacancyId,
                    BatchJobEvaluationStatus.Succeeded,
                    evaluation.Rating,
                    Shorten(evaluation.Explanation)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JobMatchingException exception)
            {
                results.Add(Failed(
                    vacancyId,
                    exception.EvaluationStatus == EvaluationStatus.TimedOut
                        ? "Evaluation timed out."
                        : "Evaluation failed."));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Batch evaluation failed for vacancy {VacancyId}", vacancyId);
                results.Add(Failed(vacancyId, "Evaluation failed."));
            }
        }

        return new BatchJobEvaluationSummary(
            vacancyIds.Count,
            results.Count(result => result.Status == BatchJobEvaluationStatus.Succeeded),
            results.Count(result => result.Status == BatchJobEvaluationStatus.Skipped),
            results.Count(result => result.Status == BatchJobEvaluationStatus.Failed),
            results);
    }

    private static BatchJobEvaluationResult Failed(Guid vacancyId, string explanation) => new(
        vacancyId,
        BatchJobEvaluationStatus.Failed,
        null,
        explanation);

    private static string Shorten(string explanation) => explanation.Length <= MaximumExplanationLength
        ? explanation
        : $"{explanation[..(MaximumExplanationLength - 3)]}...";
}
