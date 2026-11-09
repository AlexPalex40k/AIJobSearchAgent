using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Services;

public static class TechnicalRequirementVerifier
{
    public static TechnicalRequirementVerification Verify(
        ExtractedJobRequirements extractedRequirements,
        string vacancyDescription)
    {
        ArgumentNullException.ThrowIfNull(extractedRequirements);
        ArgumentNullException.ThrowIfNull(vacancyDescription);

        var required = Partition(extractedRequirements.RequiredSkills, vacancyDescription);
        var optional = Partition(extractedRequirements.OptionalSkills, vacancyDescription);
        var verifiedRequirements = extractedRequirements with
        {
            RequiredSkills = required.Verified,
            OptionalSkills = optional.Verified,
            HasClearRequirements = extractedRequirements.HasClearRequirements && required.Verified.Count > 0
        };

        return new TechnicalRequirementVerification(
            verifiedRequirements,
            required.Unverified,
            optional.Unverified);
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
            : $"Ignored extracted technical skills not mentioned in the vacancy description ({string.Join("; ", classifications)}).";
    }

    private static SkillPartition Partition(
        IEnumerable<string> skills,
        string vacancyDescription)
    {
        var verified = new List<string>();
        var unverified = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in skills)
        {
            var normalizedSkill = TechnicalSkillMatcher.Normalize(skill);
            if (!seen.Add(normalizedSkill))
            {
                continue;
            }

            (TechnicalSkillMatcher.IsMentioned(skill, vacancyDescription) ? verified : unverified)
                .Add(skill.Trim());
        }

        return new SkillPartition(verified, unverified);
    }

    private sealed record SkillPartition(
        IReadOnlyList<string> Verified,
        IReadOnlyList<string> Unverified);
}
