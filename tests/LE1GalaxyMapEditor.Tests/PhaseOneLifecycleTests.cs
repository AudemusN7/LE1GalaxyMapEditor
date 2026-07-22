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
/// Lifecycle gates for the phase-one single-transition reload and shutdown-only
/// abandon paths. These tests intentionally observe publications, document
/// attachments, composition, deferred work, and object identity independently.
/// </summary>
internal static class PhaseOneLifecycleTests
{
    public static void Register(Action<string, Action> run)
    {
        run("Phase 1: remembered startup is one session transition", RememberedStartupIsOneTransition);
        run("Phase 1: startup loader cannot replace a live session", StartupLoaderCannotReplaceLiveSession);
        run("Phase 1: ordinary discard reloads once", OrdinaryDiscardReloadsOnce);
        run("Phase 1: rejected discard preserves the live session", RejectedDiscardPreservesLiveSession);
        run("Phase 1: clean refresh follows externally updated workspace", CleanRefreshFollowsUpdatedWorkspace);
        run("Phase 1: shutdown discard abandons without refresh", ShutdownDiscardAbandonsWithoutRefresh);
        run("Phase 1: missing remembered module retains BASEGAME and diagnostics", MissingModuleRetainsBaseGameAndDiagnostics);
        run("Phase 1: corrupt startup settings produce structured fallback", CorruptStartupSettingsProduceStructuredFallback);
        run("Phase 1: reference-folder diagnostics follow the active source", ReferenceFolderDiagnosticsFollowActiveSource);
    }

    private static void RememberedStartupIsOneTransition()
    {
        WithRememberedModule((_, settingsPath, expectedModule) =>
        {
            var scheduler = new RecordingScheduler();
            using var viewModel = CreateViewModel(settingsPath, scheduler);
            var sessionChanges = new List<SessionChangedEventArgs>();
            var documentAttachments = 0;
            viewModel.Session.Changed += (_, args) => sessionChanges.Add(args);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Document))
                {
                    documentAttachments++;
                }
            };
            True(viewModel.LoadRememberedWorkspace(), "remembered workspace loads cleanly");

