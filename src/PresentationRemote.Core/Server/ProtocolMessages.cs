using System.Text;
using System.Text.Json;
using PresentationRemote.Presentation;

namespace PresentationRemote.Server;

public enum ClientMessageKind
{
    Command,
    Ping,
    Error,
}

/// <summary>A parsed message from the phone.</summary>
public readonly record struct ClientMessage(ClientMessageKind Kind, PresentationCommand Command, long? Id, string? ErrorCode)
{
    public static ClientMessage Error(string code, long? id = null) => new(ClientMessageKind.Error, default, id, code);
}

/// <summary>Error codes sent back to the phone.</summary>
public static class ProtocolErrors
{
    public const string Malformed = "malformed";
    public const string UnsupportedCommand = "unsupported_command";
    public const string TooLarge = "too_large";
}

/// <summary>
/// The wire protocol. Phone → receiver messages look like
/// <c>{"type":"presentation.next","id":7}</c>. Only the whitelisted command names are
/// accepted; everything else is answered with an error and otherwise ignored.
/// </summary>
public static class ControllerProtocol
{
    /// <summary>Real messages are ~40 bytes. Anything over this is rejected unparsed.</summary>
    public const int MaxMessageBytes = 512;

    public const string PingType = "ping";

    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 4 };

    public static ClientMessage Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ClientMessage.Error(ProtocolErrors.Malformed);
        }

        if (Encoding.UTF8.GetByteCount(text) > MaxMessageBytes)
        {
            return ClientMessage.Error(ProtocolErrors.TooLarge);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ParseOptions);
        }
        catch (JsonException)
        {
            return ClientMessage.Error(ProtocolErrors.Malformed);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ClientMessage.Error(ProtocolErrors.Malformed);
            }

            long? id = null;
            if (root.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var parsedId) || parsedId < 0)
                {
                    return ClientMessage.Error(ProtocolErrors.Malformed);
                }

                id = parsedId;
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return ClientMessage.Error(ProtocolErrors.Malformed, id);
            }

            var type = typeElement.GetString();
            if (type == PingType)
            {
                return new ClientMessage(ClientMessageKind.Ping, default, id, null);
            }

            return PresentationCommands.TryParse(type, out var command)
                ? new ClientMessage(ClientMessageKind.Command, command, id, null)
                : ClientMessage.Error(ProtocolErrors.UnsupportedCommand, id);
        }
    }

    public static string Hello(string version) => Write(w =>
    {
        w.WriteString("type", "hello");
        w.WriteString("version", version);
        w.WriteStartArray("commands");
        foreach (var name in PresentationCommands.WireNames)
        {
            w.WriteStringValue(name);
        }

        w.WriteEndArray();
    });

    public static string Pong(long? id) => Write(w =>
    {
        w.WriteString("type", "pong");
        WriteId(w, id);
    });

    public static string Ack(long? id, PresentationCommand command, CommandResult result) => Write(w =>
    {
        w.WriteString("type", "ack");
        WriteId(w, id);
        w.WriteString("command", PresentationCommands.ToWireName(command));
        w.WriteString("status", result.Outcome switch
        {
            CommandOutcome.Executed => "ok",
            CommandOutcome.RateLimited => "rate_limited",
            CommandOutcome.Rejected => "rejected",
            _ => "failed",
        });
        if (result.Message is not null)
        {
            w.WriteString("message", result.Message);
        }
    });

    public static string Error(string code, long? id) => Write(w =>
    {
        w.WriteString("type", "error");
        WriteId(w, id);
        w.WriteString("code", code);
    });

    private static void WriteId(Utf8JsonWriter w, long? id)
    {
        if (id is { } value)
        {
            w.WriteNumber("id", value);
        }
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
