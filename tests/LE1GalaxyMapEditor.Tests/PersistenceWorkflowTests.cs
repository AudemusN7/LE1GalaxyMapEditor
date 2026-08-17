using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Converters;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Presentation;
using LE1GalaxyMapEditor.Rendering;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;
using LE1GalaxyMapEditor.Workflows.Ports;
using LE1GalaxyMapEditor.Workflows.Queries;
using LegendaryExplorerCore.Packages;

namespace LE1GalaxyMapEditor.Tests;

internal static partial class Program
{
    private static void ModuleManifestRoundTrip()
    {
        WithTemporaryDirectory(folder =>
        {
            var module = new GalaxyMapModule(
                "Test Expansion", "TEST_EXPANSION", ModuleColor.Purple, folder,
                isReadOnly: false, loadOrder: 7,
                new ModuleIdReservations(
                    new RowIdRange(50, 59),
                    new RowIdRange(100, 109),
                    new RowIdRange(8000, 8099),
                    new RowIdRange(500, 599),
                    new RowIdRange(50, 79)),
                planetTextureLinks:
                [
                    new PlanetTextureLink(
                        "stable-texture-id",
                        "BIOA_TEST_EXPANSION_T.CustomPlanet01",
                        "textures/Planet_stable-texture-id_preview.png",
                        PlanetTextureCategory.Continent | PlanetTextureCategory.Atmosphere)
                ]);
            var store = new GalaxyMapModuleManifestStore();
            store.Save(module);
            var loaded = store.Load(folder);

            Equal(module.Name, loaded.Name, "manifest name");
            Equal(module.Tag, loaded.Tag, "manifest tag");
            Equal(module.Color, loaded.Color, "manifest colour");
            Equal(module.LoadOrder, loaded.LoadOrder, "manifest load order");
            Equal(module.Reservations.Planet!.Value, loaded.Reservations.Planet!.Value, "manifest Planet range");
            var planetTexture = loaded.PlanetTextureLinks.Single();
            Equal("stable-texture-id", planetTexture.Id, "manifest preserves stable Planet texture identity");
            Equal("BIOA_TEST_EXPANSION_T.CustomPlanet01", planetTexture.InMemoryPath,
                "manifest preserves Planet texture in-memory path");
            Equal("textures/Planet_stable-texture-id_preview.png", planetTexture.RelativePath,
                "manifest preserves independent local Planet texture path");
            Equal(PlanetTextureCategory.Continent | PlanetTextureCategory.Atmosphere, planetTexture.Categories,
                "manifest preserves Planet texture menu categories");
            var renamed = PlanetTextureWorkflow.CreateRenamedReference(
                loaded,
                planetTexture.InMemoryPath,
                "BIOA_TEST_EXPANSION_T.RenamedPlanet01",
                out var renameError);
            True(renameError is null && renamed is not null, "2DA Planet texture references can be renamed");
            Equal(planetTexture.Id, renamed!.PlanetTextureLinks.Single().Id,
                "renaming a Planet texture reference keeps its stable identity");
            Equal(planetTexture.RelativePath, renamed.PlanetTextureLinks.Single().RelativePath,
                "renaming a Planet texture reference keeps its staged local file");
            True(!loaded.IsReadOnly, "authoring manifest remains writable");
            True(File.Exists(Path.Combine(folder, GalaxyMapModuleManifestStore.FileName)), "manifest file exists");
        });
    }

