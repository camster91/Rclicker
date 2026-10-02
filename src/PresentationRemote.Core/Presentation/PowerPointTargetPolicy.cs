namespace PresentationRemote.Presentation;

/// <summary>
/// Decides whether it is safe to send a key right now. Key presses always go to the
/// foreground window, so without this check "Black" would type the letter "b" into
/// whatever app happens to be active.
/// </summary>
public static class PowerPointTargetPolicy
{
    /// <summary>Process name of Microsoft PowerPoint (POWERPNT.EXE).</summary>
    public const string PowerPointProcessName = "POWERPNT";

    /// <summary>Window class of PowerPoint's normal editing window.</summary>
    public const string EditorWindowClass = "PPTFrameClass";

    /// <summary>Window class of a running slide show.</summary>
    public const string SlideShowWindowClass = "screenClass";

    /// <summary>Window class of Presenter View.</summary>
    public const string PresenterViewWindowClass = "PodiumParent";

    public const string NotActiveMessage = "PowerPoint is not the active window. Click on PowerPoint on the computer, then try again.";
    public const string BlackNeedsSlideShowMessage = "Black screen only works during a slideshow. Tap Start first.";

    public static bool IsPowerPoint(ForegroundWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var name = window.ProcessName;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return string.Equals(name, PowerPointProcessName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSlideShow(ForegroundWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return string.Equals(window.WindowClass, SlideShowWindowClass, StringComparison.Ordinal)
            || string.Equals(window.WindowClass, PresenterViewWindowClass, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns null when the command may be sent, or a rejection explaining why not.
    /// </summary>
    /// <param name="restrictToPowerPoint">
    /// When false (user opted out in the desktop window) keys go to any foreground app,
    /// e.g. a PDF viewer or a browser-based slideshow.
    /// </param>
    public static CommandResult? Check(PresentationCommand command, ForegroundWindowInfo window, bool restrictToPowerPoint)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!restrictToPowerPoint)
        {
            return null;
        }

        if (!IsPowerPoint(window))
        {
            return CommandResult.Rejected(NotActiveMessage);
        }

        // "B" in the editor would type a letter into the slide. We deny only the editor
        // window (well known) instead of allowing only slideshow classes, so an unexpected
        // slideshow window class in some PowerPoint version still works.
        if (command == PresentationCommand.ToggleBlack
            && string.Equals(window.WindowClass, EditorWindowClass, StringComparison.Ordinal))
        {
            return CommandResult.Rejected(BlackNeedsSlideShowMessage);
        }

        return null;
    }
}
