namespace RClicker;

/// <summary>
/// Optional switches:
///   --relay &lt;url&gt;   use a different relay (default: the one built into this copy)
///   --console       open a console window with diagnostic logs
/// </summary>
internal sealed record CommandLineOptions(string? Relay, bool ShowConsole)
{
    public const string Usage = "Usage: rclicker.exe [--relay https://<relay address>] [--console]";

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string? relay = null;
        bool console = false;

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--relay":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("--relay needs an address, e.g. https://rclicker.example.workers.dev");
                    }

                    relay = args[++i];
                    break;

                case "--console":
                    console = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new CommandLineOptions(relay, console);
    }
}
