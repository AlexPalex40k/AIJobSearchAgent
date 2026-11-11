using System.ComponentModel.DataAnnotations;

namespace AIJobSearchAgent.Api.Contracts;

public sealed class LinkedInSearchRequest
{
    [Required, StringLength(100)]
    public string JobTitle { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Location { get; init; } = string.Empty;

    public int? PublishedWithinDays { get; init; }

    [Range(1, 10)]
    public int MaxJobs { get; init; } = 10;

    [StringLength(20)]
    public string? GeoId { get; init; }
}
