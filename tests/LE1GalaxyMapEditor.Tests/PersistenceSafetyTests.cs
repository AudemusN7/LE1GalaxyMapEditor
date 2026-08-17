using System.Globalization;
using System.IO;
using System.Text;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;

namespace LE1GalaxyMapEditor.Tests;

/// <summary>
/// Regression coverage for partial commits, retryability and durable-write boundaries.
/// </summary>
internal static partial class PersistenceSafetyTests
{
    private const string ClusterCsvFileName = "GalaxyMap_Cluster_part.csv";

    public static void Register(Action<string, Action> run)
    {
        run("Writable physical-instance edit recomposes projections", WritablePhysicalInstanceEditRecomposesProjections);
        run("Partial commit refreshes MainViewModel projections", PartialCommitRefreshesMainViewModelProjections);
        run("Pending-file failure stops later commit stages", PendingFileFailureStopsLaterStages);
        run("CSV failure preserves retryable module state", CsvFailurePreservesRetryableState);
        run("Manifest failure exposes completed file and CSV writes", ManifestFailureAfterEarlierWrites);
        run("Partial multi-module commit isolates failed module", PartialMultiModuleCommitIsolation);
        run("PCC partial commit is retryable", PccPartialCommitIsRetryable);
        run("Shader preflight preserves complete state", ShaderPreflightPreservesCompleteState);
        run("Manifest-backed read-only metadata commits", ManifestBackedReadOnlyMetadataCommits);
        run("Unmanifested read-only metadata waits for workspace persistence", UnmanifestedReadOnlyMetadataWaitsForWorkspacePersistence);
        run("Workspace metadata failure isolates earlier durable writes", WorkspaceMetadataFailureIsolatesEarlierDurableWrites);
    }

    private static void ManifestBackedReadOnlyMetadataCommits()
    {
        WithTemporaryDirectory(parent =>
        {
            var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
            var data = CreateCommitLayer(parent, baseLayer, "READ_ONLY_MANIFEST", 10, clusterIndex: 0);
            var writable = data.Layer.Module;
            var readOnly = new GalaxyMapModule(
                writable.Name,
                writable.Tag,
                writable.Color,
                writable.FolderPath,
                isReadOnly: true,
                writable.LoadOrder,
                writable.Reservations,
                writable.ClusterTextureLinks);
            data.Layer.ReplaceModule(readOnly);
            var manifestStore = new GalaxyMapModuleManifestStore();
            manifestStore.Save(readOnly);

            var workspace = new GalaxyMapWorkspace(baseLayer, [data.Layer]);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session, manifestStore: manifestStore);
            var workflows = new WorkspaceWorkflowService(
                session,
                edits,
                new CsvGalaxyMapLoader(),
                manifestStore,
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            var csvBefore = File.ReadAllBytes(data.CsvPath);
            var revisionBefore = session.Revision;

            var staged = workflows.UpdateModuleMetadata(
                readOnly,
                "Updated read-only manifest",
                readOnly.Tag,
                ModuleColor.Magenta,
                loadOrder: 25,
                readOnly.Reservations,
                new HistoryPresentationState(null, NavigationTarget.Galaxy, readOnly.Tag, false));

            True(staged.Succeeded, "metadata editing remains available for a read-only non-BASEGAME module");
            var replacement = workspace.Modules.Single(module => module.Tag == readOnly.Tag);
            True(replacement.IsReadOnly, "metadata editing preserves the module's read-only CSV policy");
            True(session.Changes.ContainsModule(replacement.Tag),
                "read-only manifest metadata remains staged until Commit");

            var committed = edits.Commit();

            True(committed.Succeeded, "manifest-backed read-only metadata commits successfully");
            var persisted = manifestStore.Load(data.Folder);
            Equal("Updated read-only manifest", persisted.Name,
                "Commit writes the updated read-only module name to module.json");
            Equal(ModuleColor.Magenta, persisted.Color,
                "Commit writes the updated read-only module colour to module.json");
            Equal(25, persisted.LoadOrder,
                "Commit writes the updated read-only module load order to module.json");
            True(persisted.IsReadOnly, "Commit preserves the manifest's read-only flag");
            SequenceEqual(csvBefore, File.ReadAllBytes(data.CsvPath),
                "metadata-only Commit does not rewrite the read-only module CSV");
            True(!session.Changes.HasChanges, "successful metadata Commit clears its staged state");
            True(!session.History.CanUndo, "successful metadata Commit clears the pre-commit history");
            Equal(revisionBefore + 2, session.Revision,
                "metadata staging and Commit each publish one session revision");
        });
    }

