using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed partial class LinkedInHtmlParser
{
    public const int MaximumDiscoveryJobs = 250;

    private static readonly string[] CardSelectors =
    [
        ".jobs-search__results-list > li",
        ".base-search-card",
        ".job-search-card"
    ];

    private static readonly string[] DescriptionSelectors =
    [
        ".show-more-less-html__markup",
        ".description__text",
        ".jobs-description__content"
    ];

    public IReadOnlyList<JobDiagnostic> Parse(string html, Uri baseUri, int maxJobs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxJobs, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxJobs, MaximumDiscoveryJobs);

        var document = new HtmlParser().ParseDocument(html);
        var results = new List<JobDiagnostic>();

        AddStructuredJobs(document, baseUri, results, maxJobs);
        if (results.Count < maxJobs)
        {
            AddCardJobs(document, baseUri, results, maxJobs);
        }

        return results;
    }

    public string? ExtractDescription(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var visibleDescription = FirstText(document, DescriptionSelectors);
        if (!string.IsNullOrWhiteSpace(visibleDescription))
        {
            return visibleDescription;
        }

        foreach (var element in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (TryReadJson(element.TextContent, out var root))
            {
                var posting = FindJobPosting(root);
                if (posting is { } value && TryGetString(value, "description", out var description))
                {
                    return CleanText(description);
                }
            }
        }

        return null;
    }

    private static void AddStructuredJobs(
        IDocument document,
        Uri baseUri,
        List<JobDiagnostic> results,
        int maxJobs)
    {
        foreach (var element in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (!TryReadJson(element.TextContent, out var root))
            {
                continue;
            }

            foreach (var posting in EnumerateJobPostings(root))
            {
                var job = FromStructuredData(posting, baseUri);
                AddDistinct(results, job, maxJobs);
                if (results.Count == maxJobs)
                {
                    return;
                }
            }
        }
    }

    private static void AddCardJobs(
        IDocument document,
        Uri baseUri,
        List<JobDiagnostic> results,
        int maxJobs)
    {
        var cards = document.QuerySelectorAll(string.Join(",", CardSelectors));
        foreach (var card in cards)
        {
            var title = FirstText(card,
                ".base-search-card__title", ".job-search-card__title", "h3");
            var company = FirstText(card,
                ".base-search-card__subtitle", ".job-search-card__subtitle", "h4");
            var location = FirstText(card,
                ".job-search-card__location", ".job-result-card__location");
            var linkValue = FirstAttribute(card, "href",
                "a.base-card__full-link", "a[href*='/jobs/view/']");
            var publishedValue = FirstAttribute(card, "datetime",
                "time.job-search-card__listdate", "time.job-search-card__listdate--new", "time[datetime]");
            var description = FirstText(card, DescriptionSelectors);
            var url = ParseHttpUrl(linkValue, baseUri);

            if (title is null && company is null && url is null)
            {
                continue;
            }

            AddDistinct(results, CreateJob(
                title,
                company,
                location,
                url,
                ParsePublishedAt(publishedValue),
                description), maxJobs);
            if (results.Count == maxJobs)
            {
                return;
            }
        }
    }

    private static JobDiagnostic FromStructuredData(JsonElement posting, Uri baseUri)
    {
        TryGetString(posting, "title", out var title);
        TryGetString(posting, "description", out var description);
        TryGetString(posting, "url", out var urlValue);
        TryGetString(posting, "datePosted", out var datePosted);

        string? company = null;
        if (posting.TryGetProperty("hiringOrganization", out var organization))
        {
            TryGetString(organization, "name", out company);
        }

        var location = ReadLocation(posting);
        return CreateJob(
            CleanText(title),
            CleanText(company),
            CleanText(location),
            ParseHttpUrl(urlValue, baseUri),
            ParsePublishedAt(datePosted),
            CleanText(description));
    }

    private static string? ReadLocation(JsonElement posting)
    {
        if (!posting.TryGetProperty("jobLocation", out var location))
        {
            return null;
        }

        var firstLocation = location.ValueKind == JsonValueKind.Array
            ? location.EnumerateArray().FirstOrDefault()
            : location;
        if (firstLocation.ValueKind != JsonValueKind.Object ||
            !firstLocation.TryGetProperty("address", out var address))
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var name in new[] { "addressLocality", "addressRegion", "addressCountry" })
        {
            if (TryGetString(address, name, out var part) && !string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part);
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<JsonElement> EnumerateJobPostings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsJobPosting(element))
            {
                yield return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var posting in EnumerateJobPostings(property.Value))
                {
                    yield return posting;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var posting in EnumerateJobPostings(child))
                {
                    yield return posting;
                }
            }
        }
    }

    private static JsonElement? FindJobPosting(JsonElement element) =>
        EnumerateJobPostings(element).Cast<JsonElement?>().FirstOrDefault();

    private static bool IsJobPosting(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => type.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static bool TryReadJson(string json, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string? FirstText(IParentNode parent, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var text = CleanText(parent.QuerySelector(selector)?.TextContent);
            if (text is not null)
            {
                return text;
            }
        }

        return null;
    }

    private static string? FirstAttribute(IParentNode parent, string attributeName, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var value = parent.QuerySelector(selector)?.GetAttribute(attributeName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
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

    private static JobDiagnostic CreateJob(
        string? title,
        string? company,
        string? location,
        Uri? url,
        DateTimeOffset? publishedAt,
        string? description)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) missing.Add("title");
        if (string.IsNullOrWhiteSpace(company)) missing.Add("company");
        if (string.IsNullOrWhiteSpace(location)) missing.Add("location");
        if (url is null) missing.Add("url");
        if (string.IsNullOrWhiteSpace(description)) missing.Add("description");

        return new JobDiagnostic(title, company, location, url, publishedAt, description, missing);
    }

    private static DateTimeOffset? ParsePublishedAt(string? value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var publishedAt)
            ? publishedAt
            : null;

    private static void AddDistinct(List<JobDiagnostic> results, JobDiagnostic candidate, int maxJobs)
    {
        if (results.Count >= maxJobs || !IsIdentifiable(candidate))
        {
            return;
        }

        var candidateIdentity = GetIdentity(candidate);
        var alreadyPresent = results.Any(existing =>
            string.Equals(GetIdentity(existing), candidateIdentity, StringComparison.OrdinalIgnoreCase));

        if (!alreadyPresent)
        {
            results.Add(candidate);
        }
    }

    private static bool IsIdentifiable(JobDiagnostic job) =>
        job.Url is not null ||
        !string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.Company);

    internal static string GetIdentity(JobDiagnostic job)
    {
        if (job.Url is not null)
        {
            var linkedInJobId = TryGetLinkedInJobId(job.Url);
            if (linkedInJobId is not null)
            {
                return $"linkedin:{linkedInJobId}";
            }

            var normalizedUrl = new UriBuilder(job.Url)
            {
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri.GetComponents(
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.UriEscaped).TrimEnd('/');
            return $"url:{normalizedUrl}";
        }

        return $"title-company:{job.Title?.Trim()}|{job.Company?.Trim()}";
    }

    public static string? TryGetLinkedInJobId(Uri url)
    {
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                (string.Equals(parts[0], "currentJobId", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "jobId", StringComparison.OrdinalIgnoreCase)) &&
                parts[1].All(char.IsDigit))
            {
                return parts[1];
            }
        }

        var match = LinkedInJobIdRegex().Match(url.AbsolutePath.TrimEnd('/'));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withoutMarkup = HtmlTagRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(System.Net.WebUtility.HtmlDecode(withoutMarkup), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("(?:^|[-/])(\\d+)$")]
    private static partial Regex LinkedInJobIdRegex();
}
