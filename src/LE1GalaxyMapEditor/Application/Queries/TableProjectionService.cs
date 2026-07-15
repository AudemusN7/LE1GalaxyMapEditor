using System.Globalization;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.Workflows.Queries;

public sealed record TableColumn(string Name, int Ordinal, bool IsCanonical);

public sealed record CellInstanceValue(
    object? RawValue,
    string DisplayValue,
    string ModuleTag,
    ModuleColor ModuleColor);

public sealed record MergedTableCell(
    string DisplayValue,
    object? RawValue,
    string EffectiveModuleTag,
    ModuleColor EffectiveModuleColor,
    bool IsStaged,
    bool DiffersFromLowerInstance,
    IReadOnlyList<CellInstanceValue> OverrideChain);

public sealed record MergedTableRow(
    GalaxyMapRowKey Key,
    IReadOnlyDictionary<string, MergedTableCell> Cells);

public sealed record MergedTableSnapshot(
    GalaxyMapTable Table,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<MergedTableRow> Rows,
    long SessionRevision);

/// <summary>
/// Builds immutable table-viewer read models from the live editor session. It
/// never reparses CSV files, so staged values and effective provenance exactly
/// match the map, hierarchy, and inspector.
/// </summary>
public sealed class TableProjectionService(EditorSession session)
{
    public MergedTableSnapshot Project(GalaxyMapTable table)
    {
        var workspace = session.Workspace;
        if (workspace is null)
        {
            return new MergedTableSnapshot(table, CanonicalColumns(table), [], session.Revision);
        }

        var columns = Columns(workspace, table);
        var rows = EffectiveRows(workspace.EffectiveDocument, table)
            .OrderBy(row => row.RowId)
            .Select(row => ProjectRow(workspace, row, columns))
            .ToArray();
        return new MergedTableSnapshot(table, columns, rows, session.Revision);
    }

    private MergedTableRow ProjectRow(
        GalaxyMapWorkspace workspace,
        GalaxyMapRow effectiveRow,
        IReadOnlyList<TableColumn> columns)
    {
        var chain = workspace.GetOverrideChain(effectiveRow.Key);
        var winningModule = chain.LastOrDefault()?.Origin?.Module ??
                            effectiveRow.Origin?.Module ??
                            GalaxyMapModule.BaseGame;
        var isStaged = session.Changes.DirtyTables.TryGetValue(winningModule.Tag, out var dirtyTables) &&
                       dirtyTables.Contains(effectiveRow.Table);
        var cells = new Dictionary<string, MergedTableCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            var instances = chain.Select(row =>
            {
                var raw = GalaxyMapRowValues.RawValue(row, column.Name);
                var module = row.Origin?.Module ?? GalaxyMapModule.BaseGame;
                return new CellInstanceValue(raw, Display(raw), module.Tag, module.Color);
            }).ToArray();
            var rawValue = GalaxyMapRowValues.RawValue(effectiveRow, column.Name);
            var differs = instances.Length > 1 && !ValuesEqual(
                instances[^1].RawValue,
                instances[^2].RawValue);
            cells[column.Name] = new MergedTableCell(
                Display(rawValue),
                rawValue,
                winningModule.Tag,
                winningModule.Color,
                isStaged,
                differs,
                instances);
        }

