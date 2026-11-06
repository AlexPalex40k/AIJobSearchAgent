namespace AIJobSearchAgent.Domain.Profiles;

public sealed class CandidateProfile
{
    public string? Name { get; init; }
    public required string ProfessionalTitle { get; init; }
    public decimal YearsProfessionalExperience { get; init; }
    public IReadOnlyList<CandidateSkill> TechnicalSkills { get; init; } = [];
    public IReadOnlyList<string> PreferredJobTitles { get; init; } = [];
    public IReadOnlyList<string> PreferredLocations { get; init; } = [];
    public RemoteWorkPreference RemoteWorkPreference { get; init; }
    public IReadOnlyList<SpokenLanguage> SpokenLanguages { get; init; } = [];
    public string? AdditionalExperienceOrPreferences { get; init; }
}