    private static void UnmanifestedReadOnlyMetadataWaitsForWorkspacePersistence()
    {
        WithTemporaryDirectory(parent =>
        {
            var folder = Path.Combine(parent, "legacy-read-only");
            Directory.CreateDirectory(folder);
            var module = new GalaxyMapModule(
                "Legacy read-only",
                "LEGACY_READ_ONLY",
                ModuleColor.Green,
                folder,
                isReadOnly: true,
                loadOrder: 10,
                ModuleIdReservations.Empty);
            var layer = new GalaxyMapLayer(module);
            var workspace = new GalaxyMapWorkspace(new CsvGalaxyMapLoader().LoadBuiltInLayer(), [layer]);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session);
            var settingsPath = Path.Combine(parent, "workspace.json");
            var workspaceStore = new GalaxyMapWorkspaceStore(settingsPath);
            workspaceStore.Save([RememberedModule.FromModule(module)], activeModuleTag: null);
            var workflows = new WorkspaceWorkflowService(
                session,
                edits,
                new CsvGalaxyMapLoader(),
                workspaceStore: workspaceStore);

            var staged = workflows.UpdateModuleMetadata(
                module,
                "Updated legacy read-only",
                module.Tag,
                ModuleColor.Magenta,
                loadOrder: 30,
                module.Reservations,
                new HistoryPresentationState(null, NavigationTarget.Galaxy, module.Tag, false));
            True(staged.Succeeded, "unmanifested read-only metadata can be staged");
            var replacement = workspace.Modules.Single(candidate => candidate.Tag == module.Tag);
            var revisionBeforeCommit = session.Revision;
            var compositionBeforeCommit = workspace.CompositionRevision;
            var persistenceCalls = 0;
            void PersistWorkspace()
            {
                persistenceCalls++;
                workflows.RememberCurrentWorkspace();
            }

            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);
            var failed = edits.Commit(PersistWorkspace);

            True(!failed.Succeeded, "workspace persistence failure fails the metadata commit");
            True(failed.Impact is null, "no durable write publishes no partial impact");
            True(session.Changes.ContainsModule(replacement.Tag),
                "workspace-only metadata remains staged when its authoritative save fails");
            True(session.History.CanUndo, "failed workspace-only save preserves undo history");
            Equal(revisionBeforeCommit, session.Revision,
                "failed workspace-only save publishes no session revision");
            Equal(compositionBeforeCommit, workspace.CompositionRevision,
                "failed workspace-only save performs no recomposition");
            Equal(1, persistenceCalls,
                "failed commit attempts its authoritative workspace save exactly once");

            Directory.Delete(settingsPath);
            var retried = edits.Commit(PersistWorkspace);

            True(retried.Succeeded, "workspace-only metadata commit is retryable");
            var remembered = workspaceStore.Load().Modules.Single().UnmanifestedReadOnlyModule;
            True(remembered is not null,
                "workspace.json remains the metadata authority for the legacy mount");
            Equal("Updated legacy read-only", remembered!.Name,
                "retry persists the updated legacy module name");
            Equal(ModuleColor.Magenta, remembered.Color,
                "retry persists the updated legacy module colour");
            Equal(30, remembered.LoadOrder,
                "retry persists the updated legacy module load order");
            True(!session.Changes.HasChanges, "successful workspace-only retry clears staged state");
            True(!session.History.CanUndo, "successful workspace-only retry clears history");
            Equal(2, persistenceCalls,
                "successful retry performs one additional workspace save without duplication");
        });
    }

    private static void WorkspaceMetadataFailureIsolatesEarlierDurableWrites()
    {
        WithTemporaryDirectory(parent =>
        {
            var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
            var writable = CreateCommitLayer(parent, baseLayer, "CSV_BEFORE_WORKSPACE", 10, clusterIndex: 0);
            var legacyFolder = Path.Combine(parent, "legacy-after-csv");
            Directory.CreateDirectory(legacyFolder);
            var legacy = new GalaxyMapModule(
                "Legacy after CSV",
                "LEGACY_AFTER_CSV",
                ModuleColor.Green,
                legacyFolder,
                isReadOnly: true,
                loadOrder: 20,
                ModuleIdReservations.Empty);
            var legacyLayer = new GalaxyMapLayer(legacy);
            var workspace = new GalaxyMapWorkspace(baseLayer, [writable.Layer, legacyLayer]);
            workspace.SetActiveModule(writable.Module);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session);
            var settingsPath = Path.Combine(parent, "workspace.json");
            var workspaceStore = new GalaxyMapWorkspaceStore(settingsPath);
            workspaceStore.Save(
                [RememberedModule.FromModule(writable.Module), RememberedModule.FromModule(legacy)],
                writable.Module.Tag);
            var workflows = new WorkspaceWorkflowService(
                session,
                edits,
                new CsvGalaxyMapLoader(),
                workspaceStore: workspaceStore);

            StageRowEdit(edits, writable.Physical, "CSV crossed first", writable.Module);
            var stagedMetadata = workflows.UpdateModuleMetadata(
                legacy,
                "Workspace retry value",
                legacy.Tag,
                ModuleColor.Magenta,
                loadOrder: 25,
                legacy.Reservations,
                new HistoryPresentationState(null, NavigationTarget.Galaxy, legacy.Tag, false));
            True(stagedMetadata.Succeeded, "legacy metadata is staged after the writable CSV edit");
            var replacement = workspace.Modules.Single(module => module.Tag == legacy.Tag);
            var revisionBeforeCommit = session.Revision;
            var compositionBeforeCommit = workspace.CompositionRevision;
            var persistenceCalls = 0;
            void PersistWorkspace()
            {
                persistenceCalls++;
                workflows.RememberCurrentWorkspace();
            }

            WorkflowResult failed;
            using (File.Open(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = edits.Commit(PersistWorkspace);
            }

            True(!failed.Succeeded && failed.Impact is not null,
                "workspace failure after CSV durability reports a partial commit");
            Equal("CSV crossed first", LoadClusterName(writable.Folder, writable.Physical.RowId),
                "earlier writable CSV remains durably committed");
            True(session.Changes.ContainsModule(writable.Module.Tag),
                "workspace-finalisation failure keeps the earlier module retryable");
            True(session.Changes.ContainsModule(replacement.Tag),
                "workspace-owned metadata remains staged for retry");
            True(!session.History.CanUndo,
                "crossing the earlier CSV boundary invalidates pre-commit history");
            Equal(revisionBeforeCommit + 1, session.Revision,
                "partial workspace failure publishes exactly once");
            Equal(compositionBeforeCommit + 1, workspace.CompositionRevision,
                "partial workspace failure recomposes exactly once");
            Equal(1, persistenceCalls,
                "partial commit attempts workspace finalisation exactly once");
            SequenceEqual(new[] { GalaxyMapTable.Cluster }, failed.Impact!.Tables,
                "partial impact reports the earlier durable table");
            SequenceEqual(new[] { writable.Physical.Key }, failed.Impact.Rows,
                "partial impact reports the earlier durable row");
            Equal("Legacy after CSV",
                workspaceStore.Load().Modules.Single(module =>
                    module.UnmanifestedReadOnlyModule is not null)
                    .UnmanifestedReadOnlyModule!.Name,
                "failed atomic workspace replacement retains prior metadata");

            var retried = edits.Commit(PersistWorkspace);
            True(retried.Succeeded, "workspace-only remainder is retryable");
            Equal("Workspace retry value",
                workspaceStore.Load().Modules.Single(module =>
                    module.UnmanifestedReadOnlyModule is not null)
                    .UnmanifestedReadOnlyModule!.Name,
                "retry persists the remaining workspace metadata");
            True(!session.Changes.HasChanges, "retry clears the remaining workspace-owned state");
            Equal(2, persistenceCalls,
                "retry performs one additional workspace save without duplication");
        });
    }

    private static CommitFixture CreateCommitFixture(
        string parent,
        string tag,
        int loadOrder,
        int clusterIndex)
    {
        var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
        var data = CreateCommitLayer(parent, baseLayer, tag, loadOrder, clusterIndex);
        var workspace = new GalaxyMapWorkspace(baseLayer, [data.Layer]);
        workspace.SetActiveModule(data.Module);
        var session = new EditorSession(workspace);
        return new CommitFixture(
            data.Folder,
            data.OriginalModuleName,
            data.Module,
            data.Layer,
            data.Physical,
            data.CsvPath,
            workspace,
            session,
            new EditSessionService(session));
    }

    private static CommitLayer CreateCommitLayer(
        string parent,
        GalaxyMapLayer baseLayer,
        string tag,
        int loadOrder,
        int clusterIndex)
    {
        var folder = Path.Combine(parent, tag);
        Directory.CreateDirectory(folder);
        var originalName = $"Original {tag}";
        var original = new GalaxyMapModule(
            originalName,
            tag,
            ModuleColor.Green,
            folder,
            isReadOnly: false,
            loadOrder,
            ModuleIdReservations.Empty);
        new GalaxyMapModuleManifestStore().Save(original);

        var current = original.With(name: $"Updated {tag}");
        var layer = new GalaxyMapLayer(current);
        var source = baseLayer.Clusters.OrderBy(cluster => cluster.RowId).ElementAt(clusterIndex);
        var physical = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, current);
        layer.Upsert(physical);
        new GalaxyMapCsvWriter().WriteTable(layer, GalaxyMapTable.Cluster);

        return new CommitLayer(
            folder,
            originalName,
            current,
            layer,
            physical,
            Path.Combine(folder, ClusterCsvFileName));
    }

    private static void StageCompleteModuleChange(
        CommitFixture fixture,
        string nameText,
        string relativePath,
        byte[] contents)
    {
        StageRowEdit(fixture.Edits, fixture.Physical, nameText, fixture.Module);
        fixture.Edits.MarkMetadataDirty(fixture.Module);
        fixture.Edits.StageFile(new PendingFileWrite(
            fixture.Module.Tag,
            relativePath,
            contents,
            "phase-zero commit boundary",
            fixture.Physical.Key));
    }

    private static void StageRowEdit(
        EditSessionService edits,
        Cluster physical,
        string nameText,
        GalaxyMapModule module)
    {
        var presentation = new HistoryPresentationState(
            physical.Key,
            NavigationTarget.Galaxy,
            module.Tag,
            InspectPhysicalInstance: true);
        var result = edits.ExecuteMutation(new EditMutationRequest(
            [physical.Key],
            [GalaxyMapTable.Cluster],
            () =>
            {
                physical.NameText = nameText;
                physical.CsvSnapshot!.MarkDirty("NameText");
            },
            presentation,
            $"changed Cluster row {physical.RowId}",
            IsStructural: false));
        True(result.Succeeded, "fixture stages its table edit");
    }

    private static List<ChangeImpact> ObservePublishedImpacts(EditorSession session)
    {
        var impacts = new List<ChangeImpact>();
        session.Changed += (_, eventArgs) => impacts.Add(eventArgs.Impact);
        return impacts;
    }

    private static void AssertNoDurableProgress(
        CommitFixture fixture,
        long revisionBefore,
        long compositionBefore,
        IReadOnlyList<ChangeImpact> publishedImpacts,
        string boundary)
    {
        True(fixture.Session.Changes.ContainsModule(fixture.Module.Tag),
            $"{boundary} retains all module changes for retry");
        True(fixture.Session.History.CanUndo, $"{boundary} retains undo history");
        Equal(revisionBefore, fixture.Session.Revision, $"{boundary} publishes no session revision");
        Equal(compositionBefore, fixture.Workspace.CompositionRevision,
            $"{boundary} performs no unnecessary recomposition");
        Equal(0, publishedImpacts.Count, $"{boundary} publishes no change impact");
    }

    private static void AssertDurablePartialFailure(
        CommitFixture fixture,
        long revisionBefore,
        long compositionBefore,
        IReadOnlyList<ChangeImpact> publishedImpacts,
        IReadOnlyCollection<GalaxyMapTable> expectedTables,
        IReadOnlyCollection<GalaxyMapRowKey> expectedRows,
        string boundary)
    {
        True(fixture.Session.Changes.ContainsModule(fixture.Module.Tag),
            $"{boundary} retains the failed module for retry");
        True(!fixture.Session.History.CanUndo,
            $"{boundary} clears history after crossing a durable boundary");
        Equal(revisionBefore + 1, fixture.Session.Revision,
            $"{boundary} publishes exactly one session revision");
        Equal(compositionBefore + 1, fixture.Workspace.CompositionRevision,
            $"{boundary} recomposes exactly once");
        Equal(1, publishedImpacts.Count, $"{boundary} publishes exactly one impact");
        var impact = publishedImpacts[0];
        True(!impact.IsStructural, $"{boundary} does not claim a structural document change");
        SequenceEqual(expectedTables.Order(), impact.Tables.Order(),
            $"{boundary} reports only durably written tables");
        SequenceEqual(expectedRows.OrderBy(row => row.Table).ThenBy(row => row.RowId),
            impact.Rows.OrderBy(row => row.Table).ThenBy(row => row.RowId),
            $"{boundary} reports only rows made durable");
    }

    private static void AssertSuccessfulRetry(CommitFixture fixture, string expectedName, long expectedRevision)
    {
        Equal(expectedName, LoadClusterName(fixture.Folder, fixture.Physical.RowId),
            "successful retry writes the edited CSV value");
        Equal(fixture.Module.Name, LoadManifestName(fixture.Folder),
            "successful retry writes current module metadata");
        True(!fixture.Session.Changes.HasChanges, "successful retry clears staged change state");
        True(!fixture.Session.History.CanUndo, "successful retry clears history");
        Equal(expectedRevision, fixture.Session.Revision, "successful retry publishes one structural revision");
        True(!fixture.Workspace.EffectiveDocument.ClustersByRowId[fixture.Physical.RowId]
                .CsvSnapshot!.IsDirty("NameText"),
            "successful retry recomposes a clean effective snapshot");
    }

    private static IReadOnlyDictionary<string, byte[]> CaptureFiles(params string[] paths)
        => paths.ToDictionary(
            Path.GetFullPath,
            path => File.ReadAllBytes(path),
            StringComparer.OrdinalIgnoreCase);

    private static void AssertFilesEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual,
        string description)
    {
        SequenceEqual(expected.Keys.Order(StringComparer.OrdinalIgnoreCase),
            actual.Keys.Order(StringComparer.OrdinalIgnoreCase), description);
        foreach (var path in expected.Keys)
        {
            SequenceEqual(expected[path], actual[path], $"{description}: {path}");
        }
    }

    private static void AssertChangeSetEqual(
        EditChangeSetSnapshot expected,
        EditChangeSetSnapshot actual,
        string description)
    {
        SequenceEqual(
            expected.DirtyTables
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}:{string.Join(',', pair.Value.Order())}"),
            actual.DirtyTables
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}:{string.Join(',', pair.Value.Order())}"),
            $"{description}: dirty tables");
        SequenceEqual(expected.DirtyModuleMetadata.Order(StringComparer.OrdinalIgnoreCase),
            actual.DirtyModuleMetadata.Order(StringComparer.OrdinalIgnoreCase),
            $"{description}: dirty metadata");
        var expectedFiles = expected.PendingFiles
            .OrderBy(file => file.ModuleTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actualFiles = actual.PendingFiles
            .OrderBy(file => file.ModuleTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Equal(expectedFiles.Length, actualFiles.Length, $"{description}: pending-file count");
        for (var index = 0; index < expectedFiles.Length; index++)
        {
            var left = expectedFiles[index];
            var right = actualFiles[index];
            Equal(left.ModuleTag, right.ModuleTag, $"{description}: pending module");
            Equal(left.RelativePath, right.RelativePath, $"{description}: pending path");
            Equal(left.Purpose, right.Purpose, $"{description}: pending purpose");
            Equal(left.RelatedRow, right.RelatedRow, $"{description}: pending row");
            Equal(left.CacheKey, right.CacheKey, $"{description}: pending cache key");
            SequenceEqual(left.Contents, right.Contents, $"{description}: pending contents");
        }
        SequenceEqual(
            expected.WorkspaceModuleChanges
                .OrderBy(change => change.FolderPath, StringComparer.OrdinalIgnoreCase),
            actual.WorkspaceModuleChanges
                .OrderBy(change => change.FolderPath, StringComparer.OrdinalIgnoreCase),
            $"{description}: workspace changes");
    }

    private static void AssertHistoryEqual(
        EditHistorySnapshot expected,
        EditHistorySnapshot actual,
        string description)
    {
        SequenceEqual(expected.Undo, actual.Undo, $"{description}: undo stack");
        SequenceEqual(expected.Redo, actual.Redo, $"{description}: redo stack");
    }

    private static string LoadManifestName(string folder)
        => new GalaxyMapModuleManifestStore().Load(folder).Name;

    private static string LoadClusterName(string folder, int rowId)
    {
        var manifest = new GalaxyMapModuleManifestStore().Load(folder);
        return new CsvGalaxyMapLoader().LoadPartFolder(folder, manifest)
            .Clusters.Single(cluster => cluster.RowId == rowId).NameText;
    }

    private static InspectorFieldViewModel ClusterXField(MainViewModel viewModel)
        => viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
            .Fields.Single(field => field.Name == "X");

    private static double TableClusterX(MainViewModel viewModel, GalaxyMapRowKey key)
    {
        var columnIndex = viewModel.TableViewer.Columns
            .Select((column, index) => (column, index))
            .Single(pair => string.Equals(pair.column.Name, "X", StringComparison.OrdinalIgnoreCase)).index;
        var value = viewModel.TableViewer.Rows.Single(row => row.Key == key).Cells[columnIndex].DisplayValue;
        return double.Parse(value, CultureInfo.InvariantCulture);
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

    private static double DistinctCoordinate(double current, double first, double second)
        => Math.Abs(current - first) > 0.000001 ? first : second;

    private static string TextureDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "textures");
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Test texture directory was not deployed: {path}");
        }

        return path;
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"le1-galaxy-phase-zero-{Guid.NewGuid():N}");
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

    private static void Equal<T>(T expected, T actual, string description)
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

    private sealed record CommitFixture(
        string Folder,
        string OriginalModuleName,
        GalaxyMapModule Module,
        GalaxyMapLayer Layer,
        Cluster Physical,
        string CsvPath,
        GalaxyMapWorkspace Workspace,
        EditorSession Session,
        EditSessionService Edits);

    private sealed record CommitLayer(
        string Folder,
        string OriginalModuleName,
        GalaxyMapModule Module,
        GalaxyMapLayer Layer,
        Cluster Physical,
        string CsvPath);
}
