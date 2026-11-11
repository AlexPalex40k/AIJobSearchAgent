using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Infrastructure;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class LinkedInJobProviderTests
{
    [Theory]
    [InlineData(1, "f_TPR=r86400")]
    [InlineData(7, "f_TPR=r604800")]
    public void SearchUrlBuilder_adds_supported_publication_filter(int days, string expectedFilter)
    {
        var url = LinkedInSearchUrlBuilder.Build(
            new LinkedInJobSearchCriteria(".NET", "Serbia", days, 10));

        Assert.Equal("https", url.Scheme);
        Assert.Equal("www.linkedin.com", url.Host);
        Assert.Equal("/jobs/search", url.AbsolutePath);
        Assert.Contains("keywords=.NET", url.Query, StringComparison.Ordinal);
        Assert.Contains("location=Serbia", url.Query, StringComparison.Ordinal);
        Assert.Contains(expectedFilter, url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchUrlBuilder_encodes_location_and_optional_geo_id()
    {
        var url = LinkedInSearchUrlBuilder.Build(
            new LinkedInJobSearchCriteria("C# backend", "Novi Sad, Serbia", null, 5, "102713980"));

        Assert.Contains("keywords=C%23%20backend", url.Query, StringComparison.Ordinal);
        Assert.Contains("location=Novi%20Sad%2C%20Serbia", url.Query, StringComparison.Ordinal);
        Assert.Contains("geoId=102713980", url.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("f_TPR", url.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Serbia", null, 10)]
    [InlineData(".NET", " ", null, 10)]
    [InlineData(".NET", "Serbia", 30, 10)]
    [InlineData(".NET", "Serbia", null, 0)]
    [InlineData(".NET", "Serbia", null, 11)]
    public void SearchUrlBuilder_rejects_invalid_criteria(
        string jobTitle,
        string location,
        int? publishedWithinDays,
        int maxJobs)
    {
        Assert.ThrowsAny<ArgumentException>(() => LinkedInSearchUrlBuilder.Build(
            new LinkedInJobSearchCriteria(jobTitle, location, publishedWithinDays, maxJobs)));
    }

    [Fact]
    public void Map_uses_stable_id_and_canonical_url_and_preserves_missing_description()
    {
        var createdAt = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var scraped = new LinkedInScrapedVacancy(
            " Senior Engineer ",
            " Example ",
            " Belgrade ",
            new Uri("https://www.linkedin.com/jobs/view/senior-engineer-4487735427?trackingId=abc"),
            new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero),
            null);

        var vacancy = LinkedInJobProvider.Map(scraped, createdAt);

        Assert.NotNull(vacancy);
        Assert.Equal("4487735427", vacancy.ExternalId);
        Assert.Equal("LinkedIn", vacancy.Source);
        Assert.Equal("Senior Engineer", vacancy.Title);
        Assert.Equal("Example", vacancy.Company);
        Assert.Equal("Belgrade", vacancy.Location);
        Assert.False(vacancy.IsRemote);
        Assert.Equal(string.Empty, vacancy.Description);
        Assert.Equal("https://www.linkedin.com/jobs/view/4487735427", vacancy.Url);
        Assert.Equal(createdAt, vacancy.CreatedAt);
    }

    [Fact]
    public async Task SearchWithDiagnosticsAsync_removes_duplicates_and_returns_partial_results()
    {
        var first = CreateScrapedVacancy("4487735427", "First", description: null);
        var duplicate = CreateScrapedVacancy("4487735427", "Duplicate", "Description");
        var withoutStableId = new LinkedInScrapedVacancy(
            "No ID", "Example", "Remote", new Uri("https://www.linkedin.com/jobs/view/no-id"), null, null);
        var scraper = new StubScraper(new LinkedInScrapeResult(
            [first, duplicate, withoutStableId],
            IsPartial: false,
            IsBlocked: false,
            LoginRequired: true,
            RestrictionReason: "Login restricted some descriptions.",
            Limitations: ["One description was unavailable."]));
        var provider = CreateProvider(scraper);

        var result = await provider.SearchWithDiagnosticsAsync(
            new LinkedInJobSearchCriteria(".NET", "Serbia", 7, 10),
            CancellationToken.None);

        var vacancy = Assert.Single(result.Vacancies);
        Assert.Equal("4487735427", vacancy.ExternalId);
        Assert.Equal(string.Empty, vacancy.Description);
        Assert.True(result.IsPartial);
        Assert.True(result.LoginRequired);
        Assert.False(result.IsBlocked);
        Assert.Contains(result.Limitations, warning => warning.Contains("stable job ID", StringComparison.Ordinal));
        Assert.Equal(10, scraper.ReceivedMaxJobs);
        Assert.Contains("f_TPR=r604800", scraper.ReceivedUrl?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_adapts_existing_provider_criteria()
    {
        var scraper = new StubScraper(new LinkedInScrapeResult(
            [CreateScrapedVacancy("123456", ".NET Engineer", "Build APIs")],
            false,
            false,
            false,
            null,
            []));
        IJobProvider provider = CreateProvider(scraper);

        var vacancies = await provider.SearchAsync(
            new JobSearchCriteria([".NET", "backend"], 1, "Budapest", 1, 3),
            CancellationToken.None);

        Assert.Single(vacancies);
        Assert.Contains("keywords=.NET%20backend", scraper.ReceivedUrl?.Query, StringComparison.Ordinal);
        Assert.Equal(3, scraper.ReceivedMaxJobs);
    }

    [Fact]
    public async Task SearchWithDiagnosticsAsync_propagates_cancellation()
    {
        var scraper = new StubScraper(new LinkedInScrapeResult([], false, false, false, null, []));
        var provider = CreateProvider(scraper);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.SearchWithDiagnosticsAsync(
            new LinkedInJobSearchCriteria(".NET", "Serbia", null, 1),
            cancellation.Token));

        Assert.Equal(cancellation.Token, scraper.ReceivedCancellationToken);
    }

    [Fact]
    public void DependencyInjection_keeps_arbeitnow_default_and_registers_linkedin_independently()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddInfrastructure(
            configuration,
            Path.Combine(Path.GetTempPath(), $"linkedin-di-{Guid.NewGuid():N}.db"),
            Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ArbeitnowJobProvider>(scope.ServiceProvider.GetRequiredService<IJobProvider>());
        Assert.IsType<LinkedInJobProvider>(scope.ServiceProvider.GetRequiredService<ILinkedInJobProvider>());
    }

    private static LinkedInJobProvider CreateProvider(ILinkedInSearchScraper scraper) =>
        new(scraper, NullLogger<LinkedInJobProvider>.Instance);

    private static LinkedInScrapedVacancy CreateScrapedVacancy(
        string id,
        string title,
        string? description) =>
        new(
            title,
            "Example",
            "Remote",
            new Uri($"https://www.linkedin.com/jobs/view/{id}?trackingId=test"),
            null,
            description);

    private sealed class StubScraper(LinkedInScrapeResult result) : ILinkedInSearchScraper
    {
        public Uri? ReceivedUrl { get; private set; }

        public int ReceivedMaxJobs { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<LinkedInScrapeResult> SearchAsync(
            Uri searchUrl,
            int maxJobs,
            CancellationToken cancellationToken)
        {
            ReceivedUrl = searchUrl;
            ReceivedMaxJobs = maxJobs;
            ReceivedCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
