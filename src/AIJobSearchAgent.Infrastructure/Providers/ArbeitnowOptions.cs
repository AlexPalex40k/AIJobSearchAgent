using System.ComponentModel.DataAnnotations;

namespace AIJobSearchAgent.Infrastructure.Providers;

public sealed class ArbeitnowOptions
{
    public const string SectionName = "JobProviders:Arbeitnow";

    [Required, Url]
    public string BaseUrl { get; init; } = "https://www.arbeitnow.com";

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;

    [Range(1, 10)]
    public int MaxPages { get; init; } = 3;
}
