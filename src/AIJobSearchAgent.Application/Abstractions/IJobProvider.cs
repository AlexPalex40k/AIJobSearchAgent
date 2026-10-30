using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobProvider
{
    string SourceName { get; }

    Task<IReadOnlyList<JobVacancy>> SearchAsync(
        JobSearchCriteria criteria,
        CancellationToken cancellationToken);
}
