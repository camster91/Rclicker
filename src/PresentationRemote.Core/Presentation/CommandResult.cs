namespace PresentationRemote.Presentation;

public enum CommandOutcome
{
    /// <summary>The action was sent to the presentation.</summary>
    Executed,

    /// <summary>Dropped on purpose: a duplicate tap or too many commands too quickly.</summary>
    RateLimited,

    /// <summary>Not sent because it would be unsafe right now (for example PowerPoint is not the active window).</summary>
    Rejected,

    /// <summary>We tried, and Windows reported an error.</summary>
    Failed,
}

/// <summary>Result of one command. <see cref="Message"/> is short, plain English and safe to show on the phone.</summary>
public readonly record struct CommandResult(CommandOutcome Outcome, string? Message = null)
{
    public static CommandResult Executed { get; } = new(CommandOutcome.Executed);

    public bool IsSuccess => Outcome == CommandOutcome.Executed;

    public static CommandResult RateLimited(string message) => new(CommandOutcome.RateLimited, message);

    public static CommandResult Rejected(string message) => new(CommandOutcome.Rejected, message);

    public static CommandResult Failed(string message) => new(CommandOutcome.Failed, message);
}
