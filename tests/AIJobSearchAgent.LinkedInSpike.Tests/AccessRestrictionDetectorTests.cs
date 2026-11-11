using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class AccessRestrictionDetectorTests
{
    private readonly AccessRestrictionDetector detector = new();

    [Fact]
    public void Detect_identifies_verification_content()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "verification.html"));

        var restriction = detector.Detect(
            new Uri("https://www.linkedin.com/jobs/search"),
            html);

        Assert.True(restriction.IsBlocked);
        Assert.False(restriction.LoginRequired);
        Assert.Contains("verification", restriction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://www.linkedin.com/login", true)]
    [InlineData("https://www.linkedin.com/authwall?trk=jobs", true)]
    [InlineData("https://www.linkedin.com/checkpoint/challenge/123", false)]
    public void Detect_identifies_restricted_destinations(string url, bool loginRequired)
    {
        var restriction = detector.Detect(new Uri(url), "<html></html>");

        Assert.True(restriction.IsBlocked);
        Assert.Equal(loginRequired, restriction.LoginRequired);
    }

    [Fact]
    public void Detect_does_not_treat_a_normal_sign_in_link_as_a_login_wall()
    {
        var restriction = detector.Detect(
            new Uri("https://www.linkedin.com/jobs/search"),
            "<nav><a href='/login'>Sign in</a></nav><main>Public jobs</main>");

        Assert.False(restriction.IsBlocked);
    }

    [Fact]
    public void DetectRenderedPage_does_not_block_for_inactive_captcha_markup()
    {
        var html = ReadFixture("job-results-with-inactive-captcha.html");

        var restriction = detector.DetectRenderedPage(
            new Uri("https://www.linkedin.com/jobs/search"),
            html,
            "Senior .NET Engineer Example Systems Budapest, Hungary",
            visibleChallengeElement: false);

        Assert.False(restriction.IsBlocked);
        Assert.True(restriction.HtmlContainsVerificationText);
        Assert.False(restriction.VisibleVerificationChallenge);
        Assert.Contains("no active visible challenge", restriction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectRenderedPage_blocks_when_verification_text_is_visible()
    {
        var html = ReadFixture("verification.html");

        var restriction = detector.DetectRenderedPage(
            new Uri("https://www.linkedin.com/jobs/search"),
            html,
            "Security verification Verify you are human",
            visibleChallengeElement: false);

        Assert.True(restriction.IsBlocked);
        Assert.True(restriction.HtmlContainsVerificationText);
        Assert.True(restriction.VisibleVerificationChallenge);
        Assert.Contains("visible", restriction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectRenderedPage_does_not_treat_a_passive_visible_captcha_reference_as_active()
    {
        const string html = """
            <html><body>
            <main>Senior .NET Engineer</main>
            <footer>This site may use CAPTCHA services.</footer>
            </body></html>
            """;

        var restriction = detector.DetectRenderedPage(
            new Uri("https://www.linkedin.com/jobs/search"),
            html,
            "Senior .NET Engineer This site may use CAPTCHA services.",
            visibleChallengeElement: false);

        Assert.False(restriction.IsBlocked);
        Assert.True(restriction.HtmlContainsVerificationText);
        Assert.False(restriction.VisibleVerificationChallenge);
    }

    [Fact]
    public void DetectRenderedPage_does_not_treat_visible_login_popup_as_full_page_block_when_cards_exist()
    {
        var html = ReadFixture("eight-jobs-with-login-popup.html");

        var restriction = detector.DetectRenderedPage(
            new Uri("https://www.linkedin.com/jobs/search"),
            html,
            "Sign in to see more jobs Join LinkedIn Backend Engineer 1 Company 1",
            visibleChallengeElement: false,
            visibleLoginPopupWithListings: true);

        Assert.False(restriction.IsBlocked);
        Assert.False(restriction.LoginRequired);
    }

    [Fact]
    public void DetectRenderedPage_blocks_actual_login_destination()
    {
        var restriction = detector.DetectRenderedPage(
            new Uri("https://www.linkedin.com/login"),
            "<html><body>Sign in</body></html>",
            "Sign in",
            visibleChallengeElement: false,
            visibleLoginPopupWithListings: false);

        Assert.True(restriction.IsBlocked);
        Assert.True(restriction.LoginRequired);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
