using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class SeleniumPageClassifierTests
{
    [Fact]
    public void Classify_does_not_downgrade_for_inactive_captcha_markup()
    {
        var restriction = new AccessRestriction(
            false,
            false,
            "CAPTCHA-related markup was found, but no active visible challenge was detected.",
            HtmlContainsVerificationText: true,
            VisibleVerificationChallenge: false);

        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 12,
            visibleBodyTextAvailable: true,
            restriction);

        Assert.Equal(DiagnosticStatus.Success, result.Status);
    }

    [Fact]
    public void Classify_reports_blocked_when_active_challenge_prevents_extraction()
    {
        var restriction = new AccessRestriction(
            true,
            false,
            "A displayed CAPTCHA is present.",
            HtmlContainsVerificationText: true,
            VisibleVerificationChallenge: true);

        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 2,
            visibleBodyTextAvailable: true,
            restriction);

        Assert.Equal(DiagnosticStatus.Blocked, result.Status);
    }

    [Fact]
    public void Classify_reports_blocked_for_visible_challenge_without_listings()
    {
        var restriction = new AccessRestriction(
            true,
            false,
            "Verification challenge text is visible.",
            HtmlContainsVerificationText: true,
            VisibleVerificationChallenge: true);

        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 0,
            visibleBodyTextAvailable: true,
            restriction);

        Assert.Equal(DiagnosticStatus.Blocked, result.Status);
    }

    [Fact]
    public void Classify_reports_loading_when_document_is_incomplete_without_content()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: true,
            visibleJobCards: 0,
            visibleBodyTextAvailable: false,
            AccessRestriction.None);

        Assert.Equal(DiagnosticStatus.Loading, result.Status);
    }

    [Fact]
    public void Classify_reports_accessible_when_login_popup_was_closed_and_descriptions_are_available()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 8,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            loginPopupDetected: true,
            loginPopupClosed: true,
            extractedVacancies: 8,
            vacanciesWithDescriptions: 8,
            requestedVacancies: 8);

        Assert.Equal(DiagnosticStatus.Success, result.Status);
        Assert.Contains("popup was closed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_reports_partial_when_popup_cannot_close_but_cards_are_readable()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 8,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            loginPopupDetected: true,
            loginPopupClosed: false,
            extractedVacancies: 8,
            vacanciesWithDescriptions: 0,
            requestedVacancies: 8);

        Assert.Equal(DiagnosticStatus.PartialSuccess, result.Status);
        Assert.Contains("remained open", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_reports_partial_when_cards_lack_descriptions()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 8,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            extractedVacancies: 8,
            vacanciesWithDescriptions: 3,
            requestedVacancies: 8);

        Assert.Equal(DiagnosticStatus.PartialSuccess, result.Status);
        Assert.Contains("descriptions", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_reports_partial_when_fewer_than_ten_vacancies_are_available()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 8,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            extractedVacancies: 8,
            vacanciesWithDescriptions: 8,
            requestedVacancies: 10);

        Assert.Equal(DiagnosticStatus.PartialSuccess, result.Status);
        Assert.Contains("fewer", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_reports_success_when_popup_remains_but_ten_jobs_and_descriptions_are_extracted()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 10,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            loginPopupDetected: true,
            loginPopupClosed: false,
            extractedVacancies: 10,
            vacanciesWithDescriptions: 10,
            requestedVacancies: 10);

        Assert.Equal(DiagnosticStatus.Success, result.Status);
        Assert.Contains("remained open", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_reports_no_vacancies_for_loaded_empty_page()
    {
        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 0,
            visibleBodyTextAvailable: true,
            AccessRestriction.None,
            extractedVacancies: 0,
            vacanciesWithDescriptions: 0,
            requestedVacancies: 10);

        Assert.Equal(DiagnosticStatus.NoVacancies, result.Status);
    }

    [Fact]
    public void Classify_reports_blocked_for_full_page_authentication_wall()
    {
        var restriction = new AccessRestriction(
            true,
            true,
            "The current URL is a LinkedIn login wall.");

        var result = SeleniumPageClassifier.Classify(
            isStillLoading: false,
            visibleJobCards: 0,
            visibleBodyTextAvailable: true,
            restriction,
            extractedVacancies: 0,
            vacanciesWithDescriptions: 0);

        Assert.Equal(DiagnosticStatus.Blocked, result.Status);
    }
}
