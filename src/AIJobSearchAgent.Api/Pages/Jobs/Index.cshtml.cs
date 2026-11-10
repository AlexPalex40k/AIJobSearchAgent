using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Api.Pages.Jobs;

[AutoValidateAntiforgeryToken]
public sealed class IndexModel(
    GetRankedJobsUseCase getRankedJobs,
    ImportAndEvaluateJobsUseCase importAndEvaluateJobs,
    EvaluateJobsBatchUseCase evaluateJobsBatch,
    IOptions<ArbeitnowOptions> arbeitnowOptions,
    ILogger<IndexModel> logger) : PageModel
{
    public const int PageSize = 20;
    public const int MaximumImportKeywords = 20;
    public const int MaximumImportKeywordLength = 100;

    public PagedResult<RankedJobVacancyDto> Jobs { get; private set; } =
        new([], 0, 1, PageSize);

    public string? Keyword { get; private set; }
    public string Sort { get; private set; } = RankedJobSortParser.DefaultValue;
    public int PageNumber { get; private set; } = 1;
    public int MaximumImportPages => arbeitnowOptions.Value.MaxPages;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < Jobs.TotalPages;

    [BindProperty]
    public ImportInput ImportForm { get; set; } = new()
    {
        MaximumPages = Math.Min(2, arbeitnowOptions.Value.MaxPages)
    };

    [BindProperty]
    public List<Guid> SelectedJobIds { get; set; } = [];

    [TempData]
    public string? ImportSummaryMessage { get; set; }

    [TempData]
    public string? EvaluationSummaryMessage { get; set; }

    public string? ImportErrorMessage { get; private set; }
    public string? EvaluationErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? keyword,
        string? sort,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        await LoadJobsAsync(keyword, sort, page, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(
        string? keyword,
        string? sort,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var keywords = ParseImportKeywords(ImportForm.Keywords);
        ValidateImport(keywords);
        if (!IsFieldValid(nameof(ImportForm)))
        {
            await LoadJobsAsync(keyword, sort, page, cancellationToken);
            return Page();
        }

        try
        {
            var summary = await importAndEvaluateJobs.ExecuteAsync(
                new JobSearchCriteria(keywords, ImportForm.MaximumPages),
                cancellationToken);
            ImportSummaryMessage =
                $"Import completed: fetched {summary.Import.TotalFetched}, matched {summary.Import.TotalMatched}, " +
                $"inserted {summary.Import.Inserted}, duplicates {summary.Import.SkippedDuplicates}, " +
                $"invalid {summary.Import.RejectedInvalid}. " +
                $"Evaluation completed: {summary.SuccessfullyEvaluatedCount} succeeded, " +
                $"{summary.SkippedCount} skipped, {summary.FailedCount} failed.";
            return RedirectToJobs(keyword, sort, page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JobProviderException exception)
        {
            logger.LogWarning(exception, "Job import from the jobs page failed because the provider was unavailable");
            ImportErrorMessage = "Arbeitnow is currently unavailable. Please try the import again later.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Job import from the jobs page failed unexpectedly");
            ImportErrorMessage = "The import could not be completed. Please try again.";
        }

        await LoadJobsAsync(keyword, sort, page, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostEvaluateSelectedAsync(
        string? keyword,
        string? sort,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        ValidateSelection();
        if (!IsFieldValid(nameof(SelectedJobIds)))
        {
            await LoadJobsAsync(keyword, sort, page, cancellationToken);
            return Page();
        }

        var distinctIds = SelectedJobIds.Distinct().ToArray();
        await LoadJobsAsync(keyword, sort, page, cancellationToken);
        var currentPageIds = Jobs.Items.Select(job => job.VacancyId).ToHashSet();
        if (distinctIds.Any(id => !currentPageIds.Contains(id)))
        {
            ModelState.AddModelError(
                nameof(SelectedJobIds),
                "Select only vacancies shown on the current page.");
            return Page();
        }

        try
        {
            var summary = await evaluateJobsBatch.ExecuteAsync(distinctIds, cancellationToken);
            EvaluationSummaryMessage =
                $"Evaluation completed: requested {summary.TotalRequestedVacancies}, " +
                $"succeeded {summary.SuccessfullyEvaluatedCount}, skipped {summary.SkippedCount}, " +
                $"failed {summary.FailedCount}.";
            return RedirectToJobs(keyword, sort, page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CandidateProfileException exception)
        {
            logger.LogWarning(exception, "Batch evaluation from the jobs page could not load the candidate profile");
            EvaluationErrorMessage = "The candidate profile could not be loaded. Check the local profile file and try again.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Batch evaluation from the jobs page failed unexpectedly");
            EvaluationErrorMessage = "The selected vacancies could not be evaluated. Please try again.";
        }

        return Page();
    }

    public static bool IsSafeExternalUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
        (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);

    private async Task LoadJobsAsync(
        string? keyword,
        string? sort,
        int page,
        CancellationToken cancellationToken)
    {
        Keyword = keyword;
        PageNumber = Math.Max(page, 1);
        Sort = string.IsNullOrWhiteSpace(sort) ? RankedJobSortParser.DefaultValue : sort.Trim().ToLowerInvariant();

        if (!RankedJobSortParser.TryParse(Sort, out var parsedSort))
        {
            ModelState.AddModelError(nameof(sort), "Choose rating descending, rating ascending, or newest first.");
            Sort = RankedJobSortParser.DefaultValue;
            parsedSort = RankedJobSort.RatingDescending;
        }

        if (keyword?.Length > KeywordMatcher.MaximumSearchQueryLength)
        {
            ModelState.AddModelError(
                nameof(keyword),
                $"Search must be at most {KeywordMatcher.MaximumSearchQueryLength} characters.");
        }
        else if (KeywordMatcher.TokenizeSearchQuery(keyword).Count > KeywordMatcher.MaximumSearchTokenCount)
        {
            ModelState.AddModelError(
                nameof(keyword),
                $"Search must contain at most {KeywordMatcher.MaximumSearchTokenCount} terms.");
        }

        if (!IsFieldValid(nameof(keyword)))
        {
            Jobs = new PagedResult<RankedJobVacancyDto>([], 0, PageNumber, PageSize);
            return;
        }

        Jobs = await getRankedJobs.ExecuteAsync(
            new RankedJobQuery(Keyword, null, PageNumber, PageSize, parsedSort),
            cancellationToken);
    }

    private static IReadOnlyList<string> ParseImportKeywords(string? value) => KeywordMatcher.NormalizeKeywords(
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void ValidateImport(IReadOnlyList<string> keywords)
    {
        var keywordKey = $"{nameof(ImportForm)}.{nameof(ImportInput.Keywords)}";
        var pageKey = $"{nameof(ImportForm)}.{nameof(ImportInput.MaximumPages)}";

        if (keywords.Count == 0)
        {
            ModelState.AddModelError(keywordKey, "Enter at least one keyword before importing.");
        }
        else if (keywords.Count > MaximumImportKeywords)
        {
            ModelState.AddModelError(keywordKey, $"Enter no more than {MaximumImportKeywords} keywords.");
        }
        else if (keywords.Any(keyword => keyword.Length > MaximumImportKeywordLength))
        {
            ModelState.AddModelError(
                keywordKey,
                $"Each keyword must be at most {MaximumImportKeywordLength} characters.");
        }

        if (ImportForm.MaximumPages < 1 || ImportForm.MaximumPages > MaximumImportPages)
        {
            ModelState.AddModelError(
                pageKey,
                $"Maximum pages must be between 1 and {MaximumImportPages}.");
        }
    }

    private void ValidateSelection()
    {
        if (SelectedJobIds.Count == 0)
        {
            ModelState.AddModelError(nameof(SelectedJobIds), "Select at least one vacancy to evaluate.");
        }
        else if (SelectedJobIds.Count > EvaluateJobsBatchUseCase.MaximumBatchSize)
        {
            ModelState.AddModelError(
                nameof(SelectedJobIds),
                $"Select no more than {EvaluateJobsBatchUseCase.MaximumBatchSize} vacancies.");
        }
        else if (SelectedJobIds.Any(id => id == Guid.Empty))
        {
            ModelState.AddModelError(nameof(SelectedJobIds), "One or more selected vacancy IDs are invalid.");
        }
    }

    private bool IsFieldValid(string key) => ModelState
        .Where(entry =>
            entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            entry.Key.StartsWith($"{key}.", StringComparison.OrdinalIgnoreCase))
        .All(entry => entry.Value?.Errors.Count is null or 0);

    private RedirectToPageResult RedirectToJobs(string? keyword, string? sort, int page) => RedirectToPage(
        "/Jobs/Index",
        new
        {
            keyword,
            sort = RankedJobSortParser.TryParse(sort, out _) ? sort : RankedJobSortParser.DefaultValue,
            page = Math.Max(page, 1)
        });

    public sealed class ImportInput
    {
        public string? Keywords { get; set; }
        public int MaximumPages { get; set; } = 2;
    }
}
