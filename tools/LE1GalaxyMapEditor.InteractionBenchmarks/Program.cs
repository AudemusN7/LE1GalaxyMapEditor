using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Queries;

namespace LE1GalaxyMapEditor.InteractionBenchmarks;

internal static class Program
{
    private const int Iterations = 20;

    [STAThread]
    private static void Main()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();

        var textureRoot = Path.Combine(AppContext.BaseDirectory, "resources", "textures");
        var viewModel = new MainViewModel(
            new CsvGalaxyMapLoader(),
            new GalaxyMapTextureService(textureRoot),
            new GalaxyMapWorkspaceStore(Path.Combine(temporaryDirectory.Path, "workspace.json")),
            (_, modules) => modules.Single(module => !module.IsReadOnly));
        Require(viewModel.LoadBuiltIn(), "BASEGAME failed to load.");
        Require(viewModel.CreateModule(
            temporaryDirectory.Path,
            "Interaction benchmark",
            "INTERACTION_BENCH",
            ModuleColor.Cyan,
            Reservations()), "Benchmark module could not be created.");

        Console.WriteLine("LE1 Galaxy Map Editor interaction measurement gates");
        Console.WriteLine($"Runtime: {Environment.Version}; process: {(Environment.Is64BitProcess ? "x64" : "x86")}");
        Console.WriteLine("Timings are observational baselines, not pass/fail thresholds.\n");