            Equal(1L, viewModel.Session.Revision, "startup publishes one session revision");
            Equal(1, sessionChanges.Count, "startup emits one session notification");
            True(sessionChanges.Single().Impact.IsStructural, "startup notification is structural");
            Equal(1, documentAttachments, "startup attaches one effective document");
            Equal(1, viewModel.HierarchyRoots.Count, "startup constructs one galaxy hierarchy root");
            True(viewModel.HierarchyRoots.Single().IsGalaxyRoot, "startup hierarchy root has galaxy semantics");
            Equal(1L, viewModel.Workspace!.CompositionRevision,
                "complete remembered layer stack is composed once");
            Equal(1, viewModel.Workspace.ModuleLayers.Count, "one remembered module is mounted");
            Equal(expectedModule.Tag, viewModel.ActiveModule!.Tag, "remembered active module is restored");
            True(!viewModel.HasError, "clean restoration raises no error banner");
        });
    }

    private static void StartupLoaderCannotReplaceLiveSession()
    {
        WithRememberedModule((_, settingsPath, _) =>
        {
            using var viewModel = CreateViewModel(settingsPath, new RecordingScheduler());
            True(viewModel.LoadRememberedWorkspace(), "initial remembered workspace loads");
            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            StageClusterX(viewModel, key, DistinctCoordinate(
                viewModel.Document!.ClustersByRowId[key.RowId].X));
            var workspace = viewModel.Workspace;
            var document = viewModel.Document;
            var selectedNode = FindNode(viewModel, key);
            var revision = viewModel.Session.Revision;
            var pendingCount = viewModel.PendingChangeCount;

            True(!viewModel.LoadRememberedWorkspace(),
                "startup-only loader rejects a second live-session transition");

            True(ReferenceEquals(workspace, viewModel.Workspace),
                "rejected startup reload preserves workspace identity");
            True(ReferenceEquals(document, viewModel.Document),
                "rejected startup reload preserves document identity");
            True(ReferenceEquals(selectedNode, FindNode(viewModel, key)) && selectedNode.IsSelected,
                "rejected startup reload preserves hierarchy selection");
            Equal(revision, viewModel.Session.Revision,
                "rejected startup reload publishes no revision");
            Equal(pendingCount, viewModel.PendingChangeCount,
                "rejected startup reload preserves staged changes");
            True(viewModel.UndoCommand.CanExecute(null),
                "rejected startup reload preserves undo history");
        });
    }

    private static void RejectedDiscardPreservesLiveSession()
    {
        WithRememberedModule((parent, settingsPath, expectedModule) =>
        {
            var scheduler = new RecordingScheduler();
            using var viewModel = CreateViewModel(settingsPath, scheduler);
            True(viewModel.LoadRememberedWorkspace(), "remembered workspace loads");
            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            StageClusterX(viewModel, key, DistinctCoordinate(
                viewModel.Document!.ClustersByRowId[key.RowId].X));
            viewModel.RowInstanceTabs.Single(tab => tab.Module.Tag == expectedModule.Tag)
                .SelectCommand.Execute(null);

            var workspace = viewModel.Workspace;
            var document = viewModel.Document;
            var selectedNode = FindNode(viewModel, key);
            var revision = viewModel.Session.Revision;
            var composition = workspace!.CompositionRevision;
            var pendingCount = viewModel.PendingChangeCount;
            var diagnostics = viewModel.ValidationDiagnostics.ToArray();
            var sessionChanges = 0;
            var documentAttachments = 0;
            viewModel.Session.Changed += (_, _) => sessionChanges++;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Document))
                {
                    documentAttachments++;
                }
            };

            var movedFolder = Path.Combine(parent, "temporarily-missing-module");
            Directory.Move(expectedModule.FolderPath!, movedFolder);
            viewModel.DiscardPendingChanges();

            True(viewModel.HasPendingChanges, "rejected discard retains staged state");
            Equal(pendingCount, viewModel.PendingChangeCount, "rejected discard retains every staged change");
            True(viewModel.UndoCommand.CanExecute(null), "rejected discard retains undo history");
            Equal(revision, viewModel.Session.Revision, "rejected discard publishes no revision");
            Equal(composition, workspace.CompositionRevision, "rejected discard performs no composition on the live workspace");
            Equal(0, sessionChanges, "rejected discard emits no session notification");
            Equal(0, documentAttachments, "rejected discard attaches no document");
            True(ReferenceEquals(workspace, viewModel.Workspace), "rejected discard preserves workspace identity");
            True(ReferenceEquals(document, viewModel.Document), "rejected discard preserves document identity");
            True(ReferenceEquals(selectedNode, FindNode(viewModel, key)) && selectedNode.IsSelected,
                "rejected discard preserves hierarchy selection and identity");
            SequenceEqual(diagnostics, viewModel.ValidationDiagnostics,
                "rejected discard preserves accepted startup diagnostics");
            True(viewModel.ErrorMessage.Contains("currently mounted module", StringComparison.OrdinalIgnoreCase),
                "rejected discard explains why the live session was preserved");
        });
    }

    private static void OrdinaryDiscardReloadsOnce()
    {
        WithRememberedModule((_, settingsPath, expectedModule) =>
        {
            var scheduler = new RecordingScheduler();
            using var viewModel = CreateViewModel(settingsPath, scheduler);
            True(viewModel.LoadRememberedWorkspace(), "remembered workspace loads");

            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            var originalX = viewModel.Document!.ClustersByRowId[key.RowId].X;
            StageClusterX(viewModel, key, DistinctCoordinate(originalX));
            True(viewModel.HasPendingChanges, "scalar edit is staged before discard");
            True(viewModel.UndoCommand.CanExecute(null), "scalar edit creates undo history");
            True(scheduler.HasPendingAction, "scalar edit schedules deferred validation");

            var oldWorkspace = viewModel.Workspace;
            var oldDocument = viewModel.Document;
            var revisionBefore = viewModel.Session.Revision;
            var sessionChanges = 0;
            var documentAttachments = 0;
            viewModel.Session.Changed += (_, _) => sessionChanges++;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Document))
                {
                    documentAttachments++;
                }
            };

            viewModel.DiscardPendingChanges();

            Equal(revisionBefore + 1, viewModel.Session.Revision,
                "ordinary discard publishes one replacement revision");
            Equal(1, sessionChanges, "ordinary discard emits one session notification");
            Equal(1, documentAttachments, "ordinary discard attaches one replacement document");
            True(!ReferenceEquals(oldWorkspace, viewModel.Workspace), "ordinary discard replaces the workspace");
            True(!ReferenceEquals(oldDocument, viewModel.Document), "ordinary discard replaces the effective document");
            Equal(1L, viewModel.Workspace!.CompositionRevision,
                "discarded remembered stack is composed once");
            Equal(expectedModule.Tag, viewModel.ActiveModule!.Tag, "active remembered module is restored");
            NearlyEqual(originalX, viewModel.Document!.ClustersByRowId[key.RowId].X,
                "discard restores the committed BASEGAME value");
            True(!viewModel.HasPendingChanges, "ordinary discard clears staged state");
            True(!viewModel.UndoCommand.CanExecute(null), "ordinary discard clears undo history");
            True(!scheduler.HasPendingAction, "replacement validation cancels the stale deferred validation");
            Equal("Discarded all uncommitted changes.", viewModel.StatusMessage,
                "ordinary discard retains its user-facing completion status");
        });
    }

    private static void CleanRefreshFollowsUpdatedWorkspace()
    {
        WithRememberedModule((_, settingsPath, _) =>
        {
            using var viewModel = CreateViewModel(settingsPath, new RecordingScheduler());
            True(viewModel.LoadRememberedWorkspace(), "remembered workspace loads");
            True(!viewModel.HasPendingChanges, "clean workspace has no staged data to protect");
            new GalaxyMapWorkspaceStore(settingsPath).Save([], activeModuleTag: null);
            var revisionBefore = viewModel.Session.Revision;

            True(viewModel.RefreshRememberedWorkspace(),
                "clean refresh accepts the externally updated remembered workspace");

            Equal(revisionBefore + 1, viewModel.Session.Revision,
                "clean refresh publishes one replacement revision");
            Equal(0, viewModel.Workspace!.ModuleLayers.Count,
                "clean refresh reflects an intentional module removal from workspace.json");
            True(viewModel.Document is not null, "clean refresh retains usable BASEGAME data");
            True(!viewModel.HasError, "clean authoritative refresh raises no error banner");
        });
    }

    private static void ShutdownDiscardAbandonsWithoutRefresh()
    {
        WithRememberedModule((_, settingsPath, _) =>
        {
            var scheduler = new RecordingScheduler();
            var viewModel = CreateViewModel(settingsPath, scheduler);
            try
            {
                True(viewModel.LoadRememberedWorkspace(), "remembered workspace loads");
                var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
                StageClusterX(viewModel, key, DistinctCoordinate(
                    viewModel.Document!.ClustersByRowId[key.RowId].X));
                True(viewModel.HasPendingChanges, "scalar edit is staged before shutdown");
                True(scheduler.HasPendingAction, "deferred validation is pending before shutdown");

                var workspace = viewModel.Workspace;
                var document = viewModel.Document;
                var hierarchyRoot = viewModel.HierarchyRoots.Single();
                var revision = viewModel.Session.Revision;
                var composition = workspace!.CompositionRevision;
                var sessionChanges = 0;
                var documentAttachments = 0;
                var hierarchyChanges = 0;
                viewModel.Session.Changed += (_, _) => sessionChanges++;
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.Document))
                    {
                        documentAttachments++;
                    }
                };
                NotifyCollectionChangedEventHandler hierarchyChanged = (_, _) => hierarchyChanges++;
                viewModel.HierarchyRoots.CollectionChanged += hierarchyChanged;

                viewModel.AbandonPendingChangesForShutdown();

                True(!viewModel.HasPendingChanges, "shutdown abandon clears staged state");
                True(!viewModel.UndoCommand.CanExecute(null), "shutdown abandon clears undo history");
                Equal(revision, viewModel.Session.Revision, "shutdown abandon publishes no session revision");
                Equal(composition, workspace.CompositionRevision, "shutdown abandon performs no composition");
                Equal(0, sessionChanges, "shutdown abandon emits no session notification");
                Equal(0, documentAttachments, "shutdown abandon attaches no document");
                Equal(0, hierarchyChanges, "shutdown abandon performs no hierarchy rebuild");
                True(ReferenceEquals(workspace, viewModel.Workspace), "shutdown abandon retains workspace identity");
                True(ReferenceEquals(document, viewModel.Document), "shutdown abandon retains document identity");
                True(ReferenceEquals(hierarchyRoot, viewModel.HierarchyRoots.Single()),
                    "shutdown abandon retains hierarchy identity");
                True(!scheduler.HasPendingAction, "shutdown abandon cancels deferred validation");

                viewModel.Dispose();
                viewModel.Dispose();
                Equal(1, scheduler.DisposeCount, "MainViewModel disposal is deterministic and idempotent");
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

    private static void MissingModuleRetainsBaseGameAndDiagnostics()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            var missingFolder = Path.Combine(parent, "missing-module");
            var metadata = new UnmanifestedReadOnlyModule(
                "Missing module",
                "MISSING_LIFECYCLE",
                ModuleColor.Red,
                10,
                ModuleIdReservations.Empty,
                new Dictionary<int, string>());
            new GalaxyMapWorkspaceStore(settingsPath).Save(
                [new RememberedModule(missingFolder, metadata)],
                activeModuleTag: null);

            using var viewModel = CreateViewModel(settingsPath, new RecordingScheduler());
            var documentAttachments = 0;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Document))
                {
                    documentAttachments++;
                }
            };

            True(!viewModel.LoadRememberedWorkspace(), "missing remembered module reports restoration failure");
            Equal(1L, viewModel.Session.Revision, "failed module restoration still publishes one BASEGAME session");
            Equal(1, documentAttachments, "failed module restoration attaches BASEGAME once");
            Equal(1L, viewModel.Workspace!.CompositionRevision, "BASEGAME fallback composes once");
            Equal(0, viewModel.Workspace.ModuleLayers.Count, "missing module is not partially mounted");
            True(viewModel.Document is not null, "BASEGAME remains usable");
            True(viewModel.HasError, "restoration failure remains visible");
            True(viewModel.ValidationDiagnostics.Any(diagnostic =>
                    diagnostic.Code == "WORKSPACE-MODULE-MISSING" &&
                    diagnostic.ModuleTag == metadata.Tag),
                "structured missing-module diagnostic is preserved");
        });
    }

    private static void CorruptStartupSettingsProduceStructuredFallback()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            File.WriteAllText(settingsPath, "{ this is not valid json");
            using var viewModel = CreateViewModel(settingsPath, new RecordingScheduler());

            True(!viewModel.LoadRememberedWorkspace(), "corrupt startup settings report failure");
            Equal(1L, viewModel.Session.Revision, "corrupt startup fallback publishes once");
            Equal(1L, viewModel.Workspace!.CompositionRevision, "corrupt startup fallback composes BASEGAME once");
            Equal(0, viewModel.Workspace.ModuleLayers.Count, "corrupt startup fallback mounts no partial modules");
            True(viewModel.Document is not null, "corrupt startup fallback leaves BASEGAME usable");
            True(viewModel.ValidationDiagnostics.Any(diagnostic => diagnostic.Code == "WORKSPACE-LOAD"),
                "corrupt startup fallback emits a structured workspace diagnostic");
            True(viewModel.HasError, "corrupt startup fallback retains the error banner");
        });
    }

    private static void ReferenceFolderDiagnosticsFollowActiveSource()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = CreateMissingModuleSettings(parent);
            var validReference = Path.Combine(parent, "reference");
            CopyReferenceCsvFiles(validReference);

            using (var successful = CreateViewModel(settingsPath, new RecordingScheduler()))
            {
                True(!successful.LoadRememberedWorkspace(), "missing module creates startup diagnostic");
                True(successful.ValidationDiagnostics.Any(diagnostic =>
                        diagnostic.Code.StartsWith("WORKSPACE-", StringComparison.Ordinal)),
                    "remembered-workspace diagnostic is initially visible");

                True(successful.LoadFolder(validReference), "valid command-line-style reference folder loads");
                True(successful.Workspace is null, "reference folder replaces the authoring workspace view");
                True(successful.ValidationDiagnostics.All(diagnostic =>
                        !diagnostic.Code.StartsWith("WORKSPACE-", StringComparison.Ordinal)),
                    "successful reference load suppresses irrelevant remembered-workspace diagnostics");
            }

            using var failed = CreateViewModel(settingsPath, new RecordingScheduler());
            True(!failed.LoadRememberedWorkspace(), "second missing-module workspace loads with diagnostics");
            var workspace = failed.Workspace;
            var document = failed.Document;
            var diagnostics = failed.ValidationDiagnostics.ToArray();

            True(!failed.LoadFolder(Path.Combine(parent, "not-a-reference-folder")),
                "invalid reference folder reports failure");
            True(ReferenceEquals(workspace, failed.Workspace),
                "failed reference load preserves restored workspace identity");
            True(ReferenceEquals(document, failed.Document),
                "failed reference load preserves restored effective document");
            SequenceEqual(diagnostics, failed.ValidationDiagnostics,
                "failed reference load preserves remembered-workspace diagnostics");
        });
    }

    private static MainViewModel CreateViewModel(string settingsPath, RecordingScheduler scheduler)
        => new(
            new CsvGalaxyMapLoader(),
            new GalaxyMapTextureService(TextureDirectory()),
            new GalaxyMapWorkspaceStore(settingsPath),
            deferredScheduler: scheduler);

    private static void StageClusterX(MainViewModel viewModel, GalaxyMapRowKey key, double value)
    {
        FindNode(viewModel, key).IsSelected = true;
        var field = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
            .Fields.Single(candidate => candidate.Name == "X");
        field.Value = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HierarchyNodeViewModel FindNode(MainViewModel viewModel, GalaxyMapRowKey key)
    {
        static IEnumerable<HierarchyNodeViewModel> Flatten(IEnumerable<HierarchyNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }

        return Flatten(viewModel.HierarchyRoots).Single(node => node.Model?.Key == key);
    }

    private static double DistinctCoordinate(double current)
        => Math.Abs(current - 0.73) > 0.000001 ? 0.73 : 0.27;

    private static string TextureDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "textures");
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Test texture directory was not deployed: {path}");
        }

        return path;
    }

    private static string CreateMissingModuleSettings(string parent)
    {
        var settingsPath = Path.Combine(parent, "workspace.json");
        var metadata = new UnmanifestedReadOnlyModule(
            "Missing module",
            "MISSING_REFERENCE",
            ModuleColor.Red,
            10,
            ModuleIdReservations.Empty,
            new Dictionary<int, string>());
        new GalaxyMapWorkspaceStore(settingsPath).Save(
            [new RememberedModule(Path.Combine(parent, "missing-reference-module"), metadata)],
            activeModuleTag: null);
        return settingsPath;
    }

    private static void CopyReferenceCsvFiles(string destination)
    {
        Directory.CreateDirectory(destination);
        var assembly = typeof(CsvGalaxyMapLoader).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name =>
                     name.StartsWith(CsvGalaxyMapLoader.BuiltInResourcePrefix, StringComparison.Ordinal) &&
                     name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = resourceName[CsvGalaxyMapLoader.BuiltInResourcePrefix.Length..];
            using var source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded test CSV could not be opened: {resourceName}");
            using var output = File.Create(Path.Combine(destination, fileName));
            source.CopyTo(output);
        }
    }

    private static void WithRememberedModule(Action<string, string, GalaxyMapModule> test)
    {
        WithTemporaryDirectory(parent =>
        {
            var moduleFolder = Path.Combine(parent, "Remembered Lifecycle Module");
            Directory.CreateDirectory(moduleFolder);
            var module = new GalaxyMapModule(
                "Remembered Lifecycle Module",
                "LIFECYCLE",
                ModuleColor.Cyan,
                moduleFolder,
                isReadOnly: false,
                loadOrder: 12,
                ModuleIdReservations.Empty);
            new GalaxyMapModuleManifestStore().Save(module);
            var settingsPath = Path.Combine(parent, "workspace.json");
            new GalaxyMapWorkspaceStore(settingsPath).Save([RememberedModule.FromModule(module)], module.Tag);
            test(parent, settingsPath, module);
        });
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"le1-galaxy-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            test(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void True(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
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
        if (Math.Abs(expected - actual) > 0.000001)
        {
            throw new InvalidOperationException($"{description}: expected '{expected:R}', got '{actual:R}'.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string description)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{description}: sequences differ.");
        }
    }

    private sealed class RecordingScheduler : IDeferredScheduler
    {
        private Action? _pending;

        public bool HasPendingAction => _pending is not null;
        public int DisposeCount { get; private set; }

        public void Schedule(TimeSpan delay, Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _pending = action;
        }

        public void Cancel() => _pending = null;

        public void Dispose()
        {
            DisposeCount++;
            Cancel();
        }
    }
}
