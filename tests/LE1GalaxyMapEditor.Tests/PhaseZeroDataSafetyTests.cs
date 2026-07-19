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
/// Characterisation and regression coverage for data-safety paths identified in phase zero.
/// </summary>
internal static class PhaseZeroDataSafetyTests
{
    private const string ClusterCsvFileName = "GalaxyMap_Cluster_part.csv";

    public static void Register(Action<string, Action> run)
    {
        run("Phase 1: writable physical-instance edit recomposes projections", WritablePhysicalInstanceEditRecomposesProjections);
        run("Phase 1: partial commit refreshes MainViewModel projections", PartialCommitRefreshesMainViewModelProjections);
        run("Phase 0: pending-file failure stops later commit stages", PendingFileFailureStopsLaterStages);
        run("Phase 0: CSV failure preserves retryable module state", CsvFailurePreservesRetryableState);
        run("Phase 0: manifest failure exposes completed file and CSV writes", ManifestFailureAfterEarlierWrites);
        run("Phase 0: partial multi-module commit isolates failed module", PartialMultiModuleCommitIsolation);
        run("Phase 1: manifest-backed read-only metadata commits", ManifestBackedReadOnlyMetadataCommits);
        run("Phase 1: unmanifested read-only metadata waits for workspace persistence", UnmanifestedReadOnlyMetadataWaitsForWorkspacePersistence);
        run("Phase 1: workspace metadata failure isolates earlier durable writes", WorkspaceMetadataFailureIsolatesEarlierDurableWrites);
    }

