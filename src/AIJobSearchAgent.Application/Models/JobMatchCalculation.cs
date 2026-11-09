namespace AIJobSearchAgent.Application.Models;

public sealed record JobMatchCalculation(
    int Rating,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingOptionalSkills,
    string Explanation);
