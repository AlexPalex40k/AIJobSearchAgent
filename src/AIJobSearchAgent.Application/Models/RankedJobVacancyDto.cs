using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Models;

public sealed record RankedJobVacancyDto(
    Guid VacancyId,
    string Title,
    string Company,
    string Location,
    bool IsRemote,
    string Source,
    string Url,
    DateTimeOffset? PublishedAt,
    int? Rating,
    EvaluationStatus? EvaluationStatus,
    DateTimeOffset? EvaluatedAt,
    string? Explanation);
