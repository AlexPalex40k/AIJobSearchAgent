using System.Text.Json.Serialization;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Benchmarks;

public enum ReferenceLabelStatus
{
    Unlabeled,
    Provisional,
    Approved
}

public sealed class BenchmarkDataset
{
    public required string DatasetVersion { get; init; }
    public required IReadOnlyList<BenchmarkCase> Cases { get; init; }
}

public sealed class BenchmarkCase
{
    public required string Id { get; init; }
    public required string VacancyTitle { get; init; }
    public required string Company { get; init; }
    public required string Location { get; init; }
    public bool IsRemote { get; init; }
    public required string Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VacancyUrl { get; init; }
    public int? ExpectedRating { get; init; }
    public ReferenceLabelStatus LabelStatus { get; init; }
    public required string Rationale { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public IReadOnlyList<string> ExpectedMatchedSkills { get; init; } = [];
    public IReadOnlyList<string> ExpectedMissingSkills { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record BenchmarkDatasetSnapshot(BenchmarkDataset Dataset, string DatasetHash);

public sealed record RatioMetric(double? Value, int Numerator, int Denominator);

public sealed record AverageMetric(double? Value, int EvaluationCount);

public sealed record LatencyMetrics(
    double? MeanMilliseconds,
    double? MedianMilliseconds,
    double? P95Milliseconds,
    int EvaluationCount);

public sealed record RatingDistributionEntry(int Rating, int Count);

public sealed class BenchmarkAggregateMetrics
{
    public required int TotalCases { get; init; }
    public required int SuccessfulEvaluations { get; init; }
    public required int FailedEvaluations { get; init; }
    public required int TimedOutEvaluations { get; init; }
    public required int ApprovedLabelCount { get; init; }
    public required int ProvisionalLabelCount { get; init; }
    public required int UnlabeledCount { get; init; }
    public required RatioMetric FailureRate { get; init; }
    public required RatioMetric? ExactMatchAccuracy { get; init; }
    public required RatioMetric? WithinOnePointAccuracy { get; init; }
    public required AverageMetric? MeanAbsoluteError { get; init; }
    public required IReadOnlyList<RatingDistributionEntry> RatingDistribution { get; init; }
    public required LatencyMetrics Latency { get; init; }
}

public sealed class BenchmarkCaseResult
{
    public required string CaseId { get; init; }
    public string VacancyTitle { get; init; } = string.Empty;
    public string VacancyDescription { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VacancyUrl { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required ReferenceLabelStatus LabelStatus { get; init; }
    public int? ExpectedRating { get; init; }
    public int? PredictedRating { get; init; }
    public required EvaluationStatus EvaluationStatus { get; init; }
    public required string ModelName { get; init; }
    public required string PromptVersion { get; init; }
    public string? Explanation { get; init; }
    public required IReadOnlyList<string> MatchedSkills { get; init; }
    public required IReadOnlyList<string> MissingRequiredSkills { get; init; }
    public required IReadOnlyList<string> MissingOptionalSkills { get; init; }
    public required double LatencyMilliseconds { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class BenchmarkRunResult
{
    public const string CurrentSchemaVersion = "1.0";

    public required string SchemaVersion { get; init; }
    public required Guid RunId { get; init; }
    public required string DatasetVersion { get; init; }
    public required string DatasetHash { get; init; }
    public required string BenchmarkProfileHash { get; init; }
    public required string ModelName { get; init; }
    public required string PromptVersion { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public string? CaseFilter { get; init; }
    public required BenchmarkAggregateMetrics AggregateMetrics { get; init; }
    public required IReadOnlyList<BenchmarkCaseResult> CaseResults { get; init; }
}

public sealed record ComparisonRunSummary(
    Guid RunId,
    string ModelName,
    string PromptVersion,
    BenchmarkAggregateMetrics AggregateMetrics);

public sealed record ComparisonDelta(
    Guid BaselineRunId,
    Guid ComparedRunId,
    double? MeanAbsoluteErrorDifference,
    double? ExactMatchAccuracyDifference,
    double? WithinOnePointAccuracyDifference,
    double? FailureRateDifference,
    double? MeanLatencyMillisecondsDifference);

public sealed record CasePrediction(Guid RunId, string ModelName, int Rating);

public sealed record PredictionDisagreement(
    string CaseId,
    IReadOnlyList<CasePrediction> Predictions);

public sealed class BenchmarkComparisonReport
{
    public required string DatasetVersion { get; init; }
    public required string DatasetHash { get; init; }
    public required string BenchmarkProfileHash { get; init; }
    public required bool PromptVersionsDiffer { get; init; }
    public required IReadOnlyList<string> PromptVersions { get; init; }
    public required IReadOnlyList<ComparisonRunSummary> Runs { get; init; }
    public required IReadOnlyList<ComparisonDelta> DeltasFromFirstRun { get; init; }
    public required IReadOnlyList<PredictionDisagreement> PredictionDisagreements { get; init; }
}
