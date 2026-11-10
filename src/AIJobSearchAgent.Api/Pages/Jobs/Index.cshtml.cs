using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIJobSearchAgent.Api.Pages.Jobs;

public sealed class IndexModel(GetRankedJobsUseCase getRankedJobs) : PageModel
{
    public const int PageSize = 20;

    public PagedResult<RankedJobVacancyDto> Jobs { get; private set; } =
        new([], 0, 1, PageSize);

    public string? Keyword { get; private set; }
    public string Sort { get; private set; } = RankedJobSortParser.DefaultValue;
    public int PageNumber { get; private set; } = 1;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < Jobs.TotalPages;

    public async Task<IActionResult> OnGetAsync(
        string? keyword,
        string? sort,
        int page = 1,
        CancellationToken cancellationToken = default)
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

        if (ModelState.TryGetValue(nameof(keyword), out var keywordState) && keywordState.Errors.Count > 0)
        {
            Jobs = new PagedResult<RankedJobVacancyDto>([], 0, PageNumber, PageSize);
            return Page();
        }

        Jobs = await getRankedJobs.ExecuteAsync(
            new RankedJobQuery(Keyword, null, PageNumber, PageSize, parsedSort),
            cancellationToken);
        return Page();
    }

    public static bool IsSafeExternalUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
        (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
}
