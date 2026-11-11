using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Parse_accepts_required_arguments_and_defaults_max_jobs()
    {
        var result = CommandLineOptions.Parse(
        [
            "--mode", "http",
            "--url", "https://www.linkedin.com/jobs/search/?keywords=dotnet"
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiagnosticMode.Http, result.Options?.Mode);
        Assert.Equal(10, result.Options?.MaxJobs);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("many")]
    public void Parse_rejects_invalid_max_jobs(string value)
    {
        var result = ParseWith("--max-jobs", value);

        Assert.False(result.IsSuccess);
        Assert.Contains("1 through 10", result.Error);
    }

    [Fact]
    public void Parse_accepts_eight_jobs_for_a_bounded_multi_card_diagnostic()
    {
        var result = ParseWith("--max-jobs", "8");

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Options?.MaxJobs);
    }

    [Theory]
    [InlineData("http://www.linkedin.com/jobs/search")]
    [InlineData("https://example.com/jobs/search")]
    [InlineData("https://www.linkedin.com/feed")]
    [InlineData("not-a-url")]
    public void Parse_rejects_non_public_linkedin_jobs_urls(string value)
    {
        var result = CommandLineOptions.Parse(["--mode", "http", "--url", value]);

        Assert.False(result.IsSuccess);
        Assert.Contains("linkedin.com/jobs/search", result.Error);
    }

    [Fact]
    public void Parse_rejects_browser_binary_in_http_mode()
    {
        var result = ParseWith("--browser-binary", "missing-chrome.exe");

        Assert.False(result.IsSuccess);
        Assert.Contains("selenium mode", result.Error);
    }

    [Fact]
    public void Parse_rejects_unknown_arguments()
    {
        var result = ParseWith("--pages", "2");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown argument", result.Error);
    }

    [Fact]
    public void Parse_accepts_headless_for_selenium_mode()
    {
        var result = CommandLineOptions.Parse(
        [
            "--mode", "selenium",
            "--url", "https://www.linkedin.com/jobs/search",
            "--headless"
        ]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Options?.Headless);
    }

    [Fact]
    public void Parse_rejects_headless_for_http_mode()
    {
        var result = CommandLineOptions.Parse(
        [
            "--mode", "http",
            "--url", "https://www.linkedin.com/jobs/search",
            "--headless"
        ]);

        Assert.False(result.IsSuccess);
        Assert.Contains("selenium mode", result.Error);
    }

    private static CommandLineParseResult ParseWith(string name, string value) =>
        CommandLineOptions.Parse(
        [
            "--mode", "http",
            "--url", "https://www.linkedin.com/jobs/search",
            name, value
        ]);
}
