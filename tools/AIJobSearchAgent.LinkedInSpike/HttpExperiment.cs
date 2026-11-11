using System.Net.Http.Headers;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed class HttpExperiment(
    HttpClient httpClient,
    LinkedInHtmlParser parser,
    AccessRestrictionDetector restrictionDetector)
{
    public async Task<DiagnosticReport> RunAsync(
        Uri url,
        int maxJobs,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AIJobSearchAgent-LinkedInSpike", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var finalUrl = response.RequestMessage?.RequestUri ?? url;
            var restriction = restrictionDetector.Detect(finalUrl, html);
            var jobs = restriction.IsBlocked
                ? []
                : parser.Parse(html, finalUrl, maxJobs);
            var limitations = BuildLimitations(response.IsSuccessStatusCode, restriction, jobs);

            return DiagnosticReportFactory.Create(
                "HTTP",
                url,
                finalUrl,
                response.IsSuccessStatusCode && !restriction.IsBlocked,
                jobs.Count > 0,
                response.StatusCode,
                restriction,
                jobs,
                limitations: limitations);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(url, "The HTTP request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return Failed(url, $"HTTP request failed: {SafeMessage(exception)}");
        }
    }

    private static IReadOnlyList<string> BuildLimitations(
        bool success,
        AccessRestriction restriction,
        IReadOnlyList<JobDiagnostic> jobs)
    {
        var limitations = new List<string>();
        if (!success) limitations.Add("The server did not return a successful HTTP status.");
        if (restriction.IsBlocked && restriction.Reason is not null) limitations.Add(restriction.Reason);
        if (!restriction.IsBlocked && jobs.Count == 0) limitations.Add("No recognizable public job listing markup or JobPosting metadata was found.");
        if (jobs.Any(job => job.Description is null)) limitations.Add("One or more listings did not expose a usable description in the returned HTML.");
        if (jobs.Any(job => job.Url is null)) limitations.Add("One or more listing links were missing or malformed.");
        return limitations;
    }

    private static DiagnosticReport Failed(Uri url, string error) =>
        DiagnosticReportFactory.Create(
            "HTTP",
            url,
            null,
            false,
            false,
            null,
            AccessRestriction.None,
            [],
            [error],
            ["No response content was available for inspection."]);

    private static string SafeMessage(Exception exception)
    {
        var firstLine = exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine) ? exception.GetType().Name : firstLine[..Math.Min(firstLine.Length, 300)];
    }
}
