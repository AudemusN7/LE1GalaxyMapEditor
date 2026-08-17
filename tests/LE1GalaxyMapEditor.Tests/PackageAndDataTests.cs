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
    private static void SyntheticCsvLoadAndLinking()
    {
        WithFixture(folder =>
        {
            var document = new CsvGalaxyMapLoader().LoadFolder(folder);

            Equal(3, document.Clusters.Count, "cluster count");
            Equal(3, document.Systems.Count, "system count");
            Equal(3, document.Planets.Count, "planet count");
            SequenceEqual([20, 1, 6], document.Clusters.Select(cluster => cluster.RowId), "sparse row IDs and source order");

            var cluster07 = document.ClustersByRowId[6];
            Equal(1, cluster07.Systems.Count, "system linked by Cluster row ID");
            Equal(4, cluster07.Systems[0].RowId, "linked System row ID");

            var planet = document.PlanetsByRowId[1];
            NotNull(planet.PlotPlanet, "PlotPlanet same-row link");
            Equal(10101, planet.PlotPlanet!.Code, "PlotPlanet code");
            NotNull(planet.LinkedMap, "Map row zero link");
            Equal(0, planet.LinkedMap!.RowId, "Map row zero is valid");
            Equal("quoted, value", planet.ExtraFields["ExtraPlanet"], "quoted unknown field");
            Equal("line one\r\nline two", planet.ExtraFields["Multiline"], "multiline unknown field");
            True(document.ClustersByRowId[20].ExtraFields.ContainsKey("ExtraCluster"), "blank unknown field retained");
            Equal(string.Empty, document.ClustersByRowId[20].ExtraFields["ExtraCluster"], "blank unknown value retained");

            Equal(3, document.Relays.Count, "all relays retained");
            Equal(2, document.Relays.Count(relay => relay.IsResolved), "resolved relay count");
            var labelEncodedRelay = document.Relays.Single(relay => relay.RowId == 1);
            Equal(6, labelEncodedRelay.StartCluster!.RowId, "70000 resolves through Cluster07 label, not row 7");
            Equal(20, labelEncodedRelay.EndCluster!.RowId, "210000 resolves through Cluster21 label");
            True(!document.Relays.Single(relay => relay.RowId == 2).IsResolved, "unresolved relay retained");
            True(document.Warnings.Any(warning => warning.Contains("40000", StringComparison.Ordinal)),
                "unresolved relay warning names its encoded endpoint");

            var movedSystem = document.SystemsByRowId[4];
            movedSystem.ClusterRowId = 20;
            document.RebuildRelationships();
            True(document.ClustersByRowId[20].Systems.Contains(movedSystem), "relationship rebuild follows edited foreign key");
        });
    }

    private static void EmbeddedVanillaCsvData()
    {
        var loader = new CsvGalaxyMapLoader();
        var document = loader.LoadBuiltIn();

        Equal(CsvGalaxyMapLoader.BuiltInSourceName, document.SourceFolder, "built-in source description");
        True(document.IsSourceReadOnly, "built-in source is read-only");
        Equal(17, document.Clusters.Count, "built-in Cluster count");
        Equal(44, document.Systems.Count, "built-in System count");
        Equal(240, document.Planets.Count, "built-in Planet count");
        Equal(7, document.PlotPlanets.Count, "built-in PlotPlanet count");
        Equal(107, document.Maps.Count, "built-in Map count");
        Equal(17, document.Relays.Count, "built-in Relay count");
        Equal(16, document.Relays.Count(relay => relay.IsResolved), "built-in resolved Relay count");
        Equal("BIOA_GalaxyMap_T.Cluster03", document.ClustersByRowId[1].Background,
            "built-in Serpent background reference");
        NotNull(document.PlanetsByRowId[1].PlotPlanet, "built-in PlotPlanet relationship");
        NotNull(document.PlanetsByRowId[1].LinkedMap, "built-in Map relationship");

        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GalaxyMap_Cluster.csv"] = "7BB1FEDCF4E3A5D0B7B86BF99427144F44B567821D42B37203EA452BF079C129",
            ["GalaxyMap_Map.csv"] = "CD0405C1CB81D47FEC06B8153377619524FB23C55938D4A41481376877BE185C",
            ["GalaxyMap_Planet.csv"] = "E5D6B975E6123D8A28A9932D172C7ABDC542CEDBDA6D193837CCB8733948D256",
            ["GalaxyMap_PlotPlanet.csv"] = "B24DF58848024E37A72614DF932FF1C9992FCAC7CB79446BC76A2CD32F8A94B8",
            ["GalaxyMap_Relay.csv"] = "5FE5B6B706D7DA1DD250C483962C07559C97D9E5726F406F06BE4DF2471CB373",
            ["GalaxyMap_System.csv"] = "10E988BB1F96D22D7226CA9CBB17FEA5EA03A3D517CCDD64FF4872614C18249A"
        };
        foreach (var (fileName, expectedHash) in expectedHashes)
        {
            using var stream = typeof(CsvGalaxyMapLoader).Assembly.GetManifestResourceStream(
                CsvGalaxyMapLoader.BuiltInResourcePrefix + fileName);
            NotNull(stream, $"embedded CSV exists: {fileName}");
            Equal(expectedHash, Convert.ToHexString(SHA256.HashData(stream!)),
                $"embedded BASEGAME CSV is verbatim: {fileName}");
        }

        var viewModel = new MainViewModel(
            loader,
            new GalaxyMapTextureService(FindTextureDirectory()));
        True(viewModel.LoadBuiltIn(), "MainViewModel loads the embedded source");
        Equal(CsvGalaxyMapLoader.BuiltInSourceName, viewModel.SourceFolder, "built-in source appears in the UI");
    }

    private static void LecBlankPccTemplate()
    {
        WithTemporaryDirectory(folder =>
        {
            var cookedPath = Directory.CreateDirectory(Path.Combine(folder, "CookedPCConsole")).FullName;
            var packagePath = Path.Combine(cookedPath, "GXM_Test.pcc");
            new GalaxyMapTemplatePackageService().Create(packagePath);
            var module = new GalaxyMapModule(
                "PCC Template",
                "DLC_MOD_PCC_TEMPLATE",
                ModuleColor.Purple,
                cookedPath,
                isReadOnly: false,
                loadOrder: 1,
                TestReservations());

            var layer = new PccGalaxyMapLoader().Load(packagePath, module);
            Equal(6, layer.Schemas.Count, "template table count");
            Equal(0, layer.AllRows().Count(), "template starts without authored rows");
            Equal(Path.GetFullPath(packagePath), layer.SourcePackagePath!, "template source PCC");
            NotNull(layer.SourcePackageFingerprint, "template source fingerprint");

            foreach (var pair in PccGalaxyMapLoader.SupportedExports)
            {
                var schema = layer.GetSchema(pair.Key);
                NotNull(schema, $"{pair.Key} schema");
                Equal(pair.Value, schema!.SourceIdentity!.ExportObjectName, $"{pair.Key} export identity");
                Equal("Bio2DANumberedRows", schema.SourceIdentity.ExportClassName, $"{pair.Key} export class");
                Equal(GalaxyMapCellType.Int, schema.DefaultCellType(CsvRowSnapshot.RowIdColumnName),
                    $"{pair.Key} row ID type");
            }

            var planetSchema = layer.GetSchema(GalaxyMapTable.Planet)!;
            Equal(GalaxyMapCellType.Float, planetSchema.DefaultCellType("Brightness1"),
                "appearance scalar is a numeric PCC cell");
            Equal(GalaxyMapCellType.Float, planetSchema.DefaultCellType("Atmosphere_ColorR"),
                "appearance vector component is a numeric PCC cell");
            Equal(GalaxyMapCellType.Int, planetSchema.DefaultCellType("SunColor1"),
                "packed appearance colour remains an integer PCC cell");

            var partialPath = Path.Combine(cookedPath, "GXM_Partial.pcc");
            new GalaxyMapTemplatePackageService().Create(
                partialPath,
                [GalaxyMapTable.Planet, GalaxyMapTable.PlotPlanet]);
            var partialLayer = new PccGalaxyMapLoader().Load(partialPath, module);
            SequenceEqual(
                new[] { GalaxyMapTable.Planet, GalaxyMapTable.PlotPlanet },
                partialLayer.Schemas.Keys.OrderBy(table => table),
                "only reserved partial 2DAs are created");

            var emptyPath = Path.Combine(cookedPath, "GXM_Empty.pcc");
            new GalaxyMapTemplatePackageService().Create(emptyPath, []);
            var emptyLayer = new PccGalaxyMapLoader().Load(emptyPath, module, allowEmpty: true);
            Equal(0, emptyLayer.Schemas.Count, "blank reservations create no partial 2DAs");

            var addedMap = new MapEntry
            {
                RowId = 1000,
                MapName = "BIOA_SCHEMA_TEST",
                StartPoint = "start_schema_test"
            };
            GalaxyMapRowAuthoring.PrepareNewRow(layer, addedMap);
            layer.Add(addedMap);
            NotNull(layer.GetSchema(GalaxyMapTable.Map)!.SourceIdentity,
                "new row preserves the PCC-backed table identity");
            new PccGalaxyMapWriter().WriteTables(layer, [GalaxyMapTable.Map]);
            using (var committedPackage = MEPackageHandler.OpenLE1Package(packagePath, forceLoadFromDisk: true))
            {
                Equal(1, committedPackage.Exports.Count(export =>
                        !export.IsDefaultObject &&
                        string.Equals(export.ClassName, "Bio2DANumberedRows", StringComparison.Ordinal) &&
                        string.Equals(export.ObjectName.Name, "GalaxyMap_Map_part", StringComparison.OrdinalIgnoreCase)),
                    "adding a Map row does not import a duplicate PCC export");
            }

            Throws<InvalidOperationException>(
                () => layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Map)),
                message => message.Contains("cannot be replaced", StringComparison.OrdinalIgnoreCase),
                "a physical source identity cannot be silently erased");

            var identityFallbackLayer = new GalaxyMapLayer(module);
            identityFallbackLayer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Map));
            identityFallbackLayer.SetPackageSource(packagePath, layer.SourcePackageFingerprint!);
            var fallbackMap = new MapEntry
            {
                RowId = 1001,
                MapName = "BIOA_IDENTITY_FALLBACK",
                StartPoint = "start_identity_fallback"
            };
            GalaxyMapRowAuthoring.PrepareNewRow(identityFallbackLayer, fallbackMap);
            identityFallbackLayer.Add(fallbackMap);
            new PccGalaxyMapWriter().WriteTables(identityFallbackLayer, [GalaxyMapTable.Map]);

            var fallbackReloaded = new PccGalaxyMapLoader().Load(packagePath, module);
            Equal("BIOA_IDENTITY_FALLBACK", fallbackReloaded.Maps.Single().MapName,
                "writer reuses the physical export when in-memory identity is unavailable");
            using var fallbackPackage = MEPackageHandler.OpenLE1Package(packagePath, forceLoadFromDisk: true);
            Equal(1, fallbackPackage.Exports.Count(export =>
                    !export.IsDefaultObject &&
                    string.Equals(export.ClassName, "Bio2DANumberedRows", StringComparison.Ordinal) &&
                    string.Equals(export.ObjectName.Name, "GalaxyMap_Map_part", StringComparison.OrdinalIgnoreCase)),
                "identity fallback cannot create a duplicate PCC export");
        });
    }

    private static void TransactionalPccTableCommit()
    {
        WithTemporaryDirectory(folder =>
        {
            var cookedPath = Directory.CreateDirectory(Path.Combine(folder, "CookedPCConsole")).FullName;
            var packagePath = Path.Combine(cookedPath, "GXM_Write_Test.pcc");
            new GalaxyMapTemplatePackageService().Create(packagePath, []);
            var module = new GalaxyMapModule(
                "PCC Write Test",
                "DLC_MOD_PCC_WRITE_TEST",
                ModuleColor.Cyan,
                cookedPath,
                isReadOnly: false,
                loadOrder: 1,
                TestReservations());
            var loader = new PccGalaxyMapLoader();
            var layer = loader.Load(packagePath, module, allowEmpty: true);
            Equal(0, layer.Schemas.Count, "commit fixture starts without partial 2DA exports");
            var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
            var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
            workspace.SetActiveModule(module);

            var factory = new GalaxyMapRowFactory(workspace);
            var created = factory.CreateCluster("PCC Cluster", 1, 0.75);
            var second = factory.CreateCluster("Second PCC Cluster", 0.25, 0.5);
            layer.SetSourceRowOrder(GalaxyMapTable.Cluster, [second.RowId, created.RowId]);
            new PccGalaxyMapWriter(loader).WriteTables(layer, [GalaxyMapTable.Cluster]);

            var reloaded = loader.Load(packagePath, module);
            SequenceEqual(
                new[] { GalaxyMapTable.Cluster },
                reloaded.Schemas.Keys,
                "commit imports only the newly required partial 2DA export");
            var actual = reloaded.Clusters.Single(cluster => cluster.RowId == created.RowId);
            Equal(created.RowId, actual.RowId, "committed cluster row ID");
            Equal("PCC Cluster", actual.NameText, "committed cluster name");
            NearlyEqual(1, actual.X, "committed cluster X");
            NearlyEqual(0.75, actual.Y, "committed cluster Y");
            True(created.CsvSnapshot?.HasChanges == false,
                "committed row snapshot is clean");
            Equal(GalaxyMapCellType.Int,
                actual.CsvSnapshot!.GetOriginalCell("X")!.Value.Type,
                "whole numeric value uses PCC integer type");
            Equal(GalaxyMapCellType.Float,
                actual.CsvSnapshot.GetOriginalCell("Y")!.Value.Type,
                "decimal numeric value uses PCC float type");
            Equal(GalaxyMapCellType.Name,
                actual.CsvSnapshot.GetOriginalCell("NameText")!.Value.Type,
                "new NameText cell uses canonical PCC name type");
            SequenceEqual(
                new[] { created.RowId, second.RowId }.OrderBy(rowId => rowId),
                reloaded.GetSourceRowOrder(GalaxyMapTable.Cluster),
                "PCC commit sorts physical rows numerically");
            SequenceEqual(
                reloaded.GetSourceRowOrder(GalaxyMapTable.Cluster),
                layer.GetSourceRowOrder(GalaxyMapTable.Cluster),
                "successful PCC commit updates the live physical row order");

            var earth = (Planet)GalaxyMapRowCloner.CloneForOverride(
                baseLayer.Planets.Single(planet => planet.RowId == 6),
                module);
            earth.NameText = "Earth2";
            earth.CsvSnapshot!.MarkDirty("NameText");
            layer.Upsert(earth);
            new PccGalaxyMapWriter(loader).WriteTables(layer, [GalaxyMapTable.Planet]);

            var earthReloaded = loader.Load(packagePath, module).Planets.Single(planet => planet.RowId == 6);
            Equal("Earth2", earthReloaded.NameText,
                "BASEGAME Earth override round-trips through an on-demand Planet export");
            using (var committedPackage = MEPackageHandler.OpenLE1Package(packagePath, forceLoadFromDisk: true))
            {
                foreach (var table in new[] { GalaxyMapTable.Cluster, GalaxyMapTable.Planet })
                {
                    var tableExport = committedPackage.Exports.Single(export =>
                        string.Equals(
                            export.ObjectName.Name,
                            PccGalaxyMapLoader.SupportedExports[table],
                            StringComparison.OrdinalIgnoreCase));
                    Equal("BIOG_2DA_GalaxyMap_X", tableExport.Parent?.ObjectName.Name!,
                        $"on-demand {table} export is nested under the galaxy-map package export");
                }
            }

            created.NameText = "Externally blocked";
            created.CsvSnapshot!.MarkDirty("NameText");
            File.SetLastWriteTimeUtc(packagePath, File.GetLastWriteTimeUtc(packagePath).AddSeconds(2));
            Throws<InvalidOperationException>(
                () => new PccGalaxyMapWriter(loader).WriteTables(layer, [GalaxyMapTable.Cluster]),
                message => message.Contains("changed outside", StringComparison.OrdinalIgnoreCase),
                "external PCC fingerprint change blocks replacement");
        });
    }

    private static void DlcPccDiscoveryAndProfiles()
    {
        WithTemporaryDirectory(folder =>
        {
            var dlcPath = Directory.CreateDirectory(Path.Combine(folder, "DLC_MOD_PROFILE_TEST")).FullName;
            var cookedPath = Directory.CreateDirectory(Path.Combine(dlcPath, "CookedPCConsole")).FullName;
            File.WriteAllText(
                Path.Combine(dlcPath, "AutoLoad.ini"),
                "[ME1DLCMOUNT]\r\nModName=Profile Test Mod\r\nModMount=3141\r\n",
                new UTF8Encoding(false));
            var packagePath = Path.Combine(cookedPath, "BIOG_Profile_GalaxyMap.pcc");
            new GalaxyMapTemplatePackageService().Create(packagePath);
            var profileDirectory = Path.Combine(folder, "appdata", "modules");
            var profileStore = new GalaxyMapModuleProfileStore(profileDirectory);
            var discovery = new DlcModuleDiscoveryService(profileStore);

            var discovered = discovery.Discover(packagePath);
            True(discovered.IsNewProfile, "first PCC discovery creates a profile candidate");
            Equal("Profile Test Mod", discovered.Module.Name, "ModName is sourced from AutoLoad.ini");
            Equal(3141, discovered.Module.LoadOrder, "ModMount is sourced from AutoLoad.ini");
            Equal("DLC_MOD_PROFILE_TEST", discovered.Module.Tag, "DLC directory supplies module tag");
            Equal(Path.GetFullPath(packagePath), discovered.Module.GalaxyMapPackagePath!,
                "profile module resolves the selected PCC");

            var customized = discovered.Profile with
            {
                DisplayName = "Readable Profile Name",
                ModuleColor = ModuleColor.Purple,
                TlkLocale = LegendaryExplorerCore.Packages.MELocalization.FRA,
                ResourcePackages = ["CookedPCConsole/BIOA_Profile_Resources.pcc"],
                Reservations = TestReservations()
            };
            profileStore.Save(customized);
            var restored = discovery.Discover(packagePath);
            True(!restored.IsNewProfile, "existing PCC identity reuses its profile");
            Equal("Readable Profile Name", restored.Module.Name,
                "editor-owned display name overrides AutoLoad ModName");
            Equal(ModuleColor.Purple, restored.Module.Color, "module colour is restored from AppData");
            Equal(LegendaryExplorerCore.Packages.MELocalization.FRA, restored.Module.TlkLocale,
                "TLK locale is restored from AppData");
            Equal(TestReservations(), restored.Module.Reservations, "reservations are restored from AppData");
            Equal(1, restored.Module.ResourcePackagePaths.Count, "resource PCC registration is restored");

            var workspacePath = Path.Combine(folder, "appdata", "workspace.json");
            var workspaceStore = new GalaxyMapProfileWorkspaceStore(workspacePath);
            workspaceStore.Save([customized.ProfileId], customized.ProfileId);
            var remembered = workspaceStore.Load();
            SequenceEqual([customized.ProfileId], remembered.ProfileIds, "workspace stores profile identities");
            Equal(customized.ProfileId, remembered.ActiveProfileId!, "workspace stores active profile identity");

            File.WriteAllText(
                Path.Combine(dlcPath, "AutoLoad.ini"),
                "[ME1DLCMOUNT]\r\nModName=Profile Test Mod\r\nModMount=broken\r\n",
                new UTF8Encoding(false));
            Throws<GalaxyMapLoadException>(
                () => discovery.Discover(packagePath),
                message => message.Contains("valid non-negative integer", StringComparison.OrdinalIgnoreCase),
                "malformed ModMount is rejected rather than becoming zero");
        });
    }

    private static void ProfileWorkspacePccRelink()
    {
        WithTemporaryDirectory(folder =>
        {
            var dlcPath = Directory.CreateDirectory(Path.Combine(folder, "DLC_MOD_RELINK_TEST")).FullName;
            var cookedPath = Directory.CreateDirectory(Path.Combine(dlcPath, "CookedPCConsole")).FullName;
            File.WriteAllText(
                Path.Combine(dlcPath, "AutoLoad.ini"),
                "[ME1DLCMOUNT]\r\nModName=Relink Test\r\nModMount=4242\r\n",
                new UTF8Encoding(false));
            var packagePath = Path.Combine(cookedPath, "BIOG_Relink_GalaxyMap.pcc");
            new GalaxyMapTemplatePackageService().Create(packagePath);
            var seedModule = new GalaxyMapModule(
                "Reservation seed", "DLC_MOD_RELINK_TEST", ModuleColor.Cyan, cookedPath,
                isReadOnly: false, loadOrder: 4242, TestReservations());
            var pccLoader = new PccGalaxyMapLoader();
            var seedLayer = pccLoader.Load(packagePath, seedModule);
            seedLayer.Add(new Cluster { RowId = 450, Label = "Cluster50", NameText = "Seed A" });
            seedLayer.Add(new Cluster { RowId = 455, Label = "Cluster51", NameText = "Seed B" });
            seedLayer.Add(new GalaxySystem { RowId = 720, Label = "System01", NameText = "Seed System" });
            new PccGalaxyMapWriter(pccLoader).WriteTables(
                seedLayer,
                [GalaxyMapTable.Cluster, GalaxyMapTable.System]);
            var profileStore = new GalaxyMapModuleProfileStore(Path.Combine(folder, "appdata", "modules"));
            var workspaceStore = new GalaxyMapProfileWorkspaceStore(Path.Combine(folder, "appdata", "workspace.json"));
            var loader = new CsvGalaxyMapLoader();
            var session = new EditorSession();
            var edits = new EditSessionService(session, profileStore: profileStore);
            var workflows = new WorkspaceWorkflowService(
                session,
                edits,
                loader,
                workspaceStore: new GalaxyMapWorkspaceStore(Path.Combine(folder, "legacy-unused.json")),
                profileWorkspaceStore: workspaceStore,
                profileStore: profileStore);

            True(workflows.LoadBuiltIn().Succeeded, "profile workspace loads BASEGAME");
            True(workflows.OpenExistingModule(packagePath).Succeeded, "selected PCC mounts through discovery");
            Equal(new RowIdRange(450, 455), session.Workspace!.Modules.Single().Reservations.Cluster!.Value,
                "new profile infers its Cluster reservation from physical PCC rows");
            Equal(new RowIdRange(720, 720), session.Workspace.Modules.Single().Reservations.System!.Value,
                "new profile infers its System reservation from physical PCC rows");
            True(edits.Commit(workflows.CommitCurrentWorkspace).Succeeded, "linked profile workspace commits");

            var restoredSession = new EditorSession();
            var restoredEdits = new EditSessionService(restoredSession, profileStore: profileStore);
            var restoredWorkflows = new WorkspaceWorkflowService(
                restoredSession,
                restoredEdits,
                loader,
                workspaceStore: new GalaxyMapWorkspaceStore(Path.Combine(folder, "legacy-unused.json")),
                profileWorkspaceStore: workspaceStore,
                profileStore: profileStore);
            var restored = restoredWorkflows.LoadRememberedWorkspace();
            True(restored.Succeeded, "remembered profile reopens its PCC");
            Equal(1, restoredSession.Workspace!.ModuleLayers.Count, "one profile-backed layer restored");
            Equal(Path.GetFullPath(packagePath),
                restoredSession.Workspace.ModuleLayers.Single().SourcePackagePath!,
                "restored layer retains PCC source identity");

            var restoredModule = restoredSession.Workspace.Modules.Single();
            True(restoredWorkflows.ForgetModule(restoredModule).Succeeded,
                "forget removes a clean linked profile");
            Equal(0, restoredSession.Workspace.ModuleLayers.Count,
                "forgotten module is removed from the live workspace");
            Equal(0, workspaceStore.Load().ProfileIds.Count,
                "forgotten module is removed from workspace persistence");
            Equal(0, profileStore.LoadAll().Count, "forget deletes only the editor profile");
            True(File.Exists(packagePath), "forget leaves the DLC PCC untouched");
        });
    }

    private static void TlkCacheDiagnosticsAndStrRefSchema()
    {
        WithTemporaryDirectory(folder =>
        {
            var cachePath = Path.Combine(folder, "LE1LoadedTLKs.JSON");
            File.WriteAllText(
                cachePath,
                "[{\"Item1\":1,\"Item2\":\"missing.pcc\"},{\"bad\":true},42,{\"Item1\":2,\"Item2\":\"\\u0000\"}]",
                new UTF8Encoding(false));
            var service = new GalaxyMapTlkService(cachePath);
            service.Reload();
            Equal("BioTlkFile", GalaxyMapTlkService.TalkFileClassName,
                "LE1 TLK exports use the BioTlkFile class name");
            Equal(4, service.Diagnostics.Count, "each malformed or missing TLK entry is diagnosed");
            Equal(0, service.AvailableLocales.Count, "invalid TLK entries do not create a locale fallback");
            True(service.Find(LegendaryExplorerCore.Packages.MELocalization.INT, 1) is null,
                "missing locale lookup does not silently fall back");
            Equal(LegendaryExplorerCore.Packages.MELocalization.INT,
                GalaxyMapTlkService.ResolvePackageLocale("DLC_MOD_Test_GlobalTlk.pcc", LegendaryExplorerCore.Packages.MELocalization.DEU)!.Value,
                "unsuffixed GlobalTlk packages are authoritatively INT");
            Equal(LegendaryExplorerCore.Packages.MELocalization.DEU,
                GalaxyMapTlkService.ResolvePackageLocale("DLC_MOD_Test_GlobalTlk_DE.pcc", LegendaryExplorerCore.Packages.MELocalization.INT)!.Value,
                "the canonical DE suffix resolves to German");
            True(GalaxyMapTlkService.ResolvePackageLocale(
                    "DLC_MOD_Test_GlobalTlk_GE.pcc", LegendaryExplorerCore.Packages.MELocalization.DEU) is null,
                "the legacy GE suffix is not treated as canonical German");
            True(GalaxyMapTlkService.ResolvePackageLocale(
                    "DLC_MOD_Test_GlobalTlk_HU.pcc", LegendaryExplorerCore.Packages.MELocalization.INT) is null,
                "optional Hungarian packages cannot enter the INT index");

            var baseGameSettings = new BaseGameSettingsStore(Path.Combine(folder, "basegame.json"));
            Equal(LegendaryExplorerCore.Packages.MELocalization.INT, baseGameSettings.LoadLocale(),
                "BASEGAME locale defaults to INT");
            baseGameSettings.SaveLocale(LegendaryExplorerCore.Packages.MELocalization.FRA);
            Equal(LegendaryExplorerCore.Packages.MELocalization.FRA, baseGameSettings.LoadLocale(),
                "BASEGAME locale persists independently of module profiles");

            var inspector = new PropertyInspectorViewModel(tlk: service);
            inspector.Inspect(new CsvGalaxyMapLoader().LoadBuiltInLayer().Clusters.First());
            var lookup = inspector.Sections.SelectMany(section => section.Fields)
                .Single(field => field.Name == "Name").StrRefLookup;
            NotNull(lookup, "StrRef field exposes reusable lookup presentation");
            Equal("TLK cache unavailable", lookup!.State,
                "inspector distinguishes an unavailable TLK cache");

            var dlcPath = Directory.CreateDirectory(Path.Combine(folder, "DLC_MOD_TLK_SCAN")).FullName;
            var cookedPath = Directory.CreateDirectory(Path.Combine(dlcPath, "CookedPCConsole")).FullName;
            var galaxyMapPackage = Path.Combine(cookedPath, "BIOG_TlkScan_GalaxyMap.pcc");
            var globalTlkPackage = Path.Combine(cookedPath, "DLC_MOD_TLK_SCAN_GlobalTlk.pcc");
            new GalaxyMapTemplatePackageService().Create(galaxyMapPackage);
            new GalaxyMapTemplatePackageService().Create(globalTlkPackage);
            File.WriteAllText(cachePath, "[]", new UTF8Encoding(false));
            var pccModule = new GalaxyMapModule(
                "TLK Scan",
                "DLC_MOD_TLK_SCAN",
                ModuleColor.Cyan,
                cookedPath,
                isReadOnly: false,
                loadOrder: 9000,
                profileId: "tlk-scan-profile",
                dlcRootPath: dlcPath,
                galaxyMapPackagePath: galaxyMapPackage);
            service.Reload([pccModule]);
            True(service.Diagnostics.Any(diagnostic =>
                    diagnostic.Contains("DLC_MOD_TLK_SCAN_GlobalTlk.pcc", StringComparison.OrdinalIgnoreCase) &&
                    diagnostic.Contains("BioTlkFile", StringComparison.Ordinal)),
                "mounted DLC GlobalTlk PCCs are inspected for BioTlkFile exports");
        });

        True(GalaxyMapStrRefSchema.IsStrRef(GalaxyMapTable.Cluster, "Name"), "Cluster.Name is a StrRef");
        True(GalaxyMapStrRefSchema.IsStrRef(GalaxyMapTable.Planet, "Description"),
            "Planet.Description is a StrRef");
        True(!GalaxyMapStrRefSchema.IsStrRef(GalaxyMapTable.Planet, "NameText"),
            "NameText is explicitly not a StrRef");
        True(!GalaxyMapStrRefSchema.IsStrRef(GalaxyMapTable.Map, "Map"),
            "ordinary Name cells are not treated as StrRefs");
        True(PlanetAppearanceSchema.IsSupportedTextureObject("GXM_PlanetNormal01"),
            "known GXM planet textures are eligible for package-backed menus");
        True(!PlanetAppearanceSchema.IsSupportedTextureObject("Cluster03"),
            "non-planet Texture2D exports are excluded from Planet Designer menus");
        True(!PlanetAppearanceSchema.IsSupportedTextureObject("GXM_CoronaGradient"),
            "renderer-only GXM textures are excluded from editable material slots");
        True(PlanetAppearanceSchema.IsSelectablePlanetTextureObject("My_StrangelyNamedTexture", true, true),
            "Planet-referenced custom PCC textures do not have to follow the vanilla naming allowlist");
        True(!PlanetAppearanceSchema.IsSelectablePlanetTextureObject("My_StrangelyNamedTexture", true, false),
            "unreferenced custom PCC textures remain excluded from Planet dropdowns");
        True(!PlanetAppearanceSchema.IsSelectablePlanetTextureObject("My_StrangelyNamedTexture", false, true),
            "references do not admit unknown textures from non-resource game packages");
    }
}
