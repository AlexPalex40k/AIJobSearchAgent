using AIJobSearchAgent.LinkedInSpike;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure.Providers;

internal interface ILinkedInSearchScraper
{
    Task<LinkedInScrapeResult> SearchAsync(
        Uri searchUrl,
        int maxJobs,
        CancellationToken cancellationToken);
}

internal sealed class LinkedInSeleniumSearchScraper(
    SeleniumExperiment experiment,
    IOptions<LinkedInOptions> options) : ILinkedInSearchScraper
{
    public async Task<LinkedInScrapeResult> SearchAsync(
        Uri searchUrl,
        int maxJobs,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var report = await experiment.RunAsync(
            searchUrl,
            maxJobs,
            settings.BrowserBinaryPath,
            settings.Headless,
            cancellationToken);
        var limitations = report.Limitations
            .Concat(report.Errors.Select(error => $"Error: {error}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var jobs = report.Jobs.Select(job => new LinkedInScrapedVacancy(
            job.Title,
            job.Company,
            job.Location,
            job.Url,
            job.PublishedAt,
            job.Description)).ToArray();

        return new LinkedInScrapeResult(
            jobs,
            report.Status is not (DiagnosticStatus.Success or DiagnosticStatus.NoVacancies),
            report.Restriction.IsBlocked,
            report.Restriction.LoginRequired,
            report.Restriction.Reason,
            limitations);
    }
}

internal sealed record LinkedInScrapeResult(
    IReadOnlyList<LinkedInScrapedVacancy> Vacancies,
    bool IsPartial,
    bool IsBlocked,
    bool LoginRequired,
    string? RestrictionReason,
    IReadOnlyList<string> Limitations);

internal sealed record LinkedInScrapedVacancy(
    string? Title,
    string? Company,
    string? Location,
    Uri? Url,
    DateTimeOffset? PublishedAt,
    string? Description);
