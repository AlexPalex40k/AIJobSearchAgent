namespace AIJobSearchAgent.LinkedInSpike;

public static class SeleniumPageClassifier
{
    public static (DiagnosticStatus Status, string Reason) Classify(
        bool isStillLoading,
        int visibleJobCards,
        bool visibleBodyTextAvailable,
        AccessRestriction restriction,
        bool loginPopupDetected = false,
        bool? loginPopupClosed = null,
        int? extractedVacancies = null,
        int? vacanciesWithDescriptions = null,
        int? requestedVacancies = null)
    {
        var listingsVisible = visibleJobCards > 0 || extractedVacancies is > 0;

        if (restriction.IsBlocked && extractedVacancies is > 0)
        {
            return (
                DiagnosticStatus.PartialSuccess,
                "An active access restriction stopped further extraction, but already extracted vacancy cards were retained.");
        }

        if (restriction.IsBlocked)
        {
            return (DiagnosticStatus.Blocked, restriction.Reason ?? "An active access restriction was detected.");
        }

        if (isStillLoading && !listingsVisible)
        {
            return (DiagnosticStatus.Loading, "The document was still loading and no visible job listings were available.");
        }

        if (listingsVisible)
        {
            if (extractedVacancies == 0)
            {
                return (
                    DiagnosticStatus.Failed,
                    "Job cards are visible, but none contained enough recognizable information to extract a vacancy.");
            }

            if (extractedVacancies is > 0 &&
                vacanciesWithDescriptions is not null &&
                requestedVacancies is not null &&
                extractedVacancies >= requestedVacancies &&
                vacanciesWithDescriptions == extractedVacancies)
            {
                return (
                    DiagnosticStatus.Success,
                    loginPopupDetected
                        ? loginPopupClosed == true
                            ? "The login popup was closed and the requested vacancies and descriptions were extracted."
                            : "The requested vacancies and descriptions were extracted even though the login popup remained open."
                        : "The requested vacancies and usable descriptions were extracted without a blocking error.");
            }

            if (extractedVacancies is > 0 &&
                (requestedVacancies is null || vacanciesWithDescriptions is null))
            {
                return (
                    DiagnosticStatus.Success,
                    "Job listings are accessible and extraction can proceed.");
            }

            if (extractedVacancies is > 0)
            {
                return (
                    DiagnosticStatus.PartialSuccess,
                    loginPopupDetected && loginPopupClosed == false
                        ? "The login popup remained open; accessible cards were retained, but extraction was incomplete."
                        : extractedVacancies < requestedVacancies
                        ? "Fewer identifiable vacancies were available than requested; all available cards were retained."
                        : "Vacancy cards were extracted, but one or more full descriptions were unavailable.");
            }

            return (DiagnosticStatus.Success, "Visible job listings were detected without active access restrictions.");
        }

        if (visibleBodyTextAvailable)
        {
            return (
                DiagnosticStatus.NoVacancies,
                "The page loaded without an active access restriction, but no visible job cards were found.");
        }

        return (
            DiagnosticStatus.Ambiguous,
            "The document finished loading, but it exposed neither visible body text nor recognizable access signals.");
    }
}
