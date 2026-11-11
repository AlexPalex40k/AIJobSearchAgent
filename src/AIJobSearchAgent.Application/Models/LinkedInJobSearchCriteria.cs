namespace AIJobSearchAgent.Application.Models;

public sealed record LinkedInJobSearchCriteria(
    string JobTitle,
    string Location,
    int? PublishedWithinDays,
    int MaxJobs,
    string? GeoId = null);
