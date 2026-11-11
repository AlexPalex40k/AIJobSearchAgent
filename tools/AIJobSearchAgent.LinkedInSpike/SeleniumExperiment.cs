using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed class SeleniumExperiment(
    LinkedInHtmlParser parser,
    AccessRestrictionDetector restrictionDetector)
{
    private readonly SeleniumLoginPopupHandler loginPopupHandler = new();

    private static readonly By JobCards = By.CssSelector(
        ".jobs-search__results-list > li, .base-search-card, .job-search-card");
    private static readonly By VerificationElements = By.CssSelector(
        "iframe[src*='captcha' i], iframe[title*='challenge' i], " +
        "[id*='captcha' i], [class*='captcha' i], [data-test-id*='captcha' i], " +
        "form[action*='checkpoint' i], form[action*='challenge' i]");

    public Task<DiagnosticReport> RunAsync(
        Uri url,
        int maxJobs,
        string? browserBinaryPath,
        bool headless,
        CancellationToken cancellationToken) =>
        Task.Run(() => Run(url, maxJobs, browserBinaryPath, headless, cancellationToken), cancellationToken);

    private DiagnosticReport Run(
        Uri url,
        int maxJobs,
        string? browserBinaryPath,
        bool headless,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var limitations = new List<string>();

        try
        {
            var options = new ChromeOptions();
            options.AddArgument("--no-first-run");
            options.AddArgument("--disable-search-engine-choice-screen");
            if (headless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--window-size=1920,1080");
            }
            if (!string.IsNullOrWhiteSpace(browserBinaryPath))
            {
                options.BinaryLocation = browserBinaryPath;
            }

            // No service path is supplied: Selenium Manager resolves a compatible driver.
            using var driver = new ChromeDriver(options);
            if (headless)
            {
                driver.Manage().Window.Size = new System.Drawing.Size(1920, 1080);
            }
            else
            {
                driver.Manage().Window.Maximize();
            }

            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                driver.Navigate().GoToUrl(url);
                var waitResult = WaitForSearchContent(driver, cancellationToken);
                var initialPopup = loginPopupHandler.TryCloseVisiblePopup(driver, cancellationToken);
                var preliminaryUrl = ReadCurrentUri(driver) ?? url;
                var preliminaryRestriction = restrictionDetector.DetectRenderedPage(
                    preliminaryUrl,
                    driver.PageSource,
                    ReadVisibleBodyText(driver),
                    HasVisibleVerificationElement(driver),
                    initialPopup.Detected && CountDomCards(driver) > 0);

                var searchPopup = initialPopup;
                IReadOnlyList<JobDiagnostic> dynamicallyDiscoveredJobs = [];
                if (!preliminaryRestriction.IsBlocked)
                {
                    dynamicallyDiscoveredJobs = LoadAdditionalCards(
                        driver,
                        url,
                        maxJobs,
                        cancellationToken);
                    var laterPopup = loginPopupHandler.TryCloseVisiblePopup(driver, cancellationToken);
                    searchPopup = MergePopupResults(initialPopup, laterPopup);
                }

                var searchUrl = ReadCurrentUri(driver) ?? url;
                var html = driver.PageSource;
                var visibleCards = CountVisibleCards(driver);
                var domCards = CountDomCards(driver);
                var visibleBodyText = ReadVisibleBodyText(driver);
                var isStillLoading = !DocumentReady(driver);
                var restriction = restrictionDetector.DetectRenderedPage(
                    searchUrl,
                    html,
                    visibleBodyText,
                    HasVisibleVerificationElement(driver),
                    searchPopup.Detected && domCards > 0);
                var uniqueJobs = restriction.IsBlocked
                    ? []
                    : MergeUniqueJobs(
                        dynamicallyDiscoveredJobs,
                        parser.Parse(html, searchUrl, LinkedInHtmlParser.MaximumDiscoveryJobs));
                var jobs = uniqueJobs.Take(maxJobs).ToList();
                var classification = SeleniumPageClassifier.Classify(
                    isStillLoading,
                    visibleCards,
                    !string.IsNullOrWhiteSpace(visibleBodyText),
                    restriction,
                    searchPopup.Detected,
                    searchPopup.Detected ? searchPopup.Closed : null,
                    jobs.Count,
                    jobs.Count(job => !string.IsNullOrWhiteSpace(job.Description)),
                    maxJobs);
                var seleniumDiagnostics = new SeleniumPageDiagnostics(
                    searchUrl,
                    ReadPageTitle(driver),
                    visibleCards,
                    !string.IsNullOrWhiteSpace(visibleBodyText),
                    restriction.HtmlContainsVerificationText,
                    restriction.VisibleVerificationChallenge,
                    restriction.VisibleVerificationChallenge,
                    searchPopup.Detected,
                    searchPopup.Detected ? searchPopup.Closed : null,
                    isStillLoading,
                    uniqueJobs.Count,
                    jobs.Count,
                    jobs.Count,
                    classification.Reason);

                if (searchPopup.Reason is not null)
                {
                    limitations.Add(searchPopup.Reason);
                }

                if (!waitResult.DocumentReadyObserved)
                {
                    limitations.Add("The explicit wait did not observe a completed document state.");
                }

                if (!waitResult.ContentSignalObserved)
                {
                    limitations.Add("The explicit wait did not observe job cards, a no-results message, or a visible access restriction.");
                }

                if (restriction.IsBlocked)
                {
                    return DiagnosticReportFactory.Create(
                        "Selenium",
                        url,
                        searchUrl,
                        false,
                        visibleCards > 0,
                        null,
                        restriction,
                        [],
                        limitations: limitations,
                        diagnosticStatus: classification.Status,
                        seleniumDiagnostics: seleniumDiagnostics);
                }

                if (visibleCards == 0)
                {
                    limitations.Add("No job cards matched the diagnostic selectors in the rendered page.");
                }

                if (restriction.HtmlContainsVerificationText &&
                    !restriction.VisibleVerificationChallenge &&
                    restriction.Reason is not null)
                {
                    limitations.Add(restriction.Reason);
                }

                var descriptionInspection = InspectDescriptions(
                    driver,
                    jobs,
                    maxJobs,
                    errors,
                    limitations,
                    cancellationToken);

                if (descriptionInspection.BlockingRestriction is not null)
                {
                    restriction = descriptionInspection.BlockingRestriction;
                }
                else if (descriptionInspection.LoginRestrictionEncountered)
                {
                    restriction = new AccessRestriction(
                        false,
                        true,
                        "Login was required for one or more full descriptions; extracted job cards were retained.",
                        restriction.HtmlContainsVerificationText,
                        restriction.VisibleVerificationChallenge);
                }

                var popupDetected = searchPopup.Detected || descriptionInspection.LoginPopupDetected;
                var popupClosed = popupDetected
                    ? (!searchPopup.Detected || searchPopup.Closed) && descriptionInspection.AllDetectedPopupsClosed
                    : (bool?)null;

                classification = SeleniumPageClassifier.Classify(
                    isStillLoading,
                    visibleCards,
                    !string.IsNullOrWhiteSpace(visibleBodyText),
                    restriction,
                    popupDetected,
                    popupClosed,
                    jobs.Count,
                    jobs.Count(job => !string.IsNullOrWhiteSpace(job.Description)),
                    maxJobs);
                seleniumDiagnostics = seleniumDiagnostics with
                {
                    RealVerificationChallengeEncountered =
                        seleniumDiagnostics.RealVerificationChallengeEncountered ||
                        descriptionInspection.BlockingRestriction?.VisibleVerificationChallenge == true,
                    LoginPopupDetected = popupDetected,
                    LoginPopupClosed = popupClosed,
                    VerificationDetectionReason = classification.Reason
                };

                if (jobs.Any(job => job.Description is null))
                {
                    limitations.Add("One or more listings did not expose a usable full description.");
                }

                if (jobs.Any(job => job.Url is null))
                {
                    limitations.Add("One or more listing links were missing or malformed, so their detail pages were not inspected.");
                }

                return DiagnosticReportFactory.Create(
                    "Selenium",
                    url,
                    searchUrl,
                    classification.Status is
                        DiagnosticStatus.Success or
                        DiagnosticStatus.PartialSuccess or
                        DiagnosticStatus.NoVacancies,
                    visibleCards > 0 || jobs.Count > 0,
                    null,
                    restriction,
                    jobs,
                    errors,
                    limitations,
                    classification.Status,
                    seleniumDiagnostics);
            }
            finally
            {
                driver.Quit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is WebDriverException or InvalidOperationException)
        {
            errors.Add($"Browser automation failed: {exception.GetType().Name}: {SafeMessage(exception)}");
            return DiagnosticReportFactory.Create(
                "Selenium",
                url,
                null,
                false,
                false,
                null,
                AccessRestriction.None,
                [],
                errors,
                ["The browser did not provide page content for inspection."],
                DiagnosticStatus.Failed);
        }
    }

    private DescriptionInspectionResult InspectDescriptions(
        ChromeDriver driver,
        List<JobDiagnostic> jobs,
        int maxJobs,
        List<string> errors,
        List<string> limitations,
        CancellationToken cancellationToken)
    {
        var searchWindow = driver.CurrentWindowHandle;
        var loginPopupDetected = false;
        var allDetectedPopupsClosed = true;
        var loginRestrictionEncountered = false;
        AccessRestriction? blockingRestriction = null;
        for (var index = 0; index < Math.Min(jobs.Count, maxJobs); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = jobs[index];
            if (job.Description is not null || job.Url is null)
            {
                continue;
            }

            try
            {
                driver.SwitchTo().NewWindow(WindowType.Tab);
                driver.Navigate().GoToUrl(job.Url);
                WaitForDocument(driver, cancellationToken);
                var detailPopup = loginPopupHandler.TryCloseVisiblePopup(driver, cancellationToken);
                if (detailPopup.Detected)
                {
                    loginPopupDetected = true;
                    allDetectedPopupsClosed &= detailPopup.Closed;
                    if (detailPopup.Reason is not null)
                    {
                        limitations.Add($"Listing {index + 1}: {detailPopup.Reason}");
                    }
                }

                var detailUrl = ReadCurrentUri(driver) ?? job.Url;
                var detailHtml = driver.PageSource;
                var detailRestriction = restrictionDetector.DetectRenderedPage(
                    detailUrl,
                    detailHtml,
                    ReadVisibleBodyText(driver),
                    HasVisibleVerificationElement(driver),
                    detailPopup.Detected);
                if (detailRestriction.IsBlocked)
                {
                    if (detailRestriction.VisibleVerificationChallenge)
                    {
                        blockingRestriction = detailRestriction;
                        limitations.Add(detailRestriction.Reason ?? "A listing detail page showed a verification challenge.");
                        break;
                    }

                    if (detailRestriction.LoginRequired)
                    {
                        loginRestrictionEncountered = true;
                        limitations.Add(
                            $"Listing {index + 1}: the full description required login; the extracted card was retained.");
                        continue;
                    }
                }

                if (detailRestriction.HtmlContainsVerificationText && detailRestriction.Reason is not null)
                {
                    limitations.Add($"Listing {index + 1}: {detailRestriction.Reason}");
                }

                var description = parser.ExtractDescription(detailHtml);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    jobs[index] = job with
                    {
                        Description = description,
                        MissingFields = job.MissingFields
                            .Where(field => field != "description")
                            .ToArray()
                    };
                }
                else
                {
                    limitations.Add(
                        $"Listing {index + 1}: no usable full description was accessible; the extracted card was retained.");
                }
            }
            catch (WebDriverException exception)
            {
                errors.Add($"Could not inspect listing {index + 1}: {SafeMessage(exception)}");
            }
            finally
            {
                if (driver.WindowHandles.Count > 1)
                {
                    driver.Close();
                }

                driver.SwitchTo().Window(searchWindow);
            }
        }

        return new DescriptionInspectionResult(
            blockingRestriction,
            loginRestrictionEncountered,
            loginPopupDetected,
            allDetectedPopupsClosed);
    }

    private PageWaitResult WaitForSearchContent(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        var documentReadyObserved = false;
        try
        {
            var wait = CreateWait(driver);
            wait.Until(currentDriver =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DocumentReady(currentDriver) ||
                       CountVisibleCards(currentDriver) > 0 ||
                       loginPopupHandler.IsVisiblePopup(currentDriver) ||
                       HasVisibleVerificationElement(currentDriver) ||
                       IsRestrictedUrl(currentDriver.Url);
            });
            documentReadyObserved = DocumentReady(driver);
        }
        catch (WebDriverTimeoutException)
        {
            // Capture the final rendered state below even when document readiness times out.
        }

        var contentSignalObserved = false;
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
            contentSignalObserved = wait.Until(currentDriver =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var visibleText = ReadVisibleBodyText(currentDriver);
                return CountVisibleCards(currentDriver) > 0 ||
                       loginPopupHandler.IsVisiblePopup(currentDriver) ||
                       HasVisibleVerificationElement(currentDriver) ||
                       IsRestrictedUrl(currentDriver.Url) ||
                       visibleText.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
                       visibleText.Contains("security verification", StringComparison.OrdinalIgnoreCase) ||
                       visibleText.Contains("no matching jobs", StringComparison.OrdinalIgnoreCase) ||
                       visibleText.Contains("no jobs found", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (WebDriverTimeoutException)
        {
            // A timeout is a diagnostic result, not proof of an access restriction.
        }

        return new PageWaitResult(documentReadyObserved, contentSignalObserved);
    }

    private IReadOnlyList<JobDiagnostic> LoadAdditionalCards(
        IWebDriver driver,
        Uri fallbackUrl,
        int targetVacancies,
        CancellationToken cancellationToken)
    {
        const int maximumScrollAttempts = 5;
        var discovered = new List<JobDiagnostic>();
        for (var attempt = 0; attempt < maximumScrollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentUrl = ReadCurrentUri(driver) ?? fallbackUrl;
            AddUniqueJobs(discovered, parser.Parse(
                driver.PageSource,
                currentUrl,
                LinkedInHtmlParser.MaximumDiscoveryJobs));
            if (discovered.Count >= targetVacancies)
            {
                return discovered;
            }

            var cards = driver.FindElements(JobCards);
            var domCountBefore = cards.Count;
            if (domCountBefore == 0)
            {
                return discovered;
            }

            var knownIdentities = discovered
                .Select(LinkedInHtmlParser.GetIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scriptExecutor = (IJavaScriptExecutor)driver;
            var containers = driver.FindElements(By.CssSelector(".jobs-search__results-list"));
            if (containers.Count > 0)
            {
                scriptExecutor.ExecuteScript(
                    "arguments[0].scrollTop = arguments[0].scrollHeight;",
                    containers[0]);
            }

            scriptExecutor.ExecuteScript(
                "arguments[0].scrollIntoView({block: 'end'});",
                cards[^1]);

            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(3));
                wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
                wait.Until(currentDriver =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var updatedUrl = ReadCurrentUri(currentDriver) ?? fallbackUrl;
                    var uniqueAfter = parser.Parse(
                        currentDriver.PageSource,
                        updatedUrl,
                        LinkedInHtmlParser.MaximumDiscoveryJobs);
                    return uniqueAfter.Any(job =>
                               !knownIdentities.Contains(LinkedInHtmlParser.GetIdentity(job))) ||
                           CountDomCards(currentDriver) > domCountBefore;
                });
            }
            catch (WebDriverTimeoutException)
            {
                currentUrl = ReadCurrentUri(driver) ?? fallbackUrl;
                AddUniqueJobs(discovered, parser.Parse(
                    driver.PageSource,
                    currentUrl,
                    LinkedInHtmlParser.MaximumDiscoveryJobs));
                return discovered;
            }
        }

        var finalUrl = ReadCurrentUri(driver) ?? fallbackUrl;
        AddUniqueJobs(discovered, parser.Parse(
            driver.PageSource,
            finalUrl,
            LinkedInHtmlParser.MaximumDiscoveryJobs));
        return discovered;
    }

    private static IReadOnlyList<JobDiagnostic> MergeUniqueJobs(
        IReadOnlyList<JobDiagnostic> first,
        IReadOnlyList<JobDiagnostic> second)
    {
        var merged = new List<JobDiagnostic>();
        AddUniqueJobs(merged, first);
        AddUniqueJobs(merged, second);
        return merged;
    }

    private static void AddUniqueJobs(
        List<JobDiagnostic> destination,
        IEnumerable<JobDiagnostic> candidates)
    {
        var identities = destination
            .Select(LinkedInHtmlParser.GetIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (identities.Add(LinkedInHtmlParser.GetIdentity(candidate)))
            {
                destination.Add(candidate);
            }
        }
    }

    private static LoginPopupResult MergePopupResults(
        LoginPopupResult initial,
        LoginPopupResult later)
    {
        if (!initial.Detected)
        {
            return later;
        }

        if (!later.Detected)
        {
            return initial;
        }

        return new LoginPopupResult(
            true,
            initial.Closed || later.Closed,
            later.Reason ?? initial.Reason);
    }

    private static void WaitForDocument(IWebDriver driver, CancellationToken cancellationToken)
    {
        var wait = CreateWait(driver);
        wait.Until(currentDriver =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DocumentReady(currentDriver);
        });
    }

    private static WebDriverWait CreateWait(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        return wait;
    }

    private static bool DocumentReady(IWebDriver driver) =>
        string.Equals(
            ((IJavaScriptExecutor)driver).ExecuteScript("return document.readyState")?.ToString(),
            "complete",
            StringComparison.OrdinalIgnoreCase);

    private static int CountVisibleCards(IWebDriver driver) =>
        driver.FindElements(JobCards).Count(element =>
        {
            try
            {
                return element.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
        });

    private static int CountDomCards(IWebDriver driver) =>
        driver.FindElements(JobCards).Count;

    private static bool HasVisibleVerificationElement(IWebDriver driver) =>
        driver.FindElements(VerificationElements).Any(element =>
        {
            try
            {
                return element.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
        });

    private static string ReadVisibleBodyText(IWebDriver driver)
    {
        try
        {
            return driver.FindElement(By.TagName("body")).Text ?? string.Empty;
        }
        catch (NoSuchElementException)
        {
            return string.Empty;
        }
        catch (StaleElementReferenceException)
        {
            return string.Empty;
        }
    }

    private static string ReadPageTitle(IWebDriver driver)
    {
        try
        {
            return driver.Title;
        }
        catch (WebDriverException)
        {
            return string.Empty;
        }
    }

    private static bool IsRestrictedUrl(string url) =>
        url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/authwall", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/checkpoint/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/challenge/", StringComparison.OrdinalIgnoreCase);

    private static Uri? ReadCurrentUri(IWebDriver driver) =>
        Uri.TryCreate(driver.Url, UriKind.Absolute, out var current) ? current : null;

    private static string SafeMessage(Exception exception)
    {
        var firstLine = exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine)
            ? exception.GetType().Name
            : firstLine[..Math.Min(firstLine.Length, 300)];
    }

    private sealed record PageWaitResult(bool DocumentReadyObserved, bool ContentSignalObserved);

    private sealed record DescriptionInspectionResult(
        AccessRestriction? BlockingRestriction,
        bool LoginRestrictionEncountered,
        bool LoginPopupDetected,
        bool AllDetectedPopupsClosed);
}
