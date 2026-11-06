using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Abstractions;

public interface ICandidateProfileProvider
{
    Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken);
}
