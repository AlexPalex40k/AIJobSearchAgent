using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Models;

public sealed record CandidateProfileSnapshot(CandidateProfile Profile, string ProfileHash);
