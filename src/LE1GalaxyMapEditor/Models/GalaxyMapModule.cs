using System.IO;
using System.Text.RegularExpressions;

namespace LE1GalaxyMapEditor.Models;

public enum ModuleColor
{
    BaseGameBlue,
    Red,
    Pink,
    Purple,
    Cyan,
    Yellow,
    Green,
    White,
    Magenta
}

public readonly record struct RowIdRange
{
    public RowIdRange(int start, int end)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "A reserved row ID range cannot start below zero.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "A reserved row ID range cannot end before it starts.");
        }

        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }
    public long Length => (long)End - Start + 1;

    public bool Contains(int rowId) => rowId >= Start && rowId <= End;

    public bool Overlaps(RowIdRange other) => Start <= other.End && other.Start <= End;

    public override string ToString() => $"{Start}-{End}";
}

/// <summary>
/// Per-table reservations for rows created by a module. PlotPlanet deliberately
/// uses the Planet reservation because both records must share a row ID.
/// </summary>
public sealed record ModuleIdReservations(
    RowIdRange? Cluster = null,
    RowIdRange? System = null,
    RowIdRange? Planet = null,
    RowIdRange? Map = null,
    RowIdRange? Relay = null)
{
    public static ModuleIdReservations Empty { get; } = new();

    public RowIdRange? GetRange(GalaxyMapTable table) => table switch
    {
        GalaxyMapTable.Cluster => Cluster,
        GalaxyMapTable.System => System,
        GalaxyMapTable.Planet or GalaxyMapTable.PlotPlanet => Planet,
        GalaxyMapTable.Map => Map,
        GalaxyMapTable.Relay => Relay,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
    };
}

/// <summary>
/// Immutable metadata for a mounted galaxy-map layer. BASEGAME is the only
/// module allowed to use blue and is always read-only.
/// </summary>
public sealed partial class GalaxyMapModule
{
    public const string BaseGameTag = "BASEGAME";

    public static GalaxyMapModule BaseGame { get; } = new(
        "Mass Effect 1 Base Game",
        BaseGameTag,
        ModuleColor.BaseGameBlue,
        folderPath: null,
        isReadOnly: true,
        loadOrder: 0,
        ModuleIdReservations.Empty,
        clusterTextureLinks: null,
        isBaseGame: true);

    public GalaxyMapModule(
        string name,
        string tag,
        ModuleColor color,
        string? folderPath,
        bool isReadOnly,
        int loadOrder,
        ModuleIdReservations? reservations = null,
        IReadOnlyDictionary<int, string>? clusterTextureLinks = null)
        : this(name, tag, color, folderPath, isReadOnly, loadOrder,
            reservations ?? ModuleIdReservations.Empty, clusterTextureLinks, isBaseGame: false)
    {
    }

    private GalaxyMapModule(
        string name,
        string tag,
        ModuleColor color,
        string? folderPath,
        bool isReadOnly,
        int loadOrder,
        ModuleIdReservations reservations,
        IReadOnlyDictionary<int, string>? clusterTextureLinks,
        bool isBaseGame)
    {
        Name = RequireValue(name, nameof(name));
        Tag = RequireValue(tag, nameof(tag)).ToUpperInvariant();
        if (!IsValidTag(Tag))
        {
            throw new ArgumentException(
                "A module tag must contain only letters, numbers, underscores, or hyphens.", nameof(tag));
        }

        if (isBaseGame)
        {
            if (Tag != BaseGameTag || color != ModuleColor.BaseGameBlue || !isReadOnly)
            {
                throw new ArgumentException("BASEGAME must be blue and read-only.");
            }
        }
        else if (Tag == BaseGameTag || color == ModuleColor.BaseGameBlue)
        {
            throw new ArgumentException("BASEGAME and its blue colour are reserved for the built-in source data.");
        }

        Color = color;
        FolderPath = string.IsNullOrWhiteSpace(folderPath) ? null : Path.GetFullPath(folderPath);
        IsReadOnly = isReadOnly;
        if (loadOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(loadOrder), "Mount priority cannot be negative.");
        }

        LoadOrder = loadOrder;
        Reservations = reservations;
        ClusterTextureLinks = new Dictionary<int, string>(
            clusterTextureLinks ?? new Dictionary<int, string>(),
            EqualityComparer<int>.Default);
        IsBaseGame = isBaseGame;
    }

    public string Name { get; }
    public string Tag { get; }
    public ModuleColor Color { get; }
    public string? FolderPath { get; }
    public bool IsReadOnly { get; }
    public int LoadOrder { get; }
    public ModuleIdReservations Reservations { get; }
    public IReadOnlyDictionary<int, string> ClusterTextureLinks { get; }
    public bool IsBaseGame { get; }

    public GalaxyMapModule With(
        string? name = null,
        string? tag = null,
        ModuleColor? color = null,
        int? loadOrder = null,
        ModuleIdReservations? reservations = null,
        IReadOnlyDictionary<int, string>? clusterTextureLinks = null)
    {
        if (IsBaseGame)
        {
            throw new InvalidOperationException("BASEGAME module metadata cannot be edited.");
        }

        return new GalaxyMapModule(
            name ?? Name,
            tag ?? Tag,
            color ?? Color,
            FolderPath,
            IsReadOnly,
            loadOrder ?? LoadOrder,
            reservations ?? Reservations,
            clusterTextureLinks ?? ClusterTextureLinks);
    }

    public override string ToString() => $"{Name} [{Tag}]";

    public static bool IsValidTag(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           TagPattern().IsMatch(value.Trim().ToUpperInvariant());

    public static string SuggestTag(string? value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in (value ?? string.Empty).Trim().ToUpperInvariant())
        {
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var tag = builder.ToString().Trim('_', '-');
        return tag.Length == 0 ? "MODULE" : tag;
    }

    private static string RequireValue(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    [GeneratedRegex("^[A-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}

public sealed record GalaxyMapRowOrigin(GalaxyMapModule Module, bool OverridesLowerLayer)
{
    public string ModuleTag => Module.Tag;
    public ModuleColor Color => Module.Color;
    public bool IsBaseGame => Module.IsBaseGame;
    public bool IsReadOnly => Module.IsReadOnly;
}
