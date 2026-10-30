using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Services;

public static class KeywordMatcher
{
    public static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords) =>
        keywords?
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

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
