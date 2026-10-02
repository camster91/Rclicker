using System.Reflection;

namespace RClicker;

public static class AppInfo
{
    public const string Name = "rclicker";

    /// <summary>Semantic version from the build (e.g. "0.1.0"), without the git hash suffix.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Relay address baked in at build time (MSBuild property RelayUrl), or empty.
    /// </summary>
    public static string? BuiltInRelay { get; } = Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "RelayUrl")?.Value;

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        int plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
    }
}
