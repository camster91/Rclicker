using System.Collections.Frozen;
using System.Reflection;

namespace PresentationRemote.Server;

/// <summary>
/// The phone UI, served from embedded resources through a fixed whitelist of paths.
/// Nothing is read from disk, so there is no path traversal surface.
/// </summary>
public sealed class StaticAssets
{
    private readonly FrozenDictionary<string, Asset> _assets;

    private StaticAssets(FrozenDictionary<string, Asset> assets) => _assets = assets;

    public static StaticAssets LoadEmbedded()
    {
        var assembly = typeof(StaticAssets).Assembly;
        var index = Load(assembly, "wwwroot/index.html", "text/html; charset=utf-8");
        var map = new Dictionary<string, Asset>(StringComparer.Ordinal)
        {
            ["/"] = index,
            ["/index.html"] = index,
            ["/app.js"] = Load(assembly, "wwwroot/app.js", "text/javascript; charset=utf-8"),
            ["/styles.css"] = Load(assembly, "wwwroot/styles.css", "text/css; charset=utf-8"),
        };
        return new StaticAssets(map.ToFrozenDictionary(StringComparer.Ordinal));
    }

    public IEnumerable<string> Paths => _assets.Keys;

    public bool TryGet(string? path, out Asset asset)
    {
        if (path is not null && _assets.TryGetValue(path, out var found))
        {
            asset = found;
            return true;
        }

        asset = default;
        return false;
    }

    private static Asset Load(Assembly assembly, string name, string contentType)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing from the build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new Asset(buffer.ToArray(), contentType);
    }

    public readonly record struct Asset(byte[] Content, string ContentType);
}
