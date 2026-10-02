using RClicker.Presentation;

namespace RClicker.Tests.Presentation;

public class PowerPointTargetPolicyTests
{
    private static readonly ForegroundWindowInfo SlideShow = new("POWERPNT", "screenClass");
    private static readonly ForegroundWindowInfo PresenterView = new("POWERPNT", "PodiumParent");
    private static readonly ForegroundWindowInfo Editor = new("POWERPNT", "PPTFrameClass");

    [Theory]
    [InlineData("POWERPNT")]
    [InlineData("powerpnt")]
    [InlineData("POWERPNT.EXE")]
    public void RecognisesPowerPointProcess(string name) =>
        Assert.True(PowerPointTargetPolicy.IsPowerPoint(new ForegroundWindowInfo(name, null)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WINWORD")]
    [InlineData("explorer")]
    [InlineData("POWERPNTX")]
    public void OtherProcesses_AreNotPowerPoint(string? name) =>
        Assert.False(PowerPointTargetPolicy.IsPowerPoint(new ForegroundWindowInfo(name, null)));

    [Fact]
    public void AllCommandsAllowed_InSlideShowAndPresenterView()
    {
        foreach (var command in Enum.GetValues<PresentationCommand>())
        {
            Assert.Null(PowerPointTargetPolicy.Check(command, SlideShow, restrictToPowerPoint: true));
            Assert.Null(PowerPointTargetPolicy.Check(command, PresenterView, restrictToPowerPoint: true));
        }
    }

    [Fact]
    public void Black_IsBlockedInEditor_SoItCannotTypeTheLetterB()
    {
        var result = PowerPointTargetPolicy.Check(PresentationCommand.ToggleBlack, Editor, restrictToPowerPoint: true);

        Assert.NotNull(result);
        Assert.Equal(CommandOutcome.Rejected, result.Value.Outcome);
    }

    [Theory]
    [InlineData(PresentationCommand.Start)]
    [InlineData(PresentationCommand.Next)]
    [InlineData(PresentationCommand.Previous)]
    [InlineData(PresentationCommand.End)]
    public void NavigationAndStart_AllowedInEditor(PresentationCommand command) =>
        Assert.Null(PowerPointTargetPolicy.Check(command, Editor, restrictToPowerPoint: true));

    [Fact]
    public void EverythingRejected_WhenAnotherAppOrNothingIsActive()
    {
        foreach (var command in Enum.GetValues<PresentationCommand>())
        {
            Assert.Equal(CommandOutcome.Rejected, PowerPointTargetPolicy.Check(command, new("chrome", "Chrome_WidgetWin_1"), true)!.Value.Outcome);
            Assert.Equal(CommandOutcome.Rejected, PowerPointTargetPolicy.Check(command, ForegroundWindowInfo.Unknown, true)!.Value.Outcome);
        }
    }

    [Fact]
    public void NoRestriction_AllowsAnyWindow() =>
        Assert.Null(PowerPointTargetPolicy.Check(PresentationCommand.ToggleBlack, new("chrome", null), restrictToPowerPoint: false));
}
