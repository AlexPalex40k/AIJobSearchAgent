using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class JobVacancyRepository(AppDbContext dbContext) : IJobVacancyRepository
{
    public async Task<SaveJobsResult> AddNewAsync(
        IReadOnlyCollection<JobVacancy> vacancies,
        CancellationToken cancellationToken)
    {
        if (vacancies.Count == 0)
        {
            return new SaveJobsResult(0, 0);
        }

        var distinctVacancies = new Dictionary<string, JobVacancy>(StringComparer.Ordinal);
        var duplicates = 0;

        foreach (var vacancy in vacancies)
        {
            if (!distinctVacancies.TryAdd(CreateKey(vacancy.Source, vacancy.ExternalId), vacancy))
            {
                duplicates++;
            }
        }

        var sources = distinctVacancies.Values.Select(vacancy => vacancy.Source).Distinct().ToArray();
        var externalIds = distinctVacancies.Values.Select(vacancy => vacancy.ExternalId).Distinct().ToArray();
        var existingPairs = await dbContext.JobVacancies
            .AsNoTracking()
            .Where(vacancy => sources.Contains(vacancy.Source) && externalIds.Contains(vacancy.ExternalId))
            .Select(vacancy => new { vacancy.Source, vacancy.ExternalId })
            .ToListAsync(cancellationToken);
        var existingKeys = existingPairs
            .Select(pair => CreateKey(pair.Source, pair.ExternalId))
            .ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        foreach (var (key, vacancy) in distinctVacancies)
        {
            if (existingKeys.Contains(key))
            {
                duplicates++;
                continue;
            }

            dbContext.JobVacancies.Add(vacancy);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                inserted++;
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                dbContext.Entry(vacancy).State = EntityState.Detached;
                duplicates++;
            }
        }

        return new SaveJobsResult(inserted, duplicates);
    }

    public async Task<PagedResult<JobVacancy>> GetAsync(
        JobQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<JobVacancy> vacancies = dbContext.JobVacancies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            vacancies = vacancies.Where(vacancy =>
                vacancy.Title.ToLower().Contains(keyword) ||
                vacancy.Description.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim().ToLower();
            vacancies = vacancies.Where(vacancy => vacancy.Source.ToLower() == source);
        }

        var totalCount = await vacancies.CountAsync(cancellationToken);
        var items = await vacancies
            .OrderByDescending(vacancy => vacancy.PublishedAt.HasValue)
            .ThenByDescending(vacancy => vacancy.PublishedAt)
            .ThenByDescending(vacancy => vacancy.CreatedAt)
            .ThenBy(vacancy => vacancy.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<JobVacancy>(items, totalCount, query.Page, query.PageSize);
    }

    public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.JobVacancies.AsNoTracking().SingleOrDefaultAsync(vacancy => vacancy.Id == id, cancellationToken);

    private static string CreateKey(string source, string externalId) => $"{source}\u001f{externalId}";

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
