namespace AIJobSearchAgent.Application.Models;

public sealed record TechnicalRequirementVerification(
    ExtractedJobRequirements VerifiedRequirements,
    IReadOnlyList<string> UnverifiedRequiredSkills,
    IReadOnlyList<string> UnverifiedOptionalSkills)
{
    public IReadOnlyList<TechnicalSkillClassificationChange> ReclassifiedSkills { get; init; } = [];

    public IReadOnlyList<string> RejectedCoreSkills { get; init; } = [];
}

public sealed record TechnicalSkillClassificationChange(
    string Skill,
    string OriginalClassification,
    string VerifiedClassification);
