using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Models;

public sealed record JobVacancyDto(
    Guid Id,
    string ExternalId,
    string Source,
    string Title,
    string Company,
    string Location,
    bool IsRemote,
    string Description,
    string Url,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt)
{
    public static JobVacancyDto FromEntity(JobVacancy job) => new(
        job.Id,
        job.ExternalId,
        job.Source,
        job.Title,
        job.Company,
        job.Location,
        job.IsRemote,
        job.Description,
        job.Url,
        job.PublishedAt,
        job.CreatedAt);
}
