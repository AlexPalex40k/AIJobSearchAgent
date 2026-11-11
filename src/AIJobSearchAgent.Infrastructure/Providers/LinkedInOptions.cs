namespace AIJobSearchAgent.Infrastructure.Providers;

public sealed class LinkedInOptions
{
    public const string SectionName = "JobProviders:LinkedIn";

    public string? BrowserBinaryPath { get; init; }

    public bool Headless { get; init; }
}
