using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class LinkedInHtmlParserTests
{
    private static readonly Uri SearchUrl = new("https://www.linkedin.com/jobs/search/?keywords=dotnet");
    private readonly LinkedInHtmlParser parser = new();

    [Fact]
    public void Parse_extracts_public_job_cards_and_respects_maximum()
    {
        var jobs = parser.Parse(ReadFixture("public-results.html"), SearchUrl, 1);

        var job = Assert.Single(jobs);
        Assert.Equal("Senior .NET Engineer", job.Title);
        Assert.Equal("Example Systems", job.Company);
        Assert.Equal("Budapest, Hungary", job.Location);
        Assert.Equal("https://www.linkedin.com/jobs/view/123?trackingId=public", job.Url?.ToString());
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), job.PublishedAt);
        Assert.Null(job.Description);
        Assert.Contains("description", job.MissingFields);
    }

    [Fact]
    public void Parse_reports_missing_description_and_malformed_link()
    {
        var jobs = parser.Parse(ReadFixture("public-results.html"), SearchUrl, 3);

        Assert.Equal(2, jobs.Count);
        var malformed = jobs[1];
        Assert.Equal("Platform Engineer", malformed.Title);
        Assert.Null(malformed.Url);
        Assert.Null(malformed.Description);
        Assert.Contains("url", malformed.MissingFields);
        Assert.Contains("description", malformed.MissingFields);
    }

    [Fact]
    public void Parse_extracts_job_posting_structured_metadata()
    {
        var jobs = parser.Parse(ReadFixture("structured-job.html"), SearchUrl, 3);

        var job = Assert.Single(jobs);
        Assert.Equal("Backend Developer", job.Title);
        Assert.Equal("Example Software", job.Company);
        Assert.Equal("Budapest, HU", job.Location);
        Assert.Equal("https://www.linkedin.com/jobs/view/456", job.Url?.ToString());
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 8, 30, 0, TimeSpan.Zero), job.PublishedAt);
        Assert.Equal("Build & maintain APIs with C#.", job.Description);
        Assert.Empty(job.MissingFields);
    }

    [Fact]
    public void Parse_returns_empty_for_empty_search_results()
    {
        var jobs = parser.Parse(ReadFixture("empty-results.html"), SearchUrl, 3);

        Assert.Empty(jobs);
    }

    [Fact]
    public void Parse_preserves_eight_identifiable_cards_despite_login_popup_and_missing_descriptions()
    {
        var html = ReadFixture("eight-jobs-with-login-popup.html");

        var jobs = parser.Parse(html, SearchUrl, 8);

        Assert.Equal(8, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.NotNull(job.Title);
            Assert.NotNull(job.Company);
            Assert.NotNull(job.Location);
            Assert.NotNull(job.Url);
            Assert.NotNull(job.PublishedAt);
            Assert.Null(job.Description);
            Assert.Contains("description", job.MissingFields);
        });
        Assert.True(SeleniumLoginPopupHandler.IsLoginPromptText("Sign in to see more jobs. Join LinkedIn."));
    }

    [Fact]
    public void Parse_limits_extraction_to_ten_unique_vacancies()
    {
        var cards = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 12).Select(index => $$"""
                <li><div class="base-search-card">
                  <a class="base-card__full-link" href="/jobs/view/{{1000 + index}}">
                    <h3 class="base-search-card__title">Engineer {{index}}</h3>
                  </a>
                  <h4 class="base-search-card__subtitle">Company {{index}}</h4>
                  <span class="job-search-card__location">Remote</span>
                </div></li>
                """));
        var html = $"<ul class='jobs-search__results-list'>{cards}</ul>";

        var jobs = parser.Parse(html, SearchUrl, 10);

        Assert.Equal(10, jobs.Count);
        Assert.Equal(10, jobs.Select(job => job.Url).Distinct().Count());
    }

    [Fact]
    public void Parse_returns_all_identifiable_vacancies_when_fewer_than_ten_exist()
    {
        var jobs = parser.Parse(ReadFixture("public-results.html"), SearchUrl, 10);

        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public void Parse_deduplicates_tracking_urls_by_linkedin_job_id()
    {
        const string html = """
            <ul class="jobs-search__results-list">
              <li><div class="base-search-card">
                <a class="base-card__full-link" href="/jobs/view/backend-engineer-12345?trackingId=first">
                  <h3 class="base-search-card__title">Backend Engineer</h3>
                </a>
                <h4 class="base-search-card__subtitle">Example One</h4>
              </div></li>
              <li><div class="base-search-card">
                <a class="base-card__full-link" href="/jobs/view/software-engineer-12345?trackingId=second">
                  <h3 class="base-search-card__title">Software Engineer</h3>
                </a>
                <h4 class="base-search-card__subtitle">Example Two</h4>
              </div></li>
            </ul>
            """;

        var jobs = parser.Parse(html, SearchUrl, 10);

        Assert.Single(jobs);
        Assert.Contains("trackingId=first", jobs[0].Url?.Query);
    }

    [Fact]
    public void Parse_falls_back_to_normalized_url_for_deduplication()
    {
        const string html = """
            <ul class="jobs-search__results-list">
              <li><div class="base-search-card">
                <a class="base-card__full-link" href="/jobs/view/backend-role?trackingId=first">
                  <h3 class="base-search-card__title">Backend Engineer</h3>
                </a>
                <h4 class="base-search-card__subtitle">Example</h4>
              </div></li>
              <li><div class="base-search-card">
                <a class="base-card__full-link" href="/jobs/view/backend-role?trackingId=second">
                  <h3 class="base-search-card__title">Backend Engineer</h3>
                </a>
                <h4 class="base-search-card__subtitle">Example</h4>
              </div></li>
            </ul>
            """;

        var jobs = parser.Parse(html, SearchUrl, 10);

        Assert.Single(jobs);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
