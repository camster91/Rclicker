using RClicker.Presentation;
using RClicker.Tests.TestDoubles;

namespace RClicker.Tests.Presentation;

public class PdfPresentationTests
{
    [Theory]
    [InlineData("Acrobat")]
    [InlineData("AcroRd32")]
    [InlineData("ACROBAT.EXE")]
    [InlineData("SumatraPDF")]
    [InlineData("sumatrapdf.exe")]
    public void RecognisesDedicatedViewers(string name) =>
        Assert.True(PdfTargetPolicy.IsPdfViewer(new(name, null)));

    [Theory]
    [InlineData(null)]
    [InlineData("notepad")]
    [InlineData("msedge")]
    [InlineData("chrome")]
    [InlineData("POWERPNT")]
    [InlineData("AcrobatHelper")]
    public void PdfModeRejectsUnrelatedAndUnknownApps_EvenWithoutPowerPointRestriction(string? name)
    {
        var keys = new FakeKeySender();
        var controller = new KeyboardPresentationController(keys,
            new FakeForegroundWindow { Current = new(name, null) })
            { Target = PresentationTarget.Pdf, RestrictToPowerPoint = false };
        foreach (var command in Enum.GetValues<PresentationCommand>())
            Assert.Equal(CommandOutcome.Rejected, new CommandRouter(controller).Execute(command).Outcome);
        Assert.Empty(keys.Sent);
    }

    [Theory]
    [InlineData("Acrobat")]
    [InlineData("AcroRd32")]
    [InlineData("SumatraPDF")]
    public void PdfCommandsUsePageKeysAndFullScreenChord_BlackSendsNothing(string name)
    {
        var keys = new FakeKeySender();
        var controller = new KeyboardPresentationController(keys,
            new FakeForegroundWindow { Current = new(name, null) }) { Target = PresentationTarget.Pdf };
        Assert.True(controller.Start().IsSuccess);
        Assert.True(controller.Next().IsSuccess);
        Assert.True(controller.Previous().IsSuccess);
        Assert.Equal(CommandOutcome.Rejected, controller.ToggleBlack().Outcome);
        Assert.True(controller.End().IsSuccess);
        Assert.Equal([PresentationKey.ControlL, PresentationKey.PageDown, PresentationKey.PageUp, PresentationKey.Escape], keys.Sent);
    }

    [Fact]
    public void SwitchingModesRestoresPowerPointMappingAndGuard()
    {
        var keys = new FakeKeySender();
        var foreground = new FakeForegroundWindow { Current = new("Acrobat", null) };
        var controller = new KeyboardPresentationController(keys, foreground) { Target = PresentationTarget.Pdf };
        Assert.True(controller.Next().IsSuccess);
        controller.Target = PresentationTarget.PowerPoint;
        Assert.Equal(CommandOutcome.Rejected, controller.Next().Outcome);
        foreground.Current = new("POWERPNT", "screenClass");
        Assert.True(controller.Next().IsSuccess);
        Assert.Equal([PresentationKey.PageDown, PresentationKey.RightArrow], keys.Sent);
    }
}
