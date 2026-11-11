using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.LinkedInSpike;
using Microsoft.Extensions.Logging;

namespace AIJobSearchAgent.Infrastructure.Providers;

public sealed class LinkedInJobProvider : ILinkedInJobProvider
{
    private readonly ILinkedInSearchScraper scraper;
    private readonly ILogger<LinkedInJobProvider> logger;

    internal LinkedInJobProvider(
        ILinkedInSearchScraper scraper,
        ILogger<LinkedInJobProvider> logger)
    {
        this.scraper = scraper;
        this.logger = logger;
    }

    public string SourceName => "LinkedIn";

    public async Task<IReadOnlyList<JobVacancy>> SearchAsync(
        JobSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var searchPhrase = string.Join(
            ' ',
            criteria.Keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)).Select(keyword => keyword.Trim()));
        var linkedInCriteria = new LinkedInJobSearchCriteria(
            searchPhrase,
            criteria.Location ?? string.Empty,
            criteria.PublishedWithinDays,
            criteria.MaxJobs,
            criteria.GeoId);
        var result = await SearchWithDiagnosticsAsync(linkedInCriteria, cancellationToken);
        return result.Vacancies;
    }

    public async Task<LinkedInJobSearchResult> SearchWithDiagnosticsAsync(
        LinkedInJobSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var searchUrl = LinkedInSearchUrlBuilder.Build(criteria);
        var scraped = await scraper.SearchAsync(searchUrl, criteria.MaxJobs, cancellationToken);
        var limitations = scraped.Limitations.ToList();
        var vacancies = new List<JobVacancy>();
        var externalIds = new HashSet<string>(StringComparer.Ordinal);
        var createdAt = DateTimeOffset.UtcNow;
        var omittedVacancy = false;

        foreach (var scrapedVacancy in scraped.Vacancies)
        {
            var vacancy = Map(scrapedVacancy, createdAt);
            if (vacancy is null)
            {
                omittedVacancy = true;
                limitations.Add("A discovered LinkedIn vacancy had no stable job ID and was omitted.");
                continue;
            }

            if (externalIds.Add(vacancy.ExternalId))
            {
                vacancies.Add(vacancy);
            }
        }

        var distinctLimitations = limitations.Distinct(StringComparer.Ordinal).ToArray();
        var isPartial = scraped.IsPartial ||
                        omittedVacancy ||
                        vacancies.Any(vacancy => string.IsNullOrWhiteSpace(vacancy.Description));
        logger.LogInformation(
            "LinkedIn returned {VacancyCount} vacancies; partial: {IsPartial}; blocked: {IsBlocked}",
            vacancies.Count,
            isPartial,
            scraped.IsBlocked);

        return new LinkedInJobSearchResult(
            vacancies,
            isPartial,
            scraped.IsBlocked,
            scraped.LoginRequired,
            scraped.RestrictionReason,
            distinctLimitations);
    }

    internal static JobVacancy? Map(LinkedInScrapedVacancy scraped, DateTimeOffset createdAt)
    {
        if (scraped.Url is null)
        {
            return null;
        }

        var externalId = LinkedInHtmlParser.TryGetLinkedInJobId(scraped.Url);
        if (externalId is null)
        {
            return null;
        }

        return new JobVacancy
        {
            ExternalId = externalId,
            Source = "LinkedIn",
            Title = scraped.Title?.Trim() ?? string.Empty,
            Company = scraped.Company?.Trim() ?? string.Empty,
            Location = scraped.Location?.Trim() ?? string.Empty,
            IsRemote = false,
            Description = scraped.Description?.Trim() ?? string.Empty,
            Url = $"https://www.linkedin.com/jobs/view/{externalId}",
            PublishedAt = scraped.PublishedAt,
            CreatedAt = createdAt
        };
    }
}
