using System.Text.Json;
using RClicker.Presentation;
using RClicker.Server;

namespace RClicker.Tests.Server;

public class ControllerProtocolTests
{
    [Theory]
    [InlineData("""{"type":"presentation.next"}""", PresentationCommand.Next)]
    [InlineData("""{"type":"presentation.previous","id":3}""", PresentationCommand.Previous)]
    [InlineData("""{"type":"presentation.start"}""", PresentationCommand.Start)]
    [InlineData("""{"type":"presentation.black"}""", PresentationCommand.ToggleBlack)]
    [InlineData("""{"type":"presentation.end"}""", PresentationCommand.End)]
    [InlineData("""{ "id": 9, "type": "presentation.next", "extra": {"ignored": true} }""", PresentationCommand.Next)]
    public void Parse_AcceptsWhitelistedCommands(string json, PresentationCommand expected)
    {
        var message = ControllerProtocol.Parse(json);

        Assert.Equal(ClientMessageKind.Command, message.Kind);
        Assert.Equal(expected, message.Command);
    }

    [Fact]
    public void Parse_ReadsId()
    {
        Assert.Equal(42, ControllerProtocol.Parse("""{"type":"presentation.next","id":42}""").Id);
        Assert.Null(ControllerProtocol.Parse("""{"type":"presentation.next"}""").Id);
    }

    [Theory]
    [InlineData("""{"type":"press-key","key":"F4"}""")]
    [InlineData("""{"type":"send-text","text":"hello"}""")]
    [InlineData("""{"type":"execute-command","command":"calc.exe"}""")]
    [InlineData("""{"type":"run-process","path":"cmd.exe"}""")]
    [InlineData("""{"type":"PRESENTATION.NEXT"}""")]
    [InlineData("""{"type":"presentation.goto","slide":5}""")]
    public void Parse_RejectsUnsupportedCommands(string json)
    {
        var message = ControllerProtocol.Parse(json);

        Assert.Equal(ClientMessageKind.Error, message.Kind);
        Assert.Equal(ProtocolErrors.UnsupportedCommand, message.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("presentation.next")]
    [InlineData("{not json")]
    [InlineData("""{"type":"presentation.next" """)]
    [InlineData("[]")]
    [InlineData("""["presentation.next"]""")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":null}""")]
    [InlineData("""{"type":["presentation.next"]}""")]
    [InlineData("""{"type":"presentation.next","id":"7"}""")]
    [InlineData("""{"type":"presentation.next","id":-1}""")]
    [InlineData("""{"type":"presentation.next","id":1.5}""")]
    [InlineData("""{"a":{"b":{"c":{"d":{"e":{}}}}},"type":"presentation.next"}""")]
    public void Parse_TreatsMalformedInputAsError_WithoutThrowing(string? json)
    {
        var message = ControllerProtocol.Parse(json);

        Assert.Equal(ClientMessageKind.Error, message.Kind);
        Assert.Equal(ProtocolErrors.Malformed, message.ErrorCode);
    }

    [Fact]
    public void Parse_RejectsOversizedMessages()
    {
        var json = $$"""{"type":"presentation.next","pad":"{{new string('x', ControllerProtocol.MaxMessageBytes)}}"}""";

        Assert.Equal(ProtocolErrors.TooLarge, ControllerProtocol.Parse(json).ErrorCode);
    }

    [Fact]
    public void Parse_Ping()
    {
        Assert.Equal(ClientMessageKind.Ping, ControllerProtocol.Parse("""{"type":"ping"}""").Kind);
    }

    [Fact]
    public void ServerMessages_AreValidJson()
    {
        using var ack = JsonDocument.Parse(ControllerProtocol.Ack(5, PresentationCommand.ToggleBlack, CommandResult.Rejected("Say \"hi\"")));
        Assert.Equal("ack", ack.RootElement.GetProperty("type").GetString());
        Assert.Equal(5, ack.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("presentation.black", ack.RootElement.GetProperty("command").GetString());
        Assert.Equal("rejected", ack.RootElement.GetProperty("status").GetString());
        Assert.Equal("Say \"hi\"", ack.RootElement.GetProperty("message").GetString());

        using var hello = JsonDocument.Parse(ControllerProtocol.Hello("0.1.0"));
        Assert.Equal(5, hello.RootElement.GetProperty("commands").GetArrayLength());

        using var error = JsonDocument.Parse(ControllerProtocol.Error(ProtocolErrors.Malformed, null));
        Assert.False(error.RootElement.TryGetProperty("id", out _));
    }
}
