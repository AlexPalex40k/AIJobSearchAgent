namespace AIJobSearchAgent.Application.Models;

public enum RequirementClassification
{
    Mandatory,
    Optional,
    Contextual,
    Ambiguous
}

public enum CefrLevel
{
    A1,
    A2,
    B1,
    B2,
    C1,
    C2
}

public enum WorkArrangement
{
    Remote,
    Hybrid,
    OnSite
}

public enum RelocationDisposition
{
    Required,
    Available,
    NotAvailable
}

public sealed record RequirementEvidence(string Quote);

public sealed record TechnicalRequirement(
    string Name,
    RequirementEvidence Evidence);

public sealed record OverallExperienceRequirement(
    int? MinimumYears,
    string Description,
    RequirementEvidence Evidence);

public sealed record TechnologyExperienceRequirement(
    string Technology,
    int? MinimumYears,
    string Description,
    RequirementEvidence Evidence);

public sealed record LanguageRequirement(
    string Language,
    CefrLevel? CefrLevel,
    string ProficiencyDescription,
    RequirementClassification Classification,
    RequirementEvidence Evidence);

public sealed record LocationRequirement(
    string Location,
    RequirementClassification Classification,
    RequirementEvidence Evidence);

public sealed record WorkArrangementCondition(
    WorkArrangement Arrangement,
    RequirementClassification Classification,
    string Details,
    RequirementEvidence Evidence);

public sealed record RelocationCondition(
    RelocationDisposition Disposition,
    RequirementClassification Classification,
    string Details,
    RequirementEvidence Evidence);

public sealed record AmbiguousRequirement(
    string Subject,
    string Description,
    RequirementEvidence Evidence);

public sealed record VacancyRequirementsExtraction(
    IReadOnlyList<TechnicalRequirement> MandatoryTechnicalSkills,
    IReadOnlyList<TechnicalRequirement> OptionalTechnicalSkills,
    IReadOnlyList<TechnicalRequirement> ContextualTechnologyMentions,
    IReadOnlyList<OverallExperienceRequirement> OverallExperienceRequirements,
    IReadOnlyList<TechnologyExperienceRequirement> TechnologyExperienceRequirements,
    IReadOnlyList<LanguageRequirement> LanguageRequirements,
    IReadOnlyList<LocationRequirement> LocationRequirements,
    IReadOnlyList<WorkArrangementCondition> WorkArrangementConditions,
    IReadOnlyList<RelocationCondition> RelocationConditions,
    IReadOnlyList<AmbiguousRequirement> AmbiguousRequirements,
    bool IsDescriptionInsufficient,
    IReadOnlyList<string> InsufficientInformation,
    string ModelName,
    string PromptVersion,
    DateTimeOffset ExtractedAt);
