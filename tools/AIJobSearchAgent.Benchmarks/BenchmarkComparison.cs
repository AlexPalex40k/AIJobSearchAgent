using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Benchmarks;

public static class BenchmarkComparison
{
    public static BenchmarkComparisonReport Compare(IReadOnlyList<BenchmarkRunResult> runs)
    {
        if (runs.Count < 2)
        {
            throw new BenchmarkValidationException("At least two benchmark results are required for comparison.");
        }

        var baseline = runs[0];
        var baselineCaseIds = baseline.CaseResults
            .Select(result => result.CaseId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var run in runs.Skip(1))
        {
            if (!string.Equals(run.DatasetVersion, baseline.DatasetVersion, StringComparison.Ordinal) ||
                !string.Equals(run.DatasetHash, baseline.DatasetHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new BenchmarkValidationException("Benchmark results use different datasets and cannot be compared directly.");
            }

            if (!string.Equals(
                    run.BenchmarkProfileHash,
                    baseline.BenchmarkProfileHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BenchmarkValidationException("Benchmark results use different candidate profiles and cannot be compared directly.");
            }

            var comparedCaseIds = run.CaseResults
                .Select(result => result.CaseId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!baselineCaseIds.SetEquals(comparedCaseIds))
            {
                throw new BenchmarkValidationException("Benchmark results evaluated different case sets and cannot be compared directly.");
            }
        }

        var promptVersions = runs
            .Select(run => run.PromptVersion)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();

        return new BenchmarkComparisonReport
        {
            DatasetVersion = baseline.DatasetVersion,
            DatasetHash = baseline.DatasetHash,
            BenchmarkProfileHash = baseline.BenchmarkProfileHash,
            PromptVersionsDiffer = promptVersions.Length > 1,
            PromptVersions = promptVersions,
            Runs = runs.Select(run => new ComparisonRunSummary(
                run.RunId,
                run.ModelName,
                run.PromptVersion,
                run.AggregateMetrics)).ToArray(),
            DeltasFromFirstRun = runs.Skip(1)
                .Select(run => CreateDelta(baseline, run))
                .ToArray(),
            PredictionDisagreements = FindDisagreements(runs)
        };
    }

    private static ComparisonDelta CreateDelta(BenchmarkRunResult baseline, BenchmarkRunResult compared) => new(
        baseline.RunId,
        compared.RunId,
        Difference(
            compared.AggregateMetrics.MeanAbsoluteError?.Value,
            baseline.AggregateMetrics.MeanAbsoluteError?.Value),
        Difference(
            compared.AggregateMetrics.ExactMatchAccuracy?.Value,
            baseline.AggregateMetrics.ExactMatchAccuracy?.Value),
        Difference(
            compared.AggregateMetrics.WithinOnePointAccuracy?.Value,
            baseline.AggregateMetrics.WithinOnePointAccuracy?.Value),
        Difference(compared.AggregateMetrics.FailureRate.Value, baseline.AggregateMetrics.FailureRate.Value),
        Difference(
            compared.AggregateMetrics.Latency.MeanMilliseconds,
            baseline.AggregateMetrics.Latency.MeanMilliseconds));

    private static double? Difference(double? value, double? baseline) =>
        value.HasValue && baseline.HasValue ? value.Value - baseline.Value : null;

    private static IReadOnlyList<PredictionDisagreement> FindDisagreements(
        IReadOnlyList<BenchmarkRunResult> runs) => runs
        .SelectMany(run => run.CaseResults
            .Where(result =>
                result.EvaluationStatus == EvaluationStatus.Succeeded &&
                result.PredictedRating.HasValue)
            .Select(result => new
            {
                result.CaseId,
                Prediction = new CasePrediction(run.RunId, run.ModelName, result.PredictedRating!.Value)
            }))
        .GroupBy(item => item.CaseId, StringComparer.OrdinalIgnoreCase)
        .Where(group =>
            group.Select(item => item.Prediction.RunId).Distinct().Count() > 1 &&
            group.Select(item => item.Prediction.Rating).Distinct().Count() > 1)
        .Select(group => new PredictionDisagreement(
            group.First().CaseId,
            group.Select(item => item.Prediction).ToArray()))
        .OrderBy(disagreement => disagreement.CaseId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
