using System.Net;
using System.Text;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Tests;

public sealed class ArbeitnowJobProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsResponseAndHandlesMissingFields()
    {
        const string json = """
            {
              "data": [
                {
                  "slug": "dotnet-engineer-123",
                  "company_name": "Example GmbH",
                  "title": ".NET Engineer",
                  "description": "<p>Build &amp; maintain APIs.</p><br>Work remotely.",
                  "remote": true,
                  "url": "https://www.arbeitnow.com/jobs/dotnet-engineer-123",
                  "location": "Berlin",
                  "created_at": 1704067200
                },
                {
                  "slug": "incomplete-456",
                  "url": "https://www.arbeitnow.com/jobs/incomplete-456"
                }
              ],
              "meta": { "current_page": 1, "last_page": 1 }
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(json))
        {
            BaseAddress = new Uri("https://www.arbeitnow.com")
        };
        var provider = new ArbeitnowJobProvider(
            httpClient,
            Options.Create(new ArbeitnowOptions { MaxPages = 3 }),
            NullLogger<ArbeitnowJobProvider>.Instance);

        var jobs = await provider.SearchAsync(
            new JobSearchCriteria([], 3),
            CancellationToken.None);

        Assert.Equal(2, jobs.Count);
        var mapped = jobs[0];
        Assert.Equal("dotnet-engineer-123", mapped.ExternalId);
        Assert.Equal("Example GmbH", mapped.Company);
        Assert.True(mapped.IsRemote);
        Assert.DoesNotContain('<', mapped.Description);
        Assert.Contains("Build & maintain APIs.", mapped.Description);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1704067200), mapped.PublishedAt);

        var incomplete = jobs[1];
        Assert.Equal(string.Empty, incomplete.Title);
        Assert.Equal(string.Empty, incomplete.Description);
        Assert.Null(incomplete.PublishedAt);
    }

    [Fact]
    public async Task SearchAsync_FiltersWithCaseInsensitiveOrSemantics()
    {
        const string json = """
            {
              "data": [
                { "slug": "one", "title": "Java Engineer", "description": "JVM", "company_name": "A", "location": "X", "url": "https://example.test/one" },
                { "slug": "two", "title": "Backend Engineer", "description": "Modern C#", "company_name": "B", "location": "Y", "url": "https://example.test/two" }
              ],
              "meta": { "current_page": 1, "last_page": 1 }
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(json))
        {
            BaseAddress = new Uri("https://www.arbeitnow.com")
        };
        var provider = new ArbeitnowJobProvider(
            httpClient,
            Options.Create(new ArbeitnowOptions()),
            NullLogger<ArbeitnowJobProvider>.Instance);

        var jobs = await provider.SearchAsync(
            new JobSearchCriteria(["python", "c#"], 1),
            CancellationToken.None);

        Assert.Single(jobs);
        Assert.Equal("two", jobs[0].ExternalId);
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
