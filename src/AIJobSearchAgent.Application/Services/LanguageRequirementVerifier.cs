using System.Text.RegularExpressions;
using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Services;

public static class LanguageRequirementVerifier
{
    private const string RequirementMarkerPattern =
        "require|requires|required|requirement|requirements|mandatory|must|need|needs|needed|essential|prerequisite";
    private const string AllowedQualifierPattern =
        "a|an|at|least|minimum|professional|working|fluent|fluency|native|spoken|written|business|proficiency|knowledge|command|good|excellent|strong|level|language|skills|in|of|to|speak|have|is|are|explicitly|cefr|A1|A2|B1|B2|C1|C2";

    public static IReadOnlyList<RequiredLanguage> Verify(
        IEnumerable<RequiredLanguage> extractedLanguages,
        string vacancyDescription)
    {
        ArgumentNullException.ThrowIfNull(extractedLanguages);
        ArgumentNullException.ThrowIfNull(vacancyDescription);

        var verified = new List<RequiredLanguage>();
        var seenLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractedLanguage in extractedLanguages)
        {
            var language = extractedLanguage.Language.Trim();
            if (!seenLanguages.Add(language) ||
                !TryFindMandatoryLanguageSegment(language, vacancyDescription, out var requirementSegment))
            {
                continue;
            }

            var verifiedLevel = IsLevelSupported(
                language,
                extractedLanguage.MinimumLevel,
                requirementSegment)
                ? extractedLanguage.MinimumLevel
                : null;
            verified.Add(new RequiredLanguage(language, verifiedLevel));
        }

        return verified;
    }

    private static bool TryFindMandatoryLanguageSegment(
        string language,
        string description,
        out string requirementSegment)
    {
        var languagePattern = CreateTokenPattern(language);
        var markerBeforeLanguage =
            $@"\b(?:{RequirementMarkerPattern})\b(?:\s+|\s*[:,-]\s*)(?:(?:{AllowedQualifierPattern})\b(?:\s+|\s*[:,-]\s*)){{0,8}}{languagePattern}";
        var languageBeforeMarker =
            $@"{languagePattern}(?!-speaking\b)(?:(?:\s+|\s*[:,-]\s*)(?:{AllowedQualifierPattern})\b){{0,8}}(?:\s+|\s*[:,-]\s*)\b(?:{RequirementMarkerPattern})\b";

        foreach (var segment in SplitIntoSegments(description))
        {
            if (Regex.IsMatch(segment, markerBeforeLanguage, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(segment, languageBeforeMarker, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                requirementSegment = segment;
                return true;
            }
        }

        requirementSegment = string.Empty;
        return false;
    }

    private static bool IsLevelSupported(
        string language,
        string? minimumLevel,
        string requirementSegment)
    {
        if (string.IsNullOrWhiteSpace(minimumLevel))
        {
            return true;
        }

        var languagePattern = CreateTokenPattern(language);
        if (string.Equals(minimumLevel, "native", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(
                requirementSegment,
                $@"(?:\bnative\b(?:\s+\w+){{0,2}}\s+{languagePattern}|{languagePattern}(?:\s+\w+){{0,2}}\s+\bnative\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        var levelPattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(minimumLevel)}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(requirementSegment, levelPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(requirementSegment, languagePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string CreateTokenPattern(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"\s+", tokens.Select(Regex.Escape))}(?![\p{{L}}\p{{N}}])";
    }

    private static IEnumerable<string> SplitIntoSegments(string description) =>
        Regex.Split(description, @"[\r\n.!?;]+")
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0);
}
