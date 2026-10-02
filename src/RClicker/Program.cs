using Microsoft.Extensions.Logging;
using RClicker.Native;
using RClicker.UI;

namespace RClicker;

internal static class Program
{
    // Per-user-session mutex: two receivers would mean two servers sending keys.
    private const string SingleInstanceMutexName = @"Local\RClicker.SingleInstance.v1";

    [STAThread]
    private static int Main(string[] args)
    {
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show($"{ex.Message}\n\n{CommandLineOptions.Usage}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        if (options.ShowConsole)
        {
            NativeMethods.AllocConsole();
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "rclicker is already running on this computer. Use the window that is already open.",
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 1;
        }

        using var loggerFactory = LoggerFactory.Create(builder => ConfigureLogging(builder, options));
        var logger = loggerFactory.CreateLogger("RClicker");
        logger.LogInformation("{App} {Version} starting", AppInfo.Name, AppInfo.Version);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unhandled UI exception");
            MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        using var form = new MainForm(options, loggerFactory, b => ConfigureLogging(b, options));
        Application.Run(form);
        logger.LogInformation("{App} stopped", AppInfo.Name);
        return 0;
    }

    private static void ConfigureLogging(ILoggingBuilder builder, CommandLineOptions options)
    {
        builder.SetMinimumLevel(options.ShowConsole ? LogLevel.Debug : LogLevel.Information);
        builder.AddDebug(); // Visible in Visual Studio / DebugView.
        if (options.ShowConsole)
        {
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
        }
    }
}
