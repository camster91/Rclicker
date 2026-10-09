namespace RClicker.Presentation;

public enum PresentationTarget
{
    PowerPoint,
    Pdf,
}

/// <summary>Dedicated PDF viewers only: browser processes also contain unrelated pages.</summary>
public static class PdfTargetPolicy
{
    public const string NotActiveMessage = "Click the PDF document in Adobe Acrobat/Reader or SumatraPDF on the computer, then try again.";
    public const string BlackNotSupportedMessage = "Black screen is not supported in PDF mode. Use Next, Previous, Start or End.";

    public static bool IsPdfViewer(ForegroundWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var name = window.ProcessName;
        if (name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true) name = name[..^4];
        return string.Equals(name, "Acrobat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "AcroRd32", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "SumatraPDF", StringComparison.OrdinalIgnoreCase);
    }

    public static CommandResult? Check(PresentationCommand command, ForegroundWindowInfo window)
    {
        if (!IsPdfViewer(window)) return CommandResult.Rejected(NotActiveMessage);
        return command == PresentationCommand.ToggleBlack
            ? CommandResult.Rejected(BlackNotSupportedMessage) : null;
    }
}
