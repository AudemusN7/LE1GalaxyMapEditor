using System.Globalization;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.Workflows.Editing;

public static class GalaxyMapRowAuthoring
{
    public static void PrepareNewRow(GalaxyMapLayer layer, GalaxyMapRow row)
    {
        var schema = CsvGalaxyMapLoader.GetCanonicalSchema(row.Table);
        layer.SetSchema(schema);
        var known = KnownColumns(row.Table);
        foreach (var header in schema.Headers.Skip(1).Where(header => !known.Contains(header)))
        {
            if (!row.ExtraFields.ContainsKey(header))
            {
                row.AddExtraField(header, GalaxyMapDefaults.ExtraValue(row.Table, header));
            }
        }

        row.Origin = new GalaxyMapRowOrigin(layer.Module, OverridesLowerLayer: false);
        var values = schema.Headers.Select((_, index) => index == 0
            ? row.RowId.ToString(CultureInfo.InvariantCulture)
            : string.Empty).ToArray();
        var snapshot = new CsvRowSnapshot(
            $"GalaxyMap_{row.Table}_part.csv",
            sourceRowNumber: 0,
            schema.Headers,
            values);
        for (var index = 0; index < schema.Headers.Count; index++)
        {
            snapshot.MarkDirty(index == 0 ? CsvRowSnapshot.RowIdColumnName : schema.Headers[index]);
        }

        row.CsvSnapshot = snapshot;
    }

    public static CsvRowSnapshot EnsureSnapshot(GalaxyMapRow row)
        => row.CsvSnapshot ?? throw new InvalidOperationException(
            $"{row.Table} row {row.RowId} has no source snapshot and cannot be written safely.");

    private static IReadOnlySet<string> KnownColumns(GalaxyMapTable table) => table switch
    {
        GalaxyMapTable.Cluster => Fields("Label", "X", "Y", "Name", "NameText", "SphereSize", "Background"),
        GalaxyMapTable.System => Fields("Label", "Cluster", "X", "Y", "Name", "NameText", "Scale", "ShowNebula"),
        GalaxyMapTable.Planet => Fields("Label", "System", "X", "Y", "Name", "NameText", "ActiveWorld",
            "Description", "ButtonLabel", "Map", "Scale", "RingColor", "OrbitRing", "SystemLevelType",
            "PlanetLevelType", "Event", "ImageIndex"),
        GalaxyMapTable.PlotPlanet => Fields("Code", "Name", "NameText"),
        GalaxyMapTable.Map => Fields("Map", "StartPoint"),
        GalaxyMapTable.Relay => Fields("StartCluster", "EndCluster"),
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
    };

    private static HashSet<string> Fields(params string[] fields)
        => new(fields, StringComparer.OrdinalIgnoreCase);
}
