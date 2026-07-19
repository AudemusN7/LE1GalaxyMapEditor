using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Ports;

namespace LE1GalaxyMapEditor.Tests;

/// <summary>
/// Phase-zero gates for refresh fan-out. They assert semantic boundaries and
/// upper bounds rather than preserving today's known over-refreshing as desired
/// behaviour. The printed counts are the baseline that later optimisation phases
/// should reduce.
/// </summary>
internal static class PhaseZeroRefreshAndTableTests
{
    public static void Register(Action<string, Action> run)
    {
        run("Phase 0: scalar and structural refresh boundaries", ScalarAndStructuralRefreshBoundaries);
        run("Phase 0: table invalidation scope and projection identity", TableInvalidationScopeAndProjectionIdentity);
        run("Optimisation: table invalidation survives attachment and reopening", TableInvalidationSurvivesAttachmentAndReopening);
    }

    private static void ScalarAndStructuralRefreshBoundaries()
    {
        WithEditor((viewModel, scheduler, folder) =>
        {
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(
                    folder,
                    "Refresh Gate",
                    "REFRESH_GATE",
                    ModuleColor.Cyan,
                    TestReservations()),
                "writable module is created");

            var selectedKey = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            var selectedNode = FindNode(viewModel, selectedKey);
            selectedNode.IsSelected = true;
            viewModel.ToggleTableViewCommand.Execute(null);
            Equal(GalaxyMapTable.Cluster, viewModel.TableViewer.SelectedTable,
                "Cluster table is selected for the scalar projection gate");

            var beforeNodes = RowNodes(viewModel).ToDictionary(node => node.Model!.Key);
            var beforeRoot = viewModel.HierarchyRoots.Single();
            var beforeDocument = viewModel.Document!;
            var beforeComposition = viewModel.Workspace!.CompositionRevision;
            var beforeRevision = viewModel.Session.Revision;
            var beforeScheduleCount = scheduler.ScheduleCount;
            var beforeColumnDefinitions = viewModel.TableViewer.Columns.ToArray();

            using (var probe = new RefreshProbe(viewModel))
            {
                var xField = viewModel.Inspector.Sections
                    .Single(section => section.Title == "Cluster")
                    .Fields.Single(field => field.Name == "X");
                var originalX = ((Cluster)selectedNode.Model!).X;
                var replacementX = originalX < 0.5 ? "0.73" : "0.27";
                xField.Value = replacementX;

                Equal(beforeRevision + 1, viewModel.Session.Revision,
                    "one scalar edit publishes one session revision");
                Equal(1, probe.SessionChanges.Count, "one scalar session change notification");
                var scalarImpact = probe.SessionChanges.Single().Impact;
                True(!scalarImpact.IsStructural, "ordinary scalar edit is non-structural");
                SetEqual([GalaxyMapTable.Cluster], scalarImpact.Tables, "scalar impact table");
                SetEqual([selectedKey], scalarImpact.Rows, "scalar impact row");

                var compositionDelta = viewModel.Workspace.CompositionRevision - beforeComposition;
                True(compositionDelta is >= 0 and <= 1,
                    "one logical scalar edit composes at most once");
                True(!ReferenceEquals(beforeDocument, viewModel.Document) || compositionDelta == 0,
                    "a composed scalar edit attaches its new effective document");
                NearlyEqual(double.Parse(replacementX, System.Globalization.CultureInfo.InvariantCulture),
                    viewModel.Document!.ClustersByRowId[1].X,
                    "scalar value reaches the effective document");

                Equal(0, probe.HierarchyRootChanges,
                    "scalar refresh does not replace the hierarchy root collection");
                True(ReferenceEquals(beforeRoot, viewModel.HierarchyRoots.Single()),
                    "scalar refresh preserves the galaxy root identity");
                foreach (var (key, node) in beforeNodes)
                {
                    True(ReferenceEquals(node, FindNode(viewModel, key)),
                        $"scalar refresh preserves hierarchy node identity for {key}");
                }

                True(probe.NodePropertyChanges <= beforeNodes.Count * 24,
                    "scalar hierarchy notifications remain within the measured phase-zero ceiling");
                True(probe.InspectorResetCount <= 1,
                    "one scalar edit reconstructs the inspector at most once");
                Equal(0, probe.RelayStateNotifications,
                    "ordinary document attachment does not republish an already-idle Relay state");
                True(probe.TableColumnNotifications <= 1,
                    "one scalar edit publishes table columns at most once");
                True(probe.TableRowResets <= 1,
                    "one scalar edit performs at most one full table-row reset");
                SequenceEqual(beforeColumnDefinitions, viewModel.TableViewer.Columns,
                    "scalar refresh preserves table column definitions");
                Equal(GalaxyMapTable.Cluster, viewModel.TableViewer.SelectedTable,
                    "scalar refresh preserves selected table");
                Equal(viewModel.Session.Revision, viewModel.TableViewer.SessionRevision,
                    "visible table catches up to the scalar session revision");

                Equal(beforeScheduleCount + 1, scheduler.ScheduleCount,
                    "scalar edit schedules validation once");
                Equal(0, probe.ValidationResets,
                    "deferred scalar validation does not run synchronously");
                True(scheduler.HasPendingAction, "scalar validation is pending");
                scheduler.RunPending();
                Equal(1, probe.ValidationResets,
                    "coalesced scalar validation publishes one result");

                Console.WriteLine(
                    $"      scalar baseline: compositions={compositionDelta}, " +
                    $"node notifications={probe.NodePropertyChanges}, inspector resets={probe.InspectorResetCount}, " +
                    $"table column publications={probe.TableColumnNotifications}, table row resets={probe.TableRowResets}");
            }

            beforeDocument = viewModel.Document!;
            beforeComposition = viewModel.Workspace.CompositionRevision;
            beforeRevision = viewModel.Session.Revision;
            beforeScheduleCount = scheduler.ScheduleCount;
            beforeRoot = viewModel.HierarchyRoots.Single();
            var beforeRowCount = RowNodes(viewModel).Count;

            using (var probe = new RefreshProbe(viewModel))
            {
                viewModel.AddClusterCommand.Execute(null);

                Equal(beforeRevision + 1, viewModel.Session.Revision,
                    "one structural edit publishes one session revision");
                Equal(1, probe.SessionChanges.Count, "one structural session change notification");
                var structuralImpact = probe.SessionChanges.Single().Impact;
                True(structuralImpact.IsStructural, "row creation is structural");
                True(structuralImpact.Tables.SetEquals([GalaxyMapTable.Cluster]),
                    "structural impact names its table");

                var compositionDelta = viewModel.Workspace.CompositionRevision - beforeComposition;
                Equal(1L, compositionDelta,
                    "one structural edit performs exactly one document composition");
                True(!ReferenceEquals(beforeDocument, viewModel.Document),
                    "structural edit attaches a new effective document");
                Equal(beforeRowCount + 1, RowNodes(viewModel).Count,
                    "structural edit adds exactly one hierarchy row");
                var createdKey = structuralImpact.Rows.Single();
                NotNull(viewModel.Workspace.Resolve(createdKey), "structural impact row resolves in the workspace");
                NotNull(FindNode(viewModel, createdKey), "structural impact row exists in the hierarchy");

                True(probe.HierarchyRootChanges <= 2,
                    "one structural edit rebuilds or updates hierarchy roots at most once");
                True(probe.InspectorResetCount <= 1,
                    "one structural edit reconstructs the inspector at most once");
                True(probe.TableColumnNotifications <= 1,
                    "one structural edit publishes table columns at most once");
                True(probe.TableRowResets <= 1,
                    "one structural edit performs at most one full table-row reset");
                True(scheduler.ScheduleCount - beforeScheduleCount <= 1,
                    "one structural edit schedules validation at most once");
                True(probe.ValidationResets <= 1,
                    "one structural edit publishes validation at most once");

                Console.WriteLine(
                    $"      structural baseline: compositions={compositionDelta}, root changes={probe.HierarchyRootChanges}, " +
                    $"root replaced={!ReferenceEquals(beforeRoot, viewModel.HierarchyRoots.Single())}, " +
                    $"inspector resets={probe.InspectorResetCount}, table row resets={probe.TableRowResets}");
            }
        });
    }

    private static void TableInvalidationScopeAndProjectionIdentity()
    {
        WithEditor((viewModel, _, _) =>
        {
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            viewModel.ToggleTableViewCommand.Execute(null);
            var table = viewModel.TableViewer;
            Equal(GalaxyMapTable.Cluster, table.SelectedTable, "initial selected table");

            var tabs = table.Tabs.ToArray();
            var columns = table.Columns;
            var rows = table.Rows.ToArray();
            var revision = table.SessionRevision;
            var columnNotifications = 0;
            var rowResets = 0;
            PropertyChangedEventHandler propertyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(TableViewerViewModel.Columns))
                {
                    columnNotifications++;
                }
            };
            NotifyCollectionChangedEventHandler rowsChanged = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    rowResets++;
                }
            };
            table.PropertyChanged += propertyChanged;
            table.Rows.CollectionChanged += rowsChanged;
            try
            {
                table.Invalidate(ChangeImpact.For(
                    [GalaxyMapTable.Planet],
                    [new GalaxyMapRowKey(GalaxyMapTable.Planet, 1)],
                    isStructural: false));
                table.RefreshIfNeeded();

                Equal(0, columnNotifications,
                    "an unrelated scalar impact does not reproject selected-table columns");
                Equal(0, rowResets,
                    "an unrelated scalar impact does not reproject selected-table rows");
                True(ReferenceEquals(columns, table.Columns),
                    "unrelated invalidation preserves the column-list identity");
                SequenceReferenceEqual(rows, table.Rows,
                    "unrelated invalidation preserves every row view-model identity");
                Equal(revision, table.SessionRevision,
                    "unrelated invalidation leaves the projected revision unchanged");

                table.Invalidate(ChangeImpact.For(
                    [GalaxyMapTable.Cluster],
                    [new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1)],
                    isStructural: false));
                table.RefreshIfNeeded();

                Equal(GalaxyMapTable.Cluster, table.SelectedTable,
                    "selected-table refresh preserves tab selection");
                SequenceReferenceEqual(tabs, table.Tabs,
                    "projection refresh preserves tab view-model identities");
                SequenceEqual(columns, table.Columns,
                    "projection refresh preserves column definitions and order");
                SequenceEqual(rows.Select(row => row.Key), table.Rows.Select(row => row.Key),
                    "projection refresh preserves stable row keys and order");
                Equal(0, columnNotifications,
                    "an unchanged schema retains the existing table columns");
                Equal(1, rowResets,
                    "one selected-table invalidation resets rows exactly once");
                True(ReferenceEquals(columns, table.Columns),
                    "an unchanged schema preserves the column-list identity");

                Console.WriteLine(
                    $"      table baseline: selected refresh column identity preserved={ReferenceEquals(columns, table.Columns)}, " +
                    $"first-row identity preserved={ReferenceEquals(rows[0], table.Rows[0])}, row resets={rowResets}");
            }
            finally
            {
                table.PropertyChanged -= propertyChanged;
                table.Rows.CollectionChanged -= rowsChanged;
            }
        });
    }

    private static void TableInvalidationSurvivesAttachmentAndReopening()
    {
        WithEditor((viewModel, _, folder) =>
        {
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(
                    folder,
                    "Table Refresh Gate",
                    "TABLE_REFRESH_GATE",
                    ModuleColor.Cyan,
                    TestReservations()),
                "writable module is created");
            viewModel.ToggleTableViewCommand.Execute(null);

            var table = viewModel.TableViewer;
            Equal(GalaxyMapTable.Cluster, table.SelectedTable, "Cluster table is selected");
            var columns = table.Columns;
            var rows = table.Rows.ToArray();
            var columnNotifications = 0;
            var rowResets = 0;
            PropertyChangedEventHandler propertyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(TableViewerViewModel.Columns))
                {
                    columnNotifications++;
                }
            };
            NotifyCollectionChangedEventHandler rowsChanged = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    rowResets++;
                }
            };
            table.PropertyChanged += propertyChanged;
            table.Rows.CollectionChanged += rowsChanged;
            try
            {
                var planetNode = RowNodes(viewModel).First(node => node.Model is Planet);
                planetNode.IsSelected = true;
                var xField = viewModel.Inspector.Sections
                    .Single(section => section.Title == "System-view display")
                    .Fields.Single(field => field.Name == "X");
                var originalX = ((Planet)planetNode.Model!).X;
                xField.Value = originalX < 0.5 ? "0.73" : "0.27";

                Equal(0, columnNotifications,
                    "a visible unrelated Planet edit does not republish Cluster columns");
                Equal(0, rowResets,
                    "a visible unrelated Planet edit does not reset Cluster rows");
                True(ReferenceEquals(columns, table.Columns),
                    "unrelated document attachment preserves Cluster column identity");
                SequenceReferenceEqual(rows, table.Rows,
                    "unrelated document attachment preserves Cluster row identities");

                viewModel.ToggleTableViewCommand.Execute(null);
                viewModel.ToggleTableViewCommand.Execute(null);
                Equal(0, columnNotifications,
                    "reopening an unchanged table does not republish columns");
                Equal(0, rowResets,
                    "reopening an unchanged table does not reset rows");

                viewModel.ToggleTableViewCommand.Execute(null);
                var clusterNode = FindNode(viewModel, new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1));
                clusterNode.IsSelected = true;
                var clusterNameField = viewModel.Inspector.Sections
                    .Single(section => section.Title == "Cluster")
                    .Fields.Single(field => field.Name == "NameText");
                clusterNameField.Value = "Hidden table refresh gate";
                Equal(0, rowResets,
                    "a relevant edit remains deferred while the table is hidden");

                viewModel.ToggleTableViewCommand.Execute(null);
                Equal(0, columnNotifications,
                    "the deferred refresh retains unchanged columns");
                Equal(1, rowResets,
                    "reopening after a relevant hidden edit performs one projection");
                True(ReferenceEquals(columns, table.Columns),
                    "the deferred refresh preserves the stable column list");
                Equal(viewModel.Session.Revision, table.SessionRevision,
                    "the reopened table catches up to the active editor session");
            }
            finally
            {
                table.PropertyChanged -= propertyChanged;
                table.Rows.CollectionChanged -= rowsChanged;
            }
        });
    }

    private static IReadOnlyList<HierarchyNodeViewModel> RowNodes(MainViewModel viewModel)
        => Flatten(viewModel.HierarchyRoots).Where(node => node.Model is not null).ToArray();

    private static IEnumerable<HierarchyNodeViewModel> Flatten(IEnumerable<HierarchyNodeViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var descendant in Flatten(root.Children))
            {
                yield return descendant;
            }
        }
    }

    private static HierarchyNodeViewModel FindNode(MainViewModel viewModel, GalaxyMapRowKey key)
        => RowNodes(viewModel).Single(node => node.Model!.Key == key);

    private static ModuleIdReservations TestReservations()
        => new(
            new RowIdRange(100, 199),
            new RowIdRange(1000, 1099),
            new RowIdRange(10000, 10099),
            new RowIdRange(1000, 1099),
            new RowIdRange(1000, 1099));

    private static void WithEditor(Action<MainViewModel, ManualDeferredScheduler, string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LE1GalaxyMapEditorPhaseZero", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var scheduler = new ManualDeferredScheduler();
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                workspaceStore: new GalaxyMapWorkspaceStore(Path.Combine(folder, "workspace.json")),
                deferredScheduler: scheduler);
            test(viewModel, scheduler, folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class ManualDeferredScheduler : IDeferredScheduler
    {
        private Action? _pending;

        public int ScheduleCount { get; private set; }
        public int CancelCount { get; private set; }
        public int ExecutionCount { get; private set; }
        public bool HasPendingAction => _pending is not null;

        public void Schedule(TimeSpan delay, Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            ScheduleCount++;
            _pending = action;
        }

        public void Cancel()
        {
            CancelCount++;
            _pending = null;
        }

        public void RunPending()
        {
            var pending = _pending ?? throw new InvalidOperationException("No deferred validation was pending.");
            _pending = null;
            ExecutionCount++;
            pending();
        }

        public void Dispose() => Cancel();
    }

    private sealed class RefreshProbe : IDisposable
    {
        private readonly MainViewModel _viewModel;
        private readonly IReadOnlyList<HierarchyNodeViewModel> _nodes;
        private readonly EventHandler<SessionChangedEventArgs> _sessionChanged;
        private readonly NotifyCollectionChangedEventHandler _hierarchyChanged;
        private readonly NotifyCollectionChangedEventHandler _inspectorChanged;
        private readonly NotifyCollectionChangedEventHandler _validationChanged;
        private readonly NotifyCollectionChangedEventHandler _tableRowsChanged;
        private readonly PropertyChangedEventHandler _tablePropertyChanged;
        private readonly PropertyChangedEventHandler _nodePropertyChanged;
        private readonly PropertyChangedEventHandler _viewModelPropertyChanged;

        public RefreshProbe(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _nodes = Flatten(viewModel.HierarchyRoots).ToArray();
            _sessionChanged = (_, args) => SessionChanges.Add(args);
            _hierarchyChanged = (_, _) => HierarchyRootChanges++;
            _inspectorChanged = (_, args) =>
            {
                InspectorCollectionChanges++;
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    InspectorResetCount++;
                }
            };
            _validationChanged = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    ValidationResets++;
                }
            };
            _tableRowsChanged = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    TableRowResets++;
                }
            };
            _tablePropertyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(TableViewerViewModel.Columns))
                {
                    TableColumnNotifications++;
                }
            };
            _nodePropertyChanged = (_, _) => NodePropertyChanges++;
            _viewModelPropertyChanged = (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.PendingRelaySource) or
                    nameof(MainViewModel.IsAddingRelay) or
                    nameof(MainViewModel.RelayLinkPrompt))
                {
                    RelayStateNotifications++;
                }
            };

            viewModel.Session.Changed += _sessionChanged;
            viewModel.PropertyChanged += _viewModelPropertyChanged;
            viewModel.HierarchyRoots.CollectionChanged += _hierarchyChanged;
            viewModel.Inspector.Sections.CollectionChanged += _inspectorChanged;
            viewModel.ValidationDiagnostics.CollectionChanged += _validationChanged;
            viewModel.TableViewer.Rows.CollectionChanged += _tableRowsChanged;
            viewModel.TableViewer.PropertyChanged += _tablePropertyChanged;
            foreach (var node in _nodes)
            {
                node.PropertyChanged += _nodePropertyChanged;
            }
        }

        public List<SessionChangedEventArgs> SessionChanges { get; } = [];
        public int HierarchyRootChanges { get; private set; }
        public int InspectorCollectionChanges { get; private set; }
        public int InspectorResetCount { get; private set; }
        public int ValidationResets { get; private set; }
        public int TableRowResets { get; private set; }
        public int TableColumnNotifications { get; private set; }
        public int NodePropertyChanges { get; private set; }
        public int RelayStateNotifications { get; private set; }

        public void Dispose()
        {
            _viewModel.Session.Changed -= _sessionChanged;
            _viewModel.PropertyChanged -= _viewModelPropertyChanged;
            _viewModel.HierarchyRoots.CollectionChanged -= _hierarchyChanged;
            _viewModel.Inspector.Sections.CollectionChanged -= _inspectorChanged;
            _viewModel.ValidationDiagnostics.CollectionChanged -= _validationChanged;
            _viewModel.TableViewer.Rows.CollectionChanged -= _tableRowsChanged;
            _viewModel.TableViewer.PropertyChanged -= _tablePropertyChanged;
            foreach (var node in _nodes)
            {
                node.PropertyChanged -= _nodePropertyChanged;
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string description) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void NearlyEqual(double expected, double actual, string description)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string description)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{description}: sequences differ.");
        }
    }

    private static void SequenceReferenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string description)
        where T : class
    {
        if (!expected.Zip(actual, ReferenceEquals).All(equal => equal) || expected.Count() != actual.Count())
        {
            throw new InvalidOperationException($"{description}: object identities differ.");
        }
    }

    private static void SetEqual<T>(IEnumerable<T> expected, IReadOnlySet<T> actual, string description)
    {
        if (!actual.SetEquals(expected))
        {
            throw new InvalidOperationException($"{description}: sets differ.");
        }
    }

    private static void True(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    private static void NotNull(object? value, string description) => True(value is not null, description);
}
