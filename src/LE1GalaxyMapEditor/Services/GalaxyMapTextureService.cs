using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Services;

/// <summary>
/// Resolves the texture names used by the galaxy-map CSV and decodes the game
/// textures without applying any source alpha channel.
/// </summary>
public sealed partial class GalaxyMapTextureService
{
    public const string GalaxyAssetName = "galaxy.jpg";
    public const string SystemAssetName = "stars01.png";
    private const int MaximumCachedTextures = 6;

    private readonly Func<string, Stream?> _openTextureStream;
    private readonly Action<string, TimeSpan>? _decodeObserver;
    private readonly Dictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _leastRecentlyUsed = [];
    private readonly object _cacheLock = new();

    public GalaxyMapTextureService()
        : this(CreateDirectoryStreamFactory(ApplicationResourcePaths.TextureDirectory), null)
    {
    }

    internal GalaxyMapTextureService(Action<string, TimeSpan> decodeObserver)
        : this(
            CreateDirectoryStreamFactory(ApplicationResourcePaths.TextureDirectory),
            decodeObserver ?? throw new ArgumentNullException(nameof(decodeObserver)))
    {
    }

    /// <summary>
    /// Creates a loader backed by a texture directory. The default application
    /// instance points this at the deployable resources\textures folder.
    /// </summary>
    public GalaxyMapTextureService(string textureDirectory)
        : this(CreateDirectoryStreamFactory(textureDirectory), null)
    {
    }

    private GalaxyMapTextureService(
        Func<string, Stream?> openTextureStream,
        Action<string, TimeSpan>? decodeObserver)
    {
        _openTextureStream = openTextureStream;
        _decodeObserver = decodeObserver;
    }

    public BitmapSource? GetGalaxyTexture() => LoadTexture(GalaxyAssetName);

    public BitmapSource? GetSystemTexture() => LoadTexture(SystemAssetName);

    public BitmapSource? GetClusterTexture(string? backgroundReference)
    {
        var assetName = ResolveClusterAssetName(backgroundReference);
        return assetName is null ? null : LoadTexture(assetName);
    }

    public BitmapSource? GetModuleClusterTexture(GalaxyMapModule module, int clusterRowId)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.FolderPath is null ||
            !module.ClusterTextureLinks.TryGetValue(clusterRowId, out var relativePath))
        {
            return null;
        }

        var fullPath = ResolveModuleTexturePath(module, relativePath);
        return fullPath is null ? null : LoadFileTexture(fullPath);
    }

    public BitmapSource? LoadTextureBytes(string cacheKey, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(contents);
        return LoadCached($"memory:{cacheKey}", () => new MemoryStream(contents, writable: false));
    }

    public static string? ResolveModuleTexturePath(GalaxyMapModule module, string relativePath)
    {
        if (module.FolderPath is null || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(module.FolderPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    public static string? ResolveClusterAssetName(string? backgroundReference)
    {
        if (string.IsNullOrWhiteSpace(backgroundReference))
        {
            return null;
        }

        var value = backgroundReference.Trim();
        if (value.Contains('/') || value.Contains('\\'))
        {
            return null;
        }

        if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }
        else if (value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^5];
        }

        var finalDot = value.LastIndexOf('.');
        var objectName = finalDot >= 0 ? value[(finalDot + 1)..] : value;
        var match = ClusterAssetPattern().Match(objectName);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 99)
        {
            return null;
        }

        return $"Cluster{number:00}.jpg";
    }

    private BitmapSource? LoadTexture(string assetName)
        => LoadCached($"asset:{assetName}", () => _openTextureStream(assetName));

    private BitmapSource? LoadFileTexture(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                return null;
            }

            // The fingerprint prevents a committed replacement texture at the
            // same path from being hidden behind a stale cache entry.
            var cacheKey = $"file:{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            return LoadCached(cacheKey, () => OpenSharedRead(file.FullName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private BitmapSource? LoadCached(string cacheKey, Func<Stream?> openStream)
    {
        lock (_cacheLock)
        {
            if (TryGetCached(cacheKey, out var cached))
            {
                return cached;
            }
        }

        BitmapSource? texture;
        var decodeClock = Stopwatch.StartNew();
        try
        {
            using var stream = openStream();
            if (stream is null)
            {
                return null;
            }

            var decoded = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            // Bgr32 deliberately has no alpha channel. WPF retains the original
            // hidden RGB bytes while treating every pixel as fully opaque.
            texture = new FormatConvertedBitmap(decoded, PixelFormats.Bgr32, null, 0);
            texture.Freeze();
            decodeClock.Stop();
            _decodeObserver?.Invoke(cacheKey, decodeClock.Elapsed);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or FileFormatException
                                           or NotSupportedException
                                           or InvalidOperationException
                                           or ArgumentException)
        {
            return null;
        }

        lock (_cacheLock)
        {
            // Another caller may have completed the same decode while this one
            // was outside the lock. Prefer the established cached instance.
            if (TryGetCached(cacheKey, out var cached))
            {
                return cached;
            }

            var node = _leastRecentlyUsed.AddFirst(cacheKey);
            _cache[cacheKey] = new CacheEntry(texture, node);
            while (_cache.Count > MaximumCachedTextures && _leastRecentlyUsed.Last is { } oldest)
            {
                _leastRecentlyUsed.RemoveLast();
                _cache.Remove(oldest.Value);
            }
        }

        return texture;
    }

    private bool TryGetCached(string cacheKey, out BitmapSource? texture)
    {
        if (!_cache.TryGetValue(cacheKey, out var entry))
        {
            texture = null;
            return false;
        }

        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddFirst(entry.Node);
        texture = entry.Texture;
        return true;
    }

    private static Func<string, Stream?> CreateDirectoryStreamFactory(string textureDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textureDirectory);
        var root = Path.GetFullPath(textureDirectory);
        return assetName =>
        {
            if (Path.IsPathRooted(assetName) ||
                !string.Equals(Path.GetFileName(assetName), assetName, StringComparison.Ordinal))
            {
                return null;
            }

            var path = Path.Combine(root, assetName);
            return File.Exists(path) ? OpenSharedRead(path) : null;
        };
    }

    private static FileStream OpenSharedRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private sealed record CacheEntry(BitmapSource Texture, LinkedListNode<string> Node);

    [GeneratedRegex("^Cluster([0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClusterAssetPattern();
}
