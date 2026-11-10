using System.Text.RegularExpressions;
using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Services;

public static class TechnicalRequirementVerifier
{
    private static readonly HashSet<string> GenericWorkPractices = new(StringComparer.OrdinalIgnoreCase)
    {
        "code review",
        "code reviews",
        "communication",
        "leadership",
        "mentoring",
        "problem solving",
        "teamwork"
    };

    public static TechnicalRequirementVerification Verify(
        ExtractedJobRequirements extractedRequirements,
        string vacancyDescription)
    {
        ArgumentNullException.ThrowIfNull(extractedRequirements);
        ArgumentNullException.ThrowIfNull(vacancyDescription);

        var candidates = CreateCandidates(extractedRequirements);
        var verifiedRequired = new List<string>();
        var verifiedOptional = new List<string>();
        var unverifiedRequired = new List<string>();
        var unverifiedOptional = new List<string>();

        foreach (var candidate in candidates)
        {
            var explicitClassification = GetExplicitClassification(candidate.Name, vacancyDescription);
            if (IsClearlyNonTechnical(candidate.Name) ||
                !TechnicalSkillMatcher.IsMentioned(candidate.Name, vacancyDescription) ||
                explicitClassification == ExplicitClassification.Excluded)
            {
                AddByClassification(
                    candidate.OriginalClassification,
                    candidate.Name,
                    unverifiedRequired,
                    unverifiedOptional);
                continue;
            }

            var finalClassification = explicitClassification switch
            {
                ExplicitClassification.Required => SkillClassification.Required,
                ExplicitClassification.Optional => SkillClassification.Optional,
                _ => candidate.OriginalClassification
            };
            AddByClassification(
                finalClassification,
                candidate.Name,
                verifiedRequired,
                verifiedOptional);
        }

        var verifiedRequirements = extractedRequirements with
        {
            RequiredSkills = verifiedRequired,
            CoreRequiredSkills = VerifyCoreSkills(
                extractedRequirements.CoreRequiredSkills,
                verifiedRequired),
            OptionalSkills = verifiedOptional,
            HasClearRequirements = extractedRequirements.HasClearRequirements && verifiedRequired.Count > 0
        };

        return new TechnicalRequirementVerification(
            verifiedRequirements,
            unverifiedRequired,
            unverifiedOptional);
    }

    public static string? CreateTransparencyExplanation(TechnicalRequirementVerification verification)
    {
        var classifications = new List<string>();
        if (verification.UnverifiedRequiredSkills.Count > 0)
        {
            classifications.Add($"required: {string.Join(", ", verification.UnverifiedRequiredSkills)}");
        }

        if (verification.UnverifiedOptionalSkills.Count > 0)
        {
            classifications.Add($"optional: {string.Join(", ", verification.UnverifiedOptionalSkills)}");
        }

        return classifications.Count == 0
            ? null
            : $"Ignored extracted technical skills not supported as requirements by the vacancy description ({string.Join("; ", classifications)}).";
    }

    private static IReadOnlyList<string> VerifyCoreSkills(
        IEnumerable<string> extractedCoreSkills,
        IEnumerable<string> verifiedRequiredSkills)
    {
        var requiredByKey = verifiedRequiredSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .GroupBy(TechnicalSkillMatcher.Normalize, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var verifiedCore = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var coreSkill in extractedCoreSkills.Where(skill => !string.IsNullOrWhiteSpace(skill)))
        {
            var key = TechnicalSkillMatcher.Normalize(coreSkill);
            if (requiredByKey.TryGetValue(key, out var requiredSkill) && seen.Add(key))
            {
                verifiedCore.Add(requiredSkill);
            }
        }

        return verifiedCore;
    }

    private static IReadOnlyList<SkillCandidate> CreateCandidates(ExtractedJobRequirements requirements)
    {
        var candidates = new List<SkillCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidates(requirements.RequiredSkills, SkillClassification.Required);
        AddCandidates(requirements.OptionalSkills, SkillClassification.Optional);
        return candidates;

        void AddCandidates(IEnumerable<string> skills, SkillClassification classification)
        {
            foreach (var skill in skills)
            {
                if (seen.Add(TechnicalSkillMatcher.Normalize(skill)))
                {
                    candidates.Add(new SkillCandidate(skill.Trim(), classification));
                }
            }
        }
    }

    private static bool IsClearlyNonTechnical(string skill)
    {
        var normalizedSkill = skill.Trim();
        return GenericWorkPractices.Contains(normalizedSkill) ||
            Regex.IsMatch(
                normalizedSkill,
                @"\b(?:years?|months?)\b.*\bexperience\b|\bexperience\b.*\b(?:years?|months?)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static ExplicitClassification GetExplicitClassification(
        string skill,
        string vacancyDescription)
    {
        var patterns = TechnicalSkillMatcher.GetMentionPatterns(skill);
        var isExcluded = patterns.Any(pattern => MatchesAny(vacancyDescription,
            $@"{pattern}\s+(?:experience\s+)?(?:is\s+|are\s+)?(?:not\s+relevant|irrelevant|not\s+required|not\s+used|outside\s+(?:the|this)\s+role)",
            $@"\b(?:no|without)\s+(?:need\s+for\s+)?{pattern}"));
        if (isExcluded)
        {
            return ExplicitClassification.Excluded;
        }

        var isRequired = patterns.Any(pattern => MatchesAny(vacancyDescription,
            $@"{pattern}\s+(?:experience\s+|knowledge\s+)?(?:is\s+|are\s+)?(?:required|mandatory|essential)\b",
            $@"\b(?:required|mandatory|essential)\b\s+(?:experience\s+(?:with|in)\s+|knowledge\s+of\s+)?{pattern}",
            $@"\bmust\b\s+(?:have|know|use|work\s+with)\s+{pattern}"));
        var isOptional = patterns.Any(pattern => MatchesAny(vacancyDescription,
            $@"{pattern}\s+(?:experience\s+|knowledge\s+)?(?:is\s+|are\s+)?(?:optional|preferred|useful|a\s+bonus|nice-to-have|nice\s+to\s+have)\b",
            $@"\b(?:optional|preferred|useful|bonus|nice-to-have|nice\s+to\s+have)\b\s+(?:experience\s+(?:with|in)\s+|knowledge\s+of\s+)?{pattern}"));

        return (isRequired, isOptional) switch
        {
            (true, false) => ExplicitClassification.Required,
            (false, true) => ExplicitClassification.Optional,
            _ => ExplicitClassification.None
        };
    }

    private static bool MatchesAny(string text, params string[] patterns) => patterns.Any(pattern => Regex.IsMatch(
        text,
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)));

    private static void AddByClassification(
        SkillClassification classification,
        string skill,
        ICollection<string> required,
        ICollection<string> optional)
    {
        (classification == SkillClassification.Required ? required : optional).Add(skill);
    }

    private enum SkillClassification
    {
        Required,
        Optional
    }

    private enum ExplicitClassification
    {
        None,
        Required,
        Optional,
        Excluded
    }

    private sealed record SkillCandidate(string Name, SkillClassification OriginalClassification);
}
