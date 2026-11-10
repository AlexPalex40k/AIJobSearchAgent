using AIJobSearchAgent.Api.Pages.Jobs;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Tests;

public sealed class JobsPageActionTests
{
    [Fact]
    public async Task OnPostImportAsync_ParsesKeywordsUsesImportUseCaseAndShowsSummary()
    {
        var harness = new PageHarness();
        harness.Provider.Results =
        [
            CreateVacancy("one", ".NET Engineer", "C# and ASP.NET Core"),
            CreateVacancy("two", "Java Engineer", "Spring"),
            CreateVacancy("invalid", ".NET Engineer", ".NET", url: "not-a-url")
        ];
        harness.Vacancies.SaveResult = new SaveJobsResult(1, 0);
        harness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = " .NET, C#, , ASP.NET Core, .net ",
            MaximumPages = 2
        };

        var result = await harness.Model.OnPostImportAsync(
            "backend",
            "newest",
            2,
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Jobs/Index", redirect.PageName);
        Assert.Equal("backend", redirect.RouteValues?["keyword"]);
        Assert.Equal("newest", redirect.RouteValues?["sort"]);
        Assert.Equal(2, redirect.RouteValues?["page"]);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.NotNull(harness.Provider.ReceivedCriteria);
        Assert.Equal([".NET", "C#", "ASP.NET Core"], harness.Provider.ReceivedCriteria.Keywords);
        Assert.Equal(2, harness.Provider.ReceivedCriteria.MaxPages);
        Assert.Equal(
            "Import completed: fetched 3, matched 1, inserted 1, duplicates 0, invalid 1.",
            harness.Model.ImportSummaryMessage);
        Assert.Equal(0, harness.Matching.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" , , ")]
    public async Task OnPostImportAsync_RejectsEmptyKeywords(string? keywords)
    {
        var harness = new PageHarness();
        harness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = keywords,
            MaximumPages = 2
        };