    private static void PartialLayersOverrideDeterministically()
    {
        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Mounted DLC", "MOUNTED_DLC", ModuleColor.Red, null,
            isReadOnly: true, loadOrder: 1,
            new ModuleIdReservations(Cluster: new RowIdRange(50, 59)));
        var layer = new GalaxyMapLayer(module);
        var source = baseLayer.Clusters.Single(row => row.RowId == 1);
        var overridden = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, module);
        overridden.NameText = "Overridden Serpent";
        layer.Upsert(overridden);
        layer.Upsert(new Cluster
        {
            RowId = 50,
            Label = "Cluster50",
            X = 0.4,
            Y = 0.6,
            NameText = "New DLC Cluster",
            SphereSize = 4,
            Background = "BIOA_GalaxyMap_T.Cluster01"
        });

        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        Equal("Overridden Serpent", workspace.EffectiveDocument.ClustersByRowId[1].NameText,
            "same-ID module row wins");
        Equal("Serpent Nebula", source.NameText, "lower physical row is untouched");
        Equal("MOUNTED_DLC", workspace.EffectiveDocument.ClustersByRowId[1].Origin!.ModuleTag,
            "effective override provenance");
        True(workspace.EffectiveDocument.ClustersByRowId.ContainsKey(50), "new module row is composed");
        Equal(2, workspace.GetOverrideChain(new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1)).Count,
            "override chain retains both physical rows");
    }

    private static void AtomicPartialCsvWriterContract()
    {
        WithTemporaryDirectory(folder =>
        {
            var loader = new CsvGalaxyMapLoader();
            var workspace = new GalaxyMapWorkspace(loader.LoadBuiltInLayer());
            var module = CreateTestModule(folder, "WRITER_TEST", ModuleColor.Green);
            var layer = new GalaxyMapLayer(module);
            workspace.Mount(layer);
            workspace.SetActiveModule(module);

            var factory = new GalaxyMapRowFactory(workspace);
            factory.CreateCluster("First new Cluster");
            factory.CreateCluster("Second new Cluster");
            layer.Clusters.Move(1, 0);

            var baseCluster = workspace.EffectiveDocument.ClustersByRowId[1];
            var baseOverride = (Cluster)GalaxyMapRowCloner.CloneForOverride(baseCluster, module);
            baseOverride.X = 0.314159;
            baseOverride.CsvSnapshot!.MarkDirty("X");
            layer.Upsert(baseOverride);

            new GalaxyMapCsvWriter().WriteTable(layer, GalaxyMapTable.Cluster);
            var path = Path.Combine(folder, "GalaxyMap_Cluster_part.csv");
            var bytes = File.ReadAllBytes(path);
            SequenceEqual([0xEF, 0xBB, 0xBF], bytes.Take(3).Select(value => (int)value), "UTF-8 BOM");
            var text = File.ReadAllText(path, Encoding.UTF8);
            True(text.EndsWith("\r\n", StringComparison.Ordinal), "final CRLF");
            True(!text.Replace("\r\n", string.Empty).Contains('\n'), "records do not use bare LF");
            True(text.TrimStart('\uFEFF').StartsWith(",Label,X,Y", StringComparison.Ordinal),
                "unnamed first header is preserved");

            var ids = text.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(line => int.Parse(line[..line.IndexOf(',')], CultureInfo.InvariantCulture)).ToArray();
            SequenceEqual(ids.OrderBy(id => id), ids, "writer sorts rows numerically");

            var caseVariantLines = File.ReadAllLines(path, Encoding.UTF8);
            caseVariantLines[0] = caseVariantLines[0].ToLowerInvariant();
            File.WriteAllLines(path, caseVariantLines, new UTF8Encoding(true));

            var reloaded = loader.LoadPartFolder(folder, module);
            Equal("label", reloaded.GetSchema(GalaxyMapTable.Cluster)!.Headers[1],
                "partial CSV headers are accepted without case sensitivity");
            var reloadedOverride = reloaded.Clusters.Single(row => row.RowId == 1);
            NearlyEqual(0.314159, reloadedOverride.X, "dirty value was serialized");
            Equal(baseCluster.NameText, reloadedOverride.NameText, "untouched known value preserved");
            Equal(baseCluster.ExtraFields["Colour"], reloadedOverride.ExtraFields["Colour"],
                "untouched raw extra value preserved");
        });
    }

    private static void DuplicateRowDeleteFollowsActiveModule()
    {
        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var source = baseLayer.Planets.First();
        var activeModule = new GalaxyMapModule(
            "Active delete target",
            "ACTIVE_DELETE",
            ModuleColor.Cyan,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 1,
            TestReservations());
        var highestModule = new GalaxyMapModule(
            "Highest mounted override",
            "HIGHEST_OVERRIDE",
            ModuleColor.Red,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 2,
            TestReservations());
        var activeLayer = new GalaxyMapLayer(activeModule);
        var highestLayer = new GalaxyMapLayer(highestModule);
        var activePlanet = (Planet)GalaxyMapRowCloner.CloneForOverride(source, activeModule);
        var highestPlanet = (Planet)GalaxyMapRowCloner.CloneForOverride(source, highestModule);
        activePlanet.NameText = "Active-module instance";
        highestPlanet.NameText = "Highest-mounted instance";
        activeLayer.Upsert(activePlanet);
        highestLayer.Upsert(highestPlanet);

        var workspace = new GalaxyMapWorkspace(baseLayer, [activeLayer, highestLayer]);
        workspace.SetActiveModule(activeModule);
        var session = new EditorSession(workspace);
        var edits = new EditSessionService(session);
        var workflow = new RowAuthoringWorkflow(session, edits, new InspectorEditWorkflow(session, edits));
        var visiblePlanet = (Planet)workspace.Resolve(source.Key)!;
        Equal(highestModule.Tag, visiblePlanet.Origin!.ModuleTag,
            "the hierarchy row is supplied by the highest-mounted module");

        var presentation = new HistoryPresentationState(
            visiblePlanet.Key,
            NavigationTarget.Galaxy,
            activeModule.Tag,
            InspectPhysicalInstance: true);
        var deleted = workflow.Delete(visiblePlanet, presentation);

        True(deleted.Succeeded, "duplicate row deletion succeeds");
        True(activeLayer.Find(source.Key) is null,
            "deletion removes the same-key physical row from the active module");
        NotNull(highestLayer.Find(source.Key),
            "deletion leaves the higher-mounted module's physical row intact");
        Equal(highestModule.Tag, workspace.Resolve(source.Key)!.Origin!.ModuleTag,
            "the surviving effective row still comes from the highest-mounted module");
        Equal(activeModule.Tag, workspace.ActiveModule!.Tag,
            "deletion does not silently switch the active authoring module");
        True(session.Changes.DirtyTables.TryGetValue(activeModule.Tag, out var activeDirtyTables) &&
             activeDirtyTables.Contains(GalaxyMapTable.Planet),
            "the active module's Planet table is staged for writing");
        True(!session.Changes.DirtyTables.ContainsKey(highestModule.Tag),
            "the higher-mounted module is not marked dirty");

        var restored = edits.Undo(presentation);
        True(restored.Succeeded, "active-module deletion participates in undo");
        var restoredActiveLayer = session.Workspace!.ModuleLayers.Single(layer =>
            string.Equals(layer.Module.Tag, activeModule.Tag, StringComparison.OrdinalIgnoreCase));
        NotNull(restoredActiveLayer.Find(source.Key),
            "undo restores the deleted active-module row");
    }

    private static void TableProjectionFollowsEditorSession()
    {
        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Projection Test",
            "PROJECTION_TEST",
            ModuleColor.Cyan,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 1,
            TestReservations());
        var layer = new GalaxyMapLayer(module);
        var canonical = CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster);
        layer.SetSchema(new CsvTableSchema(
            GalaxyMapTable.Cluster,
            canonical.Headers.Concat(["FutureColumn"])));

        var source = baseLayer.Clusters.First();
        var physical = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, module);
        physical.X += 0.01;
        physical.AddExtraField("FutureColumn", "retained");
        physical.CsvSnapshot!.MarkDirty("X");
        layer.Upsert(physical);

        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        workspace.SetActiveModule(module);
        var session = new EditorSession(workspace);
        var edits = new EditSessionService(session);
        edits.MarkTableDirty(module, GalaxyMapTable.Cluster);
        edits.Publish(ChangeImpact.For([GalaxyMapTable.Cluster], [physical.Key], isStructural: false));

        var snapshot = new TableProjectionService(session).Project(GalaxyMapTable.Cluster);
        Equal(session.Revision, snapshot.SessionRevision, "projection carries the current session revision");
        SequenceEqual(snapshot.Rows.Select(row => row.Key.RowId).OrderBy(id => id),
            snapshot.Rows.Select(row => row.Key.RowId), "projection sorts true sparse row IDs");
        True(snapshot.Columns.All(column => column.IsCanonical) &&
             snapshot.Columns.All(column => column.Name != "FutureColumn"),
            "writable workspace projections expose only importable canonical columns");

        var projected = snapshot.Rows.Single(row => row.Key == physical.Key);
        var x = projected.Cells["X"];
        Equal(module.Tag, x.EffectiveModuleTag, "winning physical row supplies effective provenance");
        Equal(2, x.OverrideChain.Count, "override comparison includes both physical instances");
        True(x.DiffersFromLowerInstance, "changed values are distinguished from lower instances");
        True(x.IsStaged, "dirty session tables mark projected cells as staged");
        True(!projected.Cells["Label"].IsStaged,
            "projection marks the exact dirty cell rather than the entire staged table");
    }

    private static void TableCellEditingUsesExistingWorkflows()
    {
        var longTlkTooltip = new TableCellViewModel(
            "123",
            GalaxyMapModule.BaseGameTag,
            ModuleColor.BaseGameBlue,
            isStaged: false,
            differsFromLowerInstance: false,
            overrideCount: 1,
            "Effective value supplied by BIOGame_INT.pcc.\n\n" + new string('x', 125)).ToolTipText;
        True(longTlkTooltip.Split('\n').All(line => line.Length <= 60),
            "2DA hover text wraps every line to at most 60 characters");

        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Table Editing Test",
            "TABLE_EDIT_TEST",
            ModuleColor.Magenta,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 1,
            TestReservations());
        var layer = new GalaxyMapLayer(module);
        foreach (var table in Enum.GetValues<GalaxyMapTable>())
        {
            layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(table));
        }

        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        workspace.SetActiveModule(module);
        var session = new EditorSession(workspace);
        var edits = new EditSessionService(session);
        var workflow = new InspectorEditWorkflow(session, edits);
        var source = workspace.EffectiveDocument.Clusters.First(cluster =>
            cluster.Systems.SelectMany(system => system.Planets).Any());
        var presentation = new HistoryPresentationState(
            source.Key,
            NavigationTarget.Galaxy,
            null,
            InspectPhysicalInstance: false);

        var invalid = workflow.ApplyTableCellEdit(source, "X", "not-a-number", module, presentation);
        True(!invalid.Succeeded, "invalid table token is rejected before staging");
        True(layer.Find(source.Key) is null, "invalid table token does not materialise an override");

        var tableViewer = new TableViewerViewModel(
            new TableProjectionService(session),
            (key, column, token) => workflow.ApplyTableCellEdit(
                (GalaxyMapRow)workspace.Resolve(key)!, column, token, module, presentation),
            () => true);
        tableViewer.RefreshIfNeeded();
        var projectedSource = tableViewer.Rows.Single(row => row.Key == source.Key);
        var xColumnIndex = tableViewer.Columns.ToList().FindIndex(column => column.Name == "X");
        projectedSource.Cells[xColumnIndex].EditValue = "not-a-number";
        var invalidCell = tableViewer.CommitCellEdit(projectedSource, xColumnIndex, "not-a-number");
        True(!invalidCell.Succeeded && projectedSource.Cells[xColumnIndex].HasError,
            "invalid table input immediately marks its cell as invalid");
        tableViewer.CancelCellEdit(projectedSource, xColumnIndex);
        True(!projectedSource.Cells[xColumnIndex].HasError,
            "cancelling an invalid table edit clears its validation state");
        Equal(projectedSource.Cells[xColumnIndex].DisplayValue, projectedSource.Cells[xColumnIndex].EditValue,
            "cancelling an invalid table edit restores the projected value");

        var locked = workflow.ApplyTableCellEdit(source, CsvRowSnapshot.RowIdColumnName, "999", module, presentation);
        True(!locked.Succeeded, "Row ID remains structurally read-only in the table editor");

        var newX = source.X < 0.5 ? "0.73" : "0.27";
        var scalar = workflow.ApplyTableCellEdit(source, "X", newX, module, presentation);
        True(scalar.Succeeded, "ordinary table scalar edit succeeds through InspectorEditWorkflow");
        var physical = (Cluster)layer.Find(source.Key)!;
        True(physical.CsvSnapshot!.IsDirty("X"), "edited table column is marked dirty");
        True(!physical.CsvSnapshot.IsDirty("Label"), "untouched table column remains clean");

        var projected = new TableProjectionService(session).Project(GalaxyMapTable.Cluster)
            .Rows.Single(row => row.Key == source.Key);
        True(projected.Cells["X"].IsStaged, "table projection highlights the edited cell");
        True(!projected.Cells["Label"].IsStaged, "table projection leaves sibling cells unhighlighted");
        Equal(module.Tag, projected.Cells["Label"].EffectiveModuleTag,
            "a one-cell BASEGAME edit correctly changes provenance for the complete physical override row");

        var currentCluster = workspace.EffectiveDocument.ClustersByRowId[source.RowId];
        var dependentPlanet = currentCluster.Systems.SelectMany(system => system.Planets).First();
        var originalActiveWorld = dependentPlanet.ActiveWorld;
        var managed = workflow.ApplyTableCellEdit(
            currentCluster,
            "Label",
            "Cluster99",
            module,
            presentation);
        True(managed.Succeeded, "managed identity cell edit succeeds through the existing cascade workflow");
        True(workspace.EffectiveDocument.PlanetsByRowId[dependentPlanet.RowId].ActiveWorld != originalActiveWorld,
            "table identity edit updates dependent ActiveWorld values");
        True(session.Changes.DirtyTables[module.Tag].Contains(GalaxyMapTable.Planet),
            "managed table edit stages its dependent table through the shared workflow");

        var undo = edits.Undo(presentation);
        True(undo.Succeeded, "table edits participate in shared undo history");
        Equal(source.Label, session.Workspace!.EffectiveDocument.ClustersByRowId[source.RowId].Label,
            "undo restores the managed identity edit while preserving the earlier scalar transaction");
    }

    private static void TableDirtyHighlightsClearAfterCommit()
    {
        WithTemporaryDirectory(folder =>
        {
            var loader = new CsvGalaxyMapLoader();
            var baseLayer = loader.LoadBuiltInLayer();
            var module = new GalaxyMapModule(
                "Table Commit Test",
                "TABLE_COMMIT_TEST",
                ModuleColor.Green,
                folder,
                isReadOnly: false,
                loadOrder: 1,
                TestReservations());
            var layer = new GalaxyMapLayer(module);
            layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster));
            var source = baseLayer.Clusters.First();
            var physical = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, module);
            physical.X = source.X < 0.5 ? 0.73 : 0.27;
            physical.CsvSnapshot!.MarkDirty("X");
            layer.Upsert(physical);
            var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
            workspace.SetActiveModule(module);
            var session = new EditorSession(workspace);
            var edits = new EditSessionService(session);
            edits.MarkTableDirty(module, GalaxyMapTable.Cluster);

            var before = new TableProjectionService(session).Project(GalaxyMapTable.Cluster)
                .Rows.Single(row => row.Key == physical.Key);
            True(before.Cells["X"].IsStaged, "dirty cell is highlighted before commit");

            var committed = edits.Commit();
            True(committed.Succeeded, "table fixture commits its staged CSV");
            var after = new TableProjectionService(session).Project(GalaxyMapTable.Cluster)
                .Rows.Single(row => row.Key == physical.Key);
            True(!after.Cells["X"].IsStaged,
                "dirty-column snapshot no longer paints as staged after the change set is committed");
        });
    }

    private static void EditTransactionRollbackAndHistoryContract()
    {
        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Transaction Test",
            "TRANSACTION_TEST",
            ModuleColor.Green,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 1,
            TestReservations());
        var layer = new GalaxyMapLayer(module);
        layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster));
        var source = baseLayer.Clusters.First();
        var physical = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, module);
        layer.Upsert(physical);

        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        workspace.SetActiveModule(module);
        var session = new EditorSession(workspace);
        var edits = new EditSessionService(session);
        var presentation = new HistoryPresentationState(
            physical.Key,
            NavigationTarget.Galaxy,
            module.Tag,
            InspectPhysicalInstance: false);
        var originalX = workspace.EffectiveDocument.ClustersByRowId[physical.RowId].X;
        var revision = session.Revision;

        var failed = edits.ExecuteMutation(new EditMutationRequest(
            [physical.Key],
            [GalaxyMapTable.Cluster],
            () =>
            {
                var replacement = (Cluster)GalaxyMapRowCloner.Clone(layer.Clusters.Single());
                replacement.X = originalX + 0.25;
                layer.Upsert(replacement);
                throw new InvalidOperationException("synthetic rollback");
            },
            presentation,
            "unreachable"));

        True(!failed.Succeeded, "expected mutation failure is reported");
        NearlyEqual(originalX, workspace.EffectiveDocument.ClustersByRowId[physical.RowId].X,
            "failed mutation restores the effective row");
        True(!session.Changes.HasChanges, "failed mutation restores the staged change set");
        Equal(0, session.History.UndoCount, "failed mutation does not leave an undo entry");
        Equal(revision, session.Revision, "failed mutation does not publish a session revision");

        var succeeded = edits.ExecuteMutation(new EditMutationRequest(
            [physical.Key],
            [GalaxyMapTable.Cluster],
            () =>
            {
                var replacement = (Cluster)GalaxyMapRowCloner.Clone(layer.Clusters.Single());
                replacement.X = originalX + 0.5;
                layer.Upsert(replacement);
            },
            presentation,
            "transaction contract"));

        True(succeeded.Succeeded, "valid mutation succeeds");
        Equal(1, session.History.UndoCount, "one logical mutation creates exactly one undo entry");
        Equal(revision + 1, session.Revision, "successful mutation publishes one session revision");
        True(session.Changes.DirtyTables[module.Tag].Contains(GalaxyMapTable.Cluster),
            "successful mutation stages its table");
    }

    private static void MainViewModelWritesFullRowOverrides()
    {
        WithTemporaryDirectory(parent =>
        {
            var targetRequests = 0;
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) =>
                {
                    targetRequests++;
                    return modules.Single();
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Live Editing", "LIVE_EDIT", ModuleColor.Cyan,
                TestReservations()), "authoring module is created");

            var clusterNode = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            clusterNode.IsSelected = true;
            var xField = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            xField.Value = "0.31";
            Equal(1, targetRequests, "BASEGAME edit requests a target even with one writable module");

            var moduleFolder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.HasPendingChanges, "edit remains staged");
            True(!File.Exists(Path.Combine(moduleFolder, "GalaxyMap_Cluster_part.csv")),
                "staged edit is not automatically written");
            True(viewModel.CommitPendingChanges(), "manual commit succeeds");
            True(File.Exists(Path.Combine(moduleFolder, "GalaxyMap_Cluster_part.csv")),
                "commit creates Cluster_part CSV");
            NearlyEqual(0.31, viewModel.Document!.ClustersByRowId[1].X, "effective edit remains visible");
            Equal("LIVE_EDIT", viewModel.Document.ClustersByRowId[1].Origin!.ModuleTag,
                "edited BASEGAME row becomes an active-module override");
            Equal(2, viewModel.Workspace!.GetOverrideChain(
                new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1)).Count, "full override chain");

            var physical = viewModel.Workspace.ActiveLayer!.Clusters.Single(row => row.RowId == 1);
            Equal(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster).Headers.Count,
                physical.CsvSnapshot!.Headers.Count, "override contains every canonical column");
            Equal("Serpent Nebula", physical.NameText, "unchanged BASEGAME field copied into full override");
        });
    }

    private static void CommitPreviewDescribesAndProtectsStagedWrites()
    {
        WithTemporaryDirectory(parent =>
        {
            CommitPreview? reviewed = null;
            var allowCommit = false;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) => modules.Single(),
                confirmAction: _ => true,
                commitReviewAction: preview =>
                {
                    reviewed = preview;
                    return allowCommit;
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Preview Test", "PREVIEW_TEST", ModuleColor.Cyan,
                TestReservations()), "authoring module is created");

            var clusterNode = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            clusterNode.IsSelected = true;
            var xField = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            var original = xField.Value;
            xField.Value = "0.31";

            var directPreview = viewModel.CreateCommitPreview();
            Equal(1, directPreview.ChangeCount, "one changed field is counted");
            Equal(1, directPreview.FileCount, "one dirty CSV is counted");
            var section = directPreview.Sections.Single(section => section.FileName == "GalaxyMap_Cluster_part.csv");
            var entry = section.Entries.Single(entry => entry.Title == "Cluster #1");
            True(entry.Details.Single().Contains($"\"{original}\"", StringComparison.Ordinal),
                "preview includes the original CSV value");
            True(entry.Details.Single().Contains("\u2192  \"0.31\"", StringComparison.Ordinal),
                "preview includes the exact staged CSV value");

            var outputPath = Path.Combine(viewModel.ActiveModule!.FolderPath!, "GalaxyMap_Cluster_part.csv");
            viewModel.CommitCommand.Execute(null);
            NotNull(reviewed, "Commit command requests review");
            True(viewModel.HasPendingChanges, "cancelling review leaves changes staged");
            True(!File.Exists(outputPath), "cancelling review performs no CSV write");

            allowCommit = true;
            viewModel.CommitCommand.Execute(null);
            True(!viewModel.HasPendingChanges, "confirming review commits the staged changes");
            True(File.Exists(outputPath), "confirming review writes the CSV");

            var committedOverride = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            committedOverride.DeleteCommand!.Execute(null);
            var deletionPreview = viewModel.CreateCommitPreview();
            var deleted = deletionPreview.Sections
                .Single(section => section.FileName == "GalaxyMap_Cluster_part.csv")
                .Entries.Single(entry => entry.Title == "Cluster #1");
            Equal("DELETE", deleted.Badge, "removed module rows are explicit in the preview");
            True(deleted.Details.Single().Contains("removed", StringComparison.OrdinalIgnoreCase),
                "deleted row explains its pending CSV effect");

            viewModel.AddClusterCommand.Execute(null);
            var newRow = viewModel.CreateCommitPreview().Sections
                .Single(section => section.FileName == "GalaxyMap_Cluster_part.csv")
                .Entries.Single(entry => entry.Badge == "NEW");
            Equal(0, newRow.Details.Count, "new rows do not expand into every added property");
            True(newRow.Title.Split(" / ").Length >= 2,
                "new rows retain an identifying internal name in their compact title");
        });
    }

    private static void ScalarEditsPreserveHierarchyIdentity()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) => modules.Single(module => !module.IsReadOnly));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Hierarchy Retarget", "HIERARCHY_RETARGET", ModuleColor.Cyan,
                TestReservations()), "authoring module is created");

            var node = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            var originalModel = node.Model!;
            node.IsSelected = true;
            True(!node.HasMultipleInstances, "BASEGAME row starts with one physical instance");

            var name = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "NameText");
            name.Value = "Retargeted Serpent";

            var afterFirstEdit = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            True(ReferenceEquals(node, afterFirstEdit), "first override retargets the existing hierarchy node");
            True(afterFirstEdit.IsSelected, "selection survives first override creation");
            True(afterFirstEdit.HasMultipleInstances && afterFirstEdit.InstanceCount == 2,
                "instance badge updates when the override is materialised");
            True(!ReferenceEquals(originalModel, afterFirstEdit.Model),
                "node points at the newly composed effective model");
            True(ReferenceEquals(viewModel.Document!.ClustersByRowId[1], afterFirstEdit.Model),
                "node model is the current effective document row");
            Equal("Retargeted Serpent", afterFirstEdit.DisplayName, "display name refreshes immediately");

            var x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            var xBeforeEdit = ((Cluster)afterFirstEdit.Model!).X;
            x.Value = "0.31";
            var afterSecondEdit = FindNode(viewModel, row => row is Cluster { RowId: 1 });
            True(ReferenceEquals(node, afterSecondEdit), "subsequent scalar edit preserves node identity");
            True(afterSecondEdit.IsSelected, "selection survives subsequent scalar edit");
            NearlyEqual(0.31, ((Cluster)afterSecondEdit.Model!).X, "retargeted model contains scalar edit");

            viewModel.UndoCommand.Execute(null);
            NearlyEqual(xBeforeEdit, viewModel.Document!.ClustersByRowId[1].X,
                "undo restores the previous scalar value");
            Equal("Retargeted Serpent", viewModel.Document.ClustersByRowId[1].NameText,
                "undo does not cross the previous edit transaction");
            viewModel.RedoCommand.Execute(null);
            NearlyEqual(0.31, viewModel.Document!.ClustersByRowId[1].X, "redo reapplies the scalar value");
            Equal("Retargeted Serpent", FindNode(viewModel, row => row is Cluster { RowId: 1 }).DisplayName,
                "redo leaves hierarchy display data current");
        });
    }


}
