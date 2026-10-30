using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class ImportJobsUseCase(
    IJobProvider jobProvider,
    IJobVacancyRepository repository,
    ILogger<ImportJobsUseCase> logger)
{
    public async Task<ImportSummary> ExecuteAsync(
        JobSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(criteria.MaxPages, 1);

        var keywords = KeywordMatcher.NormalizeKeywords(criteria.Keywords);
        var normalizedCriteria = criteria with { Keywords = keywords };

        logger.LogInformation(
            "Starting job import from {Provider} with {KeywordCount} keywords and {MaxPages} pages",
            jobProvider.SourceName,
            keywords.Count,
            criteria.MaxPages);

        var fetched = await jobProvider.SearchAsync(normalizedCriteria, cancellationToken);
        var validMatches = new List<JobVacancy>(fetched.Count);
        var rejectedInvalid = 0;

        foreach (var vacancy in fetched)
        {
            if (!JobVacancyNormalizer.TryNormalize(vacancy))
            {
                rejectedInvalid++;
                continue;
            }

            if (KeywordMatcher.Matches(vacancy, keywords))
            {
                validMatches.Add(vacancy);
            }
        }

        var saveResult = await repository.AddNewAsync(validMatches, cancellationToken);
        var summary = new ImportSummary(
            fetched.Count,
            validMatches.Count,
            saveResult.Inserted,
            saveResult.SkippedDuplicates,
            rejectedInvalid);

        logger.LogInformation(
            "Completed job import from {Provider}: fetched {Fetched}, matched {Matched}, inserted {Inserted}, duplicates {Duplicates}, invalid {Invalid}",
            jobProvider.SourceName,
            summary.TotalFetched,
            summary.TotalMatched,
            summary.Inserted,
            summary.SkippedDuplicates,
            summary.RejectedInvalid);

        return summary;
    }
}
