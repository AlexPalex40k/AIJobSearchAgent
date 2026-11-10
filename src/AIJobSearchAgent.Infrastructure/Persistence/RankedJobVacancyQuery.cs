using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class RankedJobVacancyQuery(AppDbContext dbContext) : IRankedJobVacancyQuery
{
    private const int MaximumExplanationLength = 500;

    public async Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
        RankedJobQuery query,
        string profileHash,
        string modelName,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        IQueryable<JobVacancy> vacancies = dbContext.JobVacancies.AsNoTracking();

        foreach (var token in KeywordMatcher.TokenizeSearchQuery(query.Keyword))
        {
            var normalizedToken = token.ToLowerInvariant();
            vacancies = vacancies.Where(vacancy =>
                vacancy.Title.ToLower().Contains(normalizedToken) ||
                vacancy.Description.ToLower().Contains(normalizedToken));
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim().ToLower();
            vacancies = vacancies.Where(vacancy => vacancy.Source.ToLower() == source);
        }

        var totalCount = await vacancies.CountAsync(cancellationToken);
        var evaluations = dbContext.JobMatchEvaluations.AsNoTracking();
        var rows =
            from vacancy in vacancies
            let latestEvaluationId = evaluations
                .Where(evaluation =>
                    evaluation.JobVacancyId == vacancy.Id &&
                    evaluation.ProfileHash == profileHash &&
                    evaluation.ModelName == modelName &&
                    evaluation.PromptVersion == promptVersion)
                .OrderByDescending(evaluation => evaluation.EvaluatedAt)
                .ThenByDescending(evaluation => evaluation.Id)
                .Select(evaluation => (Guid?)evaluation.Id)
                .FirstOrDefault()
            join evaluation in evaluations
                on latestEvaluationId equals (Guid?)evaluation.Id into evaluationGroup
            from evaluation in evaluationGroup.DefaultIfEmpty()
            select new RankedJobRow
            {
                VacancyId = vacancy.Id,
                Title = vacancy.Title,
                Company = vacancy.Company,
                Location = vacancy.Location,
                IsRemote = vacancy.IsRemote,
                Source = vacancy.Source,
                Url = vacancy.Url,
                PublishedAt = vacancy.PublishedAt,
                CreatedAt = vacancy.CreatedAt,
                Rating = evaluation == null ? null : evaluation.Rating,
                EvaluationStatus = evaluation == null
                    ? null
                    : (EvaluationStatus?)evaluation.EvaluationStatus,
                EvaluatedAt = evaluation == null ? null : evaluation.EvaluatedAt,
                Explanation = evaluation == null
                    ? null
                    : evaluation.Explanation.Length <= MaximumExplanationLength
                        ? evaluation.Explanation
                        : evaluation.Explanation.Substring(0, MaximumExplanationLength - 3) + "..."
            };

        var orderedRows = query.Sort switch
        {
            RankedJobSort.RatingAscending => rows
                .OrderByDescending(row => row.Rating.HasValue)
                .ThenBy(row => row.Rating)
                .ThenByDescending(row => row.PublishedAt.HasValue)
                .ThenByDescending(row => row.PublishedAt)
                .ThenByDescending(row => row.CreatedAt)
                .ThenBy(row => row.VacancyId),
            RankedJobSort.Newest => rows
                .OrderByDescending(row => row.PublishedAt.HasValue)
                .ThenByDescending(row => row.PublishedAt)
                .ThenByDescending(row => row.CreatedAt)
                .ThenBy(row => row.VacancyId),
            _ => rows
                .OrderByDescending(row => row.Rating.HasValue)
                .ThenByDescending(row => row.Rating)
                .ThenByDescending(row => row.PublishedAt.HasValue)
                .ThenByDescending(row => row.PublishedAt)
                .ThenByDescending(row => row.CreatedAt)
                .ThenBy(row => row.VacancyId)
        };

        var items = await orderedRows
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(row => new RankedJobVacancyDto(
                row.VacancyId,
                row.Title,
                row.Company,
                row.Location,
                row.IsRemote,
                row.Source,
                row.Url,
                row.PublishedAt,
                row.Rating,
                row.EvaluationStatus,
                row.EvaluatedAt,
                row.Explanation))
            .ToListAsync(cancellationToken);

        return new PagedResult<RankedJobVacancyDto>(items, totalCount, query.Page, query.PageSize);
    }

    private sealed class RankedJobRow
    {
        public Guid VacancyId { get; init; }
        public required string Title { get; init; }
        public required string Company { get; init; }
        public required string Location { get; init; }
        public bool IsRemote { get; init; }
        public required string Source { get; init; }
        public required string Url { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public int? Rating { get; init; }
        public EvaluationStatus? EvaluationStatus { get; init; }
        public DateTimeOffset? EvaluatedAt { get; init; }
        public string? Explanation { get; init; }
    }
}
