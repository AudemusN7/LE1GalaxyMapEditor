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
    private static void ReservedRangeRowCreation()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Creation Test", "CREATE_TEST", ModuleColor.Magenta,
                TestReservations()), "module created");

            viewModel.AddClusterCommand.Execute(null);
            var cluster = viewModel.CurrentCluster!;
            Equal(100, cluster.RowId, "Cluster ID begins at reserved range start");
            viewModel.AddSystemCommand.Execute(null);
            var system = viewModel.CurrentSystem!;
            Equal(1000, system.RowId, "System ID begins at reserved range start");
            Equal(cluster.RowId, system.ClusterRowId, "new System links to selected Cluster");
            viewModel.AddPlanetCommand.Execute(null);
            var planet = viewModel.Document!.Planets.Single(row => row.RowId == 10000);
            Equal(system.RowId, planet.SystemRowId, "new Planet links to selected System");
            True(planet.ActiveWorld > 0, "new Planet receives a derived ActiveWorld code");
            var designer = viewModel.CreatePlanetDesigner(planet.Key);
            var sourcePreset = designer.PresetModules
                .Single(module => module.Tag == GalaxyMapModule.BaseGameTag)
                .Clusters.SelectMany(group => group.Systems)
                .SelectMany(group => group.Planets)
                .First();
            var targetShader = designer.Groups.SelectMany(group => group.Fields)
                .Single(item => item.Editor == PlanetAppearanceEditorKind.Shader);
            designer.CopyAppearance(sourcePreset);
            True(designer.PasteAppearance(), "BASEGAME appearance can be pasted onto a blank generated Planet");
            Equal(string.Empty, targetShader.Primary.Value,
                "pasting appearance data does not copy the source Shader name");
            targetShader.Primary.Value = "CREATE_TEST_Planet10000";
            var expectedContinentMask = sourcePreset.Appearance["ContinentMask01"];
            var expectedContinentMaskDisplay = PlanetAppearanceCodec.TextureDisplayName(expectedContinentMask);
            Equal(expectedContinentMaskDisplay,
                designer.Groups.SelectMany(group => group.Fields)
                    .Single(item => item.Definition.Id == "ContinentMask01").Primary.Value,
                "pasting copies material visuals and displays the texture object name");
            True(designer.TryApply(), "new Planet receives copied visuals and a unique Shader through the designer");
            True(designer.TryNavigateToPlanet(
                    sourcePreset.PlanetKey,
                    sourcePreset.ModuleTag,
                    PlanetDesignerNavigationChoice.Discard),
                "designer can switch away after applying a generated Planet appearance");
            True(designer.TryNavigateToPlanet(
                    planet.Key,
                    "CREATE_TEST",
                    PlanetDesignerNavigationChoice.Discard),
                "designer can return to the exact module-owned Planet row");
            Equal("CREATE_TEST_Planet10000",
                designer.Groups.SelectMany(group => group.Fields)
                    .Single(item => item.Editor == PlanetAppearanceEditorKind.Shader).Primary.Value,
                "applied Shader remains in memory after switching away and back");
            Equal(expectedContinentMaskDisplay,
                designer.Groups.SelectMany(group => group.Fields)
                    .Single(item => item.Definition.Id == "ContinentMask01").Primary.Value,
                "applied visuals remain in memory with user-facing texture names after switching away and back");

            var folder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.HasPendingChanges, "new rows remain staged");
            True(!File.Exists(Path.Combine(folder, "GalaxyMap_Cluster_part.csv")), "no automatic Cluster write");
            True(viewModel.CommitPendingChanges(), "manual row-creation commit succeeds");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Cluster_part.csv")), "Cluster part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_System_part.csv")), "System part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Planet_part.csv")), "Planet part written");
            var reloadedModule = new GalaxyMapModuleManifestStore().Load(folder);
            var reloadedPlanet = new CsvGalaxyMapLoader().LoadPartFolder(folder, reloadedModule)
                .Planets.Single(row => row.RowId == planet.RowId);
            Equal("CREATE_TEST_Planet10000", reloadedPlanet.ExtraFields["Shader"],
                "committed Shader survives reloading the module CSV");
            Equal(expectedContinentMask, reloadedPlanet.ExtraFields["ContinentMask01"],
                "committed copied appearance survives reloading the module CSV");
        });
    }

    private static void GalaxyMapIdentityLimitsAreEnforced()
    {
        Equal(99, GalaxyMapIdentityLimits.MaxClusterLabel, "Cluster label ceiling");
        Equal(9, GalaxyMapIdentityLimits.MaxSystemLabel, "System label ceiling");
        Equal(99, GalaxyMapIdentityLimits.MaxPlanetLabel, "Planet label ceiling");
        Equal(990999, GalaxyMapIdentityLimits.MaxActiveWorld, "ActiveWorld ceiling");

        var boundaryDocument = new GalaxyMapDocument();
        var cluster = new Cluster { RowId = 1, Label = "Cluster99", NameText = "Boundary Cluster" };
        var system = new GalaxySystem
        {
            RowId = 1,
            Label = "System09",
            ClusterRowId = cluster.RowId,
            NameText = "Boundary System",
            Scale = 1
        };
        boundaryDocument.Clusters.Add(cluster);
        boundaryDocument.Systems.Add(system);
        boundaryDocument.RebuildRelationships();
        True(GalaxyMapRowFactory.TryDeriveActiveWorld(system, "Planet99", out var boundaryActiveWorld),
            "maximum supported label chain derives successfully");
        Equal(990999, boundaryActiveWorld, "maximum label chain resolves to ActiveWorld 990999");
        True(!GalaxyMapRowFactory.TryDeriveActiveWorld(system, "Planet100", out _),
            "Planet100 cannot produce an ActiveWorld ID");

        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads for identity-limit authoring checks");
            True(viewModel.CreateModule(parent, "Identity Limits", "IDENTITY_LIMITS", ModuleColor.Cyan,
                TestReservations()), "identity-limit module created");

            var sourceCluster = viewModel.Document!.Clusters.First();
            var sourceSystem = sourceCluster.Systems.First();
            var sourcePlanet = sourceSystem.Planets.First();
            True(!viewModel.CloneRow(sourceCluster,
                    new CloneContentRequest(100, "Cluster100", 0, "Invalid Cluster", false)),
                "Cluster100 clone is rejected");
            True(!viewModel.CloneRow(sourceSystem,
                    new CloneContentRequest(1000, "System10", 0, "Invalid System", false)),
                "System10 clone is rejected");
            True(!viewModel.CloneRow(sourcePlanet,
                    new CloneContentRequest(10000, "Planet100", 0, "Invalid Planet", false)),
                "Planet100 clone is rejected");
            True(!viewModel.Document.ClustersByRowId.ContainsKey(100),
                "rejected Cluster clone creates no row");
            True(!viewModel.Document.SystemsByRowId.ContainsKey(1000),
                "rejected System clone creates no row");
            True(!viewModel.Document.PlanetsByRowId.ContainsKey(10000),
                "rejected Planet clone creates no row");
        });
    }

    private static void ClusterCreationRequiresGlobalLabel()
    {
        WithTemporaryDirectory(parent =>
        {
            ClusterLabelRequest? prompted = null;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                clusterLabelSelector: request =>
                {
                    prompted = request;
                    return "Cluster50";
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads for Cluster-label prompt");
            True(viewModel.CreateModule(parent, "Prompt Test", "PROMPT_TEST", ModuleColor.Cyan,
                TestReservations()), "prompt-test module created");

            viewModel.AddClusterCommand.Execute(null);
            NotNull(prompted, "Add Cluster asks for a global Cluster label");
            Equal("Cluster22", prompted!.SuggestedLabel,
                "prompt avoids gaps inside vanilla's reserved Cluster01-Cluster21 range");
            True(prompted.MountedLabels.Any(label => label.Contains("BASEGAME", StringComparison.Ordinal)),
                "prompt identifies labels from mounted modules");
            True(prompted.Validate("Cluster02") is not null,
                "vanilla-range Cluster labels are rejected even when the number is a gap");
            True(prompted.Validate("Cluster100") is not null, "Cluster100 is rejected by the prompt");
            True(prompted.Validate("Cluster03") is not null, "mounted Cluster collisions are rejected by the prompt");
            True(prompted.Validate(" Cluster51 ") is not null,
                "surrounding whitespace is deliberately rejected by the authoring prompt");
            True(prompted.Validate("Cluster51") is null, "another unused coordinated Cluster label is accepted");
            Equal("Cluster50", viewModel.Document!.ClustersByRowId[100].Label,
                "chosen Cluster label is used for the new row");
        });
    }

    private static void PartialModuleReservations()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(!viewModel.CreateModule(
                    parent,
                    "Overlapping Range",
                    "OVERLAPPING_RANGE",
                    ModuleColor.Red,
                    new ModuleIdReservations(Cluster: new RowIdRange(1, 1))),
                "reserved range cannot include a BASEGAME row ID");
            var reservations = new ModuleIdReservations(
                Planet: new RowIdRange(6207, 6210),
                Map: new RowIdRange(400, 401));
            True(viewModel.CreateModule(parent, "Planet Only", "PLANET_ONLY", ModuleColor.Cyan, reservations),
                "module can be created with unused ranges omitted");

            var module = viewModel.ActiveModule!;
            True(module.Reservations.Cluster is null, "Cluster reservation remains omitted");
            True(module.Reservations.System is null, "System reservation remains omitted");
            True(module.Reservations.Relay is null, "Relay reservation remains omitted");
            Equal(6207, new ModuleIdAllocator(viewModel.Workspace!).NextAvailable(module, GalaxyMapTable.Planet),
                "supplied Planet range remains allocatable");
            Throws<InvalidOperationException>(
                () => new ModuleIdAllocator(viewModel.Workspace!).NextAvailable(module, GalaxyMapTable.Cluster),
                message => message.Contains("no reserved Cluster", StringComparison.Ordinal),
                "omitted Cluster range prevents Cluster allocation");

            var loaded = new GalaxyMapModuleManifestStore().Load(module.FolderPath!);
            True(loaded.Reservations.Cluster is null, "omitted range round-trips through module.json");
            Equal(reservations.Planet!.Value, loaded.Reservations.Planet!.Value,
                "supplied range round-trips through module.json");
            True(new GalaxyMapValidator().Validate(viewModel.Workspace!).All(item => item.Code != "ID-NO-RESERVATION"),
                "unused omitted ranges do not create validation errors");
        });
    }

    private static void PlotPlanetAndMapPersistence()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Links Test", "LINKS_TEST", ModuleColor.Yellow,
                TestReservations()), "module created");
            var planet = viewModel.Document!.Planets.First(row => row.PlotPlanet is null && row.LinkedMap is null);
            FindNode(viewModel, row => row is Planet candidate && candidate.RowId == planet.RowId).IsSelected = true;

            var optional = viewModel.Inspector.Sections.Single(section => section.Title == "Optional relationships");
            optional.Actions.Single(action => action.Label.StartsWith("Add PlotPlanet", StringComparison.Ordinal))
                .Command.Execute(null);
            var updated = viewModel.Document.PlanetsByRowId[planet.RowId];
            NotNull(updated.PlotPlanet, "PlotPlanet is linked after live write");
            Equal(updated.ActiveWorld, updated.PlotPlanet!.Code, "PlotPlanet Code follows ActiveWorld");

            optional = viewModel.Inspector.Sections.Single(section => section.Title == "Optional relationships");
            optional.Actions.Single(action => action.Label.StartsWith("Add linked Map", StringComparison.Ordinal))
                .Command.Execute(null);
            updated = viewModel.Document.PlanetsByRowId[planet.RowId];
            NotNull(updated.LinkedMap, "Map is linked after live write");
            True(updated.MapRowId >= 1000, "Map ID comes from reserved range");

            var folder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.HasPendingChanges, "optional relationships remain staged");
            True(viewModel.CommitPendingChanges(), "optional relationship commit succeeds");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_PlotPlanet_part.csv")), "PlotPlanet part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Map_part.csv")), "Map part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Planet_part.csv")), "Planet override part written");
        });
    }

    private static void CloneDeleteAndHistory()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(), new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")), confirmAction: _ => true);
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Clone Test", "CLONE_TEST", ModuleColor.Purple, TestReservations()), "module created");

            var source = viewModel.Document!.Systems.OrderBy(system => system.Planets.Count).First();
            True(viewModel.CloneRow(source, new CloneContentRequest(1000, "System09", 0, "Cloned System", true)), "System clone succeeds");
            var clone = viewModel.Document.SystemsByRowId[1000];
            Equal(source.Planets.Count, clone.Planets.Count, "child Planets cloned");
            True(clone.Planets.All(planet => planet.SystemRowId == clone.RowId), "cloned children point at new System");
            True(clone.Planets.All(planet => planet.ActiveWorld != 0), "cloned Planet ActiveWorld values recalculated");
            foreach (var column in source.ExtraFieldOrder)
            {
                Equal(source.ExtraFields[column], clone.ExtraFields[column], $"cloned System preserves {column}");
            }
            if (source.Planets.FirstOrDefault() is { } sourcePlanet && clone.Planets.FirstOrDefault() is { } clonedPlanet)
            {
                foreach (var column in sourcePlanet.ExtraFieldOrder)
                {
                    Equal(sourcePlanet.ExtraFields[column], clonedPlanet.ExtraFields[column], $"cloned Planet preserves {column}");
                }
            }
            True(viewModel.UndoCommand.CanExecute(null), "clone can be undone");
            viewModel.UndoCommand.Execute(null);
            True(!viewModel.Document!.SystemsByRowId.ContainsKey(1000), "undo removes cloned tree");
            viewModel.RedoCommand.Execute(null);
            True(viewModel.Document!.SystemsByRowId.ContainsKey(1000), "redo restores cloned tree");

            var node = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            node.IsSelected = true;
            viewModel.Inspector.Sections.Single(section => section.Title == "System").Fields.Single(field => field.Name == "NameText").Value = "Renamed clone";
            Equal("Renamed clone", viewModel.Document.SystemsByRowId[1000].NameText, "physical property edit is staged");
            viewModel.UndoCommand.Execute(null);
            Equal("Cloned System", viewModel.Document!.SystemsByRowId[1000].NameText, "property edit can be undone");
            viewModel.RedoCommand.Execute(null);
            Equal("Renamed clone", viewModel.Document!.SystemsByRowId[1000].NameText, "property edit can be redone");

            node = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            node.DeleteCommand!.Execute(null);
            True(!viewModel.Document.SystemsByRowId.ContainsKey(1000), "delete stages removal");
            viewModel.UndoCommand.Execute(null);
            True(viewModel.Document!.SystemsByRowId.ContainsKey(1000), "delete can be undone");
            True(!File.Exists(Path.Combine(viewModel.ActiveModule!.FolderPath!, "GalaxyMap_System_part.csv")), "history remains in memory until commit");
        });
    }

    private static void ModuleOwnedRowsMoveBetweenParents()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                confirmAction: _ => true);
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Move Test", "MOVE_TEST", ModuleColor.Cyan, TestReservations()),
                "module created");

            var sourceSystem = viewModel.Document!.Systems
                .Where(system => system.Planets.Any(planet => planet.PlotPlanet is not null))
                .OrderBy(system => system.Planets.Count)
                .First();
            var targetCluster = viewModel.Document.Clusters
                .Where(cluster => cluster.RowId != sourceSystem.ClusterRowId && cluster.Systems.Count > 0)
                .First(cluster => cluster.Systems.All(system =>
                    !system.Label.Equals("System09", StringComparison.OrdinalIgnoreCase)));

            var sourceSystemRowId = sourceSystem.RowId;
            var sourceClusterRowId = sourceSystem.ClusterRowId;
            True(viewModel.MoveRow(sourceSystem, targetCluster.RowId),
                "BASEGAME System move creates an override directly");
            var movedBaseSystem = viewModel.Document.SystemsByRowId[sourceSystemRowId];
            Equal("MOVE_TEST", movedBaseSystem.Origin!.ModuleTag,
                "BASEGAME move is staged as a same-ID module override");
            Equal(targetCluster.RowId, movedBaseSystem.ClusterRowId,
                "new override receives the requested parent");
            var baseNode = FindNode(viewModel, row => row is GalaxySystem system && system.RowId == sourceSystemRowId);
            True(baseNode.SupportsParentMove, "System context menu supports parent moves");
            True(baseNode.CanMoveToParent && baseNode.MoveCommand!.CanExecute(null),
                "System move command remains enabled for the resulting override");
            viewModel.UndoCommand.Execute(null);
            sourceSystem = viewModel.Document.SystemsByRowId[sourceSystemRowId];
            Equal(sourceClusterRowId, sourceSystem.ClusterRowId,
                "direct BASEGAME move is one undoable transaction");

            True(viewModel.CloneRow(sourceSystem,
                new CloneContentRequest(1000, "System09", 0, "Movable System", true)),
                "module-owned System clone succeeds");
            var clone = viewModel.Document!.SystemsByRowId[1000];
            var originalClusterRowId = clone.ClusterRowId;
            var originalX = clone.X;
            var originalY = clone.Y;
            var cloneNode = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            True(cloneNode.CanMoveToParent && cloneNode.MoveCommand!.CanExecute(null),
                "module-owned System move command is enabled");
            True(viewModel.MoveRow(clone, targetCluster.RowId), "module-owned System moves to another Cluster");

            var moved = viewModel.Document!.SystemsByRowId[1000];
            var movedNode = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            True(!ReferenceEquals(cloneNode, movedNode),
                "parent-changing edit rebuilds hierarchy structure instead of scalar-retargeting it");
            Equal(targetCluster.RowId, ((Cluster)movedNode.Parent!.Model!).RowId,
                "rebuilt System node is nested below its new Cluster");
            True(movedNode.IsSelected, "moved System remains selected after hierarchy rebuild");
            True(ReferenceEquals(moved, movedNode.Model), "rebuilt node uses the current effective System model");
            Equal(targetCluster.RowId, moved.ClusterRowId, "System parent Cluster is updated");
            Equal(1000, moved.RowId, "System row ID is retained");
            NearlyEqual(originalX, moved.X, "structural move retains System X");
            NearlyEqual(originalY, moved.Y, "structural move retains System Y");
            foreach (var planet in moved.Planets)
            {
                var expected = ActiveWorldFor(planet.System!.Cluster!.Label, moved.Label, planet.Label);
                Equal(expected, planet.ActiveWorld, "System move recalculates child ActiveWorld");
                if (planet.PlotPlanet is not null)
                {
                    Equal(expected, planet.PlotPlanet.Code, "System move recalculates linked PlotPlanet Code");
                }
            }

            viewModel.UndoCommand.Execute(null);
            Equal(originalClusterRowId, viewModel.Document!.SystemsByRowId[1000].ClusterRowId,
                "System move is one undoable transaction");
            var undoNode = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            Equal(originalClusterRowId, ((Cluster)undoNode.Parent!.Model!).RowId,
                "undo rebuilds the System under its original Cluster");

            var targetSystemSource = viewModel.Document.ClustersByRowId[targetCluster.RowId].Systems.First();
            True(viewModel.CloneRow(targetSystemSource,
                new CloneContentRequest(1001, "System09", 0, "Collision System", false)),
                "destination-scoped collision System is created");
            True(viewModel.MoveRow(viewModel.Document.SystemsByRowId[1000], targetCluster.RowId),
                "System move resolves a destination label collision");
            moved = viewModel.Document.SystemsByRowId[1000];
            True(!moved.Label.Equals("System09", StringComparison.OrdinalIgnoreCase),
                "conflicting System label is allocated automatically");
            Equal(viewModel.Document.ClustersByRowId[targetCluster.RowId].Systems.Count,
                viewModel.Document.ClustersByRowId[targetCluster.RowId].Systems
                    .Select(system => system.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                "destination System labels remain unique");

            var sourcePlanet = sourceSystem.Planets.First(planet => planet.PlotPlanet is not null);
            True(viewModel.CloneRow(sourcePlanet,
                new CloneContentRequest(10090, "Planet99", 0, "Movable Planet", false)),
                "module-owned Planet clone succeeds");
            var planetClone = viewModel.Document.PlanetsByRowId[10090];
            var destinationSystem = viewModel.Document.SystemsByRowId[targetSystemSource.RowId];
            True(viewModel.MoveRow(planetClone, destinationSystem.RowId),
                "module-owned Planet moves to another System");
            var movedPlanet = viewModel.Document.PlanetsByRowId[10090];
            var movedPlanetNode = FindNode(viewModel, row => row is Planet { RowId: 10090 });
            Equal(destinationSystem.RowId, ((GalaxySystem)movedPlanetNode.Parent!.Model!).RowId,
                "rebuilt Planet node is nested below its new System");
            True(movedPlanetNode.IsSelected, "moved Planet remains selected after hierarchy rebuild");
            Equal(destinationSystem.RowId, movedPlanet.SystemRowId, "Planet parent System is updated");
            Equal(10090, movedPlanet.RowId, "Planet row ID is retained");
            var expectedPlanetCode = ActiveWorldFor(
                movedPlanet.System!.Cluster!.Label,
                movedPlanet.System.Label,
                movedPlanet.Label);
            Equal(expectedPlanetCode, movedPlanet.ActiveWorld, "Planet move recalculates ActiveWorld");
            Equal(expectedPlanetCode, movedPlanet.PlotPlanet!.Code,
                "Planet move recalculates linked PlotPlanet Code");
        });
    }

    private static void ShiftDragStagesRoundedCoordinates()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Drag Test", "DRAG_TEST", ModuleColor.Purple, TestReservations()),
                "module created");

            var baseCluster = viewModel.Document!.Clusters.First();
            var baseOriginalX = baseCluster.X;
            var baseOriginalY = baseCluster.Y;
            True(viewModel.BeginCoordinateDrag(baseCluster),
                "BASEGAME coordinate drag chooses the active edit module");
            viewModel.PreviewCoordinateDrag(baseCluster, new Point(0.44, 0.55));
            True(viewModel.CompleteCoordinateDrag(), "BASEGAME coordinate drag stages an override");
            var movedBaseCluster = viewModel.Document.ClustersByRowId[baseCluster.RowId];
            Equal("DRAG_TEST", movedBaseCluster.Origin!.ModuleTag,
                "BASEGAME coordinate move becomes a same-ID module override");
            NearlyEqual(0.44, movedBaseCluster.X, "BASEGAME override receives dragged X");
            NearlyEqual(0.55, movedBaseCluster.Y, "BASEGAME override receives dragged Y");
            viewModel.UndoCommand.Execute(null);
            baseCluster = viewModel.Document.ClustersByRowId[baseCluster.RowId];
            NearlyEqual(baseOriginalX, baseCluster.X, "undo restores BASEGAME X");
            NearlyEqual(baseOriginalY, baseCluster.Y, "undo restores BASEGAME Y");
            Equal(1, viewModel.Workspace!.GetOverrideChain(baseCluster.Key).Count,
                "undo removes the coordinate override entirely");
            True(viewModel.CloneRow(baseCluster,
                new CloneContentRequest(100, "Cluster99", 0, "Movable Cluster", false)),
                "module-owned Cluster clone succeeds");

            var clone = viewModel.Document.ClustersByRowId[100];
            var originalX = clone.X;
            var originalY = clone.Y;
            True(!viewModel.IsCoordinateOverlayVisible, "coordinate overlay starts hidden");
            viewModel.SetShiftDragMode(true);
            True(viewModel.IsCoordinateOverlayVisible,
                "Shift exposes live coordinates while the coordinate grid is off");
            True(viewModel.BeginCoordinateDrag(clone), "module-owned Cluster begins coordinate drag");
            var rounded = viewModel.PreviewCoordinateDrag(clone, new Point(0.126, 0.994));
            NearlyEqual(0.13, rounded.X, "drag preview rounds X to two decimals");
            NearlyEqual(0.99, rounded.Y, "drag preview rounds Y to two decimals");
            NearlyEqual(0.13, clone.X, "effective Cluster moves live on X");
            NearlyEqual(0.99, clone.Y, "effective Cluster moves live on Y");
            True(viewModel.CompleteCoordinateDrag(), "coordinate drag stages successfully");
            NearlyEqual(0.13, viewModel.Document!.ClustersByRowId[100].X, "staged Cluster X is rounded");
            NearlyEqual(0.99, viewModel.Document.ClustersByRowId[100].Y, "staged Cluster Y is rounded");

            viewModel.UndoCommand.Execute(null);
            NearlyEqual(originalX, viewModel.Document!.ClustersByRowId[100].X,
                "coordinate drag is one undoable X/Y transaction");
            NearlyEqual(originalY, viewModel.Document.ClustersByRowId[100].Y,
                "coordinate drag undo restores Y");

            viewModel.SetShiftDragMode(false);
            True(!viewModel.IsCoordinateOverlayVisible, "coordinate overlay hides after Shift is released");
            viewModel.ToggleCoordinateGridCommand.Execute(null);
            True(viewModel.IsCoordinateOverlayVisible, "grid toggle independently keeps the overlay visible");
        });
    }

    private static int ActiveWorldFor(string clusterLabel, string systemLabel, string planetLabel)
    {
        static int Suffix(string value, string prefix)
            => int.Parse(value[prefix.Length..], CultureInfo.InvariantCulture);

        return checked(
            Suffix(clusterLabel, "Cluster") * 10_000 +
            Suffix(systemLabel, "System") * 100 +
            Suffix(planetLabel, "Planet"));
    }

    private static void ManagedIdentityEditsCascade()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) => modules.Single(module => !module.IsReadOnly));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Cascade Test", "CASCADE_TEST", ModuleColor.Cyan,
                TestReservations()), "module created");

            var sourceSystem = viewModel.Document!.Systems.First(system => system.Planets.Any(planet => planet.PlotPlanet is not null));
            var sourcePlanet = sourceSystem.Planets.First(planet => planet.PlotPlanet is not null);
            var duplicatePlanet = sourceSystem.Planets.First(planet => planet.RowId != sourcePlanet.RowId);
            var originalPlanetLabel = sourcePlanet.Label;
            FindNode(viewModel, row => row is Planet candidate && candidate.RowId == sourcePlanet.RowId).IsSelected = true;
            var planetLabelField = viewModel.Inspector.Sections.Single(section => section.Title == "Planet")
                .Fields.Single(field => field.Name == "Label");
            planetLabelField.Value = duplicatePlanet.Label;
            True(planetLabelField.HasError, "duplicate Planet label is rejected inline");
            Equal(originalPlanetLabel, viewModel.Document.PlanetsByRowId[sourcePlanet.RowId].Label,
                "duplicate label never reaches the effective model");

            FindNode(viewModel, row => row is GalaxySystem candidate && candidate.RowId == sourceSystem.RowId).IsSelected = true;
            viewModel.Inspector.Sections.Single(section => section.Title == "System")
                .Fields.Single(field => field.Name == "Label").Value = "System09";

            var updatedPlanet = viewModel.Document.PlanetsByRowId[sourcePlanet.RowId];
            var clusterSuffix = int.Parse(updatedPlanet.System!.Cluster!.Label["Cluster".Length..], CultureInfo.InvariantCulture);
            var planetSuffix = int.Parse(updatedPlanet.Label["Planet".Length..], CultureInfo.InvariantCulture);
            Equal(clusterSuffix * 10_000 + 900 + planetSuffix, updatedPlanet.ActiveWorld,
                "System relabel recalculates child ActiveWorld");
            Equal(updatedPlanet.ActiveWorld, updatedPlanet.PlotPlanet!.Code,
                "System relabel updates linked PlotPlanet Code");

            var saturn = viewModel.Document.Planets.Single(planet => planet.NameText.Equals("Saturn", StringComparison.OrdinalIgnoreCase));
            True(saturn.RingColor != -1, "Saturn begins with a ring colour");
            FindNode(viewModel, row => row is Planet candidate && candidate.RowId == saturn.RowId).IsSelected = true;
            viewModel.Inspector.Sections.Single(section => section.Title == "System-view display")
                .Fields.Single(field => field.Name == "SystemLevelType").Value = "0";
            Equal(-1L, viewModel.Document.PlanetsByRowId[saturn.RowId].RingColor,
                "changing away from ringed type clears RingColor sentinel");
        });
    }

    private static void SpecialPropertyEditorsAndColors()
    {
        var inspector = new PropertyInspectorViewModel();
        var system = new GalaxySystem { RowId = 1, ShowNebula = 1 };
        system.AddExtraField("VisibleConditional", "1");
        inspector.Inspect(system);
        Equal(InspectorEditorKind.Checkbox, inspector.Sections.Single(section => section.Title == "System").Fields.Single(field => field.Name == "ShowNebula").EditorKind, "ShowNebula checkbox");
        Equal(InspectorEditorKind.Checkbox, inspector.Sections.SelectMany(section => section.Fields).Single(field => field.Name == "VisibleConditional").EditorKind, "conditional checkbox");

        var planet = new Planet { RowId = 2, OrbitRing = 2, SystemLevelType = 3, PlanetLevelType = 1, RingColor = -16728064 };
        inspector.Inspect(planet);
        var fields = inspector.Sections.SelectMany(section => section.Fields).ToArray();
        Equal(InspectorEditorKind.Dropdown, fields.Single(field => field.Name == "OrbitRing").EditorKind, "OrbitRing dropdown");
        Equal(InspectorEditorKind.Dropdown, fields.Single(field => field.Name == "SystemLevelType").EditorKind, "SystemLevelType dropdown");
        Equal(InspectorEditorKind.Dropdown, fields.Single(field => field.Name == "PlanetLevelType").EditorKind, "PlanetLevelType dropdown");
        Equal(InspectorEditorKind.Color, fields.Single(field => field.Name == "RingColor").EditorKind, "RingColor picker");
        Equal("Orbit ring", fields.Single(field => field.Name == "OrbitRing").Options.Single(option => option.Value == "1").ToString(), "selected dropdown label");
        Equal(Color.FromArgb(0xFF, 0x00, 0xC0, 0x00), ((SolidColorBrush)fields.Single(field => field.Name == "RingColor").ColorPreview).Color, "packed colour preview");
        planet.RingColor = 0x00123456;
        inspector.Inspect(planet);
        var transparentAlphaPreview = (SolidColorBrush)inspector.Sections.SelectMany(section => section.Fields)
            .Single(field => field.Name == "RingColor").ColorPreview;
        Equal(Color.FromArgb(0xFF, 0x12, 0x34, 0x56), transparentAlphaPreview.Color,
            "packed colour swatches display RGB opaquely while preserving stored alpha");
        var packed = ColorPickerWindow.PackArgb(0xFF, 0x12, 0x34, 0x56);
        Equal("-15584170", ColorPickerWindow.SignedDecimal(packed), "packed ARGB signed integer");
        Equal(Color.FromArgb(0xFF, 0x12, 0x34, 0x56), ColorPickerWindow.UnpackArgb(packed), "packed ARGB round trip");
    }

    private static void StructuredValidationErrorsAndWarnings()
    {
        var loader = new CsvGalaxyMapLoader();
        var module = new GalaxyMapModule(
            "Invalid Test", "INVALID_TEST", ModuleColor.Pink, null,
            isReadOnly: true, loadOrder: 1,
            new ModuleIdReservations(Cluster: new RowIdRange(100, 100)));
        var layer = new GalaxyMapLayer(module);
        layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster));
        layer.Add(new Cluster
        {
            RowId = 150,
            Label = "TypoCluster",
            X = 1.25,
            Y = 0.5,
            NameText = "Invalid",
            SphereSize = 0,
            Background = "BIOA_GalaxyMap_T.Cluster01"
        });
        var baseLayer = loader.LoadBuiltInLayer();
        var invalidPackedColorPlanet = baseLayer.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
        invalidPackedColorPlanet.SetExtraField("SunColor1", "not-a-packed-colour");
        invalidPackedColorPlanet.Scale = 0;
        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        var comprehensiveDiagnostics = new GalaxyMapValidator().Validate(workspace.EffectiveDocument);

        True(diagnostics.Any(item => item.Code == "ID-OUTSIDE-RESERVATION" && item.Severity == ValidationSeverity.Error),
            "out-of-range ID error");
        True(diagnostics.Any(item => item.Code == "LABEL-CLUSTER" && item.RowId == 150), "label typo error");
        True(diagnostics.All(item => item.Severity == ValidationSeverity.Error),
            "workspace validation suppresses advice for inactive and inherited rows");
        True(comprehensiveDiagnostics.Any(item => item.Code == "COORDINATE-OFF-CANVAS" &&
                                                  item.Severity == ValidationSeverity.Warning),
            "explicit comprehensive validation includes off-canvas advice");
        True(comprehensiveDiagnostics.Any(item => item.Code == "VALUE-NONPOSITIVE-SCALE"),
            "explicit comprehensive validation includes invisible-size advice");
        True(comprehensiveDiagnostics.All(item => item.RowId != invalidPackedColorPlanet.RowId ||
                                                  item.ColumnName != nameof(Planet.Scale)),
            "zero Planet scale is accepted without a diagnostic");
        True(comprehensiveDiagnostics.Any(item => item.Code == "TYPE-PLANET-PACKED-COLOR" &&
                                                  item.Severity == ValidationSeverity.Warning &&
                                                  item.RowId == invalidPackedColorPlanet.RowId &&
                                                  item.ColumnName == "SunColor1"),
            "invalid Planet appearance packed colours produce a non-blocking warning");
    }
}
