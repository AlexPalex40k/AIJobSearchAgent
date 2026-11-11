namespace AIJobSearchAgent.Application.Models;

public sealed record JobMatchWithDiagnosticsResult(
    JobMatchResult Result,
    JobMatchingDiagnostics Diagnostics);

public sealed record JobMatchingDiagnostics(
    IReadOnlyList<string> RawRequiredSkills,
    IReadOnlyList<string> RawOptionalSkills,
    IReadOnlyList<string> RawCoreSkills,
    IReadOnlyList<string> VerifiedRequiredSkills,
    IReadOnlyList<string> VerifiedOptionalSkills,
    IReadOnlyList<string> VerifiedCoreSkills,
    IReadOnlyList<string> RejectedRequiredSkills,
    IReadOnlyList<string> RejectedOptionalSkills,
    IReadOnlyList<string> RejectedCoreSkills,
    IReadOnlyList<TechnicalSkillClassificationChange> ReclassifiedSkills,
    IReadOnlyList<NormalizedSkillMatch> NormalizedSkillMatches,
    int FinalRating,
    string Explanation);

public sealed record NormalizedSkillMatch(
    string RequirementSkill,
    string CandidateSkill,
    string NormalizedRequirement,
    string NormalizedCandidate);
