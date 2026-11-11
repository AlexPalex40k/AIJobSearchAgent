namespace AIJobSearchAgent.LinkedInSpike;

public sealed class AccessRestrictionDetector
{
    private static readonly string[] VerificationMarkers =
    [
        "verify you are human",
        "security verification",
        "complete this security check",
        "unusual activity",
        "captcha"
    ];

    private static readonly string[] VisibleVerificationMarkers =
    [
        "verify you are human",
        "security verification",
        "complete this security check",
        "unusual activity",
        "complete the captcha",
        "solve the captcha"
    ];

    private static readonly string[] LoginWallMarkers =
    [
        "sign in to view",
        "sign in to see",
        "join linkedin to see",
        "login to linkedin"
    ];

    public AccessRestriction Detect(Uri? finalUrl, string content)
    {
        var path = finalUrl?.AbsolutePath ?? string.Empty;
        if (path.Contains("/checkpoint/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/challenge/", StringComparison.OrdinalIgnoreCase))
        {
            return new AccessRestriction(
                true,
                false,
                "LinkedIn returned a verification or checkpoint page.",
                HtmlContainsVerificationText(content),
                true);
        }

        if (path.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/authwall", StringComparison.OrdinalIgnoreCase))
        {
            return new AccessRestriction(true, true, "LinkedIn redirected the request to a login wall.");
        }

        if (VerificationMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return new AccessRestriction(
                true,
                false,
                "The HTTP response contains CAPTCHA or verification text; rendered visibility is unknown.",
                true,
                false);
        }

        if (LoginWallMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return new AccessRestriction(true, true, "The page requires login before job content can be viewed.");
        }

        return AccessRestriction.None;
    }

    public AccessRestriction DetectRenderedPage(
        Uri? currentUrl,
        string html,
        string visibleBodyText,
        bool visibleChallengeElement,
        bool visibleLoginPopupWithListings = false)
    {
        var htmlContainsVerificationText = HtmlContainsVerificationText(html);
        var path = currentUrl?.AbsolutePath ?? string.Empty;
        var verificationUrl = path.Contains("/checkpoint/", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("/challenge/", StringComparison.OrdinalIgnoreCase);
        var loginUrl = path.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                       path.Contains("/authwall", StringComparison.OrdinalIgnoreCase);
        var visibleVerificationText = VisibleVerificationMarkers.Any(marker =>
            visibleBodyText.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var visibleLoginWall = LoginWallMarkers.Any(marker =>
            visibleBodyText.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var visibleVerificationChallenge = verificationUrl ||
                                           visibleChallengeElement ||
                                           visibleVerificationText;

        if (loginUrl || visibleLoginWall && !visibleLoginPopupWithListings)
        {
            return new AccessRestriction(
                true,
                true,
                loginUrl
                    ? "The current URL is a LinkedIn login or authentication wall."
                    : "A login requirement is visible in the rendered page.",
                htmlContainsVerificationText,
                false);
        }

        if (visibleVerificationChallenge)
        {
            var reason = verificationUrl
                ? "The current URL is a LinkedIn verification or checkpoint page."
                : visibleChallengeElement
                    ? "A displayed CAPTCHA or verification element is present in the rendered page."
                    : "Verification challenge text is visible in the rendered page body.";

            return new AccessRestriction(
                true,
                false,
                reason,
                htmlContainsVerificationText,
                true);
        }

        if (htmlContainsVerificationText)
        {
            return new AccessRestriction(
                false,
                false,
                "CAPTCHA-related markup was found, but no active visible challenge was detected.",
                true,
                false);
        }

        return AccessRestriction.None;
    }

    private static bool HtmlContainsVerificationText(string content) =>
        VerificationMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
