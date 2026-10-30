namespace AIJobSearchAgent.Application.Models;

public sealed record ImportSummary(
    int TotalFetched,
    int TotalMatched,
    int Inserted,
    int SkippedDuplicates,
    int RejectedInvalid);
