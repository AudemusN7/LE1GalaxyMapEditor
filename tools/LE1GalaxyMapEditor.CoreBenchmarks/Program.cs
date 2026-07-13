using System.Diagnostics;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

const int iterations = 30;
var loader = new CsvGalaxyMapLoader();

Console.WriteLine("LE1 Galaxy Map Editor core-pipeline microbenchmarks");
Console.WriteLine($"Runtime: {Environment.Version}; process: {Environment.Is64BitProcess switch { true => "x64", false => "x86" }}");

var baseLayer = loader.LoadBuiltInLayer();
Console.WriteLine(
    $"BASEGAME rows: {string.Join(", ", Enum.GetValues<GalaxyMapTable>().Select(table => $"{table}={baseLayer.Rows(table).Count()}"))}");

var modules = CreateSyntheticModules(baseLayer, 8);
var workspace = new GalaxyMapWorkspace(baseLayer, modules);
var validator = new GalaxyMapValidator();

Measure("Load built-in layer", iterations, () => _ = loader.LoadBuiltInLayer());
Measure("Compose BASEGAME + 8 modules", iterations, workspace.Recompose);
Measure("Rebuild effective relationships", iterations, workspace.EffectiveDocument.RebuildRelationships);
Measure("Validate effective document", iterations, () => _ = validator.Validate(workspace.EffectiveDocument));
Measure("Validate complete workspace", iterations, () => _ = validator.Validate(workspace));
Measure(
    $"Clone undo state: 1 module ({CountRows(modules.Take(1))} rows)",
    iterations,
    () => _ = modules.Take(1).Select(GalaxyMapLayerCloner.Clone).ToArray());
Measure(
    $"Clone undo state: 8 modules ({CountRows(modules)} rows)",
    iterations,
    () => _ = modules.Select(GalaxyMapLayerCloner.Clone).ToArray());

static IReadOnlyList<GalaxyMapLayer> CreateSyntheticModules(GalaxyMapLayer baseLayer, int count)
{
    var layers = new List<GalaxyMapLayer>(count);
    for (var moduleIndex = 1; moduleIndex <= count; moduleIndex++)
    {
        var module = new GalaxyMapModule(
            $"Benchmark module {moduleIndex}",
            $"BENCH_{moduleIndex}",
            (ModuleColor)(((moduleIndex - 1) % 8) + 1),
            folderPath: null,
            isReadOnly: false,
            loadOrder: moduleIndex * 100,
            new ModuleIdReservations(
                new RowIdRange(100_000 + moduleIndex * 1_000, 100_999 + moduleIndex * 1_000),
                new RowIdRange(200_000 + moduleIndex * 1_000, 200_999 + moduleIndex * 1_000),
                new RowIdRange(300_000 + moduleIndex * 1_000, 300_999 + moduleIndex * 1_000),
                new RowIdRange(400_000 + moduleIndex * 1_000, 400_999 + moduleIndex * 1_000),
                new RowIdRange(500_000 + moduleIndex * 1_000, 500_999 + moduleIndex * 1_000)));
        var layer = new GalaxyMapLayer(module);

        foreach (var table in Enum.GetValues<GalaxyMapTable>())
        {
            var schema = baseLayer.GetSchema(table);
            if (schema is not null)
            {
                layer.SetSchema(new CsvTableSchema(table, schema.Headers));
            }

            var sourceRows = baseLayer.Rows(table).ToArray();
            // Each module overrides roughly a quarter of the table. Staggering the
            // starting index creates both unique and competing override chains.
            for (var index = moduleIndex % 4; index < sourceRows.Length; index += 4)
            {
                layer.Add(GalaxyMapRowCloner.Clone(sourceRows[index]));
            }

            layer.SetSourceRowOrder(table, layer.Rows(table).Select(row => row.RowId));
        }

        layers.Add(layer);
    }

    return layers;
}

static int CountRows(IEnumerable<GalaxyMapLayer> layers)
    => layers.Sum(layer => Enum.GetValues<GalaxyMapTable>().Sum(table => layer.Rows(table).Count()));

static void Measure(string name, int iterations, Action operation)
{
    operation();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var samples = new double[iterations];
    var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
    for (var index = 0; index < iterations; index++)
    {
        var started = Stopwatch.GetTimestamp();
        operation();
        samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
    Array.Sort(samples);
    Console.WriteLine(
        $"{name,-36} median {Percentile(samples, 0.50),8:N2} ms | p95 {Percentile(samples, 0.95),8:N2} ms | " +
        $"alloc {allocated / (double)iterations / 1024,10:N1} KiB/op");
}

static double Percentile(IReadOnlyList<double> sorted, double percentile)
{
    var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}
