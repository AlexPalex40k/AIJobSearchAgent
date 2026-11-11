using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class PopupDiagnosticTrackerTests
{
    private const string PersistentPopupWarning =
        "A visible login popup could not be closed; extraction continued for readable job cards.";

    [Fact]
    public void Record_adds_repeated_identical_popup_warning_only_once()
    {
        var warnings = new List<string>();
        var tracker = new PopupDiagnosticTracker(warnings);
        var popup = new LoginPopupResult(true, false, PersistentPopupWarning);

        tracker.Record(popup);
        tracker.Record(popup);
        tracker.Record(popup);

        Assert.Equal([PersistentPopupWarning], warnings);
    }

    [Fact]
    public void Record_preserves_existing_search_level_warning_without_listing_prefix()
    {
        var warnings = new List<string> { PersistentPopupWarning };
        var tracker = new PopupDiagnosticTracker(warnings);

        tracker.Record(new LoginPopupResult(true, false, PersistentPopupWarning));

        var warning = Assert.Single(warnings);
        Assert.Equal(PersistentPopupWarning, warning);
        Assert.False(warning.StartsWith("Listing", StringComparison.Ordinal));
    }

    [Fact]
    public void Record_preserves_distinct_warnings_and_popup_flags()
    {
        var warnings = new List<string>
        {
            "Listing 2: the matching details pane exposed no usable full description; the vacancy was retained."
        };
        var tracker = new PopupDiagnosticTracker(warnings);

        tracker.Record(new LoginPopupResult(
            true,
            true,
            "The visible login popup was closed using its close button."));
        tracker.Record(new LoginPopupResult(true, false, PersistentPopupWarning));

        Assert.True(tracker.Detected);
        Assert.False(tracker.AllDetectedPopupsClosed);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, warning => warning.StartsWith("Listing 2:", StringComparison.Ordinal));
        Assert.Contains("The visible login popup was closed using its close button.", warnings);
        Assert.Contains(PersistentPopupWarning, warnings);
    }

    [Fact]
    public void Record_ignores_not_detected_result_without_changing_diagnostics()
    {
        var warnings = new List<string>();
        var tracker = new PopupDiagnosticTracker(warnings);

        tracker.Record(LoginPopupResult.NotDetected);

        Assert.False(tracker.Detected);
        Assert.True(tracker.AllDetectedPopupsClosed);
        Assert.Empty(warnings);
    }
}
