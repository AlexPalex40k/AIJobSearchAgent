using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Tests;

public sealed class GetJobsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsKeywordLongerThanConfiguredLimit()
    {
        var repository = new RecordingJobVacancyRepository();
        var useCase = new GetJobsUseCase(repository);
        var query = new JobQuery(
            new string('a', KeywordMatcher.MaximumSearchQueryLength + 1),
            null,
            1,
            20);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(query, CancellationToken.None));

        Assert.False(repository.GetWasCalled);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMoreThanConfiguredTokenLimit()
    {
        var repository = new RecordingJobVacancyRepository();
        var useCase = new GetJobsUseCase(repository);
        var keyword = string.Join(
            ' ',
            Enumerable.Range(1, KeywordMatcher.MaximumSearchTokenCount + 1).Select(index => $"k{index}"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(new JobQuery(keyword, null, 1, 20), CancellationToken.None));

        Assert.False(repository.GetWasCalled);
    }

    private sealed class RecordingJobVacancyRepository : IJobVacancyRepository
    {
        public bool GetWasCalled { get; private set; }

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedResult<JobVacancy>> GetAsync(
            JobQuery query,
            CancellationToken cancellationToken)
        {
            GetWasCalled = true;
            return Task.FromResult(new PagedResult<JobVacancy>([], 0, query.Page, query.PageSize));
        }

        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
