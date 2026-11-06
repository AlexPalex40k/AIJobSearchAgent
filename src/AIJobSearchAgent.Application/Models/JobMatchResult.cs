namespace AIJobSearchAgent.Application.Models;

public sealed record JobMatchResult(
    int Rating,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingOptionalSkills,
    string Explanation,
    string ModelName,
    string PromptVersion,
    DateTimeOffset EvaluatedAt);
