namespace AIJobSearchAgent.Application.Models;

public sealed record ExtractedJobRequirements(
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> OptionalSkills,
    bool HasClearRequirements,
    decimal? MinimumYearsProfessionalExperience,
    IReadOnlyList<RequiredLanguage> RequiredLanguages);

public sealed record RequiredLanguage(string Language, string? MinimumLevel);
