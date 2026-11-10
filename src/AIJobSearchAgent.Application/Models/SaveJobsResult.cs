namespace AIJobSearchAgent.Application.Models;

public sealed record SaveJobsResult(int Inserted, int SkippedDuplicates)
{
    public IReadOnlyList<Guid> PersistedVacancyIds { get; init; } = [];
}
