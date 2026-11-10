namespace AIJobSearchAgent.Application.Models;

public sealed record BatchJobEvaluationSummary(
    int TotalRequestedVacancies,
    int SuccessfullyEvaluatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BatchJobEvaluationResult> Results);

public sealed record BatchJobEvaluationResult(
    Guid VacancyId,
    BatchJobEvaluationStatus Status,
    int? Rating,
    string Explanation);

public enum BatchJobEvaluationStatus
{
    Succeeded,
    Skipped,
    Failed
}
