using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Services;

public sealed class JobMatchCalculator
{
    private static readonly IReadOnlyDictionary<string, int> LanguageLevels =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = 1,
            ["A2"] = 2,
            ["B1"] = 3,
            ["B2"] = 4,
            ["C1"] = 5,
            ["C2"] = 6,
            ["native"] = 7
        };

    public JobMatchCalculation Calculate(
        ExtractedJobRequirements requirements,
        CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(profile);

        var candidateSkills = profile.TechnicalSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill.Name))
            .Select(skill => TechnicalSkillMatcher.Normalize(skill.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredSkills = NormalizeRequirements(requirements.RequiredSkills);
        var requiredKeys = requiredSkills.Select(skill => skill.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var optionalSkills = NormalizeRequirements(requirements.OptionalSkills)
            .Where(skill => !requiredKeys.Contains(skill.Key))
            .ToArray();

        var matchedRequired = requiredSkills.Where(skill => candidateSkills.Contains(skill.Key)).ToArray();
        var missingRequired = requiredSkills.Where(skill => !candidateSkills.Contains(skill.Key)).ToArray();
        var matchedOptional = optionalSkills.Where(skill => candidateSkills.Contains(skill.Key)).ToArray();
        var missingOptional = optionalSkills.Where(skill => !candidateSkills.Contains(skill.Key)).ToArray();

        var rating = CalculateTechnicalRating(
            requirements.HasClearRequirements,
            requiredSkills.Count,
            matchedRequired.Length,
            missingOptional.Length);
        var languageAssessment = AssessLanguages(requirements.RequiredLanguages, profile.SpokenLanguages);
        var hasExperienceShortfall = requirements.MinimumYearsProfessionalExperience is decimal minimumYears &&
            profile.YearsProfessionalExperience < minimumYears;

        if (languageAssessment.ConfirmedMismatches.Count > 0 || languageAssessment.UnknownCapabilities.Count > 0)
        {
            rating = Math.Min(rating, 3);
        }

        if (hasExperienceShortfall)
        {
            rating = Math.Min(rating, 4);
        }

        var matchedSkills = matchedRequired
            .Concat(matchedOptional)
            .Select(skill => skill.DisplayName)
            .ToArray();
        var missingRequiredSkills = missingRequired.Select(skill => skill.DisplayName).ToArray();
        var missingOptionalSkills = missingOptional.Select(skill => skill.DisplayName).ToArray();

        return new JobMatchCalculation(
            rating,
            matchedSkills,
            missingRequiredSkills,
            missingOptionalSkills,
            CreateExplanation(
                requirements.HasClearRequirements,
                matchedRequired.Select(skill => skill.DisplayName).ToArray(),
                missingRequiredSkills,
                missingOptionalSkills,
                languageAssessment,
                hasExperienceShortfall,
                requirements.MinimumYearsProfessionalExperience,
                profile.YearsProfessionalExperience));
    }

    private static int CalculateTechnicalRating(
        bool hasClearRequirements,
        int requiredSkillCount,
        int matchedRequiredCount,
        int missingOptionalCount)
    {
        if (!hasClearRequirements || requiredSkillCount == 0)
        {
            return 2;
        }

        if (matchedRequiredCount == 0)
        {
            return 1;
        }

        if (matchedRequiredCount < requiredSkillCount)
        {
            return 3;
        }

        return missingOptionalCount == 0 ? 5 : 4;
    }

    private static IReadOnlyList<NormalizedSkill> NormalizeRequirements(IEnumerable<string> skills)
    {
        var result = new List<NormalizedSkill>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            var displayName = skill.Trim();
            var key = TechnicalSkillMatcher.Normalize(displayName);
            if (seen.Add(key))
            {
                result.Add(new NormalizedSkill(key, displayName));
            }
        }

        return result;
    }

    private static LanguageAssessment AssessLanguages(
        IEnumerable<RequiredLanguage> requirements,
        IEnumerable<SpokenLanguage> candidateLanguages)
    {
        var knownLanguages = candidateLanguages
            .Where(language => !string.IsNullOrWhiteSpace(language.Language))
            .GroupBy(language => language.Language.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Level, StringComparer.OrdinalIgnoreCase);
        var mismatches = new List<string>();
        var unknownCapabilities = new List<string>();

        foreach (var requirement in requirements)
        {
            var requiredLanguage = requirement.Language.Trim();
            if (!knownLanguages.TryGetValue(requiredLanguage, out var candidateLevel))
            {
                unknownCapabilities.Add($"{requiredLanguage} is not present in the candidate profile");
                continue;
            }

            if (string.IsNullOrWhiteSpace(requirement.MinimumLevel))
            {
                continue;
            }

            if (!LanguageLevels.TryGetValue(requirement.MinimumLevel, out var requiredRank) ||
                !LanguageLevels.TryGetValue(candidateLevel, out var candidateRank))
            {
                unknownCapabilities.Add($"{requiredLanguage} level could not be compared");
            }
            else if (candidateRank < requiredRank)
            {
                mismatches.Add(
                    $"known {requiredLanguage} level {candidateLevel} is below required {requirement.MinimumLevel}");
            }
        }

        return new LanguageAssessment(mismatches, unknownCapabilities);
    }

    private static string CreateExplanation(
        bool hasClearRequirements,
        IReadOnlyList<string> matchedRequired,
        IReadOnlyList<string> missingRequired,
        IReadOnlyList<string> missingOptional,
        LanguageAssessment languageAssessment,
        bool hasExperienceShortfall,
        decimal? minimumYears,
        decimal candidateYears)
    {
        var parts = new List<string>();
        if (!hasClearRequirements || matchedRequired.Count + missingRequired.Count == 0)
        {
            parts.Add("Insufficient information: the vacancy does not provide clear required technical skills for reliable matching.");
        }
        else if (missingRequired.Count == matchedRequired.Count + missingRequired.Count)
        {
            parts.Add($"Not suitable: none of the required technical skills are present. Missing required skills: {string.Join(", ", missingRequired)}.");
        }
        else if (missingRequired.Count > 0)
        {
            parts.Add($"Partial match: matched required skills: {string.Join(", ", matchedRequired)}; missing required skills: {string.Join(", ", missingRequired)}.");
        }
        else if (missingOptional.Count > 0)
        {
            parts.Add($"Good match: all required technical skills are present; missing optional skills: {string.Join(", ", missingOptional)}.");
        }
        else
        {
            parts.Add("Excellent technical match: all required and optional technical skills are present.");
        }

        if (languageAssessment.ConfirmedMismatches.Count > 0)
        {
            parts.Add($"Mandatory language requirement not met: {string.Join("; ", languageAssessment.ConfirmedMismatches)}; rating is capped at 3.");
        }

        if (languageAssessment.UnknownCapabilities.Count > 0)
        {
            parts.Add($"Mandatory language requirement not verified: {string.Join("; ", languageAssessment.UnknownCapabilities)}; rating is capped at 3.");
        }

        if (hasExperienceShortfall)
        {
            parts.Add($"Overall professional experience is {candidateYears:0.##} years, below the required {minimumYears:0.##} years; rating is capped at 4.");
        }

        return string.Join(" ", parts);
    }

    private sealed record NormalizedSkill(string Key, string DisplayName);

    private sealed record LanguageAssessment(
        IReadOnlyList<string> ConfirmedMismatches,
        IReadOnlyList<string> UnknownCapabilities);
}