        MeasureScalarRefresh(viewModel);
        MeasureStructuralRefresh(viewModel);
        MeasureTableProjection(viewModel);
        MeasureDeferredValidation(viewModel);
    }

    private static void MeasureScalarRefresh(MainViewModel viewModel)
    {
        var node = Flatten(viewModel.HierarchyRoots.Single())
            .Single(candidate => candidate.Model is Cluster { RowId: 1 });
        node.IsSelected = true;

        // Materialise the writable override before sampling the steady scalar path.
        FindInspectorField(viewModel, "Cluster", "NameText").Value = "Interaction benchmark warm-up";
        node = Flatten(viewModel.HierarchyRoots.Single())
            .Single(candidate => candidate.Model is Cluster { RowId: 1 });
        var root = viewModel.HierarchyRoots.Single();
        var allNodes = Flatten(root).Where(candidate => !candidate.IsGalaxyRoot).ToArray();
        var samples = new double[Iterations];
        var allocations = new long[Iterations];
        var nodeNotifications = new long[Iterations];
        var inspectorChanges = new long[Iterations];
        var sessionChanges = new long[Iterations];
        var compositions = new long[Iterations];

        for (var index = 0; index < Iterations; index++)
        {
            long currentNodeNotifications = 0;
            long currentInspectorChanges = 0;
            long currentSessionChanges = 0;
            foreach (var current in allNodes)
            {
                current.PropertyChanged += NodeChanged;
            }
            viewModel.Inspector.Sections.CollectionChanged += InspectorChanged;
            viewModel.Session.Changed += SessionChanged;

            var beforeComposition = viewModel.Workspace!.CompositionRevision;
            var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            FindInspectorField(viewModel, "Cluster", "NameText").Value = $"Interaction scalar {index}";
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[index] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
            compositions[index] = viewModel.Workspace.CompositionRevision - beforeComposition;
            nodeNotifications[index] = currentNodeNotifications;
            inspectorChanges[index] = currentInspectorChanges;
            sessionChanges[index] = currentSessionChanges;

            foreach (var current in allNodes)
            {
                current.PropertyChanged -= NodeChanged;
            }
            viewModel.Inspector.Sections.CollectionChanged -= InspectorChanged;
            viewModel.Session.Changed -= SessionChanged;

            void NodeChanged(object? sender, PropertyChangedEventArgs eventArgs) => currentNodeNotifications++;
            void InspectorChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => currentInspectorChanges++;
            void SessionChanged(object? sender, SessionChangedEventArgs eventArgs) => currentSessionChanges++;
        }

        Console.WriteLine("Scalar inspector edit (steady writable override)");
        WriteDistribution("elapsed", samples, "ms");
        WriteDistribution("allocated", allocations.Select(value => value / 1024d).ToArray(), "KiB");
        WriteDistribution("node PropertyChanged", nodeNotifications.Select(value => (double)value).ToArray(), "events");
        WriteDistribution("inspector collection", inspectorChanges.Select(value => (double)value).ToArray(), "events");
        WriteDistribution("session revisions", sessionChanges.Select(value => (double)value).ToArray(), "events");
        WriteDistribution("document compositions", compositions.Select(value => (double)value).ToArray(), "calls");
        Console.WriteLine($"  hierarchy identity retained: {ReferenceEquals(root, viewModel.HierarchyRoots.Single()) && ReferenceEquals(node, Flatten(viewModel.HierarchyRoots.Single()).Single(candidate => candidate.Model is Cluster { RowId: 1 }))}\n");
    }

    private static void MeasureStructuralRefresh(MainViewModel viewModel)
    {
        var root = viewModel.HierarchyRoots.Single();
        long hierarchyCollectionChanges = 0;
        long sessionChanges = 0;
        viewModel.HierarchyRoots.CollectionChanged += HierarchyChanged;
        viewModel.Session.Changed += SessionChanged;

        var beforeComposition = viewModel.Workspace!.CompositionRevision;
        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        viewModel.AddClusterCommand.Execute(null);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

        viewModel.HierarchyRoots.CollectionChanged -= HierarchyChanged;
        viewModel.Session.Changed -= SessionChanged;
        Console.WriteLine("Structural add-Cluster refresh");
        Console.WriteLine($"  elapsed: {elapsed.TotalMilliseconds:N2} ms");
        Console.WriteLine($"  allocated: {allocated / 1024d:N1} KiB");
        Console.WriteLine($"  document compositions: {viewModel.Workspace.CompositionRevision - beforeComposition}");
        Console.WriteLine($"  session revisions: {sessionChanges}");
        Console.WriteLine($"  hierarchy collection events: {hierarchyCollectionChanges}");
        Console.WriteLine($"  hierarchy root retained: {ReferenceEquals(root, viewModel.HierarchyRoots.Single())}\n");

        void HierarchyChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => hierarchyCollectionChanges++;
        void SessionChanged(object? sender, SessionChangedEventArgs eventArgs) => sessionChanges++;
    }

    private static void MeasureTableProjection(MainViewModel viewModel)
    {
        var projection = new TableProjectionService(viewModel.Session);
        Measure("Planet table projection", () => projection.Project(GalaxyMapTable.Planet));

        var table = new TableViewerViewModel(
            projection,
            (key, _, _) => WorkflowResult.Failure("Measurement only.", key),
            () => false)
        {
            SelectedTable = GalaxyMapTable.Planet
        };
        Measure("Planet projection + table VM", () => table.RefreshIfNeeded(force: true));

        var previousColumns = table.Columns.ToArray();
        var previousRows = table.Rows.ToDictionary(row => row.Key);
        table.RefreshIfNeeded(force: true);
        var retainedColumns = table.Columns.Zip(previousColumns).Count(pair => ReferenceEquals(pair.First, pair.Second));
        var retainedRows = table.Rows.Count(row => previousRows.TryGetValue(row.Key, out var previous) && ReferenceEquals(previous, row));
        var stableKeys = table.Rows.Count(row => previousRows.ContainsKey(row.Key));
        Console.WriteLine("Table refresh identity baseline");
        Console.WriteLine($"  columns retained by reference: {retainedColumns}/{table.Columns.Count}");
        Console.WriteLine($"  rows retained by reference: {retainedRows}/{table.Rows.Count}");
        Console.WriteLine($"  row keys still resolvable: {stableKeys}/{table.Rows.Count}\n");
    }

    private static void MeasureDeferredValidation(MainViewModel viewModel)
    {
        long diagnosticRefreshes = 0;
        viewModel.ValidationDiagnostics.CollectionChanged += DiagnosticsChanged;
        var node = Flatten(viewModel.HierarchyRoots.Single())
            .First(candidate => candidate.Model is Cluster { RowId: 1 });
        node.IsSelected = true;
        for (var index = 0; index < 5; index++)
        {
            FindInspectorField(viewModel, "Cluster", "NameText").Value = $"Validation burst {index}";
        }

        PumpDispatcher(TimeSpan.FromMilliseconds(400));
        viewModel.ValidationDiagnostics.CollectionChanged -= DiagnosticsChanged;
        Console.WriteLine("Deferred validation after five-edit burst");
        Console.WriteLine($"  diagnostics collection refreshes: {diagnosticRefreshes}\n");

        void DiagnosticsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => diagnosticRefreshes++;
    }

    private static InspectorFieldViewModel FindInspectorField(MainViewModel viewModel, string section, string field)
        => viewModel.Inspector.Sections.Single(item => item.Title == section)
            .Fields.Single(item => item.Name == field);

    private static IEnumerable<HierarchyNodeViewModel> Flatten(HierarchyNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Measure(string name, Action operation)
    {
        operation();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new double[Iterations];
        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Iterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            operation();
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
        Array.Sort(samples);
        Console.WriteLine($"{name}: median {Percentile(samples, 0.50):N2} ms; p95 {Percentile(samples, 0.95):N2} ms; alloc {allocated / (double)Iterations / 1048576d:N2} MiB/op");
    }

    private static void WriteDistribution(string name, double[] samples, string unit)
    {
        Array.Sort(samples);
        Console.WriteLine($"  {name}: median {Percentile(samples, 0.50):N2} {unit}; p95 {Percentile(samples, 0.95):N2} {unit}");
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static ModuleIdReservations Reservations() => new(
        Cluster: new RowIdRange(100, 199),
        System: new RowIdRange(1000, 1999),
        Planet: new RowIdRange(10000, 19999),
        Map: new RowIdRange(1000, 1999),
        Relay: new RowIdRange(1000, 1999));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"LE1GalaxyMapEditor-Benchmark-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // The operating system may briefly retain a file handle after the benchmark exits.
            }
            catch (UnauthorizedAccessException)
            {
                // A temporary benchmark directory is safe to leave for the OS cleanup policy.
            }
        }
    }
}
