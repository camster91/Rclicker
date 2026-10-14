using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RClicker.Presentation;

/// <summary>
/// Turns semantic commands into shortcuts for the selected presentation app.
/// Platform specifics (SendInput, foreground window lookup) are injected.
/// </summary>
public sealed class KeyboardPresentationController : IPresentationController
{
    private readonly IKeySender _keys;
    private readonly IForegroundWindowProvider _foreground;
    private readonly ILogger _logger;
    private volatile bool _restrictToPowerPoint = true;
    private volatile PresentationTarget _target;

    public PresentationTarget Target
    {
        get => _target;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _target = value;
        }
    }

    public KeyboardPresentationController(IKeySender keys, IForegroundWindowProvider foreground, ILogger<KeyboardPresentationController>? logger = null)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Restricts PowerPoint mode to its foreground app. PDF mode always restricts its target.</summary>
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
        var target = Target;
        var rejection = target == PresentationTarget.Pdf
            ? PdfTargetPolicy.Check(command, window)
            : PowerPointTargetPolicy.Check(command, window, RestrictToPowerPoint);
        if (rejection is not null)
        {
            _logger.LogInformation(
                "Not sending {Command}: foreground is {Process}/{Class}",
                command,
                window.ProcessName ?? "(unknown)",
                window.WindowClass ?? "(unknown)");
            return rejection.Value;
        }

        var key = target == PresentationTarget.Pdf ? command switch
        {
            PresentationCommand.Next => PresentationKey.PageDown,
            PresentationCommand.Previous => PresentationKey.PageUp,
            PresentationCommand.Start => PresentationKey.ControlL,
            PresentationCommand.End => PresentationKey.Escape,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        } : KeyFor(command);
        if (!_keys.TrySend(key, out var error))
        {
            _logger.LogWarning("Sending {Key} for {Command} failed: {Error}", key, command, error);
            return CommandResult.Failed(error ?? "Windows did not accept the key press.");
        }

        return CommandResult.Executed;
    }
}
