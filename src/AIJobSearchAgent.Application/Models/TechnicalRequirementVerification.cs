namespace AIJobSearchAgent.Application.Models;

public sealed record TechnicalRequirementVerification(
    ExtractedJobRequirements VerifiedRequirements,
    IReadOnlyList<string> UnverifiedRequiredSkills,
    IReadOnlyList<string> UnverifiedOptionalSkills);