    private static void WritablePhysicalInstanceEditRecomposesProjections()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(TextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));

            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(
                    parent,
                    "Physical Instance Test",
                    "PHYSICAL_INSTANCE_TEST",
                    ModuleColor.Cyan,
                    ModuleIdReservations.Empty),
                "writable module is created");

            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            FindNode(viewModel, key).IsSelected = true;
            var xField = ClusterXField(viewModel);
            var baseX = double.Parse(xField.Value, CultureInfo.InvariantCulture);
            var firstX = DistinctCoordinate(baseX, 0.21, 0.31);
            xField.Value = firstX.ToString("R", CultureInfo.InvariantCulture);

            var module = viewModel.ActiveModule!;
            var layer = viewModel.Workspace!.ModuleLayers.Single(candidate =>
                string.Equals(candidate.Module.Tag, module.Tag, StringComparison.OrdinalIgnoreCase));
            var physical = (Cluster)layer.Find(key)!;
            NearlyEqual(firstX, physical.X, "initial effective edit creates the physical override");
            NearlyEqual(firstX, viewModel.Document!.ClustersByRowId[key.RowId].X,
                "initial effective edit recomposes the document");

            viewModel.RowInstanceTabs.Single(tab =>
                string.Equals(tab.Module.Tag, module.Tag, StringComparison.OrdinalIgnoreCase)).SelectCommand.Execute(null);
            xField = ClusterXField(viewModel);
            var secondX = DistinctCoordinate(firstX, 0.72, 0.82);
            xField.Value = secondX.ToString("R", CultureInfo.InvariantCulture);

            NearlyEqual(secondX, physical.X, "writable physical row receives the edit");
            NearlyEqual(secondX,
                double.Parse(ClusterXField(viewModel).Value, CultureInfo.InvariantCulture),
                "physical-instance inspector reconstructs from the edited physical row");

            NearlyEqual(secondX, viewModel.Document!.ClustersByRowId[key.RowId].X,
                "physical-instance edit recomposes the effective document");
            NearlyEqual(secondX, ((Cluster)FindNode(viewModel, key).Item).X,
                "hierarchy retargets to the recomposed effective row");

            viewModel.TableViewer.SelectedTable = GalaxyMapTable.Cluster;
            viewModel.TableViewer.RefreshIfNeeded(force: true);
            NearlyEqual(secondX, TableClusterX(viewModel, key),
                "table projection follows the recomposed effective document");

            True(viewModel.UndoCommand.CanExecute(null), "physical edit creates an undo checkpoint");
            viewModel.UndoCommand.Execute(null);

            var restoredLayer = viewModel.Workspace!.ModuleLayers.Single(candidate =>
                string.Equals(candidate.Module.Tag, module.Tag, StringComparison.OrdinalIgnoreCase));
            var restoredPhysical = (Cluster)restoredLayer.Find(key)!;
            NearlyEqual(firstX, restoredPhysical.X, "undo restores the physical module row");
            NearlyEqual(firstX, viewModel.Document!.ClustersByRowId[key.RowId].X,
                "undo restores the effective document");
            NearlyEqual(firstX, ((Cluster)FindNode(viewModel, key).Item).X,
                "undo restores the hierarchy projection");
            viewModel.TableViewer.RefreshIfNeeded(force: true);
            NearlyEqual(firstX, TableClusterX(viewModel, key), "undo restores the table projection");
            NearlyEqual(firstX,
                double.Parse(ClusterXField(viewModel).Value, CultureInfo.InvariantCulture),
                "undo restores the physical-instance inspector");
        });
    }

    private static void PartialCommitRefreshesMainViewModelProjections()
    {
        WithTemporaryDirectory(parent =>
        {
            using var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(TextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads for partial-commit presentation coverage");
            True(viewModel.CreateModule(
                    parent,
                    "Partial UI module",
                    "PARTIAL_UI",
                    ModuleColor.Cyan,
                    ModuleIdReservations.Empty),
                "writable partial-commit module is created");

            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1);
            FindNode(viewModel, key).IsSelected = true;
            var originalX = double.Parse(ClusterXField(viewModel).Value, CultureInfo.InvariantCulture);
            var editedX = DistinctCoordinate(originalX, 0.64, 0.74);
            ClusterXField(viewModel).Value = editedX.ToString("R", CultureInfo.InvariantCulture);
            var module = viewModel.ActiveModule!;
            True(viewModel.UpdateModuleMetadata(
                    module,
                    "Partial UI module updated",
                    module.Tag,
                    ModuleColor.Magenta,
                    module.LoadOrder,
                    module.Reservations),
                "metadata is staged after the scalar row edit");

            var documentBeforeCommit = viewModel.Document;
            var revisionBeforeCommit = viewModel.Session.Revision;
            var compositionBeforeCommit = viewModel.Workspace!.CompositionRevision;
            var manifestPath = Path.Combine(
                viewModel.ActiveModule!.FolderPath!,
                GalaxyMapModuleManifestStore.FileName);
            bool committed;
            using (File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                committed = viewModel.CommitPendingChanges();
            }

            True(!committed, "locked manifest reports the partial commit to the UI");
            Equal(revisionBeforeCommit + 1, viewModel.Session.Revision,
                "partial commit publishes one durable-boundary revision");
            Equal(compositionBeforeCommit + 1, viewModel.Workspace.CompositionRevision,
                "partial commit recomposes once after its CSV replacement");
            True(!ReferenceEquals(documentBeforeCommit, viewModel.Document),
                "MainViewModel attaches the recomposed effective document on partial failure");
            True(ReferenceEquals(viewModel.Workspace.EffectiveDocument, viewModel.Document),
                "MainViewModel projection points at the workspace's current effective document");
            NearlyEqual(editedX, viewModel.Document!.ClustersByRowId[key.RowId].X,
                "effective document reflects the durably written CSV value");
            NearlyEqual(editedX, ((Cluster)FindNode(viewModel, key).Item).X,
                "hierarchy reflects the recomposed partial-commit value");
            viewModel.TableViewer.SelectedTable = GalaxyMapTable.Cluster;
            viewModel.TableViewer.RefreshIfNeeded(force: true);
            NearlyEqual(editedX, TableClusterX(viewModel, key),
                "2DA projection reflects the recomposed partial-commit value");
            True(viewModel.HasPendingChanges,
                "failed manifest finalisation remains staged for retry");
            True(!viewModel.UndoCommand.CanExecute(null),
                "partial disk durability removes unsafe undo history");

            True(viewModel.CommitPendingChanges(), "partial commit retries after the manifest lock is released");
            True(!viewModel.HasPendingChanges, "successful retry clears the remaining staged state");
            Equal("Partial UI module updated", LoadManifestName(viewModel.ActiveModule.FolderPath!),
                "retry persists the remaining manifest metadata");
        });
    }

    private static void PendingFileFailureStopsLaterStages()
    {
        WithTemporaryDirectory(parent =>
        {
            var fixture = CreateCommitFixture(parent, "PENDING_BOUNDARY", loadOrder: 10, clusterIndex: 0);
            StageCompleteModuleChange(fixture, "Pending boundary value", "textures/blocked.bin", [9, 8, 7]);

            var pendingPath = Path.Combine(fixture.Folder, "textures", "blocked.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
            File.WriteAllBytes(pendingPath, [1, 2, 3]);
            var csvBefore = File.ReadAllBytes(fixture.CsvPath);
            var revisionBefore = fixture.Session.Revision;
            var compositionBefore = fixture.Workspace.CompositionRevision;
            var publishedImpacts = ObservePublishedImpacts(fixture.Session);

            WorkflowResult failed;
            using (File.Open(pendingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = fixture.Edits.Commit();
            }

            True(!failed.Succeeded, "locked pending target fails the module commit");
            SequenceEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(pendingPath),
                "failed pending replacement leaves its prior bytes intact");
            SequenceEqual(csvBefore, File.ReadAllBytes(fixture.CsvPath),
                "CSV writing is not attempted after the pending-file failure");
            Equal(fixture.OriginalModuleName, LoadManifestName(fixture.Folder),
                "manifest writing is not attempted after the pending-file failure");
            True(fixture.Physical.CsvSnapshot!.IsDirty("NameText"),
                "unwritten CSV row remains dirty");
            AssertNoDurableProgress(fixture, revisionBefore, compositionBefore, publishedImpacts,
                "pending-file failure");

            var retried = fixture.Edits.Commit();
            True(retried.Succeeded, "pending-file failure is retryable after the lock is released");
            SequenceEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(pendingPath), "retry writes pending bytes");
            AssertSuccessfulRetry(fixture, "Pending boundary value", revisionBefore + 1);
        });
    }

    private static void CsvFailurePreservesRetryableState()
    {
        WithTemporaryDirectory(parent =>
        {
            var fixture = CreateCommitFixture(parent, "CSV_BOUNDARY", loadOrder: 10, clusterIndex: 0);
            StageCompleteModuleChange(fixture, "CSV boundary value", "textures/written-before-csv.bin", [4, 5, 6]);
            var csvBefore = File.ReadAllBytes(fixture.CsvPath);
            var revisionBefore = fixture.Session.Revision;
            var compositionBefore = fixture.Workspace.CompositionRevision;
            var publishedImpacts = ObservePublishedImpacts(fixture.Session);

            WorkflowResult failed;
            using (File.Open(fixture.CsvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = fixture.Edits.Commit();
            }

            True(!failed.Succeeded, "locked CSV target fails the module commit");
            SequenceEqual(new byte[] { 4, 5, 6 },
                File.ReadAllBytes(Path.Combine(fixture.Folder, "textures", "written-before-csv.bin")),
                "pending file is durably written before the CSV failure");
            SequenceEqual(csvBefore, File.ReadAllBytes(fixture.CsvPath),
                "failed CSV replacement preserves the prior committed bytes");
            Equal(fixture.OriginalModuleName, LoadManifestName(fixture.Folder),
                "manifest is not written after the CSV failure");
            True(fixture.Physical.CsvSnapshot!.IsDirty("NameText"),
                "failed CSV replacement preserves the dirty physical snapshot");
            AssertDurablePartialFailure(
                fixture,
                revisionBefore,
                compositionBefore,
                publishedImpacts,
                expectedTables: [],
                expectedRows: [],
                "CSV failure after its pending file");
            True(fixture.Workspace.EffectiveDocument.ClustersByRowId[fixture.Physical.RowId]
                    .CsvSnapshot!.IsDirty("NameText"),
                "recomposition retains the retryable dirty CSV row when CSV replacement failed");

            var retried = fixture.Edits.Commit();
            True(retried.Succeeded, "CSV failure is retryable after the lock is released");
            AssertSuccessfulRetry(fixture, "CSV boundary value", revisionBefore + 2);
        });
    }

    private static void ManifestFailureAfterEarlierWrites()
    {
        WithTemporaryDirectory(parent =>
        {
            var fixture = CreateCommitFixture(parent, "MANIFEST_BOUNDARY", loadOrder: 10, clusterIndex: 0);
            StageCompleteModuleChange(fixture, "Manifest boundary value", "textures/written-before-manifest.bin", [7, 7, 7]);
            var manifestPath = Path.Combine(fixture.Folder, GalaxyMapModuleManifestStore.FileName);
            var revisionBefore = fixture.Session.Revision;
            var compositionBefore = fixture.Workspace.CompositionRevision;
            var publishedImpacts = ObservePublishedImpacts(fixture.Session);

            WorkflowResult failed;
            using (File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = fixture.Edits.Commit();
            }

            True(!failed.Succeeded, "locked manifest fails the module commit");
            SequenceEqual(new byte[] { 7, 7, 7 },
                File.ReadAllBytes(Path.Combine(fixture.Folder, "textures", "written-before-manifest.bin")),
                "pending file remains written after the later manifest failure");
            Equal("Manifest boundary value", LoadClusterName(fixture.Folder, fixture.Physical.RowId),
                "CSV remains written after the later manifest failure");
            Equal(fixture.OriginalModuleName, LoadManifestName(fixture.Folder),
                "locked manifest retains its prior metadata");
            True(!fixture.Physical.CsvSnapshot!.IsDirty("NameText"),
                "successful CSV writing advances the physical snapshot before manifest failure");
            True(!fixture.Workspace.EffectiveDocument.ClustersByRowId[fixture.Physical.RowId]
                    .CsvSnapshot!.IsDirty("NameText"),
                "partial-failure recomposition reflects the successfully written CSV snapshot");
            AssertDurablePartialFailure(
                fixture,
                revisionBefore,
                compositionBefore,
                publishedImpacts,
                expectedTables: [GalaxyMapTable.Cluster],
                expectedRows: [fixture.Physical.Key],
                "manifest failure after earlier writes");

            var retried = fixture.Edits.Commit();
            True(retried.Succeeded, "manifest failure is retryable after the lock is released");
            AssertSuccessfulRetry(fixture, "Manifest boundary value", revisionBefore + 2);
        });
    }

    private static void PartialMultiModuleCommitIsolation()
    {
        WithTemporaryDirectory(parent =>
        {
            var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
            var first = CreateCommitLayer(parent, baseLayer, "FIRST_PARTIAL", 10, clusterIndex: 0);
            var second = CreateCommitLayer(parent, baseLayer, "SECOND_PARTIAL", 20, clusterIndex: 1);
            var workspace = new GalaxyMapWorkspace(baseLayer, [first.Layer, second.Layer]);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session);

            workspace.SetActiveModule(first.Module);
            StageRowEdit(edits, first.Physical, "First committed value", first.Module);
            edits.MarkMetadataDirty(first.Module);
            edits.StageFile(new PendingFileWrite(first.Module.Tag, "textures/first.bin", [1, 1], "phase-zero"));

            workspace.SetActiveModule(second.Module);
            StageRowEdit(edits, second.Physical, "Second retry value", second.Module);
            edits.MarkMetadataDirty(second.Module);
            edits.StageFile(new PendingFileWrite(second.Module.Tag, "textures/second.bin", [2, 2], "phase-zero"));
            var revisionBefore = session.Revision;
            var compositionBefore = workspace.CompositionRevision;
            var publishedImpacts = ObservePublishedImpacts(session);

            WorkflowResult failed;
            using (File.Open(second.CsvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = edits.Commit();
            }

            True(!failed.Succeeded, "one locked module causes the aggregate commit to report failure");
            True(!session.Changes.ContainsModule(first.Module.Tag),
                "earlier successful module is cleared from the change set");
            True(session.Changes.ContainsModule(second.Module.Tag),
                "failed module remains in the change set for retry");
            Equal("First committed value", LoadClusterName(first.Folder, first.Physical.RowId),
                "earlier module CSV remains committed");
            Equal(first.Module.Name, LoadManifestName(first.Folder),
                "earlier module manifest remains committed");
            SequenceEqual(new byte[] { 1, 1 }, File.ReadAllBytes(Path.Combine(first.Folder, "textures", "first.bin")),
                "earlier module pending file remains committed");
            Equal(second.OriginalModuleName, LoadManifestName(second.Folder),
                "failed module manifest remains unchanged");
            True(!session.History.CanUndo,
                "aggregate partial failure clears history once the earlier module is durable");
            Equal(revisionBefore + 1, session.Revision,
                "aggregate partial failure publishes exactly one session revision");
            Equal(compositionBefore + 1, workspace.CompositionRevision,
                "aggregate partial failure recomposes exactly once");
            Equal(1, publishedImpacts.Count,
                "aggregate partial failure publishes one impact");
            True(!publishedImpacts[0].IsStructural,
                "aggregate partial failure publishes a nonstructural commit impact");
            SequenceEqual(new[] { GalaxyMapTable.Cluster }, publishedImpacts[0].Tables,
                "aggregate partial failure reports its successfully written table");
            SequenceEqual(new[] { first.Physical.Key }, publishedImpacts[0].Rows,
                "aggregate partial failure reports rows made durable before failure");
            True(!workspace.EffectiveDocument.ClustersByRowId[first.Physical.RowId]
                    .CsvSnapshot!.IsDirty("NameText"),
                "recomposition reflects the earlier module's clean durable snapshot");
            True(workspace.EffectiveDocument.ClustersByRowId[second.Physical.RowId]
                    .CsvSnapshot!.IsDirty("NameText"),
                "recomposition retains the later module's retryable dirty snapshot");

            var retried = edits.Commit();
            True(retried.Succeeded, "retry commits only the remaining failed module");
            True(!session.Changes.HasChanges, "retry clears the remaining module state");
            Equal("Second retry value", LoadClusterName(second.Folder, second.Physical.RowId),
                "retry writes the failed module CSV");
            Equal(second.Module.Name, LoadManifestName(second.Folder),
                "retry writes the failed module manifest");
            True(!session.History.CanUndo, "fully successful retry clears history");
            Equal(revisionBefore + 2, session.Revision,
                "fully successful retry publishes once after the partial-failure revision");
        });
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
