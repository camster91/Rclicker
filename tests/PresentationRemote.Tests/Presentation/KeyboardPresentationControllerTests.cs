using PresentationRemote.Presentation;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Presentation;

public class KeyboardPresentationControllerTests
{
    [Theory]
    [InlineData(PresentationCommand.Next, PresentationKey.RightArrow)]
    [InlineData(PresentationCommand.Previous, PresentationKey.LeftArrow)]
    [InlineData(PresentationCommand.Start, PresentationKey.F5)]
    [InlineData(PresentationCommand.ToggleBlack, PresentationKey.B)]
    [InlineData(PresentationCommand.End, PresentationKey.Escape)]
    public void Commands_MapToPowerPointShortcuts(PresentationCommand command, PresentationKey key)
    {
        Assert.Equal(key, KeyboardPresentationController.KeyFor(command));
    }

    [Fact]
    public void EachAction_SendsExactlyOneKey()
    {
        var keys = new FakeKeySender();
        var controller = new KeyboardPresentationController(keys, new FakeForegroundWindow());

        controller.Start();
        controller.Next();
        controller.Previous();
        controller.ToggleBlack();
        controller.End();

        Assert.Equal(
            [PresentationKey.F5, PresentationKey.RightArrow, PresentationKey.LeftArrow, PresentationKey.B, PresentationKey.Escape],
            keys.Sent);
    }

    [Fact]
    public void WhenPowerPointIsNotActive_NoKeyIsSent()
    {
        var keys = new FakeKeySender();
        var window = new FakeForegroundWindow { Current = new ForegroundWindowInfo("notepad", "Notepad") };
        var controller = new KeyboardPresentationController(keys, window);

        var result = controller.ToggleBlack();

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Empty(keys.Sent);
    }

    [Fact]
    public void RestrictionCanBeTurnedOff_ForOtherPresentationApps()
    {
        var keys = new FakeKeySender();
        var window = new FakeForegroundWindow { Current = new ForegroundWindowInfo("AcroRd32", "AcrobatSDIWindow") };
        var controller = new KeyboardPresentationController(keys, window) { RestrictToPowerPoint = false };

        Assert.True(controller.Next().IsSuccess);
        Assert.Equal([PresentationKey.RightArrow], keys.Sent);
    }

    [Fact]
    public void SendFailure_IsReportedNotThrown()
    {
        var keys = new FakeKeySender { FailWith = "blocked" };
        var controller = new KeyboardPresentationController(keys, new FakeForegroundWindow());

        var result = controller.Next();

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.Equal("blocked", result.Message);
    }
}
