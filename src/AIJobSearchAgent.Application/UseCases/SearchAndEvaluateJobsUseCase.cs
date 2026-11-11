using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class SearchAndEvaluateJobsUseCase(
    ILinkedInJobProvider linkedInJobProvider,
    IJobMatchingService jobMatchingService)
{
    public async Task<SearchAndEvaluateJobsResult> ExecuteAsync(
        LinkedInJobSearchCriteria criteria,
        CandidateProfile candidateProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(candidateProfile);

        var searchResult = await linkedInJobProvider.SearchWithDiagnosticsAsync(
            criteria,
            cancellationToken);

        var uniqueVacancies = new List<JobVacancy>();
        var identities = new HashSet<(string Source, string ExternalId)>();
        var invalidVacancies = 0;

        foreach (var vacancy in searchResult.Vacancies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (vacancy is null || !JobVacancyNormalizer.TryNormalize(vacancy))
            {
                invalidVacancies++;
                continue;
            }

            if (identities.Add((vacancy.Source, vacancy.ExternalId)))
            {
                uniqueVacancies.Add(vacancy);
            }
        }

        var evaluatedResults = new List<SearchAndEvaluateJobResult>(uniqueVacancies.Count);
        foreach (var vacancy in uniqueVacancies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(vacancy.Description))
            {
                evaluatedResults.Add(CreateUnevaluatedResult(
                    vacancy,
                    SearchJobEvaluationStatus.NotEvaluated));
                continue;
            }

            try
            {
                var evaluation = await jobMatchingService.EvaluateAsync(
                    vacancy,
                    candidateProfile,
                    cancellationToken);

                evaluatedResults.Add(new SearchAndEvaluateJobResult(
                    vacancy.Source,
                    vacancy.ExternalId,
                    vacancy.Title,
                    vacancy.Company,
                    vacancy.Location,
                    vacancy.Url,
                    vacancy.PublishedAt,
                    evaluation.Rating,
                    SearchJobEvaluationStatus.Succeeded,
                    evaluation.Explanation,
                    evaluation.MatchedSkills,
                    evaluation.MissingRequiredSkills,
                    evaluation.MissingOptionalSkills));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JobMatchingException exception)
            {
                var status = exception.EvaluationStatus == EvaluationStatus.TimedOut
                    ? SearchJobEvaluationStatus.TimedOut
                    : SearchJobEvaluationStatus.Failed;
                evaluatedResults.Add(CreateUnevaluatedResult(vacancy, status));
            }
            catch (OperationCanceledException)
            {
                evaluatedResults.Add(CreateUnevaluatedResult(
                    vacancy,
                    SearchJobEvaluationStatus.TimedOut));
            }
            catch (Exception)
            {
                evaluatedResults.Add(CreateUnevaluatedResult(
                    vacancy,
                    SearchJobEvaluationStatus.Failed));
            }
        }

        var rankedResults = evaluatedResults
            .OrderBy(result => result.Rating.HasValue ? 0 : 1)
            .ThenByDescending(result => result.Rating)
            .ThenByDescending(result => result.PublishedAt)
            .ThenBy(result => result.Source, StringComparer.Ordinal)
            .ThenBy(result => result.ExternalId, StringComparer.Ordinal)
            .ToArray();

        var successfulCount = rankedResults.Count(
            result => result.EvaluationStatus == SearchJobEvaluationStatus.Succeeded);
        var failedCount = rankedResults.Count(result =>
            result.EvaluationStatus is SearchJobEvaluationStatus.Failed or
                SearchJobEvaluationStatus.TimedOut);
        var notEvaluatedCount = rankedResults.Count(
            result => result.EvaluationStatus == SearchJobEvaluationStatus.NotEvaluated);

        var sourceLimitations = searchResult.Limitations
            .Append(searchResult.RestrictionReason)
            .Where(limitation => !string.IsNullOrWhiteSpace(limitation))
            .Select(limitation => limitation!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (invalidVacancies > 0)
        {
            sourceLimitations.Add(
                $"{invalidVacancies} invalid LinkedIn vacancy result(s) were omitted.");
        }

        return new SearchAndEvaluateJobsResult(
            rankedResults.Length,
            successfulCount,
            failedCount,
            notEvaluatedCount,
            searchResult.IsPartial || searchResult.IsBlocked || invalidVacancies > 0 ||
                failedCount > 0 || notEvaluatedCount > 0,
            searchResult.IsBlocked,
            sourceLimitations,
            rankedResults);
    }

    private static SearchAndEvaluateJobResult CreateUnevaluatedResult(
        JobVacancy vacancy,
        SearchJobEvaluationStatus status) => new(
            vacancy.Source,
            vacancy.ExternalId,
            vacancy.Title,
            vacancy.Company,
            vacancy.Location,
            vacancy.Url,
            vacancy.PublishedAt,
            null,
            status,
            null,
            [],
            [],
            []);
}
