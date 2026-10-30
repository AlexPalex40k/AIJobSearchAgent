using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure.Providers;

public sealed partial class ArbeitnowJobProvider(
    HttpClient httpClient,
    IOptions<ArbeitnowOptions> options,
    ILogger<ArbeitnowJobProvider> logger) : IJobProvider
{
    public string SourceName => "Arbeitnow";

    public async Task<IReadOnlyList<JobVacancy>> SearchAsync(
        JobSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var keywords = KeywordMatcher.NormalizeKeywords(criteria.Keywords);
        var pageLimit = Math.Min(criteria.MaxPages, options.Value.MaxPages);
        var results = new List<JobVacancy>();

        for (var page = 1; page <= pageLimit; page++)
        {
            ArbeitnowResponse? response;
            try
            {
                response = await httpClient.GetFromJsonAsync<ArbeitnowResponse>(
                    $"/api/job-board-api?page={page}",
                    cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Arbeitnow request failed for page {Page}", page);
                throw new JobProviderException("Arbeitnow could not be reached or returned an HTTP error.", exception);
            }
            catch (NotSupportedException exception)
            {
                logger.LogError(exception, "Arbeitnow returned an unsupported response for page {Page}", page);
                throw new JobProviderException("Arbeitnow returned an unsupported response.", exception);
            }
            catch (System.Text.Json.JsonException exception)
            {
                logger.LogError(exception, "Arbeitnow returned invalid JSON for page {Page}", page);
                throw new JobProviderException("Arbeitnow returned invalid JSON.", exception);
            }

            if (response?.Data is not { Count: > 0 } jobs)
            {
                break;
            }

            foreach (var job in jobs)
            {
                var mapped = Map(job);
                if (KeywordMatcher.Matches(mapped, keywords))
                {
                    results.Add(mapped);
                }
            }

            if (response.Meta is { CurrentPage: var currentPage, LastPage: var lastPage } &&
                currentPage >= lastPage)
            {
                break;
            }
        }

        logger.LogInformation("Arbeitnow returned {Count} matching vacancies", results.Count);
        return results;
    }

    internal static JobVacancy Map(ArbeitnowJob job)
    {
        var externalId = FirstNonEmpty(job.Slug, ExtractExternalId(job.Url));
        var description = ConvertHtmlToPlainText(job.Description);

        return new JobVacancy
        {
            ExternalId = externalId,
            Source = "Arbeitnow",
            Title = job.Title?.Trim() ?? string.Empty,
            Company = job.CompanyName?.Trim() ?? string.Empty,
            Location = job.Location?.Trim() ?? string.Empty,
            IsRemote = job.Remote ?? false,
            Description = description,
            Url = job.Url?.Trim() ?? string.Empty,
            PublishedAt = ParsePublishedAt(job.CreatedAt),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    internal static string ConvertHtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withLineBreaks = BlockElementRegex().Replace(html, Environment.NewLine);
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedLines = decoded
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => WhitespaceRegex().Replace(line, " "));
        return string.Join(Environment.NewLine, normalizedLines).Trim();
    }

    private static DateTimeOffset? ParsePublishedAt(System.Text.Json.JsonElement? createdAt)
    {
        if (!createdAt.HasValue)
        {
            return null;
        }

        var value = createdAt.Value;
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (value.ValueKind == System.Text.Json.JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ExtractExternalId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    [GeneratedRegex("<(?:br\\s*/?|/p|/div|/li|/h[1-6])>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElementRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("[\\t \\u00A0]+")]
    private static partial Regex WhitespaceRegex();

    internal sealed class ArbeitnowResponse
    {
        [JsonPropertyName("data")]
        public List<ArbeitnowJob>? Data { get; init; }

        [JsonPropertyName("meta")]
        public ArbeitnowMeta? Meta { get; init; }
    }

    internal sealed class ArbeitnowMeta
    {
        [JsonPropertyName("current_page")]
        public int CurrentPage { get; init; }

        [JsonPropertyName("last_page")]
        public int LastPage { get; init; }
    }

    internal sealed class ArbeitnowJob
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; init; }

        [JsonPropertyName("company_name")]
        public string? CompanyName { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("remote")]
        public bool? Remote { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("location")]
        public string? Location { get; init; }

        [JsonPropertyName("created_at")]
        public System.Text.Json.JsonElement? CreatedAt { get; init; }
    }
}
