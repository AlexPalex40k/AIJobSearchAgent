using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class JobVacancyRepository(AppDbContext dbContext) : IJobVacancyRepository
{
    private const int SqliteConstraintUnique = 2067;

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

        var existingKeys = await GetExistingKeysAsync(distinctVacancies.Values, cancellationToken);
        var pendingVacancies = distinctVacancies
            .Where(pair => !existingKeys.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();
        duplicates += distinctVacancies.Count - pendingVacancies.Count;

        var inserted = 0;
        while (pendingVacancies.Count > 0)
        {
            dbContext.JobVacancies.AddRange(pendingVacancies);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                inserted += pendingVacancies.Count;
                break;
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                foreach (var vacancy in pendingVacancies)
                {
                    dbContext.Entry(vacancy).State = EntityState.Detached;
                }

                var concurrentlyInsertedKeys = await GetExistingKeysAsync(
                    pendingVacancies,
                    cancellationToken);
                if (concurrentlyInsertedKeys.Count == 0)
                {
                    throw;
                }

                duplicates += concurrentlyInsertedKeys.Count;
                pendingVacancies = pendingVacancies
                    .Where(vacancy => !concurrentlyInsertedKeys.Contains(
                        CreateKey(vacancy.Source, vacancy.ExternalId)))
                    .ToList();
            }
        }

        return new SaveJobsResult(inserted, duplicates);
    }

    public async Task<PagedResult<JobVacancy>> GetAsync(
        JobQuery query,
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

    private async Task<HashSet<string>> GetExistingKeysAsync(
        IEnumerable<JobVacancy> vacancies,
        CancellationToken cancellationToken)
    {
        var candidates = vacancies.ToArray();
        var candidateKeys = candidates
            .Select(vacancy => CreateKey(vacancy.Source, vacancy.ExternalId))
            .ToHashSet(StringComparer.Ordinal);
        var sources = candidates.Select(vacancy => vacancy.Source).Distinct().ToArray();
        var externalIds = candidates.Select(vacancy => vacancy.ExternalId).Distinct().ToArray();
        var existingPairs = await dbContext.JobVacancies
            .AsNoTracking()
            .Where(vacancy => sources.Contains(vacancy.Source) && externalIds.Contains(vacancy.ExternalId))
            .Select(vacancy => new { vacancy.Source, vacancy.ExternalId })
            .ToListAsync(cancellationToken);

        return existingPairs
            .Select(pair => CreateKey(pair.Source, pair.ExternalId))
            .Where(candidateKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique };
}
