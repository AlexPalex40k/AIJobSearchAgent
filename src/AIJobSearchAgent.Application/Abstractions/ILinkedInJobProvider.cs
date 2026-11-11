using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Abstractions;

public interface ILinkedInJobProvider : IJobProvider
{
    Task<LinkedInJobSearchResult> SearchWithDiagnosticsAsync(
        LinkedInJobSearchCriteria criteria,
        CancellationToken cancellationToken);
}
