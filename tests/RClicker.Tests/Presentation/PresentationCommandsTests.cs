using RClicker.Presentation;

namespace RClicker.Tests.Presentation;

public class PresentationCommandsTests
{
    [Theory]
    [InlineData("presentation.next", PresentationCommand.Next)]
    [InlineData("presentation.previous", PresentationCommand.Previous)]
    [InlineData("presentation.start", PresentationCommand.Start)]
    [InlineData("presentation.black", PresentationCommand.ToggleBlack)]
    [InlineData("presentation.end", PresentationCommand.End)]
    public void KnownWireNames_MapToCommands_AndBack(string wire, PresentationCommand expected)
    {
        Assert.True(PresentationCommands.TryParse(wire, out var command));
        Assert.Equal(expected, command);
        Assert.Equal(wire, PresentationCommands.ToWireName(command));
    }

    [Theory]
    [InlineData("press-key")]
    [InlineData("send-text")]
    [InlineData("execute-command")]
    [InlineData("run-process")]
    [InlineData("Presentation.Next")]
    [InlineData("presentation.next ")]
    [InlineData("presentation.")]
    [InlineData("presentation.goto")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNotACommand(string? wire)
    {
        Assert.False(PresentationCommands.TryParse(wire, out _));
    }

    [Fact]
    public void Whitelist_HasExactlyTheFiveSupportedCommands()
    {
        Assert.Equal(
            ["presentation.black", "presentation.end", "presentation.next", "presentation.previous", "presentation.start"],
            PresentationCommands.WireNames.Order(StringComparer.Ordinal));
        Assert.Equal(5, Enum.GetValues<PresentationCommand>().Length);
    }
}
