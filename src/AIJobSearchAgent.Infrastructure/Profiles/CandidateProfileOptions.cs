using System.ComponentModel.DataAnnotations;

namespace AIJobSearchAgent.Infrastructure.Profiles;

public sealed class CandidateProfileOptions
{
    public const string SectionName = "CandidateProfile";

    [Required]
    public string Path { get; init; } = "candidate-profile.json";
}
