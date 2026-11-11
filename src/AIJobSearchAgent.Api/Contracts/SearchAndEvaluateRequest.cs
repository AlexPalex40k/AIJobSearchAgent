namespace AIJobSearchAgent.Api.Contracts;

public sealed class SearchAndEvaluateRequest
{
    public string? JobTitle { get; init; }

    public string? Location { get; init; }

    public int? PublishedWithinDays { get; init; }

    public int MaxJobs { get; init; } = 10;

    public SearchCandidateProfileRequest? CandidateProfile { get; init; }
}

public sealed class SearchCandidateProfileRequest
{
    public string? ProfessionalTitle { get; init; }

    public decimal YearsProfessionalExperience { get; init; }

    public IReadOnlyList<SearchCandidateSkillRequest?>? TechnicalSkills { get; init; } = [];

    public IReadOnlyList<SearchSpokenLanguageRequest?>? SpokenLanguages { get; init; } = [];
}

public sealed class SearchCandidateSkillRequest
{
    public string? Name { get; init; }

    public string? Level { get; init; }
}

public sealed class SearchSpokenLanguageRequest
{
    public string? Language { get; init; }

    public string? Level { get; init; }
}
