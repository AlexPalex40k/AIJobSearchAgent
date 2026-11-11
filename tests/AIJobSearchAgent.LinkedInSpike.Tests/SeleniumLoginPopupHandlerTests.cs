using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.LinkedInSpike.Tests;

public sealed class SeleniumLoginPopupHandlerTests
{
    private readonly SeleniumLoginPopupHandler handler = new();

    [Fact]
    public void TryCloseVisiblePopup_reports_close_button_success()
    {
        var interaction = new StubLoginPopupInteraction(
            isVisible: true,
            closeButtonSent: true,
            escapeSent: false,
            closesAfterAction: true);

        var result = handler.TryCloseVisiblePopup(interaction, CancellationToken.None);

        Assert.True(result.Detected);
        Assert.True(result.Closed);
        Assert.Equal(1, interaction.CloseButtonAttempts);
        Assert.Equal(0, interaction.EscapeAttempts);
        Assert.Contains("close button", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCloseVisiblePopup_reports_failure_and_allows_extraction_to_continue()
    {
        var interaction = new StubLoginPopupInteraction(
            isVisible: true,
            closeButtonSent: false,
            escapeSent: true,
            closesAfterAction: false);

        var result = handler.TryCloseVisiblePopup(interaction, CancellationToken.None);

        Assert.True(result.Detected);
        Assert.False(result.Closed);
        Assert.Equal(1, interaction.CloseButtonAttempts);
        Assert.Equal(1, interaction.EscapeAttempts);
        Assert.Contains("continued", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubLoginPopupInteraction(
        bool isVisible,
        bool closeButtonSent,
        bool escapeSent,
        bool closesAfterAction) : ILoginPopupInteraction
    {
        public int CloseButtonAttempts { get; private set; }

        public int EscapeAttempts { get; private set; }

        public bool IsVisible() => isVisible;

        public bool TryClickCloseButton()
        {
            CloseButtonAttempts++;
            return closeButtonSent;
        }

        public bool TryPressEscape()
        {
            EscapeAttempts++;
            return escapeSent;
        }

        public bool WaitUntilClosed(CancellationToken cancellationToken) => closesAfterAction;
    }
}
