namespace AIJobSearchAgent.LinkedInSpike;

public sealed class PopupDiagnosticTracker
{
    private readonly ICollection<string> warnings;
    private readonly HashSet<string> reportedWarnings;

    public PopupDiagnosticTracker(ICollection<string> warnings)
    {
        this.warnings = warnings;
        reportedWarnings = new HashSet<string>(warnings, StringComparer.Ordinal);
    }

    public bool Detected { get; private set; }

    public bool AllDetectedPopupsClosed { get; private set; } = true;

    public void Record(LoginPopupResult popup)
    {
        if (!popup.Detected)
        {
            return;
        }

        Detected = true;
        AllDetectedPopupsClosed &= popup.Closed;
        if (popup.Reason is not null && reportedWarnings.Add(popup.Reason))
        {
            warnings.Add(popup.Reason);
        }
    }
}