        return new MergedTableRow(effectiveRow.Key, cells);
    }

    private static IReadOnlyList<TableColumn> Columns(GalaxyMapWorkspace workspace, GalaxyMapTable table)
    {
        var canonical = CanonicalColumns(table);
        var names = canonical.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = canonical.ToList();
        foreach (var layer in workspace.Layers)
        {
            var schemaHeaders = layer.GetSchema(table)?.Headers ?? [];
            foreach (var header in schemaHeaders.Concat(layer.Rows(table).SelectMany(row => row.ExtraFieldOrder)))
            {
                var name = string.IsNullOrWhiteSpace(header) ? CsvRowSnapshot.RowIdColumnName : header;
                if (names.Add(name))
                {
                    result.Add(new TableColumn(name, result.Count, IsCanonical: false));
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<TableColumn> CanonicalColumns(GalaxyMapTable table)
        => CsvGalaxyMapLoader.GetCanonicalSchema(table).Headers
            .Select((header, index) => new TableColumn(
                index == 0 && string.IsNullOrWhiteSpace(header) ? CsvRowSnapshot.RowIdColumnName : header,
                index,
                IsCanonical: true))
            .ToArray();

    private static IEnumerable<GalaxyMapRow> EffectiveRows(GalaxyMapDocument document, GalaxyMapTable table)
        => table switch
        {
            GalaxyMapTable.Cluster => document.Clusters,
            GalaxyMapTable.System => document.Systems,
            GalaxyMapTable.Planet => document.Planets,
            GalaxyMapTable.PlotPlanet => document.PlotPlanets,
            GalaxyMapTable.Map => document.Maps,
            GalaxyMapTable.Relay => document.Relays,
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
        };

    private static bool ValuesEqual(object? left, object? right)
        => left is double leftNumber && right is double rightNumber
            ? leftNumber.Equals(rightNumber)
            : Equals(left, right);

    private static string Display(object? value) => value switch
    {
        null => string.Empty,
        double number => GalaxyMapNumber.FormatDisplay(number),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}

internal static class GalaxyMapRowValues
{
    public static object? RawValue(GalaxyMapRow row, string column)
    {
        if (string.Equals(column, CsvRowSnapshot.RowIdColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return row.RowId;
        }

        var known = row switch
        {
            Cluster cluster => ClusterValue(cluster, column),
            GalaxySystem system => SystemValue(system, column),
            Planet planet => PlanetValue(planet, column),
            PlotPlanetEntry plotPlanet => PlotPlanetValue(plotPlanet, column),
            MapEntry map => MapValue(map, column),
            RelayConnection relay => RelayValue(relay, column),
            _ => Missing.Value
        };
        return ReferenceEquals(known, Missing.Value)
            ? row.ExtraFields.GetValueOrDefault(column)
            : known;
    }

    private static object? ClusterValue(Cluster row, string column) => column.ToUpperInvariant() switch
    {
        "LABEL" => row.Label,
        "X" => row.X,
        "Y" => row.Y,
        "NAME" => row.Name,
        "NAMETEXT" => row.NameText,
        "SPHERESIZE" => row.SphereSize,
        "BACKGROUND" => row.Background,
        _ => Missing.Value
    };

    private static object? SystemValue(GalaxySystem row, string column) => column.ToUpperInvariant() switch
    {
        "LABEL" => row.Label,
        "CLUSTER" => row.ClusterRowId,
        "X" => row.X,
        "Y" => row.Y,
        "NAME" => row.Name,
        "NAMETEXT" => row.NameText,
        "SCALE" => row.Scale,
        "SHOWNEBULA" => row.ShowNebula,
        _ => Missing.Value
    };

    private static object? PlanetValue(Planet row, string column) => column.ToUpperInvariant() switch
    {
        "LABEL" => row.Label,
        "SYSTEM" => row.SystemRowId,
        "X" => row.X,
        "Y" => row.Y,
        "NAME" => row.Name,
        "NAMETEXT" => row.NameText,
        "ACTIVEWORLD" => row.ActiveWorld,
        "DESCRIPTION" => row.Description,
        "BUTTONLABEL" => row.ButtonLabel,
        "MAP" => row.MapRowId,
        "SCALE" => row.Scale,
        "RINGCOLOR" => row.RingColor,
        "ORBITRING" => row.OrbitRing,
        "SYSTEMLEVELTYPE" => row.SystemLevelType,
        "PLANETLEVELTYPE" => row.PlanetLevelType,
        "EVENT" => row.Event,
        "IMAGEINDEX" => row.ImageIndex,
        _ => Missing.Value
    };

    private static object? PlotPlanetValue(PlotPlanetEntry row, string column) => column.ToUpperInvariant() switch
    {
        "CODE" => row.Code,
        "NAME" => row.Name,
        "NAMETEXT" => row.NameText,
        _ => Missing.Value
    };

    private static object? MapValue(MapEntry row, string column) => column.ToUpperInvariant() switch
    {
        "MAP" => row.MapName,
        "STARTPOINT" => row.StartPoint,
        _ => Missing.Value
    };

    private static object? RelayValue(RelayConnection row, string column) => column.ToUpperInvariant() switch
    {
        "STARTCLUSTER" => row.StartClusterEncoded,
        "ENDCLUSTER" => row.EndClusterEncoded,
        _ => Missing.Value
    };

    private static class Missing
    {
        public static readonly object Value = new();
    }
}
