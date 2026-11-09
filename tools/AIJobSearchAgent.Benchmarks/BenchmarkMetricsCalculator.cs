using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Benchmarks;

public static class BenchmarkMetricsCalculator
{
    private const int MinimumP95SampleSize = 20;

    public static BenchmarkAggregateMetrics Calculate(
        IReadOnlyList<BenchmarkCaseResult> results,
        bool includeApprovedLabels = true)
    {
        var successful = results
            .Where(result => result.EvaluationStatus == EvaluationStatus.Succeeded && result.PredictedRating.HasValue)
            .ToArray();
        var scoredApproved = successful
            .Where(result =>
                includeApprovedLabels &&
                result.LabelStatus == ReferenceLabelStatus.Approved &&
                result.ExpectedRating.HasValue)
            .ToArray();
        var failedCount = results.Count(result => result.EvaluationStatus == EvaluationStatus.Failed);
        var timedOutCount = results.Count(result => result.EvaluationStatus == EvaluationStatus.TimedOut);
        var failureCount = failedCount + timedOutCount;

        RatioMetric? exactAccuracy = null;
        RatioMetric? withinOneAccuracy = null;
        AverageMetric? meanAbsoluteError = null;
        if (scoredApproved.Length > 0)
        {
            var exactCount = scoredApproved.Count(result => result.PredictedRating == result.ExpectedRating);
            var withinOneCount = scoredApproved.Count(result =>
                Math.Abs(result.PredictedRating!.Value - result.ExpectedRating!.Value) <= 1);
            var absoluteErrors = scoredApproved.Select(result =>
                Math.Abs(result.PredictedRating!.Value - result.ExpectedRating!.Value));

            exactAccuracy = new RatioMetric(
                exactCount / (double)scoredApproved.Length,
                exactCount,
                scoredApproved.Length);
            withinOneAccuracy = new RatioMetric(
                withinOneCount / (double)scoredApproved.Length,
                withinOneCount,
                scoredApproved.Length);
            meanAbsoluteError = new AverageMetric(absoluteErrors.Average(), scoredApproved.Length);
        }

        var latencies = results
            .Select(result => result.LatencyMilliseconds)
            .OrderBy(milliseconds => milliseconds)
            .ToArray();

        return new BenchmarkAggregateMetrics
        {
            TotalCases = results.Count,
            SuccessfulEvaluations = successful.Length,
            FailedEvaluations = failedCount,
            TimedOutEvaluations = timedOutCount,
            ApprovedLabelCount = results.Count(result => result.LabelStatus == ReferenceLabelStatus.Approved),
            ProvisionalLabelCount = results.Count(result => result.LabelStatus == ReferenceLabelStatus.Provisional),
            UnlabeledCount = results.Count(result => result.LabelStatus == ReferenceLabelStatus.Unlabeled),
            FailureRate = new RatioMetric(
                results.Count == 0 ? null : failureCount / (double)results.Count,
                failureCount,
                results.Count),
            ExactMatchAccuracy = exactAccuracy,
            WithinOnePointAccuracy = withinOneAccuracy,
            MeanAbsoluteError = meanAbsoluteError,
            RatingDistribution = Enumerable.Range(1, 5)
                .Select(rating => new RatingDistributionEntry(
                    rating,
                    successful.Count(result => result.PredictedRating == rating)))
                .ToArray(),
            Latency = CalculateLatency(latencies)
        };
    }

    private static LatencyMetrics CalculateLatency(IReadOnlyList<double> sortedLatencies)
    {
        if (sortedLatencies.Count == 0)
        {
            return new LatencyMetrics(null, null, null, 0);
        }

        var middle = sortedLatencies.Count / 2;
        var median = sortedLatencies.Count % 2 == 0
            ? (sortedLatencies[middle - 1] + sortedLatencies[middle]) / 2
            : sortedLatencies[middle];
        double? p95 = null;
        if (sortedLatencies.Count >= MinimumP95SampleSize)
        {
            var nearestRankIndex = (int)Math.Ceiling(sortedLatencies.Count * 0.95) - 1;
            p95 = sortedLatencies[nearestRankIndex];
        }

        return new LatencyMetrics(
            sortedLatencies.Average(),
            median,
            p95,
            sortedLatencies.Count);
    }
}
