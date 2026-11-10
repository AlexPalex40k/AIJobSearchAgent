namespace AIJobSearchAgent.Application.Models;

public sealed record RankedJobQuery(
    string? Keyword,
    string? Source,
    int Page,
    int PageSize,
    RankedJobSort Sort);

public enum RankedJobSort
{
    RatingDescending,
    RatingAscending,
    Newest
}

public static class RankedJobSortParser
{
    public const string DefaultValue = "rating_desc";

    public static bool TryParse(string? value, out RankedJobSort sort)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case DefaultValue:
                sort = RankedJobSort.RatingDescending;
                return true;
            case "rating_asc":
                sort = RankedJobSort.RatingAscending;
                return true;
            case "newest":
                sort = RankedJobSort.Newest;
                return true;
            default:
                sort = default;
                return false;
        }
    }
}
