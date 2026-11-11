using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Api.Contracts;

public sealed record LinkedInSearchResponse(
    IReadOnlyList<JobVacancyDto> Vacancies,
    bool IsPartial,
    bool IsBlocked,
    bool LoginRequired,
    string? RestrictionReason,
    IReadOnlyList<string> Limitations)
{
    public static LinkedInSearchResponse FromResult(LinkedInJobSearchResult result) => new(
        result.Vacancies.Select(JobVacancyDto.FromEntity).ToArray(),
        result.IsPartial,
        result.IsBlocked,
        result.LoginRequired,
        result.RestrictionReason,
        result.Limitations);
}