        var result = await harness.Model.OnPostImportAsync(null, null, cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Provider.CallCount);
    }

    [Fact]
    public async Task OnPostImportAsync_RejectsTooManyOrOverlongKeywords()
    {
        var tooManyHarness = new PageHarness();
        tooManyHarness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = string.Join(',', Enumerable.Range(1, 21).Select(index => $"term-{index}")),
            MaximumPages = 2
        };

        var tooManyResult = await tooManyHarness.Model.OnPostImportAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(tooManyResult);
        Assert.Equal(0, tooManyHarness.Provider.CallCount);

        var overlongHarness = new PageHarness();
        overlongHarness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = new string('a', IndexModel.MaximumImportKeywordLength + 1),
            MaximumPages = 2
        };

        var overlongResult = await overlongHarness.Model.OnPostImportAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(overlongResult);
        Assert.Equal(0, overlongHarness.Provider.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task OnPostImportAsync_RejectsPageCountOutsideConfiguredLimit(int maximumPages)
    {
        var harness = new PageHarness(configuredMaximumPages: 3);
        harness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = ".NET",
            MaximumPages = maximumPages
        };

        var result = await harness.Model.OnPostImportAsync(null, null, cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Provider.CallCount);
    }

    [Fact]
    public async Task OnPostImportAsync_ShowsSafeMessageWhenProviderFails()
    {
        var harness = new PageHarness();
        harness.Provider.Exception = new JobProviderException(
            "secret provider response",
            new HttpRequestException("secret endpoint"));
        harness.Model.ImportForm = new IndexModel.ImportInput
        {
            Keywords = ".NET",
            MaximumPages = 2
        };

        var result = await harness.Model.OnPostImportAsync(null, null, cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Arbeitnow is currently unavailable. Please try the import again later.", harness.Model.ImportErrorMessage);
        Assert.DoesNotContain("secret", harness.Model.ImportErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.RankedQuery.CallCount);
    }

    [Fact]
    public async Task OnPostEvaluateSelectedAsync_DeduplicatesAndShowsSucceededSkippedAndFailedSummary()
    {
        var harness = new PageHarness();
        var successfulVacancy = CreateVacancy("successful", ".NET Engineer", ".NET");
        var skippedVacancy = CreateVacancy("skipped", "C# Engineer", "C#");
        var failedVacancy = CreateVacancy("failed", "Backend Engineer", "APIs");
        harness.Vacancies.Stored.Add(successfulVacancy.Id, successfulVacancy);
        harness.Vacancies.Stored.Add(skippedVacancy.Id, skippedVacancy);
        harness.Vacancies.Stored.Add(failedVacancy.Id, failedVacancy);
        harness.RankedQuery.CurrentVacancyIds.UnionWith(
            [successfulVacancy.Id, skippedVacancy.Id, failedVacancy.Id]);
        harness.Evaluations.Stored.Add(new JobMatchEvaluation
        {
            JobVacancyId = skippedVacancy.Id,
            ProfileHash = PageHarness.ProfileHash,
            ModelName = PageHarness.ModelName,
            PromptVersion = PageHarness.PromptVersion,
            Rating = 5,
            Explanation = "Already evaluated.",
            EvaluationStatus = EvaluationStatus.Succeeded
        });
        harness.Matching.ResultFactory = vacancy => vacancy.Id == failedVacancy.Id
            ? throw new JobMatchingException(
                "internal Ollama detail",
                PageHarness.ModelName,
                PageHarness.PromptVersion,
                EvaluationStatus.Failed)
            : new JobMatchResult(
                4,
                ["C#"],
                [],
                [],
                "Good match.",
                PageHarness.ModelName,
                PageHarness.PromptVersion,
                DateTimeOffset.Parse("2026-11-10T12:00:00Z"));
        harness.Model.SelectedJobIds =
        [
            successfulVacancy.Id,
            successfulVacancy.Id,
            skippedVacancy.Id,
            failedVacancy.Id
        ];

        var result = await harness.Model.OnPostEvaluateSelectedAsync(
            "engineer",
            "rating_asc",
            3,
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("engineer", redirect.RouteValues?["keyword"]);
        Assert.Equal("rating_asc", redirect.RouteValues?["sort"]);
        Assert.Equal(3, redirect.RouteValues?["page"]);
        Assert.Equal(2, harness.Matching.CallCount);
        Assert.Equal(
            "Evaluation completed: requested 3, succeeded 1, skipped 1, failed 1.",
            harness.Model.EvaluationSummaryMessage);
        Assert.Contains(harness.Evaluations.Stored, evaluation =>
            evaluation.JobVacancyId == successfulVacancy.Id &&
            evaluation.EvaluationStatus == EvaluationStatus.Succeeded &&
            evaluation.Rating == 4);
        Assert.Contains(harness.Evaluations.Stored, evaluation =>
            evaluation.JobVacancyId == failedVacancy.Id &&
            evaluation.EvaluationStatus == EvaluationStatus.Failed &&
            evaluation.Rating is null);
    }

    [Fact]
    public async Task OnPostEvaluateSelectedAsync_RejectsEmptySelection()
    {
        var harness = new PageHarness();

        var result = await harness.Model.OnPostEvaluateSelectedAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Matching.CallCount);
        Assert.Equal(1, harness.RankedQuery.CallCount);
    }

    [Fact]
    public async Task OnPostEvaluateSelectedAsync_RejectsMoreThanTwentyIds()
    {
        var harness = new PageHarness();
        harness.Model.SelectedJobIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList();

        var result = await harness.Model.OnPostEvaluateSelectedAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Matching.CallCount);
        Assert.Equal(1, harness.RankedQuery.CallCount);
    }

    [Fact]
    public async Task OnPostEvaluateSelectedAsync_RejectsInvalidId()
    {
        var harness = new PageHarness();
        harness.Model.SelectedJobIds = [Guid.Empty];

        var result = await harness.Model.OnPostEvaluateSelectedAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Matching.CallCount);
        Assert.Equal(1, harness.RankedQuery.CallCount);
    }

    [Fact]
    public async Task OnPostEvaluateSelectedAsync_RejectsVacancyOutsideCurrentPage()
    {
        var harness = new PageHarness();
        var vacancy = CreateVacancy("other-page", ".NET Engineer", ".NET");
        harness.Vacancies.Stored.Add(vacancy.Id, vacancy);
        harness.Model.SelectedJobIds = [vacancy.Id];

        var result = await harness.Model.OnPostEvaluateSelectedAsync(
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(harness.Model.ModelState.IsValid);
        Assert.Equal(0, harness.Matching.CallCount);
        Assert.Empty(harness.Evaluations.Stored);
    }

    [Fact]
    public void PageModel_EnablesAutomaticAntiforgeryValidation()
    {
        Assert.Contains(
            typeof(IndexModel).GetCustomAttributes(inherit: true),
            attribute => attribute is AutoValidateAntiforgeryTokenAttribute);
    }

    private static JobVacancy CreateVacancy(
        string externalId,
        string title,
        string description,
        string url = "https://example.test/jobs/1") => new()
        {
            ExternalId = externalId,
            Source = "Test",
            Title = title,
            Company = "Example",
            Location = "Remote",
            IsRemote = true,
            Description = description,
            Url = url,
            PublishedAt = DateTimeOffset.Parse("2026-11-10T10:00:00Z")
        };

    private sealed class PageHarness
    {
        public const string ProfileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string ModelName = "qwen3:1.7b";
        public const string PromptVersion = "job-match-simple-v4";

        public PageHarness(int configuredMaximumPages = 3)
        {
            var rankedJobs = new GetRankedJobsUseCase(RankedQuery, Profile, Matching);
            var importJobs = new ImportJobsUseCase(
                Provider,
                Vacancies,
                NullLogger<ImportJobsUseCase>.Instance);
            var evaluateJob = new EvaluateJobUseCase(
                Vacancies,
                Evaluations,
                Profile,
                Matching,
                NullLogger<EvaluateJobUseCase>.Instance);
            var evaluateBatch = new EvaluateJobsBatchUseCase(
                Vacancies,
                Evaluations,
                Profile,
                Matching,
                evaluateJob,
                NullLogger<EvaluateJobsBatchUseCase>.Instance);

            Model = new IndexModel(
                rankedJobs,
                importJobs,
                evaluateBatch,
                Options.Create(new ArbeitnowOptions { MaxPages = configuredMaximumPages }),
                NullLogger<IndexModel>.Instance)
            {
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext(),
                    ViewData = new ViewDataDictionary(
                        new EmptyModelMetadataProvider(),
                        new ModelStateDictionary())
                }
            };
        }

        public IndexModel Model { get; }
        public RecordingRankedQuery RankedQuery { get; } = new();
        public RecordingProvider Provider { get; } = new();
        public RecordingVacancyRepository Vacancies { get; } = new();
        public RecordingEvaluationRepository Evaluations { get; } = new();
        public RecordingProfileProvider Profile { get; } = new();
        public RecordingMatchingService Matching { get; } = new();
    }

    private sealed class RecordingRankedQuery : IRankedJobVacancyQuery
    {
        public int CallCount { get; private set; }
        public HashSet<Guid> CurrentVacancyIds { get; } = [];

        public Task<PagedResult<RankedJobVacancyDto>> ExecuteAsync(
            RankedJobQuery query,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var items = CurrentVacancyIds.Select(id => new RankedJobVacancyDto(
                id,
                ".NET Engineer",
                "Example",
                "Remote",
                true,
                "Test",
                "https://example.test/jobs/1",
                DateTimeOffset.Parse("2026-11-10T10:00:00Z"),
                null,
                null,
                null,
                null)).ToArray();
            return Task.FromResult(new PagedResult<RankedJobVacancyDto>(
                items,
                items.Length,
                query.Page,
                query.PageSize));
        }
    }

    private sealed class RecordingProvider : IJobProvider
    {
        public string SourceName => "Test";
        public IReadOnlyList<JobVacancy> Results { get; set; } = [];
        public Exception? Exception { get; set; }
        public JobSearchCriteria? ReceivedCriteria { get; private set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedCriteria = criteria;
            return Exception is null
                ? Task.FromResult(Results)
                : Task.FromException<IReadOnlyList<JobVacancy>>(Exception);
        }
    }

    private sealed class RecordingVacancyRepository : IJobVacancyRepository
    {
        public Dictionary<Guid, JobVacancy> Stored { get; } = [];
        public SaveJobsResult SaveResult { get; set; } = new(0, 0);

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken) => Task.FromResult(SaveResult);

        public Task<PagedResult<JobVacancy>> GetAsync(
            JobQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<JobVacancy>([], 0, query.Page, query.PageSize));

        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.GetValueOrDefault(id));
    }

    private sealed class RecordingEvaluationRepository : IJobMatchEvaluationRepository
    {
        public List<JobMatchEvaluation> Stored { get; } = [];

        public Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken)
        {
            Stored.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
            Guid jobVacancyId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobMatchEvaluation>>(
                Stored.Where(evaluation => evaluation.JobVacancyId == jobVacancyId).ToArray());

        public Task<JobMatchEvaluation?> GetSuccessfulAsync(
            Guid jobVacancyId,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken) => Task.FromResult(Stored.LastOrDefault(evaluation =>
                evaluation.JobVacancyId == jobVacancyId &&
                evaluation.ProfileHash == profileHash &&
                evaluation.ModelName == modelName &&
                evaluation.PromptVersion == promptVersion &&
                evaluation.EvaluationStatus == EvaluationStatus.Succeeded));
    }

    private sealed class RecordingProfileProvider : ICandidateProfileProvider
    {
        public int LoadCount { get; private set; }

        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                PageHarness.ProfileHash));
        }
    }

    private sealed class RecordingMatchingService : IJobMatchingService
    {
        public string ModelName => PageHarness.ModelName;
        public string PromptVersion => PageHarness.PromptVersion;
        public int CallCount { get; private set; }
        public Func<JobVacancy, JobMatchResult>? ResultFactory { get; set; }

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var result = ResultFactory?.Invoke(vacancy) ?? new JobMatchResult(
                4,
                ["C#"],
                [],
                [],
                "Good match.",
                ModelName,
                PromptVersion,
                DateTimeOffset.UtcNow);
            return Task.FromResult(result);
        }
    }
}
