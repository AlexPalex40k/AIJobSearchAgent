using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.Abstractions;

public interface IRankedJobVacancyQuery
{
    Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
        RankedJobQuery query,
        string profileHash,
        string modelName,
        string promptVersion,
        CancellationToken cancellationToken);
}
