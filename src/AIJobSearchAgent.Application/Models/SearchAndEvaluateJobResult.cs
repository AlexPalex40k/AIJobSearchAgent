namespace AIJobSearchAgent.Application.Models;

public sealed record SearchAndEvaluateJobResult(
    string Source,
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string OriginalUrl,
    DateTimeOffset? PublishedAt,
    int? Rating,
    SearchJobEvaluationStatus EvaluationStatus,
    string? Explanation,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingOptionalSkills);
