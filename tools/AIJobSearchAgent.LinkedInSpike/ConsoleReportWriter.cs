using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIJobSearchAgent.LinkedInSpike;

public static class ConsoleReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Write(DiagnosticReport report)
    {
        Console.WriteLine();
        Console.WriteLine("LinkedIn job search diagnostic");
        Console.WriteLine($"Approach: {report.Approach}");
        Console.WriteLine($"Status: {report.Status}");
        Console.WriteLine($"Page accessible: {YesNo(report.PageAccessible)}");
        if (report.HttpStatus is not null)
        {
            Console.WriteLine($"HTTP status: {(int)report.HttpStatus} {report.HttpStatus}");
        }

        Console.WriteLine($"Listings visible: {YesNo(report.ListingsVisible)}");
        Console.WriteLine($"Identifiable vacancies: {report.IdentifiableVacancies}");
        Console.WriteLine($"Vacancies with usable descriptions: {report.UsableDescriptions}");
        Console.WriteLine($"Vacancies without descriptions: {report.IdentifiableVacancies - report.UsableDescriptions}");
        Console.WriteLine($"Login required: {YesNo(report.Restriction.LoginRequired)}");
        Console.WriteLine($"Blocking or verification encountered: {YesNo(report.Restriction.IsBlocked)}");
        if (report.Restriction.Reason is not null)
        {
            Console.WriteLine($"Restriction: {report.Restriction.Reason}");
        }

        if (report.SeleniumDiagnostics is { } diagnostics)
        {
            Console.WriteLine($"Current URL: {diagnostics.CurrentUrl}");
            Console.WriteLine($"Page title: {diagnostics.PageTitle}");
            Console.WriteLine($"Visible job card elements: {diagnostics.VisibleJobListingCount}");
            Console.WriteLine($"Unique vacancies identified: {diagnostics.UniqueVacanciesIdentified}");
            Console.WriteLine($"Vacancies processed: {diagnostics.VacanciesSelectedForProcessing}");
            Console.WriteLine($"Vacancies extracted: {diagnostics.VacanciesSuccessfullyExtracted}");
            Console.WriteLine($"Descriptions extracted: {report.UsableDescriptions}");
            Console.WriteLine($"Descriptions unavailable: {report.IdentifiableVacancies - report.UsableDescriptions}");
            Console.WriteLine($"Visible body text available: {YesNo(diagnostics.VisibleBodyTextAvailable)}");
            Console.WriteLine($"Visible verification challenge: {YesNo(diagnostics.VisibleVerificationChallenge)}");
            Console.WriteLine($"Active verification challenge: {YesNo(diagnostics.RealVerificationChallengeEncountered)}");
            Console.WriteLine($"HTML contains CAPTCHA-related text: {YesNo(diagnostics.HtmlContainsCaptchaRelatedText)}");
            Console.WriteLine($"Login popup detected: {YesNo(diagnostics.LoginPopupDetected)}");
            Console.WriteLine($"Login popup closed: {YesNoNotApplicable(diagnostics.LoginPopupClosed)}");
            Console.WriteLine($"Page still loading: {YesNo(diagnostics.IsStillLoading)}");
            Console.WriteLine($"Reason: {diagnostics.VerificationDetectionReason}");
        }

        for (var index = 0; index < report.Jobs.Count; index++)
        {
            var job = report.Jobs[index];
            Console.WriteLine();
            Console.WriteLine($"Job {index + 1}");
            Console.WriteLine($"  Title: {job.Title ?? "[missing]"}");
            Console.WriteLine($"  Company: {job.Company ?? "[missing]"}");
            Console.WriteLine($"  Location: {job.Location ?? "[missing]"}");
            Console.WriteLine($"  URL: {job.Url?.ToString() ?? "[missing or malformed]"}");
            Console.WriteLine($"  Published: {job.PublishedAt?.ToString("O") ?? "[missing]"}");
            Console.WriteLine($"  Description: {job.Description ?? "[not publicly exposed]"}");
            if (job.MissingFields.Count > 0)
            {
                Console.WriteLine($"  Missing fields: {string.Join(", ", job.MissingFields)}");
            }
        }

        WriteList("Errors", report.Errors);
        WriteList("Limitations", report.Limitations);
    }

    public static async Task<string> WriteJsonAsync(
        DiagnosticReport report,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetFullPath("linkedin-spike-results");
        Directory.CreateDirectory(outputDirectory);
        var fileName = $"linkedin-diagnostic-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json";
        var path = Path.Combine(outputDirectory, fileName);
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    private static void WriteList(string heading, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{heading}:");
        foreach (var value in values)
        {
            Console.WriteLine($"- {value}");
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string YesNoNotApplicable(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "not applicable"
    };
}
