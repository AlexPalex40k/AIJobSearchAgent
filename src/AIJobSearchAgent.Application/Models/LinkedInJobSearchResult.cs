using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Models;

public sealed record LinkedInJobSearchResult(
    IReadOnlyList<JobVacancy> Vacancies,
    bool IsPartial,
    bool IsBlocked,
    bool LoginRequired,
    string? RestrictionReason,
    IReadOnlyList<string> Limitations);
