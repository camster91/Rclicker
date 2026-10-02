using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RClicker.Presentation;

/// <summary>
/// Version 0.1 controller: turns semantic commands into PowerPoint keyboard shortcuts.
/// Platform specifics (SendInput, foreground window lookup) are injected.
/// </summary>
public sealed class KeyboardPresentationController : IPresentationController
{
    private readonly IKeySender _keys;
    private readonly IForegroundWindowProvider _foreground;
    private readonly ILogger _logger;
    private volatile bool _restrictToPowerPoint = true;

    public KeyboardPresentationController(IKeySender keys, IForegroundWindowProvider foreground, ILogger<KeyboardPresentationController>? logger = null)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Only send keys while PowerPoint is the active window. On by default.</summary>
    public bool RestrictToPowerPoint
    {
        get => _restrictToPowerPoint;
        set => _restrictToPowerPoint = value;
    }

    /// <summary>The PowerPoint shortcut used for each command.</summary>
    public static PresentationKey KeyFor(PresentationCommand command) => command switch
    {
        PresentationCommand.Next => PresentationKey.RightArrow,
        PresentationCommand.Previous => PresentationKey.LeftArrow,
        PresentationCommand.Start => PresentationKey.F5,
        PresentationCommand.ToggleBlack => PresentationKey.B,
        PresentationCommand.End => PresentationKey.Escape,
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public CommandResult Next() => Send(PresentationCommand.Next);

    public CommandResult Previous() => Send(PresentationCommand.Previous);

    public CommandResult Start() => Send(PresentationCommand.Start);

    public CommandResult ToggleBlack() => Send(PresentationCommand.ToggleBlack);

    public CommandResult End() => Send(PresentationCommand.End);

    private CommandResult Send(PresentationCommand command)
    {
        var window = _foreground.GetForegroundWindow();
        var rejection = PowerPointTargetPolicy.Check(command, window, RestrictToPowerPoint);
        if (rejection is not null)
        {
            _logger.LogInformation(
                "Not sending {Command}: foreground is {Process}/{Class}",
                command,
                window.ProcessName ?? "(unknown)",
                window.WindowClass ?? "(unknown)");
            return rejection.Value;
        }

        var key = KeyFor(command);
        if (!_keys.TrySend(key, out var error))
        {
            _logger.LogWarning("Sending {Key} for {Command} failed: {Error}", key, command, error);
            return CommandResult.Failed(error ?? "Windows did not accept the key press.");
        }

        return CommandResult.Executed;
    }
}
