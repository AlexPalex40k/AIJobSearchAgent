using System.Diagnostics;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Benchmarks;

public sealed class BenchmarkRunner(IJobMatchingService matchingService)
{
    public async Task<BenchmarkRunResult> RunAsync(
        BenchmarkDatasetSnapshot datasetSnapshot,
        CandidateProfileSnapshot profileSnapshot,
        string configuredModel,
        string? caseFilter,
        CancellationToken cancellationToken)
    {
        var selectedCases = SelectCases(datasetSnapshot.Dataset.Cases, caseFilter);
        var results = new List<BenchmarkCaseResult>(selectedCases.Count);

        foreach (var benchmarkCase in selectedCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vacancy = new JobVacancy
            {
                ExternalId = benchmarkCase.Id,
                Source = "SyntheticBenchmark",
                Title = benchmarkCase.VacancyTitle,
                Company = benchmarkCase.Company,
                Location = benchmarkCase.Location,
                IsRemote = benchmarkCase.IsRemote,
                Description = benchmarkCase.Description,
                Url = benchmarkCase.VacancyUrl ?? string.Empty
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                JobMatchResult match;
                JobMatchingDiagnostics? diagnostics = null;
                if (matchingService is IJobMatchingDiagnosticsService diagnosticsService)
                {
                    var diagnosticResult = await diagnosticsService.EvaluateWithDiagnosticsAsync(
                        vacancy,
                        profileSnapshot.Profile,
                        cancellationToken);
                    match = diagnosticResult.Result;
                    diagnostics = diagnosticResult.Diagnostics;
                }
                else
                {
                    match = await matchingService.EvaluateAsync(
                        vacancy,
                        profileSnapshot.Profile,
                        cancellationToken);
                }

                stopwatch.Stop();
                if (match.Rating is < 1 or > 5)
                {
                    throw new InvalidOperationException("The matching service returned an out-of-range rating.");
                }

                results.Add(CreateSuccessfulResult(
                    benchmarkCase,
                    match,
                    diagnostics,
                    stopwatch.Elapsed.TotalMilliseconds));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JobMatchingException exception)
            {
                stopwatch.Stop();
                results.Add(CreateFailureResult(
                    benchmarkCase,
                    exception.EvaluationStatus,
                    exception.ModelName,
                    exception.PromptVersion,
                    stopwatch.Elapsed.TotalMilliseconds,
                    exception.Message));
            }
            catch (Exception)
            {
                stopwatch.Stop();
                results.Add(CreateFailureResult(
                    benchmarkCase,
                    EvaluationStatus.Failed,
                    configuredModel,
                    JobMatchingPrompt.Version,
                    stopwatch.Elapsed.TotalMilliseconds,
                    "Unexpected evaluation failure."));
            }
        }

        return new BenchmarkRunResult
        {
            SchemaVersion = BenchmarkRunResult.CurrentSchemaVersion,
            RunId = Guid.NewGuid(),
            DatasetVersion = datasetSnapshot.Dataset.DatasetVersion,
            DatasetHash = datasetSnapshot.DatasetHash,
            BenchmarkProfileHash = profileSnapshot.ProfileHash,
            ModelName = configuredModel,
            PromptVersion = JobMatchingPrompt.Version,
            ReferenceLabelsCompatible = string.Equals(
                datasetSnapshot.Dataset.MatchingVersion,
                JobMatchingPrompt.Version,
                StringComparison.Ordinal),
            ExecutedAt = DateTimeOffset.UtcNow,
            CaseFilter = caseFilter,
            AggregateMetrics = BenchmarkMetricsCalculator.Calculate(
                results,
                string.Equals(
                    datasetSnapshot.Dataset.MatchingVersion,
                    JobMatchingPrompt.Version,
                    StringComparison.Ordinal)),
            CaseResults = results
        };
    }

    private static IReadOnlyList<BenchmarkCase> SelectCases(
        IReadOnlyList<BenchmarkCase> cases,
        string? caseFilter)
    {
        if (string.IsNullOrWhiteSpace(caseFilter))
        {
            return cases;
        }

        var selected = cases
            .Where(benchmarkCase => string.Equals(
                benchmarkCase.Id,
                caseFilter.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selected.Length == 0)
        {
            throw new BenchmarkValidationException($"Case filter '{caseFilter}' did not match a benchmark case ID.");
        }

        return selected;
    }

    private static BenchmarkCaseResult CreateSuccessfulResult(
        BenchmarkCase benchmarkCase,
        JobMatchResult match,
        JobMatchingDiagnostics? diagnostics,
        double latencyMilliseconds) => new()
        {
            CaseId = benchmarkCase.Id,
            VacancyTitle = benchmarkCase.VacancyTitle,
            VacancyDescription = benchmarkCase.Description,
            VacancyUrl = benchmarkCase.VacancyUrl,
            Tags = benchmarkCase.Tags,
            LabelStatus = benchmarkCase.LabelStatus,
            ExpectedRating = benchmarkCase.ExpectedRating,
            PredictedRating = match.Rating,
            EvaluationStatus = EvaluationStatus.Succeeded,
            ModelName = match.ModelName,
            PromptVersion = match.PromptVersion,
            Explanation = match.Explanation,
            MatchedSkills = match.MatchedSkills,
            MissingRequiredSkills = match.MissingRequiredSkills,
            MissingOptionalSkills = match.MissingOptionalSkills,
            MatchingDiagnostics = diagnostics,
            LatencyMilliseconds = latencyMilliseconds
        };

    private static BenchmarkCaseResult CreateFailureResult(
        BenchmarkCase benchmarkCase,
        EvaluationStatus status,
        string modelName,
        string promptVersion,
        double latencyMilliseconds,
        string errorMessage) => new()
        {
            CaseId = benchmarkCase.Id,
            VacancyTitle = benchmarkCase.VacancyTitle,
            VacancyDescription = benchmarkCase.Description,
            VacancyUrl = benchmarkCase.VacancyUrl,
            Tags = benchmarkCase.Tags,
            LabelStatus = benchmarkCase.LabelStatus,
            ExpectedRating = benchmarkCase.ExpectedRating,
            EvaluationStatus = status,
            ModelName = modelName,
            PromptVersion = promptVersion,
            MatchedSkills = [],
            MissingRequiredSkills = [],
            MissingOptionalSkills = [],
            LatencyMilliseconds = latencyMilliseconds,
            ErrorMessage = errorMessage
        };
}
