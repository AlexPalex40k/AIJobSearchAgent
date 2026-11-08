namespace AIJobSearchAgent.Domain.Profiles;

public sealed class CandidateSkill
{
    public required string Name { get; init; }
    public string? Level { get; init; }
}
