using System.Collections.Frozen;

namespace PresentationRemote.Presentation;

/// <summary>Whitelist mapping between wire names and <see cref="PresentationCommand"/>.</summary>
public static class PresentationCommands
{
    public const string Next = "presentation.next";
    public const string Previous = "presentation.previous";
    public const string Start = "presentation.start";
    public const string Black = "presentation.black";
    public const string End = "presentation.end";

    private static readonly FrozenDictionary<string, PresentationCommand> ByWireName =
        new Dictionary<string, PresentationCommand>(StringComparer.Ordinal)
        {
            [Next] = PresentationCommand.Next,
            [Previous] = PresentationCommand.Previous,
            [Start] = PresentationCommand.Start,
            [Black] = PresentationCommand.ToggleBlack,
            [End] = PresentationCommand.End,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> WireNames => ByWireName.Keys;

    /// <summary>Exact, case-sensitive match against the whitelist.</summary>
    public static bool TryParse(string? wireName, out PresentationCommand command)
    {
        if (wireName is not null && ByWireName.TryGetValue(wireName, out command))
        {
            return true;
        }

        command = default;
        return false;
    }

    public static string ToWireName(PresentationCommand command) => command switch
    {
        PresentationCommand.Next => Next,
        PresentationCommand.Previous => Previous,
        PresentationCommand.Start => Start,
        PresentationCommand.ToggleBlack => Black,
        PresentationCommand.End => End,
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };
}
