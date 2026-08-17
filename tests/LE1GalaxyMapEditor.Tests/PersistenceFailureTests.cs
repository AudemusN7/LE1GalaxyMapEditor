using System.Globalization;
using System.IO;
using System.Text;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;

namespace LE1GalaxyMapEditor.Tests;

internal static partial class PersistenceSafetyTests
{
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

    private static void PccPartialCommitIsRetryable()
    {
        WithTemporaryDirectory(parent =>
        {
            var cookedPath = Directory.CreateDirectory(Path.Combine(parent, "CookedPCConsole")).FullName;
            var packagePath = Path.Combine(cookedPath, "GXM_Partial_Retry.pcc");
            new GalaxyMapTemplatePackageService().Create(packagePath, []);
            var module = new GalaxyMapModule(
                "PCC partial retry",
                "PCC_PARTIAL_RETRY",
                ModuleColor.Cyan,
                cookedPath,
                isReadOnly: false,
                loadOrder: 10,
                new ModuleIdReservations(Cluster: new RowIdRange(100, 199)));
            var pccLoader = new PccGalaxyMapLoader();
            var layer = pccLoader.Load(packagePath, module, allowEmpty: true);
            var workspace = new GalaxyMapWorkspace(new CsvGalaxyMapLoader().LoadBuiltInLayer(), [layer]);
            workspace.SetActiveModule(module);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session, pccWriter: new PccGalaxyMapWriter(pccLoader));
            var key = new GalaxyMapRowKey(GalaxyMapTable.Cluster, 100);
            var staged = edits.ExecuteMutation(new EditMutationRequest(
                [key],
                [GalaxyMapTable.Cluster],
                () => new GalaxyMapRowFactory(workspace).CreateCluster(
                    "PCC retry Cluster", 0.4, 0.6, "Cluster22"),
                new HistoryPresentationState(key, NavigationTarget.Galaxy, module.Tag, false),
                "created PCC retry Cluster",
                IsStructural: true));
            True(staged.Succeeded, "PCC fixture stages a new Cluster");

            var pendingPath = Path.Combine(cookedPath, "textures", "pcc-before-failure.bin");
            edits.StageFile(new PendingFileWrite(
                module.Tag,
                "textures/pcc-before-failure.bin",
                [4, 2, 4, 2],
                "PCC partial boundary",
                key));
            var packageBefore = File.ReadAllBytes(packagePath);
            var revisionBefore = session.Revision;
            var compositionBefore = workspace.CompositionRevision;

            WorkflowResult failed;
            using (File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failed = edits.Commit();
            }

            True(!failed.Succeeded, "locked PCC replacement reports a partial commit");
            SequenceEqual(new byte[] { 4, 2, 4, 2 }, File.ReadAllBytes(pendingPath),
                "pending file is durable before PCC replacement fails");
            SequenceEqual(packageBefore, File.ReadAllBytes(packagePath),
                "failed PCC replacement preserves the original package bytes");
            True(layer.Clusters.Single().CsvSnapshot!.HasChanges,
                "failed PCC replacement leaves the new row dirty");
            True(session.Changes.ContainsModule(module.Tag),
                "failed PCC replacement keeps the module staged for retry");
            True(!session.History.CanUndo,
                "durable pending-file progress clears unsafe history");
            Equal(revisionBefore + 1, session.Revision,
                "partial PCC failure publishes one durable-boundary revision");
            Equal(compositionBefore + 1, workspace.CompositionRevision,
                "partial PCC failure recomposes exactly once");

            var retried = edits.Commit();

            True(retried.Succeeded, "PCC commit succeeds after the package lock is released");
            var reloaded = pccLoader.Load(packagePath, module);
            Equal("PCC retry Cluster", reloaded.Clusters.Single(cluster => cluster.RowId == 100).NameText,
                "retry writes the staged PCC row");
            True(!session.Changes.HasChanges, "successful PCC retry clears staged state");
            True(!layer.Clusters.Single().CsvSnapshot!.HasChanges,
                "successful PCC retry advances the physical snapshot");
        });
    }

    private static void ShaderPreflightPreservesCompleteState()
    {
        WithTemporaryDirectory(parent =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(parent, "shader-preflight")).FullName;
            var module = new GalaxyMapModule(
                "Shader preflight",
                "SHADER_PREFLIGHT",
                ModuleColor.Magenta,
                folder,
                isReadOnly: false,
                loadOrder: 10,
                ModuleIdReservations.Empty);
            new GalaxyMapModuleManifestStore().Save(module);

            var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
            var source = baseLayer.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var physical = (Planet)GalaxyMapRowCloner.CloneForOverride(source, module);
            physical.SetExtraField("Shader", string.Empty);
            var layer = new GalaxyMapLayer(module);
            layer.Upsert(physical);
            new GalaxyMapCsvWriter().WriteTable(layer, GalaxyMapTable.Planet);

            var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
            workspace.SetActiveModule(module);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session);
            var staged = edits.ExecuteMutation(new EditMutationRequest(
                [physical.Key],
                [GalaxyMapTable.Planet],
                () =>
                {
                    physical.SetExtraField("Shader", string.Empty);
                    physical.CsvSnapshot!.MarkDirty("Shader");
                },
                new HistoryPresentationState(physical.Key, NavigationTarget.Galaxy, module.Tag, true),
                "staged invalid Shader",
                IsStructural: false));
            True(staged.Succeeded, "shader fixture stages an appearance edit");

            edits.MarkMetadataDirty(module);
            edits.StageWorkspaceModuleAdded(module);
            var pendingPath = Path.Combine(folder, "textures", "shader-guard.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
            File.WriteAllBytes(pendingPath, [1, 3, 3, 7]);
            edits.StageFile(new PendingFileWrite(
                module.Tag,
                "textures/shader-guard.bin",
                [9, 9, 9],
                "shader preflight boundary",
                physical.Key));

            var changesBefore = session.Changes.Capture();
            var historyBefore = session.History.Capture();
            var revisionBefore = session.Revision;
            var compositionBefore = workspace.CompositionRevision;
            var diskBefore = CaptureFiles(
                Path.Combine(folder, GalaxyMapModuleManifestStore.FileName),
                Path.Combine(folder, "GalaxyMap_Planet_part.csv"),
                pendingPath);

            var rejected = edits.Commit();

            True(!rejected.Succeeded &&
                 rejected.Message.Contains("unique Shader", StringComparison.OrdinalIgnoreCase),
                "shader preflight rejects the invalid appearance");
            AssertChangeSetEqual(changesBefore, session.Changes.Capture(),
                "shader preflight preserves the complete change set");
            AssertHistoryEqual(historyBefore, session.History.Capture(),
                "shader preflight preserves complete undo/redo history");
            Equal(revisionBefore, session.Revision,
                "shader preflight preserves the session revision");
            Equal(compositionBefore, workspace.CompositionRevision,
                "shader preflight performs no recomposition");
            AssertFilesEqual(diskBefore, CaptureFiles(diskBefore.Keys.ToArray()),
                "shader preflight preserves every relevant disk byte");
        });
    }
}
