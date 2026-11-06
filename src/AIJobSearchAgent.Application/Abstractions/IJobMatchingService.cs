using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobMatchingService
{
    Task<JobMatchResult> EvaluateAsync(
        JobVacancy vacancy,
        CandidateProfile profile,
        CancellationToken cancellationToken);
}
