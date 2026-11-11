using System.Net;
using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class HttpExperimentTests
{
    private static readonly Uri SearchUrl = new("https://www.linkedin.com/jobs/search/?keywords=dotnet");

    [Fact]
    public async Task RunAsync_reports_status_and_extracted_listings_from_mocked_response()
    {
        var html = ReadFixture("public-results.html");
        using var client = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, html));
        var experiment = new HttpExperiment(
            client,
            new LinkedInHtmlParser(),
            new AccessRestrictionDetector());

        var report = await experiment.RunAsync(SearchUrl, 3, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, report.HttpStatus);
        Assert.True(report.PageAccessible);
        Assert.True(report.ListingsVisible);
        Assert.Equal(2, report.IdentifiableVacancies);
        Assert.Equal(0, report.UsableDescriptions);
        Assert.Contains(report.Limitations, value => value.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_stops_when_mocked_response_contains_verification_page()
    {
        var html = ReadFixture("verification.html");
        using var client = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, html));
        var experiment = new HttpExperiment(
            client,
            new LinkedInHtmlParser(),
            new AccessRestrictionDetector());

        var report = await experiment.RunAsync(SearchUrl, 3, CancellationToken.None);

        Assert.False(report.PageAccessible);
        Assert.True(report.Restriction.IsBlocked);
        Assert.Empty(report.Jobs);
    }

    [Fact]
    public async Task RunAsync_reports_unsuccessful_http_status()
    {
        using var client = new HttpClient(
            new StubHttpMessageHandler(HttpStatusCode.TooManyRequests, "<html></html>"));
        var experiment = new HttpExperiment(
            client,
            new LinkedInHtmlParser(),
            new AccessRestrictionDetector());

        var report = await experiment.RunAsync(SearchUrl, 3, CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, report.HttpStatus);
        Assert.False(report.PageAccessible);
        Assert.Empty(report.Jobs);
        Assert.Contains(report.Limitations, value => value.Contains("HTTP status", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body),
                RequestMessage = request
            });
    }
}
