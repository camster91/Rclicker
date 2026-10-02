namespace RClicker;

/// <summary>
/// Optional switches:
///   --port &lt;n&gt;   use a specific port (no automatic fallback)
///   --console     open a console window with diagnostic logs
/// </summary>
internal sealed record CommandLineOptions(int? Port, bool ShowConsole)
{
    public const string Usage = "Usage: rclicker.exe [--port <1024-65535>] [--console]";

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        int? port = null;
        bool console = false;

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 >= args.Count
                        || !int.TryParse(args[++i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                        || value is < 1024 or > 65535)
                    {
                        throw new ArgumentException("--port needs a number between 1024 and 65535.");
                    }

                    port = value;
                    break;

                case "--console":
                    console = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new CommandLineOptions(port, console);
    }
}
