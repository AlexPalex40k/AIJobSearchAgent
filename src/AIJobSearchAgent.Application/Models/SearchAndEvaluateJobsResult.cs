namespace AIJobSearchAgent.Application.Models;

public sealed record SearchAndEvaluateJobsResult(
    int TotalVacancies,
    int SuccessfullyEvaluated,
    int FailedEvaluations,
    int NotEvaluated,
    bool IsPartial,
    bool IsBlocked,
    IReadOnlyList<string> SourceLimitations,
    IReadOnlyList<SearchAndEvaluateJobResult> Results);
