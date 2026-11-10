namespace AIJobSearchAgent.Application.Models;

public sealed record ImportAndEvaluateJobsSummary(
    ImportSummary Import,
    int SuccessfullyEvaluatedCount,
    int SkippedCount,
    int FailedCount);
