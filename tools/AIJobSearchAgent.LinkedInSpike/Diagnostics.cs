using System.Net;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed record JobDiagnostic(
    string? Title,
    string? Company,
    string? Location,
    Uri? Url,
    DateTimeOffset? PublishedAt,
    string? Description,
    IReadOnlyList<string> MissingFields);

public enum DiagnosticStatus
{
    Success,
    PartialSuccess,
    Blocked,
    Loading,
    Ambiguous,
    NoVacancies,
    Failed
}

public sealed record AccessRestriction(
    bool IsBlocked,
    bool LoginRequired,
    string? Reason,
    bool HtmlContainsVerificationText = false,
    bool VisibleVerificationChallenge = false)
{
    public static AccessRestriction None { get; } = new(false, false, null, false, false);
}

public sealed record SeleniumPageDiagnostics(
    Uri CurrentUrl,
    string PageTitle,
    int VisibleJobListingCount,
    bool VisibleBodyTextAvailable,
    bool HtmlContainsCaptchaRelatedText,
    bool VisibleVerificationChallenge,
    bool RealVerificationChallengeEncountered,
    bool LoginPopupDetected,
    bool? LoginPopupClosed,
    bool IsStillLoading,
    int UniqueVacanciesIdentified,
    int VacanciesSelectedForProcessing,
    int VacanciesSuccessfullyExtracted,
    string VerificationDetectionReason);

public sealed record DiagnosticReport(
    string Approach,
    DiagnosticStatus Status,
    Uri RequestedUrl,
    Uri? FinalUrl,
    bool PageAccessible,
    bool ListingsVisible,
    int IdentifiableVacancies,
    int UsableDescriptions,
    HttpStatusCode? HttpStatus,
    AccessRestriction Restriction,
    SeleniumPageDiagnostics? SeleniumDiagnostics,
    IReadOnlyList<JobDiagnostic> Jobs,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Limitations);

public static class DiagnosticReportFactory
{
    public static DiagnosticReport Create(
        string approach,
        Uri requestedUrl,
        Uri? finalUrl,
        bool pageAccessible,
        bool listingsVisible,
        HttpStatusCode? status,
        AccessRestriction restriction,
        IReadOnlyList<JobDiagnostic> jobs,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? limitations = null,
        DiagnosticStatus? diagnosticStatus = null,
        SeleniumPageDiagnostics? seleniumDiagnostics = null) =>
        new(
            approach,
            diagnosticStatus ?? InferStatus(pageAccessible, restriction, errors),
            requestedUrl,
            finalUrl,
            pageAccessible,
            listingsVisible,
            jobs.Count,
            jobs.Count(job => !string.IsNullOrWhiteSpace(job.Description)),
            status,
            restriction,
            seleniumDiagnostics,
            jobs,
            errors ?? [],
            limitations ?? []);

    private static DiagnosticStatus InferStatus(
        bool pageAccessible,
        AccessRestriction restriction,
        IReadOnlyList<string>? errors)
    {
        if (errors is { Count: > 0 })
        {
            return DiagnosticStatus.Failed;
        }

        if (restriction.IsBlocked)
        {
            return DiagnosticStatus.Blocked;
        }

        return pageAccessible ? DiagnosticStatus.Success : DiagnosticStatus.Ambiguous;
    }
}
