using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Models;

public sealed record JobMatchEvaluationDto(
    Guid Id,
    Guid JobVacancyId,
    string ProfileHash,
    string ModelName,
    string PromptVersion,
    int? Rating,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingOptionalSkills,
    string Explanation,
    EvaluationStatus EvaluationStatus,
    DateTimeOffset EvaluatedAt)
{
    public static JobMatchEvaluationDto FromEntity(JobMatchEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.JobVacancyId,
        evaluation.ProfileHash,
        evaluation.ModelName,
        evaluation.PromptVersion,
        evaluation.Rating,
        evaluation.MatchedSkills,
        evaluation.MissingRequiredSkills,
        evaluation.MissingOptionalSkills,
        evaluation.Explanation,
        evaluation.EvaluationStatus,
        evaluation.EvaluatedAt);
}
