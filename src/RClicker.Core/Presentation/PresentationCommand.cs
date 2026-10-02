namespace RClicker.Presentation;

/// <summary>
/// The complete set of things a phone can ask for. There is deliberately no
/// "press key", "send text" or "run" command: anything not listed here cannot happen.
/// </summary>
public enum PresentationCommand
{
    Next,
    Previous,
    Start,
    ToggleBlack,
    End,
}
