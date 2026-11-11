using System.Text.RegularExpressions;
using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Services;

public static class TechnicalRequirementVerifier
{
    private static readonly HashSet<string> GenericWorkPractices = new(StringComparer.OrdinalIgnoreCase)
    {
        "agile",
        "clean code",
        "code quality",
        "code review",
        "code reviews",
        "communication",
        "leadership",
        "mentoring",
        "problem solving",
        "solid principles",
        "teamwork"
    };

    private static readonly HashSet<string> GenericSupportingCoreSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "docker",
        "git",
        "github",
        "sql"
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
        var reclassified = new List<TechnicalSkillClassificationChange>();

        foreach (var candidate in candidates)
        {
            if (IsClearlyNonTechnical(candidate.Name) ||
                !TechnicalSkillMatcher.IsMentioned(candidate.Name, vacancyDescription))
            {
                AddByClassification(
                    candidate.OriginalClassification,
                    candidate.Name,
                    unverifiedRequired,
                    unverifiedOptional);
                continue;
            }

            var evidence = GetEvidenceClassification(candidate.Name, vacancyDescription);
            if (evidence is EvidenceClassification.None or EvidenceClassification.Excluded)
            {
                AddByClassification(
                    candidate.OriginalClassification,
                    candidate.Name,
                    unverifiedRequired,
                    unverifiedOptional);
                continue;
            }

            var finalClassification = evidence == EvidenceClassification.Required
                ? SkillClassification.Required
                : SkillClassification.Optional;
            AddByClassification(
                finalClassification,
                candidate.Name,
                verifiedRequired,
                verifiedOptional);

            if (finalClassification != candidate.OriginalClassification)
            {
                reclassified.Add(new TechnicalSkillClassificationChange(
                    candidate.Name,
                    candidate.OriginalClassification.ToString(),
                    finalClassification.ToString()));
            }
        }

        var verifiedCore = VerifyCoreSkills(
            extractedRequirements.CoreRequiredSkills,
            verifiedRequired,
            vacancyDescription,
            out var rejectedCore);
        var verifiedRequirements = extractedRequirements with
        {
            RequiredSkills = verifiedRequired,
            CoreRequiredSkills = verifiedCore,
            OptionalSkills = verifiedOptional,
            HasClearRequirements = extractedRequirements.HasClearRequirements && verifiedRequired.Count > 0
        };

