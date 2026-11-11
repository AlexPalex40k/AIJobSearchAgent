namespace AIJobSearchAgent.Application.Models;

public sealed record JobSearchCriteria(
    IReadOnlyList<string> Keywords,
    int MaxPages,
    string? Location = null,
    int? PublishedWithinDays = null,
    int MaxJobs = 10,
    string? GeoId = null);
