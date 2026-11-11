using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace AIJobSearchAgent.LinkedInSpike;

public sealed record LoginPopupResult(bool Detected, bool Closed, string? Reason)
{
    public static LoginPopupResult NotDetected { get; } = new(false, false, null);
}

public interface ILoginPopupInteraction
{
    bool IsVisible();

    bool TryClickCloseButton();

    bool TryPressEscape();

    bool WaitUntilClosed(CancellationToken cancellationToken);
}

public sealed class SeleniumLoginPopupHandler
{
    private static readonly By PopupCandidates = By.CssSelector(
        "[role='dialog'], .artdeco-modal, .contextual-sign-in-modal, .modal");
    private static readonly By CloseButtons = By.CssSelector(
        "button[aria-label*='close' i], button[aria-label*='dismiss' i], " +
        ".artdeco-modal__dismiss, .contextual-sign-in-modal__modal-dismiss, .modal__dismiss");

    public LoginPopupResult TryCloseVisiblePopup(
        IWebDriver driver,
        CancellationToken cancellationToken) =>
        TryCloseVisiblePopup(new SeleniumLoginPopupInteraction(driver), cancellationToken);

    public bool IsVisiblePopup(IWebDriver driver) =>
        new SeleniumLoginPopupInteraction(driver).IsVisible();

    public LoginPopupResult TryCloseVisiblePopup(
        ILoginPopupInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (!interaction.IsVisible())
        {
            return LoginPopupResult.NotDetected;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (interaction.TryClickCloseButton() && interaction.WaitUntilClosed(cancellationToken))
        {
            return new LoginPopupResult(true, true, "The visible login popup was closed using its close button.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (interaction.TryPressEscape() && interaction.WaitUntilClosed(cancellationToken))
        {
            return new LoginPopupResult(true, true, "The visible login popup was closed using Escape.");
        }

        return new LoginPopupResult(
            true,
            false,
            "A visible login popup could not be closed; extraction continued for readable job cards.");
    }

    public static bool IsLoginPromptText(string text) =>
        text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("join linkedin", StringComparison.OrdinalIgnoreCase);

    private sealed class SeleniumLoginPopupInteraction(IWebDriver driver) : ILoginPopupInteraction
    {
        public bool IsVisible() => FindVisibleLoginPopup(driver) is not null;

        public bool TryClickCloseButton()
        {
            var popup = FindVisibleLoginPopup(driver);
            if (popup is null)
            {
                return false;
            }

            foreach (var button in popup.FindElements(CloseButtons))
            {
                try
                {
                    if (!button.Displayed || !button.Enabled)
                    {
                        continue;
                    }

                    button.Click();
                    return true;
                }
                catch (Exception exception) when (exception is WebDriverException or InvalidOperationException)
                {
                    // Try another close control or Escape.
                }
            }

            return false;
        }

        public bool TryPressEscape()
        {
            try
            {
                new Actions(driver).SendKeys(Keys.Escape).Perform();
                return true;
            }
            catch (Exception exception) when (exception is WebDriverException or InvalidOperationException)
            {
                return false;
            }
        }

        public bool WaitUntilClosed(CancellationToken cancellationToken)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(3));
                wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
                return wait.Until(currentDriver =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return FindVisibleLoginPopup(currentDriver) is null;
                });
            }
            catch (WebDriverTimeoutException)
            {
                return false;
            }
        }

        private static IWebElement? FindVisibleLoginPopup(IWebDriver driver)
        {
            foreach (var candidate in driver.FindElements(PopupCandidates))
            {
                try
                {
                    if (candidate.Displayed && ContainsLoginPrompt(candidate))
                    {
                        return candidate;
                    }
                }
                catch (StaleElementReferenceException)
                {
                    // Continue with the current rendered DOM.
                }
            }

            return null;
        }

        private static bool ContainsLoginPrompt(IWebElement candidate)
        {
            if (IsLoginPromptText(candidate.Text))
            {
                return true;
            }

            return candidate.FindElements(By.CssSelector(
                "input[name='session_key'], input[type='password'], form[action*='login' i]")).Count > 0;
        }
    }
}
