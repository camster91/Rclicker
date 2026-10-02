namespace PresentationRemote.Presentation;

/// <summary>
/// Semantic presentation actions. The web layer only talks to this interface and never
/// sees key codes, so a future COM/API-based controller (or Google Slides, Keynote, ...)
/// can be dropped in without changing the phone protocol.
/// </summary>
public interface IPresentationController
{
    CommandResult Next();

    CommandResult Previous();

    CommandResult Start();

    CommandResult ToggleBlack();

    CommandResult End();
}
