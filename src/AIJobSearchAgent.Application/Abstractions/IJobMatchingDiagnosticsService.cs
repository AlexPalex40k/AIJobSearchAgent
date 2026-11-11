using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobMatchingDiagnosticsService
{
    Task<JobMatchWithDiagnosticsResult> EvaluateWithDiagnosticsAsync(
        JobVacancy vacancy,
        CandidateProfile profile,
        CancellationToken cancellationToken);
}
