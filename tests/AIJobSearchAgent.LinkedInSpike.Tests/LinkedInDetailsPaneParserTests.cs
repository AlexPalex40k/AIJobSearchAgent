using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class LinkedInDetailsPaneParserTests
{
    private static readonly Uri SearchUrl = new(
        "https://www.linkedin.com/jobs/search/?keywords=dotnet&currentJobId=4487735427");
    private readonly LinkedInDetailsPaneParser parser = new();

    [Fact]
    public void Parse_identifies_active_card_and_complete_details_pane_description()
    {
        var result = parser.Parse(ReadFixture("details-pane-results.html"), SearchUrl);

        Assert.Equal("4487735427", result.ActiveCardJobId);
        Assert.NotNull(result.Details);
        Assert.Equal("4487735427", result.Details.JobId);
        Assert.Equal("Senior .NET Engineer", result.Details.Title);
        Assert.Equal("Example Systems", result.Details.Company);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Build resilient services with C# and .NET.",
                "What you will do:",
                "Design APIs",
                "Review production telemetry"),
            result.Details.Description);
    }

    [Fact]
    public void Match_accepts_selected_card_when_details_job_id_matches()
    {
        var page = parser.Parse(ReadFixture("details-pane-results.html"), SearchUrl);
        var job = CreateJob("4487735427", "Senior .NET Engineer", "Example Systems");

        var result = parser.Match(job, page.ActiveCardJobId, page.Details);

        Assert.True(result.IsMatch);
        Assert.False(result.IsMismatch);
    }

    [Fact]
    public void Match_rejects_stale_details_from_previous_job()
    {
        var job = CreateJob("4487735427", "Senior .NET Engineer", "Example Systems");
        var staleDetails = new DetailsPaneSnapshot(
            "4487735426",
            "Previous Engineer",
            "Old Company",
            new Uri("https://www.linkedin.com/jobs/view/4487735426"),
            "This description belongs to the previous card.");

        var result = parser.Match(job, "4487735427", staleDetails);

        Assert.False(result.IsMatch);
        Assert.True(result.IsMismatch);
        Assert.Contains("did not match", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<html><body></body></html>")]
    [InlineData("<div class='two-pane-serp-page__detail-view'><div class='details-pane__content'><div class='show-more-less-html__markup'> </div></div></div>")]
    public void Parse_returns_no_description_for_missing_or_empty_details_pane(string html)
    {
        var result = parser.Parse(html, SearchUrl);

        Assert.Null(result.Details?.Description);
    }

    [Fact]
    public void Match_falls_back_to_title_and_company_when_ids_are_unavailable()
    {
        var job = new JobDiagnostic(
            "Platform Engineer",
            "Example",
            "Remote",
            null,
            null,
            null,
            ["url", "description"]);
        var details = new DetailsPaneSnapshot(null, "Platform Engineer", "Example", null, "Description");

        var result = parser.Match(job, null, details);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Independent_results_allow_processing_to_continue_after_a_selection_failure()
    {
        var outcomes = new[]
        {
            new PaneExtractionOutcome(SelectionFailed: true, IdentityMismatch: false, DescriptionExtracted: false),
            new PaneExtractionOutcome(SelectionFailed: false, IdentityMismatch: false, DescriptionExtracted: true)
        };

        Assert.Equal(1, outcomes.Count(outcome => outcome.SelectionFailed));
        Assert.Equal(1, outcomes.Count(outcome => outcome.DescriptionExtracted));
    }

    [Fact]
    public void Diagnostic_counters_represent_pane_extraction_without_page_navigation()
    {
        var diagnostics = new SeleniumPageDiagnostics(
            SearchUrl,
            "Jobs",
            10,
            true,
            false,
            false,
            false,
            false,
            null,
            false,
            10,
            10,
            10,
            7,
            3,
            1,
            1,
            0,
            1250,
            "Partial extraction.");

        Assert.Equal(7, diagnostics.DescriptionsExtractedFromDetailsPane);
        Assert.Equal(3, diagnostics.DescriptionsUnavailable);
        Assert.Equal(1, diagnostics.CardSelectionFailures);
        Assert.Equal(1, diagnostics.DetailPaneIdentityMismatches);
        Assert.Equal(0, diagnostics.FullPageNavigationsPerformed);
    }

    private static JobDiagnostic CreateJob(string id, string title, string company) =>
        new(
            title,
            company,
            "Remote",
            new Uri($"https://www.linkedin.com/jobs/view/{id}"),
            null,
            null,
            ["description"]);

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed record PaneExtractionOutcome(
        bool SelectionFailed,
        bool IdentityMismatch,
        bool DescriptionExtracted);
}
