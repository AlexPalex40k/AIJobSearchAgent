using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class EvaluateJobUseCase(
    IJobVacancyRepository vacancyRepository,
    IJobMatchEvaluationRepository evaluationRepository,
    ICandidateProfileProvider profileProvider,
    IJobMatchingService matchingService,
    ILogger<EvaluateJobUseCase> logger)
{
    public async Task<JobMatchEvaluationDto?> ExecuteAsync(Guid vacancyId, CancellationToken cancellationToken)
    {
        var vacancy = await vacancyRepository.GetByIdAsync(vacancyId, cancellationToken);
        if (vacancy is null)
        {
            return null;
        }

        var profileSnapshot = await profileProvider.LoadAsync(cancellationToken);
        logger.LogInformation("Starting evaluation for vacancy {VacancyId}", vacancyId);

        try
        {
            var result = await matchingService.EvaluateAsync(
                vacancy,
                profileSnapshot.Profile,
                cancellationToken);
            var evaluation = CreateSucceededEvaluation(vacancyId, profileSnapshot.ProfileHash, result);
            await evaluationRepository.AddAsync(evaluation, cancellationToken);
            logger.LogInformation(
                "Completed evaluation for vacancy {VacancyId} with rating {Rating} using {Model}",
                vacancyId,
                result.Rating,
                result.ModelName);
            return JobMatchEvaluationDto.FromEntity(evaluation);
        }
        catch (JobMatchingException exception)
        {
            var failedEvaluation = CreateFailedEvaluation(vacancyId, profileSnapshot.ProfileHash, exception);
            await evaluationRepository.AddAsync(failedEvaluation, cancellationToken);
            logger.LogWarning(
                "Evaluation for vacancy {VacancyId} failed with status {Status}",
                vacancyId,
                exception.EvaluationStatus);
            throw;
        }
    }

    private static JobMatchEvaluation CreateSucceededEvaluation(
        Guid vacancyId,
        string profileHash,
        JobMatchResult result) => new()
        {
            JobVacancyId = vacancyId,
            ProfileHash = profileHash,
            ModelName = result.ModelName,
            PromptVersion = result.PromptVersion,
            Rating = result.Rating,
            MatchedSkills = [.. result.MatchedSkills],
            MissingRequiredSkills = [.. result.MissingRequiredSkills],
            MissingOptionalSkills = [.. result.MissingOptionalSkills],
            Explanation = result.Explanation,
            EvaluationStatus = EvaluationStatus.Succeeded,
            EvaluatedAt = result.EvaluatedAt
        };

    private static JobMatchEvaluation CreateFailedEvaluation(
        Guid vacancyId,
        string profileHash,
        JobMatchingException exception) => new()
        {
            JobVacancyId = vacancyId,
            ProfileHash = profileHash,
            ModelName = exception.ModelName,
            PromptVersion = exception.PromptVersion,
            Rating = null,
            Explanation = exception.Message,
            EvaluationStatus = exception.EvaluationStatus,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
}
