using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Infrastructure.Providers;

internal static class LinkedInSearchUrlBuilder
{
    public const int MaximumJobs = 10;
    public const int MaximumTextLength = 100;

    public static Uri Build(LinkedInJobSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ValidateRequiredText(criteria.JobTitle, nameof(criteria.JobTitle));
        ValidateRequiredText(criteria.Location, nameof(criteria.Location));
        if (criteria.MaxJobs is < 1 or > MaximumJobs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria.MaxJobs),
                $"Maximum jobs must be between 1 and {MaximumJobs}.");
        }

        if (criteria.PublishedWithinDays is not (null or 1 or 7))
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria.PublishedWithinDays),
                "Publication period must be 1 day, 7 days, or omitted.");
        }

        var geoId = criteria.GeoId?.Trim();
        if (geoId is not null && (geoId.Length == 0 || geoId.Length > 20 || !geoId.All(char.IsDigit)))
        {
            throw new ArgumentException("LinkedIn geoId must contain only digits.", nameof(criteria.GeoId));
        }

        var query = new List<string>
        {
            $"keywords={Encode(criteria.JobTitle.Trim())}",
            $"location={Encode(criteria.Location.Trim())}"
        };
        if (geoId is not null)
        {
            query.Add($"geoId={Encode(geoId)}");
        }

        var timeFilter = criteria.PublishedWithinDays switch
        {
            1 => "r86400",
            7 => "r604800",
            _ => null
        };
        if (timeFilter is not null)
        {
            query.Add($"f_TPR={timeFilter}");
        }

        return new Uri($"https://www.linkedin.com/jobs/search?{string.Join('&', query)}");
    }

    private static void ValidateRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        if (value.Trim().Length > MaximumTextLength)
        {
            throw new ArgumentException(
                $"The value must not exceed {MaximumTextLength} characters.",
                parameterName);
        }
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);
}
