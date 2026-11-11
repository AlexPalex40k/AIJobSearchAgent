using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed partial class LinkedInDetailsPaneParser
{
    private static readonly string[] CompanySelectors =
    [
        ".topcard__org-name-link",
        ".top-card-layout__card a[data-tracking-control-name*='company']",
        ".topcard__flavor-row .topcard__flavor"
    ];

    public DetailsPanePageSnapshot Parse(string html, Uri baseUri)
    {
        var document = new HtmlParser().ParseDocument(html);
        var activeCard = document.QuerySelector(".job-search-card--active");
        var activeCardJobId = ReadJobId(activeCard);
        var pane = document.QuerySelector(".two-pane-serp-page__detail-view");
        if (pane is null)
        {
            return new DetailsPanePageSnapshot(activeCardJobId, null);
        }

        var content = pane.QuerySelector(".details-pane__content") ?? pane;
        var linkValue = content.QuerySelector("a.topcard__link")?.GetAttribute("href");
        var jobUrl = ParseHttpUrl(linkValue, baseUri);
        var descriptionElement = content.QuerySelector(".show-more-less-html__markup");
        var snapshot = new DetailsPaneSnapshot(
            jobUrl is null ? null : LinkedInHtmlParser.TryGetLinkedInJobId(jobUrl),
            CleanInlineText(content.QuerySelector(".top-card-layout__title")?.TextContent),
            FirstText(content, CompanySelectors),
            jobUrl,
            descriptionElement is null ? null : CleanDescription(descriptionElement.InnerHtml));

        return new DetailsPanePageSnapshot(activeCardJobId, snapshot);
    }

    public DetailsPaneIdentityResult Match(
        JobDiagnostic requestedJob,
        string? selectedCardJobId,
        DetailsPaneSnapshot? details)
    {
        if (details is null)
        {
            return new DetailsPaneIdentityResult(false, false, "The right-hand details pane was not present.");
        }

        var requestedJobId = requestedJob.Url is null
            ? null
            : LinkedInHtmlParser.TryGetLinkedInJobId(requestedJob.Url);
        var expectedJobId = selectedCardJobId ?? requestedJobId;
        if (expectedJobId is not null && details.JobId is not null)
        {
            var matches = string.Equals(expectedJobId, details.JobId, StringComparison.Ordinal);
            return new DetailsPaneIdentityResult(
                matches,
                !matches,
                matches
                    ? "The selected card and details-pane job IDs match."
                    : $"The selected card job ID '{expectedJobId}' did not match details-pane job ID '{details.JobId}'.");
        }

        if (!SameText(requestedJob.Title, details.Title))
        {
            return new DetailsPaneIdentityResult(
                false,
                true,
                "The details-pane title did not match the requested vacancy.");
        }

        if (requestedJob.Company is not null &&
            details.Company is not null &&
            !SameText(requestedJob.Company, details.Company))
        {
            return new DetailsPaneIdentityResult(
                false,
                true,
                "The details-pane company did not match the requested vacancy.");
        }

        return new DetailsPaneIdentityResult(
            true,
            false,
            "Job IDs were unavailable, so the details pane was matched by title and company.");
    }

    public static string? ReadJobIdFromEntityUrn(string? entityUrn)
    {
        if (string.IsNullOrWhiteSpace(entityUrn))
        {
            return null;
        }

        var match = EntityUrnJobIdRegex().Match(entityUrn);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadJobId(IElement? card)
    {
        if (card is null)
        {
            return null;
        }

        var entityUrn = card.GetAttribute("data-entity-urn") ??
                        card.QuerySelector("[data-entity-urn]")?.GetAttribute("data-entity-urn");
        var jobId = ReadJobIdFromEntityUrn(entityUrn);
        if (jobId is not null)
        {
            return jobId;
        }

        var href = card.QuerySelector("a[href*='/jobs/view/']")?.GetAttribute("href");
        return Uri.TryCreate(new Uri("https://www.linkedin.com/"), href, out var url)
            ? LinkedInHtmlParser.TryGetLinkedInJobId(url)
            : null;
    }

    private static string? FirstText(IParentNode parent, IEnumerable<string> selectors)
    {
        foreach (var selector in selectors)
        {
            var value = CleanInlineText(parent.QuerySelector(selector)?.TextContent);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static Uri? ParseHttpUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(baseUri, value, out var url))
        {
            return null;
        }

        return url.Scheme is "http" or "https" ? url : null;
    }

    private static string? CleanDescription(string html)
    {
        var withLineBreaks = BlockEndRegex().Replace(BrRegex().Replace(html, "\n"), "\n");
        var decoded = WebUtility.HtmlDecode(TagRegex().Replace(withLineBreaks, " "));
        var lines = decoded
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanInlineText)
            .Where(line => line is not null)
            .ToArray();
        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines!);
    }

    private static string? CleanInlineText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return InlineWhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
    }

    private static bool SameText(string? left, string? right) =>
        left is not null &&
        right is not null &&
        string.Equals(CleanInlineText(left), CleanInlineText(right), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("urn:li:jobPosting:(\\d+)(?:$|[^0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex EntityUrnJobIdRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex("</(?:p|div|li|ul|ol|h[1-6])\\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("[ \\t\\f\\v]+")]
    private static partial Regex InlineWhitespaceRegex();
}

public sealed record DetailsPanePageSnapshot(
    string? ActiveCardJobId,
    DetailsPaneSnapshot? Details);

public sealed record DetailsPaneSnapshot(
    string? JobId,
    string? Title,
    string? Company,
    Uri? Url,
    string? Description);

public sealed record DetailsPaneIdentityResult(
    bool IsMatch,
    bool IsMismatch,
    string Reason);
