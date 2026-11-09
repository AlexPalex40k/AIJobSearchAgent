using System.Text;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Services;

public sealed class JobMatchCalculator
{
    private static readonly IReadOnlyDictionary<string, string> SkillAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "csharp",
            ["c sharp"] = "csharp",
            ["csharp"] = "csharp",
            ["ef core"] = "entityframeworkcore",
            ["entity framework core"] = "entityframeworkcore",
            ["entity-framework core"] = "entityframeworkcore",
            ["asp.net core"] = "aspnetcore",
            ["asp net core"] = "aspnetcore",
            ["aspnet core"] = "aspnetcore",
            ["asp.net-core"] = "aspnetcore",
            [".net"] = "dotnet",
            ["dotnet"] = "dotnet"
        };

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
            .Select(skill => NormalizeSkill(skill.Name))
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
        var languageMismatches = FindConfirmedLanguageMismatches(requirements.RequiredLanguages, profile.SpokenLanguages);
        var hasExperienceShortfall = requirements.MinimumYearsProfessionalExperience is decimal minimumYears &&
            profile.YearsProfessionalExperience < minimumYears;

        if (languageMismatches.Count > 0)
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
                languageMismatches,
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
            var key = NormalizeSkill(displayName);
            if (seen.Add(key))
            {
                result.Add(new NormalizedSkill(key, displayName));
            }
        }

        return result;
    }

    private static string NormalizeSkill(string skill)
    {
        var whitespaceNormalized = CollapseWhitespace(skill.Trim());
        return SkillAliases.TryGetValue(whitespaceNormalized, out var alias)
            ? alias
            : whitespaceNormalized;
    }

    private static string CollapseWhitespace(string value)
    {
        var result = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    result.Append(' ');
                }

                previousWasWhitespace = true;
            }
            else
            {
                result.Append(char.ToLowerInvariant(character));
                previousWasWhitespace = false;
            }
        }

        return result.ToString();
    }

    private static IReadOnlyList<string> FindConfirmedLanguageMismatches(
        IEnumerable<RequiredLanguage> requirements,
        IEnumerable<SpokenLanguage> candidateLanguages)
    {
        var knownLanguages = candidateLanguages
            .Where(language => !string.IsNullOrWhiteSpace(language.Language))
            .GroupBy(language => language.Language.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Level, StringComparer.OrdinalIgnoreCase);
        var mismatches = new List<string>();

        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.MinimumLevel) ||
                !knownLanguages.TryGetValue(requirement.Language.Trim(), out var candidateLevel) ||
                !LanguageLevels.TryGetValue(requirement.MinimumLevel, out var requiredRank) ||
                !LanguageLevels.TryGetValue(candidateLevel, out var candidateRank) ||
                candidateRank >= requiredRank)
            {
                continue;
            }

            mismatches.Add(
                $"known {requirement.Language.Trim()} level {candidateLevel} is below required {requirement.MinimumLevel}");
        }

        return mismatches;
    }

    private static string CreateExplanation(
        bool hasClearRequirements,
        IReadOnlyList<string> matchedRequired,
        IReadOnlyList<string> missingRequired,
        IReadOnlyList<string> missingOptional,
        IReadOnlyList<string> languageMismatches,
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

        if (languageMismatches.Count > 0)
        {
            parts.Add($"Mandatory language requirement not met: {string.Join("; ", languageMismatches)}; rating is capped at 3.");
        }

        if (hasExperienceShortfall)
        {
            parts.Add($"Overall professional experience is {candidateYears:0.##} years, below the required {minimumYears:0.##} years; rating is capped at 4.");
        }

        return string.Join(" ", parts);
    }

    private sealed record NormalizedSkill(string Key, string DisplayName);
}
