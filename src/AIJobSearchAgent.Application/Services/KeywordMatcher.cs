using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Services;

public static class KeywordMatcher
{
    public const int MaximumSearchQueryLength = 100;
    public const int MaximumSearchTokenCount = 10;

    public static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords) =>
        keywords?
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    public static IReadOnlyList<string> TokenizeSearchQuery(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Phase 1 uses OR semantics: any keyword in either title or description is a match.
    public static bool Matches(JobVacancy vacancy, IReadOnlyCollection<string> keywords)
    {
        if (keywords.Count == 0)
        {
            return true;
        }

        return keywords.Any(keyword =>
            vacancy.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            vacancy.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
