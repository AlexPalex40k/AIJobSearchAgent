using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IJobRequirementsExtractor
{
    Task<VacancyRequirementsExtraction> ExtractAsync(
        JobVacancy vacancy,
        CancellationToken cancellationToken);
}
