using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobMatchingService
{
    string ModelName { get; }

    string PromptVersion { get; }

    Task<JobMatchResult> EvaluateAsync(
        JobVacancy vacancy,
        CandidateProfile profile,
        CancellationToken cancellationToken);
}