        return new TechnicalRequirementVerification(
            verifiedRequirements,
            unverifiedRequired,
            unverifiedOptional)
        {
            ReclassifiedSkills = reclassified,
            RejectedCoreSkills = rejectedCore
        };
    }

    public static string? CreateTransparencyExplanation(TechnicalRequirementVerification verification)
    {
        var parts = new List<string>();
        var rejected = new List<string>();
        if (verification.UnverifiedRequiredSkills.Count > 0)
        {
            rejected.Add($"required: {FormatList(verification.UnverifiedRequiredSkills)}");
        }

        if (verification.UnverifiedOptionalSkills.Count > 0)
        {
            rejected.Add($"optional: {FormatList(verification.UnverifiedOptionalSkills)}");
        }

        if (rejected.Count > 0)
        {
            parts.Add(
                $"Ignored extracted technical skills not supported as requirements by the vacancy description ({string.Join("; ", rejected)}).");
        }

        if (verification.ReclassifiedSkills.Count > 0)
        {
            parts.Add(
                $"Reclassified explicit requirement evidence: {FormatList(verification.ReclassifiedSkills.Select(change => $"{change.Skill} ({change.OriginalClassification} to {change.VerifiedClassification})").ToArray())}.");
        }

        if (verification.RejectedCoreSkills.Count > 0)
        {
            parts.Add(
                $"Did not treat these extracted skills as role-defining without core-responsibility evidence: {FormatList(verification.RejectedCoreSkills)}.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        const int maximumDisplayedValues = 5;
        var displayed = values.Take(maximumDisplayedValues);
        var remaining = values.Count - maximumDisplayedValues;
        return remaining > 0
            ? $"{string.Join(", ", displayed)} (+{remaining} more)"
            : string.Join(", ", displayed);
    }

    private static IReadOnlyList<string> VerifyCoreSkills(
        IEnumerable<string> extractedCoreSkills,
        IReadOnlyList<string> verifiedRequiredSkills,
        string vacancyDescription,
        out IReadOnlyList<string> rejectedCoreSkills)
    {
        var verifiedCore = new List<string>();
        var rejectedCore = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var coreSkill in extractedCoreSkills.Where(skill => !string.IsNullOrWhiteSpace(skill)))
        {
            var normalizedCore = TechnicalSkillMatcher.Normalize(coreSkill);
            if (!seen.Add(normalizedCore))
            {
                continue;
            }

            var requiredSkill = verifiedRequiredSkills.FirstOrDefault(required =>
                TechnicalSkillMatcher.IsMatch(coreSkill, required) ||
                TechnicalSkillMatcher.IsMatch(required, coreSkill));
            if (requiredSkill is not null && HasCoreResponsibilityEvidence(requiredSkill, vacancyDescription))
            {
                verifiedCore.Add(requiredSkill);
            }
            else
            {
                rejectedCore.Add(coreSkill.Trim());
            }
        }

        rejectedCoreSkills = rejectedCore;
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
            foreach (var skill in skills.Where(skill => !string.IsNullOrWhiteSpace(skill)))
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

    private static EvidenceClassification GetEvidenceClassification(
        string skill,
        string vacancyDescription)
    {
        var patterns = TechnicalSkillMatcher.GetMentionPatterns(skill);
        if (patterns.Any(pattern => MatchesAny(vacancyDescription,
                $@"{pattern}\s+(?:experience\s+)?(?:is\s+|are\s+)?(?:not\s+relevant|irrelevant|not\s+required|not\s+(?:a\s+)?requirement|not\s+used|outside\s+(?:the|this)\s+role)",
                $@"\b(?:no|without)\s+(?:need\s+for\s+)?{pattern}")))
        {
            return EvidenceClassification.Excluded;
        }

        var sectionClassification = GetSectionClassification(patterns, vacancyDescription);
        if (sectionClassification != EvidenceClassification.None)
        {
            return sectionClassification;
        }

        var foundRequired = false;
        var foundOptional = false;
        foreach (var segment in GetSegments(vacancyDescription)
                     .Where(segment => patterns.Any(pattern => IsMatch(segment, pattern))))
        {
            if (HasNegativeEvidence(segment, patterns))
            {
                continue;
            }

            if (Regex.IsMatch(
                    segment,
                    @"\b(?:optional|preferred|nice[- ]to[- ]have|bonus|a\s+plus|advantage|desirable|useful)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                foundOptional = true;
                continue;
            }

            if (Regex.IsMatch(
                    segment,
                    @"\b(?:required|required skills?|requirements?|mandatory|must have|essential)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                foundRequired = true;
                continue;
            }

            if (IsContextOnly(segment))
            {
                continue;
            }

            if (patterns.Any(pattern => HasResponsibilityOrExperienceEvidence(segment, pattern)))
            {
                foundRequired = true;
            }
        }

        if (foundRequired)
        {
            return EvidenceClassification.Required;
        }

        return foundOptional ? EvidenceClassification.Optional : EvidenceClassification.None;
    }

    private static EvidenceClassification GetSectionClassification(
        IReadOnlyList<string> skillPatterns,
        string vacancyDescription)
    {
        var section = EvidenceClassification.None;
        foreach (var rawLine in Regex.Split(vacancyDescription, @"\r?\n"))
        {
            var line = rawLine.Trim().TrimStart('-', '*', '•').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsHeading(line, @"(?:required(?: skills?)?|requirements?|must have|qualifications?)"))
            {
                section = EvidenceClassification.Required;
                continue;
            }

            if (IsHeading(line, @"(?:preferred(?: skills?)?|nice[- ]to[- ]have|good to have|bonus)"))
            {
                section = EvidenceClassification.Optional;
                continue;
            }

            if (IsHeading(line, @"(?:about us|company|our stack|our technology|benefits?)"))
            {
                section = EvidenceClassification.None;
                continue;
            }

            if (IsHeading(line, @"(?:responsibilities|what you will do|about the role)") ||
                IsContextOnly(line))
            {
                section = EvidenceClassification.None;
                continue;
            }

            if (!skillPatterns.Any(pattern => IsMatch(line, pattern)))
            {
                continue;
            }

            if (section == EvidenceClassification.None)
            {
                continue;
            }

            if (HasNegativeEvidence(line, skillPatterns))
            {
                return EvidenceClassification.Excluded;
            }

            if (Regex.IsMatch(
                    line,
                    @"\b(?:optional|preferred|nice[- ]to[- ]have|bonus|a\s+plus|advantage|desirable|useful)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return EvidenceClassification.Optional;
            }

            if (Regex.IsMatch(
                    line,
                    @"\b(?:required|mandatory|must have|essential)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return EvidenceClassification.Required;
            }

            return section;
        }

        return EvidenceClassification.None;
    }

    private static bool HasCoreResponsibilityEvidence(string skill, string vacancyDescription)
    {
        var patterns = TechnicalSkillMatcher.GetMentionPatterns(skill);
        var isGenericSupportingSkill = GenericSupportingCoreSkills.Contains(
            TechnicalSkillMatcher.Normalize(skill));
        return GetSegments(vacancyDescription)
            .Where(segment => patterns.Any(pattern => IsMatch(segment, pattern)))
            .Any(segment =>
                Regex.IsMatch(
                    segment,
                    @"\b(?:core|primary|main|role-defining|central)\s+(?:stack|technology|technologies|platform|specialization)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                !isGenericSupportingSkill &&
                !IsContextOnly(segment) &&
                patterns.Any(pattern => HasResponsibilityEvidence(segment, pattern)));
    }

    private static bool HasResponsibilityOrExperienceEvidence(string segment, string skillPattern) =>
        HasResponsibilityEvidence(segment, skillPattern) ||
        MatchesAny(
            segment,
            $@"\b(?:strong|advanced|hands-on|commercial|production)\s+(?:experience|knowledge|expertise|proficiency)\s+(?:with|in|of)\s+{skillPattern}",
            $@"\b(?:experience|knowledge|expertise|proficiency)\s+(?:with|in|of)\s+{skillPattern}",
            $@"{skillPattern}\s+(?:experience|knowledge|expertise|proficiency)\b");

    private static bool HasResponsibilityEvidence(string segment, string skillPattern) => MatchesAny(
        segment,
        $@"\b(?:build|create|develop|design|implement|maintain|modernize|migrate|operate|administer|deploy|integrate|test|troubleshoot|own|deliver|architect|work\s+with|use|using)\b[^.;]{{0,100}}{skillPattern}",
        $@"{skillPattern}[^.;]{{0,100}}\b(?:development|implementation|maintenance|migration|operations?|ownership|administration)\b");

    private static bool HasNegativeEvidence(
        string segment,
        IReadOnlyList<string> skillPatterns) => skillPatterns.Any(pattern => MatchesAny(
            segment,
            $@"{pattern}\s+(?:experience\s+)?(?:is\s+|are\s+)?(?:not\s+relevant|irrelevant|not\s+required|not\s+(?:a\s+)?requirement|not\s+used|outside\s+(?:the|this)\s+role)",
            $@"\b(?:no|without)\s+(?:need\s+for\s+)?{pattern}"));

    private static bool IsContextOnly(string segment) => Regex.IsMatch(
        segment,
        @"\b(?:our|the|company)\s+(?:current\s+)?(?:infrastructure|environment|stack|systems?)\s+(?:includes?|contains?|uses?|is built on)|\b(?:examples?|possible technologies|past projects?)\s+(?:include|use)|\bused by another team\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> GetSegments(string description) => Regex
        .Split(description, @"(?<=[.!?])\s+|\r?\n|;")
        .Select(segment => segment.Trim())
        .Where(segment => segment.Length > 0);

    private static bool IsHeading(string line, string headingPattern) => Regex.IsMatch(
        line,
        $@"^\s*(?:#{{1,6}}\s*)?{headingPattern}\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsMatch(string text, string pattern) => Regex.IsMatch(
        text,
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static bool MatchesAny(string text, params string[] patterns) =>
        patterns.Any(pattern => IsMatch(text, pattern));

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

    private enum EvidenceClassification
    {
        None,
        Required,
        Optional,
        Excluded
    }

    private sealed record SkillCandidate(string Name, SkillClassification OriginalClassification);
}
