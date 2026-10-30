namespace AIJobSearchAgent.Application.Models;

public sealed record JobSearchCriteria(IReadOnlyList<string> Keywords, int MaxPages);
