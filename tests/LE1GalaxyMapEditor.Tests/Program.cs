using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Converters;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Rendering;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;
using LE1GalaxyMapEditor.Workflows.Ports;
using LE1GalaxyMapEditor.Workflows.Queries;

namespace LE1GalaxyMapEditor.Tests;

internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Run("Synthetic CSV load and linking", SyntheticCsvLoadAndLinking);
        Run("Deployed vanilla CSV data", EmbeddedVanillaCsvData);
        Run("Invariant numeric parsing", InvariantNumericParsing);
        Run("Inspector field parsing", InspectorEditsModel);
        Run("Compact map-number formatting", CompactMapNumberFormatting);
        Run("Planet appearance columns are categorized", PlanetAppearanceColumnsAreCategorized);
        Run("Planet appearance codec, presets and templates", PlanetAppearanceCodecPresetsAndTemplates);
        Run("Planet Designer workflow and Shader guard", PlanetDesignerWorkflowAndShaderGuard);
        Run("Planet Designer BASEGAME override prompts", PlanetDesignerBaseGameOverridePrompts);
        Run("Planet preview renderer production assets", PlanetPreviewRendererProductionAssets);
        Run("Asteroid belts use a distinct visual", AsteroidBeltsUseDistinctVisual);
        Run("Map markers preserve object scale while resizing", MapMarkersPreserveObjectScaleWhileResizing);
        Run("Planet templates use verified structural defaults", PlanetTemplateDefaults);
        Run("Inspector metadata and type ranges", InspectorMetadataAndTypeRanges);
        Run("Square viewport and coordinate grid definitions", SquareViewportAndGridDefinitions);
        Run("Texture mapping ignores PNG alpha", TextureMappingIgnoresPngAlpha);
        Run("Hierarchy navigation semantics", HierarchyNavigationSemantics);
        Run("Contextual add actions follow the active view", ContextualAddActionsFollowActiveView);
        Run("Relay layer observes collection changes", RelayLayerObservesCollectionChanges);
        Run("Duplicate row IDs are rejected", DuplicateRowIdsAreRejected);
        Run("Missing table is reported", MissingTableIsReported);
        Run("Effective BASEGAME rows are detached", EffectiveBaseGameRowsAreDetached);
        Run("Module manifest round-trip", ModuleManifestRoundTrip);
        Run("Partial layers override deterministically", PartialLayersOverrideDeterministically);
        Run("Atomic partial CSV writer contract", AtomicPartialCsvWriterContract);
        Run("MainViewModel writes full-row overrides", MainViewModelWritesFullRowOverrides);
        Run("Scalar edits preserve hierarchy identity", ScalarEditsPreserveHierarchyIdentity);
        Run("Reserved-range row creation", ReservedRangeRowCreation);
        Run("Partial module reservations", PartialModuleReservations);
        Run("PlotPlanet and Map persistence", PlotPlanetAndMapPersistence);
        Run("Inherited Relay rows redirect by override", InheritedRelayRedirectPersistence);
        Run("Remembered module workspace and missing paths", RememberedModuleWorkspace);
        Run("Unlinking a module preserves its files", ModuleUnlinkPreservesFiles);
        Run("Mount priority and row-instance comparison", MountPriorityAndRowInstances);
        Run("Duplicate row delete follows the active module", DuplicateRowDeleteFollowsActiveModule);
        Run("Module Cluster textures and nebula systems", ModuleTexturesAndNebulaSystems);
        Run("Clone delete and staged history", CloneDeleteAndHistory);
        Run("Module-owned rows move between parents", ModuleOwnedRowsMoveBetweenParents);
        Run("Shift drag stages rounded coordinates", ShiftDragStagesRoundedCoordinates);
        Run("Managed identity edits cascade to dependent rows", ManagedIdentityEditsCascade);
        Run("Special property editors and packed colours", SpecialPropertyEditorsAndColors);
        Run("Structured validation errors and warnings", StructuredValidationErrorsAndWarnings);
        OptimizationRegressionTests.Register(Run);
        PhaseZeroDataSafetyTests.Register(Run);
        PhaseOneLifecycleTests.Register(Run);
        Run("Refactor: edit transaction rollback and history contract", EditTransactionRollbackAndHistoryContract);
        Run("Refactor: merged table projection follows the editor session", TableProjectionFollowsEditorSession);
        Run("2DA dirty highlights clear after commit", TableDirtyHighlightsClearAfterCommit);
        Run("2DA table cells use existing edit workflows", TableCellEditingUsesExistingWorkflows);

        var realFolder = ReadArgument(args, "--real") ?? Environment.GetEnvironmentVariable("LE1_GALAXYMAP_CSV_FOLDER");
        if (!string.IsNullOrWhiteSpace(realFolder))
        {
            Run("Supplied Legendary Explorer exports", () => RealExports(realFolder));
        }
        else
        {
            Console.WriteLine("SKIP  Supplied Legendary Explorer exports (pass --real <folder> to enable)");
        }

        var semFolder = ReadArgument(args, "--sem") ?? Environment.GetEnvironmentVariable("LE1_GALAXYMAP_SEM_FOLDER");
        if (!string.IsNullOrWhiteSpace(semFolder))
        {
            Run("Spectre Expansion Mod partial mount", () => SpectreExpansionModule(semFolder));
        }
        else
        {
            Console.WriteLine("SKIP  Spectre Expansion Mod partial mount (pass --sem <folder> to enable)");
        }

        Run("WPF views compose headlessly", WpfViewsComposeAfterLoad);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "All checks passed." : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

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
            ["GalaxyMap_Planet.csv"] = "292BA5BFB9197AAE150F22CF50CCC6CE4357AB8640F0864875A7588C55A58DD8",
            ["GalaxyMap_PlotPlanet.csv"] = "B24DF58848024E37A72614DF932FF1C9992FCAC7CB79446BC76A2CD32F8A94B8",
            ["GalaxyMap_Relay.csv"] = "5FE5B6B706D7DA1DD250C483962C07559C97D9E5726F406F06BE4DF2471CB373",
            ["GalaxyMap_System.csv"] = "10E988BB1F96D22D7226CA9CBB17FEA5EA03A3D517CCDD64FF4872614C18249A"
        };
        foreach (var (fileName, expectedHash) in expectedHashes)
        {
            var path = ApplicationResourcePaths.GetDataFilePath(fileName);
            True(File.Exists(path), $"deployed CSV exists: {fileName}");
            using var stream = File.OpenRead(path);
            Equal(expectedHash, Convert.ToHexString(SHA256.HashData(stream)),
                $"external BASEGAME CSV is verbatim: {fileName}");
        }

        var viewModel = new MainViewModel(
            loader,
            new GalaxyMapTextureService(FindTextureDirectory()));
        True(viewModel.LoadBuiltIn(), "MainViewModel loads the embedded source");
        Equal(CsvGalaxyMapLoader.BuiltInSourceName, viewModel.SourceFolder, "built-in source appears in the UI");
    }

    private static void InvariantNumericParsing()
    {
        WithFixture(folder =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var document = new CsvGalaxyMapLoader().LoadFolder(folder);
                NearlyEqual(0.5, document.ClustersByRowId[6].X, "period decimal parsed under fr-FR");
                NearlyEqual(4.2, document.ClustersByRowId[1].SphereSize, "sphere size parsed invariantly");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        });
    }

    private static void InspectorEditsModel()
    {
        WithFixture(folder =>
        {
            var document = new CsvGalaxyMapLoader().LoadFolder(folder);
            var cluster = document.ClustersByRowId[6];
            var inspector = new PropertyInspectorViewModel();
            inspector.Inspect(cluster);

            var main = inspector.Sections.Single(section => section.Title == "Cluster");
            var x = main.Fields.Single(field => field.Name == "X");
            x.Value = "0.75";
            NearlyEqual(0.75, cluster.X, "valid numeric edit updates model");
            True(!x.HasError, "valid edit has no validation error");

            x.Value = "not a number";
            NearlyEqual(0.75, cluster.X, "invalid text does not corrupt model");
            True(x.HasError, "invalid edit is identified");

            x.Value = "1.01";
            NearlyEqual(0.75, cluster.X, "off-canvas coordinate does not corrupt model");
            True(x.HasError, "coordinates outside 0-1 are rejected inline");

            var extra = inspector.Sections.Single(section => section.Title == "Advanced Cluster fields")
                .Fields.Single(field => field.Name == "ExtraCluster");
            extra.Value = "changed only in memory";
            Equal("changed only in memory", cluster.ExtraFields["ExtraCluster"], "extra field edit updates dictionary");
        });
    }

    private static void PlanetAppearanceColumnsAreCategorized()
    {
        var planet = new Planet { RowId = 42, Label = "Planet01", NameText = "Test" };
        planet.AddExtraField("ExitMap", "0");
        planet.AddExtraField("Shader", "TestShader");
        planet.AddExtraField("Horizon_Atmosphere_Intensity", "3");
        planet.AddExtraField("Corona_ColorA", "1");
        planet.AddExtraField("AfterAppearance", "kept");

        var inspector = new PropertyInspectorViewModel();
        inspector.Inspect(planet);
        var destination = inspector.Sections.Single(section => section.Title == "Destination / unused internals");
        var advanced = inspector.Sections.Single(section => section.Title == "Advanced Planet fields");
        True(inspector.Sections.All(section => section.Title != "Planet appearance"),
            "appearance parameters are absent from the general inspector");
        SequenceEqual(["ExitMap"], destination.Fields.Select(field => field.Name), "destination/internal fields");
        SequenceEqual(["AfterAppearance"], advanced.Fields.Select(field => field.Name), "advanced nonappearance fields");
        Equal(94, PlanetAppearanceSchema.Columns.Count, "explicit Planet appearance schema count");
        True(PlanetAppearanceSchema.Properties.All(property => !string.IsNullOrWhiteSpace(property.Description)),
            "every designer property carries a tooltip description");

        var decoded = PlanetAppearanceCodec.Decode(planet);
        Equal("3", decoded["Horizon_Atmosphere_Intensity"], "codec preserves the raw scalar token");
        var edited = decoded.Clone();
        edited["Horizon_Atmosphere_Intensity"] = "3.5";
        SequenceEqual(["Horizon_Atmosphere_Intensity"],
            PlanetAppearanceCodec.ChangedColumns(decoded, edited), "codec isolates the edited appearance column");
    }

    private static void PlanetAppearanceCodecPresetsAndTemplates()
    {
        WithTemporaryDirectory(folder =>
        {
            var loader = new CsvGalaxyMapLoader();
            var baseLayer = loader.LoadBuiltInLayer();
            var source = baseLayer.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var module = CreateTestModule(folder, "PRESET_TEST", ModuleColor.Cyan);
            var moduleLayer = new GalaxyMapLayer(module);
            moduleLayer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Planet));
            var overridePlanet = (Planet)GalaxyMapRowCloner.CloneForOverride(source, module);
            overridePlanet.SetExtraField("Shader", "PresetTestUniqueShader");
            moduleLayer.Upsert(overridePlanet);
            var workspace = new GalaxyMapWorkspace(baseLayer, [moduleLayer]);

            var presets = PlanetAppearancePresetCatalog.Build(workspace);
            True(presets.Any(preset => preset.ModuleTag == GalaxyMapModule.BaseGameTag),
                "preset catalog includes BASEGAME CSV rows");
            True(presets.Any(preset => preset.ModuleTag == module.Tag && preset.PlanetRowId == source.RowId),
                "preset catalog includes physical rows from mounted modules");
            var grouped = PlanetAppearancePresetCatalog.Group(presets, "PresetTestUniqueShader");
            Equal(1, grouped.Count, "preset search reaches Shader names across the hierarchy");
            Equal(ModuleColor.Cyan, grouped[0].ModuleColor,
                "Designer module groups retain their main-tree module colour");
            Equal(overridePlanet.VisualKind,
                grouped[0].Clusters.SelectMany(cluster => cluster.Systems)
                    .SelectMany(system => system.Planets).Single().VisualKind,
                "Designer Planet leaves retain their main-tree object icon kind");
            True(grouped.All(module => module.IsExpanded &&
                    module.Clusters.All(cluster => cluster.IsExpanded &&
                        cluster.Systems.All(system => system.IsExpanded))),
                "filtered preset results automatically expand every hierarchy level");
            True(grouped[0].Clusters.SelectMany(cluster => cluster.Systems).SelectMany(system => system.Planets)
                    .Any(preset => preset.Shader == "PresetTestUniqueShader"),
                "grouped preset hierarchy retains the matching Planet leaf");
            var expandedByDefault = PlanetAppearancePresetCatalog.Group(presets);
            True(expandedByDefault.All(module => !module.IsExpanded &&
                    module.Clusters.All(cluster => cluster.IsExpanded &&
                        cluster.Systems.All(system => system.IsExpanded))),
                "appearance-base modules start collapsed while their nested hierarchy remains expanded");
            var basePresetModule = expandedByDefault.Single(group =>
                group.Tag == GalaxyMapModule.BaseGameTag);
            var basePresetPlanetIds = presets
                .Where(preset => preset.ModuleTag == GalaxyMapModule.BaseGameTag)
                .Select(preset => preset.PlanetRowId)
                .ToHashSet();
            SequenceEqual(
                workspace.EffectiveDocument.Clusters
                    .Where(cluster => cluster.Systems.SelectMany(system => system.Planets)
                        .Any(planet => basePresetPlanetIds.Contains(planet.RowId)))
                    .Select(cluster => cluster.RowId),
                basePresetModule.Clusters.Select(cluster => cluster.RowId),
                "Designer Clusters retain main-tree CSV order");
            foreach (var clusterGroup in basePresetModule.Clusters)
            {
                var mainCluster = workspace.EffectiveDocument.ClustersByRowId[clusterGroup.RowId];
                SequenceEqual(
                    mainCluster.Systems
                        .Where(system => system.Planets.Any(planet => basePresetPlanetIds.Contains(planet.RowId)))
                        .Select(system => system.RowId),
                    clusterGroup.Systems.Select(system => system.RowId),
                    $"Designer Systems retain main-tree CSV order in Cluster row {clusterGroup.RowId}");
                foreach (var systemGroup in clusterGroup.Systems)
                {
                    var mainSystem = workspace.EffectiveDocument.SystemsByRowId[systemGroup.RowId];
                    SequenceEqual(
                        mainSystem.Planets
                            .Where(planet => basePresetPlanetIds.Contains(planet.RowId))
                            .Select(planet => planet.RowId),
                        systemGroup.Planets.Select(planet => planet.PlanetRowId),
                        $"Designer Planets retain main-tree CSV order in System row {systemGroup.RowId}");
                }
            }

            var appearance = PlanetAppearanceCodec.Decode(overridePlanet);
            var primaryMaskDefinition = PlanetAppearanceSchema.Properties
                .Single(property => property.Id == "ContinentMask01");
            var primaryMaskField = new PlanetAppearanceFieldViewModel(
                appearance,
                primaryMaskDefinition,
                () => { });
            Equal("GXM_ContinentMask01", primaryMaskField.Primary.Value,
                "vanilla package-qualified textures display by their object name");
            Equal("GXM_ContinentMask01", PlanetAppearanceCodec.TextureDisplayName(
                    "BIOA_GXM10_T.BIOA_GXM10_T.GXM_ContinentMask01"),
                "repeated vanilla package qualifiers are hidden from the user");
            Equal(1, primaryMaskField.TextureOptions.Count(option =>
                    option.Equals("GXM_ContinentMask01", StringComparison.OrdinalIgnoreCase)),
                "vanilla texture aliases are collapsed into one dropdown option");
            True(!primaryMaskField.TextureOptions.Any(option =>
                    option.StartsWith("BIOA_GXM10_T.", StringComparison.OrdinalIgnoreCase)),
                "vanilla package prefixes are absent from the texture dropdown");
            Equal("BIOA_GXM10_T.GXM_ContinentMask01", appearance["ContinentMask01"],
                "display normalization leaves the untouched raw CSV token intact");
            var templateFolder = Path.Combine(folder, "templates");
            var store = new PlanetAppearanceTemplateStore(templateFolder);
            store.SaveNew("Blue world", "Reusable surface", appearance);
            var template = store.LoadAll().Single();
            Equal(string.Empty, template.ToAppearance().Shader, "personal templates never restore a Shader identity");
            var json = File.ReadAllText(Directory.GetFiles(templateFolder, "*.json").Single());
            True(!json.Contains("\"Shader\"", StringComparison.OrdinalIgnoreCase),
                "personal template JSON excludes the Shader property");
            Throws<InvalidOperationException>(
                () => store.SaveNew("blue WORLD", null, appearance),
                message => message.Contains("already exists", StringComparison.OrdinalIgnoreCase),
                "template names are unique without case sensitivity");
            File.WriteAllText(Path.Combine(templateFolder, "broken.json"), "{ definitely not JSON");
            Equal(1, store.LoadAll().Count,
                "a malformed personal template does not hide valid templates");
            True(store.Warnings.Any(warning => warning.Contains("broken.json", StringComparison.OrdinalIgnoreCase)),
                "skipped personal templates produce a warning");
        });
    }

    private static void PlanetDesignerWorkflowAndShaderGuard()
    {
        WithTemporaryDirectory(folder =>
        {
            var loader = new CsvGalaxyMapLoader();
            var baseLayer = loader.LoadBuiltInLayer();
            var shaderCounts = baseLayer.Planets
                .Select(planet => planet.ExtraFields.GetValueOrDefault("Shader") ?? string.Empty)
                .Where(shader => shader.Length > 0)
                .GroupBy(shader => shader, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var source = baseLayer.Planets.First(planet =>
                PlanetAppearanceCodec.IsAppearanceCapable(planet) &&
                shaderCounts.GetValueOrDefault(planet.ExtraFields.GetValueOrDefault("Shader") ?? string.Empty) == 1);
            var module = CreateTestModule(folder, "DESIGNER_TEST", ModuleColor.Green);
            var layer = new GalaxyMapLayer(module);
            var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
            workspace.SetActiveModule(module);
            var editorSession = new EditorSession(workspace);
            var edits = new EditSessionService(editorSession);
            var workflow = new PlanetDesignerWorkflow(editorSession, edits);
            var designer = workflow.Open(workspace.EffectiveDocument.PlanetsByRowId[source.RowId]);
            designer.Draft["Bump_Amount"] = "0.375";
            designer.Draft.Shader = $"DESIGNER_TEST_Planet{source.RowId}";
            var presentation = new HistoryPresentationState(source.Key, NavigationTarget.Galaxy, null, false);
            var applied = workflow.Apply(designer, presentation);

            True(applied.Succeeded, "designer stages a valid appearance");
            Equal(1, editorSession.History.UndoCount, "an applied designer session creates one history entry");
            Equal("0.375", layer.Planets.Single(planet => planet.RowId == source.RowId).ExtraFields["Bump_Amount"],
                "designer writes the changed appearance column to the active layer");
            var restored = edits.Undo(presentation);
            True(restored.Succeeded, "designer appearance participates in shared undo");
            Equal(source.ExtraFields["Bump_Amount"],
                ((Planet)editorSession.Workspace!.Resolve(source.Key)!).ExtraFields["Bump_Amount"],
                "undo restores the prior Planet appearance");

            var copySource = (Planet)editorSession.Workspace.Resolve(source.Key)!;
            var copyTarget = editorSession.Workspace.EffectiveDocument.Planets.First(planet =>
                planet.RowId != source.RowId && PlanetAppearanceCodec.IsAppearanceCapable(planet));
            var copiedAppearance = workflow.Open(copyTarget);
            copiedAppearance.Draft.CopyVisualsFrom(PlanetAppearanceCodec.Decode(copySource));
            copiedAppearance.Draft.Shader = $"DESIGNER_TEST_Planet{copyTarget.RowId}";
            var copied = workflow.Apply(
                copiedAppearance,
                presentation with { SelectionKey = copyTarget.Key });
            True(copied.Succeeded,
                "copied visuals apply when the target has its own unique Shader instance");
            copiedAppearance.Draft.Shader = copySource.ExtraFields["Shader"];
            True(!workflow.Apply(copiedAppearance, presentation with { SelectionKey = copyTarget.Key }).Succeeded,
                "third-party appearances cannot reuse a Shader even when all visuals match");

            var duplicateTarget = workflow.Open((Planet)editorSession.Workspace.Resolve(source.Key)!);
            var sourceAppearance = PlanetAppearanceCodec.Decode(copySource);
            var anotherPlanet = editorSession.Workspace.EffectiveDocument.Planets.First(planet =>
                planet.RowId != source.RowId &&
                !string.IsNullOrWhiteSpace(planet.ExtraFields.GetValueOrDefault("Shader")) &&
                !PlanetAppearanceCodec.VisualsEqual(sourceAppearance, PlanetAppearanceCodec.Decode(planet)));
            duplicateTarget.Draft.Shader = anotherPlanet.ExtraFields["Shader"];
            True(!workflow.Apply(duplicateTarget, presentation).Succeeded,
                "designer refuses a Shader name already used by another effective Planet");
            var baseGameAppearance = PlanetAppearanceCodec.Decode(source);
            baseGameAppearance.Shader = string.Empty;
            True(PlanetShaderNameValidator.Validate(
                    editorSession.Workspace,
                    source.Key,
                    baseGameAppearance,
                    GalaxyMapModule.BaseGameTag).IsValid,
                "BASEGAME remains exempt from third-party Shader uniqueness rules");

            var navigationSource = (Planet)editorSession.Workspace.Resolve(source.Key)!;
            var navigationTarget = editorSession.Workspace.EffectiveDocument.Planets.First(planet =>
                planet.RowId != navigationSource.RowId && PlanetAppearanceCodec.IsAppearanceCapable(planet));
            var navigationTemplateFolder = Path.Combine(folder, "navigation-templates");
            Directory.CreateDirectory(navigationTemplateFolder);
            File.WriteAllText(Path.Combine(navigationTemplateFolder, "inaccessible-simulation.json"), "not JSON");
            var navigationViewModel = new PlanetDesignerViewModel(
                () => editorSession.Workspace,
                workflow.Open(navigationSource),
                session => workflow.Apply(session, presentation with { SelectionKey = session.Key }),
                key => edits.Undo(presentation with { SelectionKey = key }).Succeeded,
                key => edits.Redo(presentation with { SelectionKey = key }).Succeeded,
                () => edits.CanUndo,
                () => edits.CanRedo,
                (key, moduleTag) => moduleTag is null
                    ? editorSession.Workspace?.Resolve(key) as Planet
                    : editorSession.Workspace?.Layers.FirstOrDefault(layer =>
                            string.Equals(layer.Module.Tag, moduleTag, StringComparison.OrdinalIgnoreCase))
                        ?.Find(key) as Planet,
                new PlanetAppearanceTemplateStore(navigationTemplateFolder));
            True(navigationViewModel.StatusMessage.Contains("Skipped", StringComparison.OrdinalIgnoreCase),
                "template read warnings are surfaced without preventing Designer startup");
            True(!navigationViewModel.SaveTemplate(string.Empty, null),
                "invalid template input exposes a designer error");
            True(navigationViewModel.HasError && navigationViewModel.DismissErrorCommand.CanExecute(null),
                "designer errors expose the same dismissible banner state as the main window");
            navigationViewModel.DismissErrorCommand.Execute(null);
            True(!navigationViewModel.HasError,
                "dismissing the designer error clears its banner state");
            var navigationBump = navigationViewModel.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "Bump_Amount");
            navigationBump.Primary.Value = "0.8125";
            True(!navigationViewModel.TryNavigateToPlanet(
                    navigationTarget.Key,
                    navigationTarget.Origin?.ModuleTag,
                    PlanetDesignerNavigationChoice.Cancel),
                "dirty designer navigation can be cancelled without losing its draft");
            Equal(navigationSource.Key, navigationViewModel.PlanetKey,
                "cancelled designer navigation keeps the current Planet");
            True(navigationViewModel.TryNavigateToPlanet(
                    navigationTarget.Key,
                    navigationTarget.Origin?.ModuleTag,
                    PlanetDesignerNavigationChoice.Discard),
                "dirty designer navigation can explicitly discard its draft");
            Equal(PlanetAppearanceCodec.Decode(navigationTarget).Shader,
                navigationViewModel.Groups.SelectMany(group => group.Fields)
                    .Single(field => field.Definition.Id == "Shader").Primary.Value,
                "switching Planets refreshes the Shader field from the new row");

            var stagedBump = navigationViewModel.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "Bump_Amount");
            var stagedShader = navigationViewModel.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "Shader");
            stagedBump.Primary.Value = "0.625";
            stagedShader.Primary.Value = $"DESIGNER_TEST_Planet{navigationTarget.RowId}";
            True(navigationViewModel.TryNavigateToPlanet(
                    navigationSource.Key,
                    navigationSource.Origin?.ModuleTag,
                    PlanetDesignerNavigationChoice.Apply),
                "dirty designer navigation can stage changes before switching");
            True(navigationViewModel.TryNavigateToPlanet(
                    navigationTarget.Key,
                    module.Tag,
                    PlanetDesignerNavigationChoice.Discard),
                "designer can navigate back to a staged Planet before the main commit");
            Equal("0.625", navigationViewModel.Groups.SelectMany(group => group.Fields)
                    .Single(field => field.Definition.Id == "Bump_Amount").Primary.Value,
                "staged Planet appearance remains in the in-memory workspace before commit");
            Equal($"DESIGNER_TEST_Planet{navigationTarget.RowId}",
                navigationViewModel.Groups.SelectMany(group => group.Fields)
                    .Single(field => field.Definition.Id == "Shader").Primary.Value,
                "staged Shader remains in memory when navigating away and back");

            var guardModule = CreateTestModule(folder, "SHADER_GUARD", ModuleColor.Magenta);
            var guardLayer = new GalaxyMapLayer(guardModule);
            var newPlanet = (Planet)GalaxyMapRowCloner.Clone(source);
            newPlanet.RowId = 10000;
            newPlanet.SetExtraField("Shader", string.Empty);
            GalaxyMapRowAuthoring.PrepareNewRow(guardLayer, newPlanet);
            guardLayer.Upsert(newPlanet);
            var guardWorkspace = new GalaxyMapWorkspace(baseLayer, [guardLayer]);
            guardWorkspace.SetActiveModule(guardModule);
            var guardSession = new EditorSession(guardWorkspace);
            var guardEdits = new EditSessionService(guardSession);
            guardEdits.MarkTableDirty(guardModule, GalaxyMapTable.Planet);
            var commit = guardEdits.Commit();
            True(!commit.Succeeded && commit.Message.Contains("unique Shader", StringComparison.OrdinalIgnoreCase),
                "commit preflight blocks new appearance rows with a blank Shader");
            True(!File.Exists(Path.Combine(folder, "GalaxyMap_Planet_part.csv")),
                "Shader preflight runs before any partial CSV is written");
        });
    }

    private static void PlanetDesignerBaseGameOverridePrompts()
    {
        WithTemporaryDirectory(parent =>
        {
            var modulePromptCount = 0;
            var shaderPromptCount = 0;
            PlanetShaderNameRequest? shaderRequest = null;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                editTargetSelector: (_, modules) =>
                {
                    modulePromptCount++;
                    return modules.Single();
                },
                shaderNameSelector: request =>
                {
                    shaderPromptCount++;
                    shaderRequest = request;
                    return request.SuggestedName;
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads for the Designer override prompt");
            True(viewModel.CreateModule(
                    parent,
                    "Designer Override",
                    "DESIGNER_OVERRIDE",
                    ModuleColor.Cyan,
                    TestReservations()),
                "writable Designer target module is created");

            var source = viewModel.Document!.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var baseShader = source.ExtraFields["Shader"];
            var designer = viewModel.CreatePlanetDesigner(source.Key, GalaxyMapModule.BaseGameTag);
            var bump = designer.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "Bump_Amount");
            bump.Primary.Value = bump.Primary.Value == "0.4321" ? "0.5432" : "0.4321";

            True(!designer.HasError,
                "editing a BASEGAME appearance waits for override setup instead of rejecting its inherited Shader");
            True(designer.TryApply(),
                "BASEGAME appearance applies after target-module and Shader prompts");
            Equal(1, modulePromptCount,
                "BASEGAME Designer edit asks which writable module receives the override");
            Equal(1, shaderPromptCount,
                "BASEGAME Designer edit asks for a unique Shader name");
            NotNull(shaderRequest, "Shader prompt receives validation context");
            Equal("DESIGNER_OVERRIDE", shaderRequest!.TargetModule.Tag,
                "Shader prompt identifies the selected target module");
            True(shaderRequest.Validate(baseShader) is not null,
                "Shader prompt rejects the inherited BASEGAME Shader name");
            True(shaderRequest.Validate(shaderRequest.SuggestedName) is null,
                "Shader prompt starts with a valid unique suggestion");

            var physical = (Planet)viewModel.Workspace!.ActiveLayer!.Find(source.Key)!;
            Equal(shaderRequest.SuggestedName, physical.ExtraFields["Shader"],
                "prompted Shader name is staged in the module override");
            Equal(shaderRequest.SuggestedName,
                designer.Groups.SelectMany(group => group.Fields)
                    .Single(field => field.Definition.Id == "Shader").Primary.Value,
                "Designer refreshes its Shader field after the prompted override is applied");
            Equal(GalaxyMapModule.BaseGameTag,
                viewModel.Workspace.BaseLayer.Find(source.Key)!.Origin!.ModuleTag,
                "BASEGAME physical row remains untouched");
        });
    }

    private static void PlanetPreviewRendererProductionAssets()
    {
        var planet = new CsvGalaxyMapLoader().LoadBuiltIn().Planets.Single(planet =>
            planet.ExtraFields.GetValueOrDefault("Shader") == "GXM_Earth");
        using var renderer = new PlanetPreviewRenderer(320, 180);
        var material = PlanetAppearanceCodec.ToRenderMaterial(PlanetAppearanceCodec.Decode(planet));
        var frame = renderer.Render(material, new());
        var animatedFrame = renderer.Render(
            material, new(), timeSeconds: 8);
        Equal(320, frame.Width, "production preview uses a 16:9 render width");
        Equal(180, frame.Height, "production preview uses a 16:9 render height");
        Equal(320 * 180 * 4, frame.BgraPixels.Length, "renderer produces a complete BGRA frame");
        True(!ReferenceEquals(frame.BgraPixels, animatedFrame.BgraPixels),
            "ordinary renderer calls retain independent frame buffers");
        True(frame.BgraPixels.Any(value => value != 0), "production preview frame contains rendered pixels");
        True(!frame.BgraPixels.SequenceEqual(animatedFrame.BgraPixels),
            "advancing preview time visibly animates the material");
        Throws<ArgumentException>(
            () => renderer.Render(material, new(), new byte[320 * 180 * 4 - 1]),
            message => message.Contains("exactly", StringComparison.OrdinalIgnoreCase),
            "reusable rendering rejects a buffer which does not exactly fit the target");
        renderer.Resize(400, 225);
        var resizedFrame = renderer.Render(material, new());
        Equal(400, resizedFrame.Width, "renderer resizes its target without rebuilding the device");
        Equal(225, resizedFrame.Height, "resized renderer retains the 16:9 target");
        var reusablePixels = new byte[400 * 225 * 4];
        var reusableFrame = renderer.Render(material, new(), reusablePixels, timeSeconds: 2);
        True(ReferenceEquals(reusablePixels, reusableFrame.BgraPixels),
            "explicit reusable rendering returns the caller-owned frame buffer");
        True(reusablePixels.Any(value => value != 0),
            "explicit reusable rendering fills the complete caller-owned frame buffer");
        var nextReusableFrame = renderer.Render(material, new(), reusablePixels, timeSeconds: 3);
        True(ReferenceEquals(reusablePixels, nextReusableFrame.BgraPixels),
            "the same exact-size frame buffer can be reused on subsequent renders");
        Equal(new PlanetPreviewPixelSize(960, 540), PlanetPreviewResolution.Fit16By9(960, 540),
            "preview resolution follows an exact 16:9 viewport");
        Equal(new PlanetPreviewPixelSize(800, 450), PlanetPreviewResolution.Fit16By9(800, 800),
            "preview resolution letterboxes a tall viewport without fixing a source resolution");
    }

    private static void CompactMapNumberFormatting()
    {
        var cluster = new Cluster { RowId = 1, X = 0.29, Y = 0.5, SphereSize = 4 };
        var inspector = new PropertyInspectorViewModel();
        inspector.Inspect(cluster);
        var fields = inspector.Sections.Single(section => section.Title == "Cluster").Fields;
        Equal("0.29", fields.Single(field => field.Name == "X").Value,
            "binary floating-point detail is hidden from the inspector");
        Equal("0.5", fields.Single(field => field.Name == "Y").Value,
            "compact display omits trailing zeroes");
        Equal("4", fields.Single(field => field.Name == "SphereSize").Value,
            "whole-number scales remain whole numbers");
        Equal("0.29", GalaxyMapNumber.Serialize(0.29),
            "new CSV values use shortest round-trip serialization");

        var x = fields.Single(field => field.Name == "X");
        x.Value = "0.125";
        True(x.HasError, "more than two meaningful decimal places are rejected");
        NearlyEqual(0.29, cluster.X, "rejected precision does not change the model");
        x.Value = "0.30";
        True(!x.HasError, "two decimal places remain valid");
        NearlyEqual(0.3, cluster.X, "valid compact coordinate updates the model");
    }

    private static void AsteroidBeltsUseDistinctVisual()
    {
        WithFixture(folder =>
        {
            var planet = new CsvGalaxyMapLoader().LoadFolder(folder).PlanetsByRowId[9];
            Equal(PlanetVisualKind.AsteroidBelt, planet.VisualKind, "OrbitRing 2 visual kind");
            True(planet.IsAsteroidBelt, "asteroid belt flag");

            var visualKindChanged = false;
            planet.PropertyChanged += (_, eventArgs) => visualKindChanged |= eventArgs.PropertyName == nameof(Planet.VisualKind);
            planet.OrbitRing = 1;
            planet.OrbitRing = 2;
            True(visualKindChanged, "OrbitRing edit notifies the marker visual");

            var glyph = new PlanetGlyphConverter().Convert(
                planet.VisualKind, typeof(string), null!, CultureInfo.InvariantCulture);
            Equal("▲", (string)glyph, "asteroid belt anchor glyph");
        });
    }

    private static void MapMarkersPreserveObjectScaleWhileResizing()
    {
        NearlyEqual(0.75, ObjectScaleConverter.Calculate(0), "zero scale clamps to the visible minimum");
        NearlyEqual(0.75, ObjectScaleConverter.Calculate(0.5), "minimum scale displays at three-quarter size");
        NearlyEqual(1, ObjectScaleConverter.Calculate(1), "default scale remains unchanged");
        NearlyEqual(3.6, ObjectScaleConverter.Calculate(8), "maximum scale receives the expanded upper-bound contrast");
        NearlyEqual(3.6, ObjectScaleConverter.Calculate(80), "oversized values clamp to the expanded visual maximum");
        True(ObjectScaleConverter.Calculate(2) > 1 && ObjectScaleConverter.Calculate(2) < 2,
            "intermediate object scale remains compressed rather than linear");

        var smallDefault = ObjectScaleConverter.Calculate(1, 380);
        var smallLargeObject = ObjectScaleConverter.Calculate(4, 380);
        var referenceDefault = ObjectScaleConverter.Calculate(1, ObjectScaleConverter.ReferenceViewportExtent);
        var referenceLargeObject = ObjectScaleConverter.Calculate(4, ObjectScaleConverter.ReferenceViewportExtent);
        NearlyEqual(0.5, smallDefault, "small viewport uniformly reduces marker size");
        NearlyEqual(referenceLargeObject / referenceDefault, smallLargeObject / smallDefault,
            "viewport resizing preserves the relative object-scale ratio");
        NearlyEqual(referenceLargeObject,
            ObjectScaleConverter.Calculate(4, ObjectScaleConverter.ReferenceViewportExtent * 2),
            "large windows do not keep magnifying markers");
    }

    private static void PlanetTemplateDefaults()
    {
        var generic = new Planet();
        GalaxyMapDefaults.ApplyPlanetTemplate(generic, PlanetCreationTemplate.GenericPlanet);
        Equal(1, generic.OrbitRing, "generic Planet orbit ring");
        Equal(0, generic.SystemLevelType, "generic Planet system-view type");
        Equal(1, generic.PlanetLevelType!.Value, "generic Planet selection-view type");
        NearlyEqual(4, generic.Scale, "generic Planet scale");

        var belt = new Planet();
        GalaxyMapDefaults.ApplyPlanetTemplate(belt, PlanetCreationTemplate.AsteroidBelt);
        Equal(2, belt.OrbitRing, "asteroid belt orbit mode");
        Equal(0, belt.SystemLevelType, "asteroid belt system-view type");
        Equal(0, belt.PlanetLevelType!.Value, "asteroid belt has no selection model");
        NearlyEqual(0.01, belt.Scale, "asteroid belt vanilla scale");
        GalaxyMapDefaults.ApplyTemplateExtraValues(belt, PlanetCreationTemplate.AsteroidBelt);
        Equal("975", belt.ExtraFields["VisibleFunction"], "asteroid belt anchor is hidden");
        Equal("975", belt.ExtraFields["UsableFunction"], "asteroid belt anchor is not selectable");
        Equal("975", belt.ExtraFields["UsablePlanetFunction"], "asteroid belt anchor has no use button");
        var beltInspector = new PropertyInspectorViewModel();
        beltInspector.Inspect(belt);
        True(!beltInspector.Sections.SelectMany(section => section.Actions).Any(action =>
                action.Label.Contains("landable", StringComparison.OrdinalIgnoreCase) ||
                action.Label.Contains("linked Map", StringComparison.OrdinalIgnoreCase)),
            "asteroid belts do not expose landable-destination actions");

        var hidden = new Planet();
        GalaxyMapDefaults.ApplyPlanetTemplate(hidden, PlanetCreationTemplate.HiddenAnomaly);
        GalaxyMapDefaults.ApplyTemplateExtraValues(hidden, PlanetCreationTemplate.HiddenAnomaly);
        Equal(0, hidden.OrbitRing, "hidden anomaly has no orbit ring");
        Equal(1, hidden.SystemLevelType, "hidden anomaly system-view type");
        Equal(2, hidden.PlanetLevelType!.Value, "hidden anomaly selection-view type");
        Equal("975", hidden.ExtraFields["VisibleFunction"], "hidden anomaly visibility function");
    }

    private static void InspectorMetadataAndTypeRanges()
    {
        var planet = new Planet
        {
            RowId = 2,
            Label = "Planet01",
            ActiveWorld = 10101,
            SystemLevelType = 0,
            PlanetLevelType = 1,
            RingColor = -1
        };
        planet.AddExtraField("UsablePlanetConditional", "1");
        planet.AddExtraField("VisibleConditional", "0");
        planet.AddExtraField("VisibleFunction", "123");
        planet.AddExtraField("VisibleParameter", "0");
        var inspector = new PropertyInspectorViewModel();
        inspector.Inspect(planet);
        var fields = inspector.Sections.SelectMany(section => section.Fields).ToArray();
        var activeWorld = fields.Single(field => field.Name == "ActiveWorld");
        True(activeWorld.IsReadOnly, "ActiveWorld is presented as a derived read-only value");
        True(activeWorld.Description.Contains("Cluster suffix", StringComparison.Ordinal), "ActiveWorld tooltip explains its formula");
        Equal(InspectorEditorKind.Checkbox, fields.Single(field => field.Name == "UsablePlanetConditional").EditorKind,
            "UsablePlanetConditional uses a checkbox");
        Equal(InspectorEditorKind.Checkbox, fields.Single(field => field.Name == "VisibleParameter").EditorKind,
            "availability Parameter remains an independent checkbox");
        Equal("Visible: conditional", fields.Single(field => field.Name == "VisibleConditional").DisplayName,
            "visibility labels use the compact prefix");
        Equal("Button: conditional", fields.Single(field => field.Name == "UsablePlanetConditional").DisplayName,
            "use-button labels use the compact prefix");
        Equal("Horizon Intensity",
            GalaxyMapPropertyCatalog.Get(GalaxyMapTable.Planet, "Horizon_Atmosphere_Intensity").DisplayName,
            "long appearance names are compacted");
        True(GalaxyMapPropertyCatalog.Get(GalaxyMapTable.Planet, "Horizon_Atmosphere_Intensity").Description
                .Contains("Horizon_Atmosphere_Intensity", StringComparison.Ordinal),
            "compacted appearance names retain the raw column in their tooltip");
        SequenceEqual(Enumerable.Range(0, 8).Select(value => value.ToString(CultureInfo.InvariantCulture)),
            fields.Single(field => field.Name == "PlanetLevelType").Options.Select(option => option.Value),
            "PlanetLevelType exposes values 0 through 7");
        True(fields.Single(field => field.Name == "SystemLevelType").Options.All(option => option.Value != "6"),
            "SystemLevelType does not invent value 6");
        True(fields.Single(field => field.Name == "RingColor").ColorPreview == Brushes.Transparent,
            "RingColor -1 previews as not applicable rather than opaque white");

        var availability = inspector.Sections.Single(section => section.Title == "Visibility and usability");
        availability.IsExpanded = false;
        True(!availability.IsExpanded, "inspector section expansion state supports WPF's two-way Expander binding");
        availability.IsExpanded = true;
        var always = availability.Actions.Single(action => action.Label == "Set these rules to Always");
        always.Command.Execute(null);
        Equal("1", planet.ExtraFields["VisibleConditional"], "Always preset enables the condition");
        Equal("974", planet.ExtraFields["VisibleFunction"], "Always preset uses utility function 974");
        Equal("1", planet.ExtraFields["VisibleParameter"], "Always preset enables the independent parameter");
    }

    private static void SquareViewportAndGridDefinitions()
    {
        var child = new Border();
        var viewport = new SquareViewport { Child = child };
        viewport.Measure(new Size(900, 600));
        viewport.Arrange(new Rect(0, 0, 900, 600));
        NearlyEqual(600, child.RenderSize.Width, "wide viewport child width");
        NearlyEqual(600, child.RenderSize.Height, "wide viewport child height");
        NearlyEqual(150, VisualTreeHelper.GetOffset(child).X, "wide viewport horizontal centering");

        viewport.Measure(new Size(500, 800));
        viewport.Arrange(new Rect(0, 0, 500, 800));
        NearlyEqual(500, child.RenderSize.Width, "tall viewport child width");
        NearlyEqual(500, child.RenderSize.Height, "tall viewport child height");
        NearlyEqual(150, VisualTreeHelper.GetOffset(child).Y, "tall viewport vertical centering");

        Equal(40, CoordinateGridLayer.DivisionCount, "coordinate grid division count");
        NearlyEqual(0.025, CoordinateGridLayer.MinorIncrement, "coordinate grid minor increment");
        NearlyEqual(0.25, CoordinateGridLayer.MajorIncrement, "coordinate grid labelled increment");
        Equal(5, CoordinateGridLayer.AxisLabels.Count, "coordinate label count per axis");
        SequenceEqual(["0.00", "0.25", "0.50", "0.75", "1.00"],
            CoordinateGridLayer.AxisLabels, "quarter coordinate labels");
        SequenceEqual(["0.25", "0.50", "0.75", "1.00"],
            CoordinateGridLayer.BottomAxisLabels, "bottom axis omits its duplicate zero label");
        SequenceEqual(["0.00", "0.25", "0.50", "0.75"],
            CoordinateGridLayer.LeftAxisLabels, "left axis omits its duplicate one label");

        var coordinateGrid = new CoordinateGridLayer();
        True(!coordinateGrid.IsHitTestVisible, "coordinate grid cannot intercept map clicks");
        var normalized = CoordinateGridLayer.NormalizePosition(new Point(200, 100), new Size(400, 400));
        NearlyEqual(0.5, normalized.X, "cursor X normalization");
        NearlyEqual(0.25, normalized.Y, "cursor Y normalization");
        Equal("X 0.50   Y 0.25", CoordinateGridLayer.FormatCoordinates(normalized),
            "cursor coordinate formatting");
        var rounded = CoordinateGridLayer.RoundNormalizedPosition(new Point(0.126, 0.994));
        NearlyEqual(0.13, rounded.X, "drag coordinate X rounds to two decimals");
        NearlyEqual(0.99, rounded.Y, "drag coordinate Y rounds to two decimals");
        coordinateGrid.ShowCursor(new Point(200, 100));
        Equal(new Point(200, 100), coordinateGrid.CursorPosition!.Value, "cursor position is retained for rendering");
        coordinateGrid.HideCursor();
        True(coordinateGrid.CursorPosition is null, "cursor display can be cleared");

        var anchoredChild = new Border { Width = 150, Height = 80 };
        var normalizedCanvas = new NormalizedCanvas { Width = 500, Height = 500 };
        normalizedCanvas.Children.Add(anchoredChild);
        NormalizedCanvas.SetX(anchoredChild, 0.3);
        NormalizedCanvas.SetY(anchoredChild, 0.4);
        NormalizedCanvas.SetAnchorY(anchoredChild, 21);
        normalizedCanvas.Measure(new Size(500, 500));
        normalizedCanvas.Arrange(new Rect(0, 0, 500, 500));
        NearlyEqual(75, VisualTreeHelper.GetOffset(anchoredChild).X, "marker is horizontally centred on X");
        NearlyEqual(179, VisualTreeHelper.GetOffset(anchoredChild).Y, "marker row centre is anchored on Y");
        NormalizedCanvas.SetY(anchoredChild, 0.97);
        NormalizedCanvas.SetAnchorFromBottom(anchoredChild, true);
        normalizedCanvas.Arrange(new Rect(0, 0, 500, 500));
        NearlyEqual(426, VisualTreeHelper.GetOffset(anchoredChild).Y,
            "bottom-edge markers anchor from the lower marker row after placing their label above");
    }

    private static void TextureMappingIgnoresPngAlpha()
    {
        Equal("Cluster03.jpg",
            GalaxyMapTextureService.ResolveClusterAssetName("BIOA_GalaxyMap_T.Cluster03")!,
            "CSV object reference mapping");
        Equal("Cluster03.jpg",
            GalaxyMapTextureService.ResolveClusterAssetName("cluster3.PNG")!,
            "bare case-insensitive texture mapping");
        True(GalaxyMapTextureService.ResolveClusterAssetName(string.Empty) is null, "blank texture reference");
        True(GalaxyMapTextureService.ResolveClusterAssetName("../Cluster03") is null, "path-like reference rejected");
        True(GalaxyMapTextureService.ResolveClusterAssetName("BIOA_GalaxyMap_T.NotACluster") is null,
            "malformed texture reference rejected");

        var textures = new GalaxyMapTextureService(FindTextureDirectory());
        var galaxy = textures.GetGalaxyTexture();
        NotNull(galaxy, "galaxy texture loads");
        Equal(2048, galaxy!.PixelWidth, "galaxy texture width");
        Equal(2048, galaxy.PixelHeight, "galaxy texture height");
        Equal(PixelFormats.Bgr32, galaxy.Format, "galaxy texture has no alpha channel");
        True(galaxy.IsFrozen, "galaxy texture is safe to share across threads");

        var cluster03 = textures.GetClusterTexture("BIOA_GalaxyMap_T.Cluster03");
        NotNull(cluster03, "Serpent background asset loads");
        Equal(PixelFormats.Bgr32, cluster03!.Format, "cluster texture has no alpha channel");
        True(cluster03.IsFrozen, "cluster texture is safe to share across views");
        True(ReferenceEquals(cluster03, textures.GetClusterTexture("Cluster03")), "decoded texture is cached");

        var stars = textures.GetSystemTexture();
        NotNull(stars, "System starfield texture loads");
        Equal(2048, stars!.PixelWidth, "System starfield width");
        Equal(2048, stars.PixelHeight, "System starfield height");
        Equal(PixelFormats.Bgr32, stars.Format, "System starfield ignores PNG alpha");
        True(stars.IsFrozen, "System starfield is safe to share across threads");
    }

    private static void HierarchyNavigationSemantics()
    {
        WithFixture(folder =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()));
            True(viewModel.LoadFolder(folder), "fixture loads");

            Equal(1, viewModel.HierarchyRoots.Count, "hierarchy has one galaxy root");
            var galaxyRoot = viewModel.HierarchyRoots.Single();
            True(galaxyRoot.IsGalaxyRoot, "top hierarchy row is the Galaxy root");
            Equal("The Milky Way", galaxyRoot.DisplayName, "Galaxy root name");
            Equal("Galaxy", galaxyRoot.ItemType, "Galaxy root type");
            True(!string.IsNullOrWhiteSpace(galaxyRoot.Icon), "Galaxy root has its own icon");
            Equal(3, galaxyRoot.Children.Count, "Clusters are nested below the Galaxy root");

            galaxyRoot.IsSelected = true;
            True(viewModel.CurrentViewModel is GalaxyViewModel, "sidebar Galaxy selection opens Galaxy view");
            Equal("The Milky Way", viewModel.Inspector.Title, "Galaxy selection updates the inspector");
            var galaxy = (GalaxyViewModel)viewModel.CurrentViewModel!;
            Equal(3, galaxy.Clusters.Count, "Galaxy canvas receives Cluster nodes, not the synthetic root");

            var clusterNode = galaxyRoot.Children.Single(node => node.Item.RowId == 6);
            clusterNode.IsSelected = true;
            True(viewModel.CurrentViewModel is ClusterViewModel, "sidebar Cluster selection opens Cluster view");
            Equal(6, ((ClusterViewModel)viewModel.CurrentViewModel!).Cluster.RowId, "correct Cluster view opens");

            var systemNode = clusterNode.Children.Single();
            systemNode.IsSelected = true;
            True(viewModel.CurrentViewModel is SystemViewModel, "sidebar System selection opens System view");
            Equal(systemNode.Item.RowId, ((SystemViewModel)viewModel.CurrentViewModel!).System.RowId,
                "correct System view opens");

            var planetNode = systemNode.Children.Single();
            planetNode.IsSelected = true;
            True(viewModel.CurrentViewModel is SystemViewModel, "sidebar Planet selection stays in System view");
            Equal(systemNode.Item.RowId, ((SystemViewModel)viewModel.CurrentViewModel!).System.RowId,
                "Planet opens its containing System");
            Equal(planetNode.DisplayName, viewModel.Inspector.Title, "Planet remains the selected property object");

            var edgePlanet = new Planet
            {
                RowId = 999,
                Label = "EdgePlanet",
                NameText = "Edge Planet",
                Y = 0.97,
                PlanetLevelType = 1,
                SystemLevelType = 0
            };
            using var edgeNode = new HierarchyNodeViewModel(edgePlanet, _ => { });
            True(edgeNode.IsNearBottomEdge,
                "map labels switch above their marker at the 0.97 Y boundary");
            Equal("Double-click to open Planet Designer", edgeNode.SystemMapToolTip,
                "Planet hover text advertises the Designer action");
            edgePlanet.SystemLevelType = 2;
            Equal("Double-click to open Planet Designer", edgeNode.SystemMapToolTip,
                "Ringed Planet hover text advertises the Designer action");
            edgePlanet.SystemLevelType = 1;
            Equal(nameof(PlanetVisualKind.Anomaly), edgeNode.SystemMapToolTip,
                "non-Planet system objects retain their visual-kind hover text");
            edgePlanet.Y = 0.969;
            True(!edgeNode.IsNearBottomEdge,
                "map labels remain below their marker before the edge threshold");

            viewModel.HierarchySearch = "Horse Saturn";
            True(galaxyRoot.IsVisible && clusterNode.IsVisible && systemNode.IsVisible && planetNode.IsVisible,
                "hierarchy search retains the complete ancestor path to a matching Planet");
            True(galaxyRoot.IsExpanded && clusterNode.IsExpanded && systemNode.IsExpanded,
                "hierarchy search automatically expands the matching path");
            True(galaxyRoot.Children.Where(node => !ReferenceEquals(node, clusterNode)).All(node => !node.IsVisible),
                "hierarchy search hides unrelated branches");
            viewModel.HierarchySearch = string.Empty;
            True(galaxyRoot.Children.All(node => node.IsVisible),
                "clearing hierarchy search restores every top-level branch");

            viewModel.ActivateHierarchyNode(galaxyRoot);
            galaxy = (GalaxyViewModel)viewModel.CurrentViewModel!;
            galaxy.SelectCommand.Execute(clusterNode);
            True(viewModel.CurrentViewModel is GalaxyViewModel,
                "single-clicking a Cluster on the map remains in Galaxy view");
            True(clusterNode.IsSelected, "map-selected Cluster synchronizes to the hierarchy");

            viewModel.ActivateHierarchyNode(clusterNode);
            var cluster = (ClusterViewModel)viewModel.CurrentViewModel!;
            cluster.SelectCommand.Execute(systemNode);
            True(viewModel.CurrentViewModel is ClusterViewModel,
                "single-clicking a System on the map remains in Cluster view");
            True(systemNode.IsSelected, "map-selected System synchronizes to the hierarchy");

            viewModel.ActivateHierarchyNode(systemNode);
            var system = (SystemViewModel)viewModel.CurrentViewModel!;
            system.SelectCommand.Execute(planetNode);
            True(viewModel.CurrentViewModel is SystemViewModel,
                "single-clicking a Planet remains in System view");
            True(planetNode.IsSelected, "map-selected Planet synchronizes to the hierarchy");
        });
    }

    private static void ContextualAddActionsFollowActiveView()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");

            Equal("Add Cluster", viewModel.ContextualAddButtonText, "Galaxy view offers Add Cluster");
            True(viewModel.HasContextualAddAction, "Galaxy view exposes one contextual add action");
            True(!viewModel.ContextualAddCommand.CanExecute(null), "add action is disabled without a writable module");
            var root = viewModel.HierarchyRoots.Single();
            Equal("Add Cluster", root.AddChildMenuHeader, "Galaxy root context menu offers Add Cluster");
            True(root.SupportsAddChild, "Galaxy root supports child creation");
            True(root.AddChildCommand is not null && !root.AddChildCommand.CanExecute(null),
                "Galaxy root action is disabled without a writable module");

            True(viewModel.CreateModule(parent, "Context Add Test", "CONTEXT_ADD", ModuleColor.Magenta,
                TestReservations()), "module created");
            True(viewModel.ContextualAddCommand.CanExecute(null), "Galaxy add action enables for the active module");

            var clusterCount = viewModel.Document!.Clusters.Count;
            viewModel.ContextualAddCommand.Execute(null);
            Equal(clusterCount + 1, viewModel.Document.Clusters.Count, "canvas/header action creates a Cluster");
            Equal(100, viewModel.CurrentCluster!.RowId, "new Cluster uses the reserved range");
            Equal("Add System", viewModel.ContextualAddButtonText, "Cluster view offers Add System");

            root = viewModel.HierarchyRoots.Single();
            viewModel.ActivateHierarchyNode(root);
            root.AddChildCommand!.Execute(null);
            Equal(clusterCount + 2, viewModel.Document.Clusters.Count, "Galaxy root action creates a Cluster");
            Equal(101, viewModel.CurrentCluster!.RowId, "Galaxy root action targets the galaxy");

            var targetClusterNode = viewModel.HierarchyRoots.Single().Children
                .Single(node => node.Item.RowId == 1);
            Equal("Add System", targetClusterNode.AddChildMenuHeader, "Cluster context menu offers Add System");
            targetClusterNode.AddChildCommand!.Execute(null);
            Equal(1000, viewModel.CurrentSystem!.RowId, "Cluster action creates a System in the reserved range");
            Equal(1, viewModel.CurrentSystem.ClusterRowId, "Cluster action targets the right-clicked Cluster");
            Equal("Add Planet/Object", viewModel.ContextualAddButtonText, "System view offers Add Planet/Object");

            var targetSystemNode = FindNode(viewModel, row => row is GalaxySystem { RowId: 1000 });
            Equal("Add Planet/Object", targetSystemNode.AddChildMenuHeader, "System context menu offers Add Planet/Object");
            targetSystemNode.AddChildCommand!.Execute(null);
            Equal(1, viewModel.Document.Planets.Count(planet => planet.SystemRowId == 1000),
                "System action creates a Planet under the right-clicked System");

            viewModel.ContextualAddCommand.Execute(null);
            Equal(2, viewModel.Document.Planets.Count(planet => planet.SystemRowId == 1000),
                "System canvas/header action creates another Planet");
            var planetNode = FindNode(viewModel, row => row is Planet { RowId: 10000 });
            True(!planetNode.SupportsAddChild && planetNode.AddChildCommand is null,
                "Planet rows do not expose a child-creation action");
        });
    }

    private static void OptionalPlanetRelationshipsCanBeCreated()
    {
        WithFixture(folder =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader());
            True(viewModel.LoadFolder(folder), "fixture loads");
            var planet = viewModel.Document!.PlanetsByRowId[9];
            FindNode(viewModel, row => ReferenceEquals(row, planet)).IsSelected = true;

            var optional = viewModel.Inspector.Sections.Single(section => section.Title == "Optional relationships");
            optional.Actions.Single(action => action.Label == "Add PlotPlanet properties").Command.Execute(null);
            NotNull(planet.PlotPlanet, "new PlotPlanet link");
            Equal(planet.RowId, planet.PlotPlanet!.RowId, "PlotPlanet shares Planet row ID");
            Equal(70106, planet.PlotPlanet.Code, "PlotPlanet code derived from Cluster/System/Planet labels");
            Equal(2, viewModel.Document.PlotPlanets.Count, "one PlotPlanet row added");
            True(viewModel.Inspector.Sections.Any(section => section.Title == "Linked PlotPlanet"),
                "new PlotPlanet fields appear immediately");

            optional = viewModel.Inspector.Sections.Single(section => section.Title == "Optional relationships");
            optional.Actions.Single(action => action.Label == "Add linked Map").Command.Execute(null);
            NotNull(planet.LinkedMap, "new Map link");
            Equal(89, planet.LinkedMap!.RowId, "Map uses next available table row ID");
            Equal(planet.LinkedMap.RowId, planet.MapRowId, "Planet Map foreign key updated");
            Equal(3, viewModel.Document.Maps.Count, "one Map row added");
            True(viewModel.Inspector.Sections.Any(section => section.Title == "Linked Map"),
                "new Map fields appear immediately");
            True(viewModel.Inspector.Sections.Single(section => section.Title == "Optional relationships").Actions
                    .All(action => !action.Label.StartsWith("Add ", StringComparison.Ordinal)),
                "creation actions disappear when both links exist while destination editing remains available");
        });
    }

    private static void ClusterRelayEditingWorkflow()
    {
        WithFixture(folder =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader());
            True(viewModel.LoadFolder(folder), "fixture loads");
            Equal(1, viewModel.DiagnosticCount, "initial unresolved Relay warning");
            viewModel.ToggleDiagnosticsCommand.Execute(null);
            True(viewModel.IsDiagnosticsPanelOpen, "warning details can be opened");

            var cluster07 = viewModel.Document!.ClustersByRowId[6];
            FindNode(viewModel, row => ReferenceEquals(row, cluster07)).IsSelected = true;
            var relaySection = viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections");
            Equal(3, relaySection.Actions.Count(action => action.Label.StartsWith("Break connection", StringComparison.Ordinal)),
                "all incident Relays are manageable, including unresolved rows");
            relaySection.Actions.Single(action => action.Label.Contains("unresolved", StringComparison.OrdinalIgnoreCase))
                .Command.Execute(null);
            Equal(2, viewModel.Document.Relays.Count, "breaking a Relay removes its row in memory");
            Equal(0, viewModel.DiagnosticCount, "breaking unresolved Relay clears warning");
            True(!viewModel.IsDiagnosticsPanelOpen, "warning panel closes when warnings are gone");

            var source = viewModel.Document.ClustersByRowId[1];
            var target = viewModel.Document.ClustersByRowId[20];
            FindNode(viewModel, row => ReferenceEquals(row, source)).IsSelected = true;
            viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections")
                .Actions.Single(action => action.Label.StartsWith("Add relay", StringComparison.Ordinal))
                .Command.Execute(null);
            True(viewModel.IsAddingRelay, "add Relay enters target-selection mode");
            FindNode(viewModel, row => ReferenceEquals(row, target)).IsSelected = true;
            True(!viewModel.IsAddingRelay, "selecting a target completes Relay mode");
            var added = viewModel.Document.Relays.Single(relay =>
                relay.StartClusterEncoded == 10000 && relay.EndClusterEncoded == 210000);
            True(added.IsResolved, "new Relay resolves both endpoints");
            Equal(3, viewModel.Document.Relays.Count, "one Relay row added");

            viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections")
                .Actions.Single(action => action.Label.StartsWith("Add relay", StringComparison.Ordinal))
                .Command.Execute(null);
            FindNode(viewModel, row => ReferenceEquals(row, target)).IsSelected = true;
            True(viewModel.IsAddingRelay, "duplicate target keeps selection mode active");
            Equal(3, viewModel.Document.Relays.Count, "reverse/forward duplicate Relay rejected");
            viewModel.CancelRelayCommand.Execute(null);
            True(!viewModel.IsAddingRelay, "Relay mode can be cancelled");

            viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections")
                .Actions.Single(action => action.Label.StartsWith("Add relay", StringComparison.Ordinal))
                .Command.Execute(null);
            var galaxy = (GalaxyViewModel)viewModel.CurrentViewModel!;
            galaxy.SelectCommand.Execute(FindNode(viewModel, row => ReferenceEquals(row, source)));
            True(viewModel.IsAddingRelay, "self-link is rejected without abandoning selection mode");
            Equal(3, viewModel.Document.Relays.Count, "self-link adds no Relay row");
            viewModel.CancelRelayCommand.Execute(null);
        });
    }

    private static void RelayLayerObservesCollectionChanges()
    {
        var document = new GalaxyMapDocument();
        var start = new Cluster { RowId = 1, Label = "Cluster01", X = 0.2, Y = 0.25, NameText = "Start" };
        var end = new Cluster { RowId = 2, Label = "Cluster02", X = 0.8, Y = 0.75, NameText = "End" };
        document.Clusters.Add(start);
        document.Clusters.Add(end);
        document.RebuildRelationships();

        var layer = new RelayLayer
        {
            Width = 240,
            Height = 240,
            Connections = document.Relays
        };
        layer.Measure(new Size(240, 240));
        layer.Arrange(new Rect(0, 0, 240, 240));
        Equal(0, CountRelayPixels(layer), "empty Relay collection draws no line");

        var relay = new RelayConnection
        {
            RowId = 1,
            StartClusterEncoded = 10_000,
            EndClusterEncoded = 20_000
        };
        document.Relays.Add(relay);
        document.RebuildRelationships();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        True(CountRelayPixels(layer) > 0, "adding to the existing collection redraws the Relay line");

        True(document.Relays.Remove(relay), "Relay row can be removed");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Equal(0, CountRelayPixels(layer), "removing from the existing collection clears the Relay line");
    }

    private static int CountRelayPixels(RelayLayer layer)
    {
        const int size = 240;
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(layer);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        var redPixels = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            if (red > 150 && red > green * 1.7 && red > blue * 1.4)
            {
                redPixels++;
            }
        }

        return redPixels;
    }

    private static void DuplicateRowIdsAreRejected()
    {
        WithFixture(folder =>
        {
            var path = Path.Combine(folder, "GalaxyMap_Cluster.csv");
            File.AppendAllText(path, "1,Cluster99,0.2,0.2,999,Duplicate,4,bg,duplicate\r\n", new UTF8Encoding(false));
            Throws<GalaxyMapLoadException>(
                () => new CsvGalaxyMapLoader().LoadFolder(folder),
                message => message.Contains("duplicate row ID 1", StringComparison.OrdinalIgnoreCase),
                "duplicate row error");
        });
    }

    private static void MissingTableIsReported()
    {
        WithFixture(folder =>
        {
            File.Delete(Path.Combine(folder, "GalaxyMap_Relay.csv"));
            Throws<GalaxyMapLoadException>(
                () => new CsvGalaxyMapLoader().LoadFolder(folder),
                message => message.Contains("Missing Relay CSV", StringComparison.OrdinalIgnoreCase),
                "missing table error");
        });
    }

    private static void EffectiveBaseGameRowsAreDetached()
    {
        var loader = new CsvGalaxyMapLoader();
        var baseLayer = loader.LoadBuiltInLayer();
        var physical = baseLayer.Clusters.Single(row => row.RowId == 1);
        var originalName = physical.NameText;
        var workspace = new GalaxyMapWorkspace(baseLayer);
        var effective = workspace.EffectiveDocument.ClustersByRowId[1];

        True(!ReferenceEquals(physical, effective), "effective BASEGAME row is detached");
        effective.NameText = "Transient mutation";
        Equal(originalName, physical.NameText, "physical BASEGAME row remains unchanged");
        workspace.Recompose();
        Equal(originalName, workspace.EffectiveDocument.ClustersByRowId[1].NameText,
            "recomposition discards an unpersisted effective mutation");
    }

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

    private static void PartialModuleReservations()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
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
            True(viewModel.CloneRow(source, new CloneContentRequest(1000, "System99", 0, "Cloned System", true)), "System clone succeeds");
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
                    !system.Label.Equals("System99", StringComparison.OrdinalIgnoreCase)));

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
                new CloneContentRequest(1000, "System99", 0, "Movable System", true)),
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
                new CloneContentRequest(1001, "System99", 0, "Collision System", false)),
                "destination-scoped collision System is created");
            True(viewModel.MoveRow(viewModel.Document.SystemsByRowId[1000], targetCluster.RowId),
                "System move resolves a destination label collision");
            moved = viewModel.Document.SystemsByRowId[1000];
            True(!moved.Label.Equals("System99", StringComparison.OrdinalIgnoreCase),
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
                .Fields.Single(field => field.Name == "Label").Value = "System99";

            var updatedPlanet = viewModel.Document.PlanetsByRowId[sourcePlanet.RowId];
            var clusterSuffix = int.Parse(updatedPlanet.System!.Cluster!.Label["Cluster".Length..], CultureInfo.InvariantCulture);
            var planetSuffix = int.Parse(updatedPlanet.Label["Planet".Length..], CultureInfo.InvariantCulture);
            Equal(clusterSuffix * 10_000 + 9_900 + planetSuffix, updatedPlanet.ActiveWorld,
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
        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        var diagnostics = new GalaxyMapValidator().Validate(workspace);

        True(diagnostics.Any(item => item.Code == "ID-OUTSIDE-RESERVATION" && item.Severity == ValidationSeverity.Error),
            "out-of-range ID error");
        True(diagnostics.Any(item => item.Code == "LABEL-CLUSTER" && item.RowId == 150), "label typo error");
        True(diagnostics.Any(item => item.Code == "COORDINATE-OFF-CANVAS" && item.Severity == ValidationSeverity.Warning),
            "off-canvas coordinate warning");
        True(diagnostics.Any(item => item.Code == "VALUE-NONPOSITIVE-SCALE"), "invisible-size warning");
        True(diagnostics.Any(item => item.Code == "TYPE-PLANET-PACKED-COLOR" &&
                                     item.Severity == ValidationSeverity.Warning &&
                                     item.RowId == invalidPackedColorPlanet.RowId &&
                                     item.ColumnName == "SunColor1"),
            "invalid Planet appearance packed colours produce a non-blocking warning");
    }

    private static void InheritedRelayRedirectPersistence()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Relay Redirect", "RELAY_REDIRECT", ModuleColor.Red,
                TestReservations()), "module created");

            var local = viewModel.Document!.Clusters.Single(cluster => cluster.Label == "Cluster03");
            var relay = viewModel.Document.Relays.Single(row => row.RowId == 1);
            FindNode(viewModel, row => row is Cluster cluster && cluster.RowId == local.RowId).IsSelected = true;
            var redirect = viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections")
                .Actions.Single(action => action.Detail.Contains("Relay row 1", StringComparison.Ordinal) &&
                                          action.Label.StartsWith("Redirect", StringComparison.Ordinal));

            var incident = viewModel.Document.GetRelaysForCluster(local)
                .SelectMany(row => new[] { row.StartCluster?.RowId, row.EndCluster?.RowId })
                .Where(rowId => rowId.HasValue).Select(rowId => rowId!.Value).ToHashSet();
            var target = viewModel.Document.Clusters.First(cluster =>
                cluster.RowId != local.RowId && !incident.Contains(cluster.RowId));
            True(viewModel.Document.TryGetRelayCode(target, out var targetCode, out _), "target Relay code resolves");

            redirect.Command.Execute(null);
            FindNode(viewModel, row => row is Cluster cluster && cluster.RowId == target.RowId).IsSelected = true;

            var updated = viewModel.Document.Relays.Single(row => row.RowId == relay.RowId);
            True(updated.StartClusterEncoded == 30000 || updated.EndClusterEncoded == 30000,
                "selected Local Cluster endpoint is preserved");
            True(updated.StartClusterEncoded == targetCode || updated.EndClusterEncoded == targetCode,
                "opposite endpoint is redirected");
            Equal("RELAY_REDIRECT", updated.Origin!.ModuleTag, "redirected Relay provenance");
            Equal(2, viewModel.Workspace!.GetOverrideChain(updated.Key).Count,
                "redirect is represented by a same-row-ID override");
            True(viewModel.HasPendingChanges, "Relay redirect remains staged");
            True(viewModel.CommitPendingChanges(), "Relay redirect commit succeeds");
            True(File.Exists(Path.Combine(viewModel.ActiveModule!.FolderPath!, "GalaxyMap_Relay_part.csv")),
                "Relay override CSV written");
        });
    }

    private static void RememberedModuleWorkspace()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            var first = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath));
            True(first.LoadBuiltIn(), "first BASEGAME load");
            True(first.CreateModule(parent, "Remembered Module", "REMEMBERED", ModuleColor.Green,
                TestReservations(), loadOrder: 12), "remembered module created");
            var moduleFolder = first.ActiveModule!.FolderPath!;
            True(File.Exists(settingsPath), "workspace settings written when module is mounted");
            using (var workspaceJson = JsonDocument.Parse(File.ReadAllText(settingsPath)))
            {
                Equal(2, workspaceJson.RootElement.GetProperty("schemaVersion").GetInt32(),
                    "workspace uses location-only schema");
                var rememberedModule = workspaceJson.RootElement.GetProperty("modules")[0];
                SequenceEqual(["folderPath"], rememberedModule.EnumerateObject().Select(property => property.Name),
                    "manifest-backed workspace entry stores only its folder");
                Equal(moduleFolder, rememberedModule.GetProperty("folderPath").GetString()!,
                    "workspace remembers the module folder");
            }

            File.WriteAllText(settingsPath, $$"""
                {
                  "schemaVersion": 1,
                  "activeModuleTag": "REMEMBERED",
                  "modules": [
                    {
                      "name": "Stale workspace name",
                      "tag": "REMEMBERED",
                      "color": "Red",
                      "folderPath": {{JsonSerializer.Serialize(moduleFolder)}},
                      "isReadOnly": false,
                      "loadOrder": 999,
                      "reservations": {},
                      "clusterTextures": {}
                    }
                  ]
                }
                """, new UTF8Encoding(false));

            var restored = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath),
                confirmAction: _ => true);
            True(restored.LoadRememberedWorkspace(), "remembered workspace restores cleanly");
            Equal(1, restored.Workspace!.ModuleLayers.Count, "remembered module count");
            Equal("REMEMBERED", restored.ActiveModule!.Tag, "remembered active module");
            Equal("Remembered Module", restored.ActiveModule.Name,
                "module manifest overrides stale version-one workspace metadata");
            Equal(12, restored.ActiveModule.LoadOrder, "remembered mount priority");
            var restoredModule = restored.ActiveModule;
            restored.Workspace.SetActiveModule(null);
            True(restored.SetActiveModule(restoredModule), "module can be explicitly made active");
            Equal("REMEMBERED", new GalaxyMapWorkspaceStore(settingsPath).Load().ActiveModuleTag!,
                "explicit active-module choice is persisted immediately");
            using (var migratedWorkspaceJson = JsonDocument.Parse(File.ReadAllText(settingsPath)))
            {
                Equal(2, migratedWorkspaceJson.RootElement.GetProperty("schemaVersion").GetInt32(),
                    "version-one workspace migrates when the active choice is saved");
                SequenceEqual(["folderPath"], migratedWorkspaceJson.RootElement.GetProperty("modules")[0]
                        .EnumerateObject().Select(property => property.Name),
                    "migrated manifest-backed entry drops legacy metadata");
            }
            True(restored.UpdateModuleMetadata(
                restored.ActiveModule,
                "Remembered Module Edited",
                "REMEMBERED",
                ModuleColor.Magenta,
                25,
                restored.ActiveModule.Reservations), "module metadata edit stages");
            True(restored.HasPendingChanges, "module metadata waits for Commit");
            True(restored.CommitPendingChanges(), "module metadata commit succeeds");
            var editedManifest = new GalaxyMapModuleManifestStore().Load(moduleFolder);
            Equal(25, editedManifest.LoadOrder, "edited mount priority written to manifest");
            Equal(ModuleColor.Magenta, editedManifest.Color, "edited module colour written to manifest");
            True(restored.UpdateModuleMetadata(restored.ActiveModule, "Transient uncommitted name", "REMEMBERED",
                ModuleColor.Red, 26, restored.ActiveModule.Reservations), "transient metadata stages before refresh");
            True(restored.RefreshRememberedWorkspace(), "Refresh reloads the remembered JSON workspace");
            Equal("Remembered Module Edited", restored.ActiveModule!.Name, "Refresh restores committed manifest data");
            Equal(25, restored.ActiveModule.LoadOrder, "Refresh restores committed mount priority");
            True(!restored.HasPendingChanges, "Refresh clears confirmed transient changes");

            var movedFolder = moduleFolder + "_moved";
            Directory.Move(moduleFolder, movedFolder);
            var missing = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath));
            True(!missing.LoadRememberedWorkspace(), "missing module flags startup failure");
            True(missing.HasError, "missing module raises visible error flag");
            True(missing.ValidationDiagnostics.Any(item => item.Code == "WORKSPACE-MODULE-MISSING"),
                "missing module diagnostic is structured");
            missing.DismissErrorCommand.Execute(null);
            True(!missing.HasError, "error banner can be dismissed without removing its diagnostic");
            True(missing.ValidationDiagnostics.Any(item => item.Code == "WORKSPACE-MODULE-MISSING"),
                "dismissing the banner preserves validation details");
        });
    }

    private static void MountPriorityAndRowInstances()
    {
        WithTemporaryDirectory(parent =>
        {
            var selectedTarget = "MODULE_A";
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) => modules.Single(module => module.Tag == selectedTarget));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Module A", "MODULE_A", ModuleColor.Red,
                TestReservations(), loadOrder: 10), "Module A created");
            True(viewModel.CreateModule(parent, "Module B", "MODULE_B", ModuleColor.Cyan,
                AlternateReservations(), loadOrder: 20), "Module B created");

            FindNode(viewModel, row => row is Cluster { RowId: 1 }).IsSelected = true;
            var x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            x.Value = "0.21";
            Equal("MODULE_A", viewModel.Document!.ClustersByRowId[1].Origin!.ModuleTag,
                "first override becomes effective");

            var baseTab = viewModel.RowInstanceTabs.Single(tab => tab.Module.IsBaseGame);
            baseTab.SelectCommand.Execute(null);
            selectedTarget = "MODULE_B";
            x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            x.Value = "0.82";

            Equal("MODULE_B", viewModel.Document.ClustersByRowId[1].Origin!.ModuleTag,
                "higher-priority override wins");
            NearlyEqual(0.82, viewModel.Document.ClustersByRowId[1].X, "higher-priority value");
            Equal(3, viewModel.Workspace!.GetOverrideChain(
                new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1)).Count, "BASEGAME plus two module instances");
            True(FindNode(viewModel, row => row is Cluster { RowId: 1 }).HasMultipleInstances,
                "hierarchy marks multiple instances");
            Equal(3, viewModel.RowInstanceTabs.Count, "comparison tabs include every instance");

            viewModel.RowInstanceTabs.Single(tab => tab.Module.Tag == "MODULE_A").SelectCommand.Execute(null);
            x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            NearlyEqual(0.21, double.Parse(x.Value, CultureInfo.InvariantCulture),
                "lower-priority module instance can be inspected");
            True(viewModel.ValidationDiagnostics.Any(item => item.Code == "ID-MODULE-COLLISION"),
                "same-row module conflict remains diagnosed");
        });
    }

    private static void ModuleUnlinkPreservesFiles()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            string? confirmation = null;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath),
                confirmAction: message =>
                {
                    confirmation = message;
                    return true;
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads before unlink test");
            True(viewModel.CreateModule(parent, "Unlink Test", "UNLINK_TEST", ModuleColor.Green,
                TestReservations()), "authoring module is created");
            var moduleFolder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.UpdateModuleMetadata(
                viewModel.ActiveModule,
                "Transient unlink name",
                viewModel.ActiveModule.Tag,
                ModuleColor.Magenta,
                viewModel.ActiveModule.LoadOrder,
                viewModel.ActiveModule.Reservations), "module receives a staged metadata change");
            var stagedModule = viewModel.ActiveModule!;
            True(viewModel.HasPendingChanges, "module is dirty before unlinking");

            True(viewModel.UnlinkModule(stagedModule), "module unlinks successfully");
            Equal(0, viewModel.Workspace!.ModuleLayers.Count, "module layer is removed from memory");
            True(viewModel.ActiveModule is null, "active module clears when no writable fallback exists");
            True(!viewModel.HasPendingChanges, "unlinked module dirty state is discarded");
            True(confirmation?.Contains("staged changes", StringComparison.OrdinalIgnoreCase) == true,
                "confirmation warns about staged changes");
            True(Directory.Exists(moduleFolder), "module folder is preserved");
            True(File.Exists(Path.Combine(moduleFolder, GalaxyMapModuleManifestStore.FileName)),
                "module manifest is preserved");
            Equal(0, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "workspace JSON no longer remembers the module");
        });
    }

    private static void ModuleTexturesAndNebulaSystems()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Texture Module", "TEXTURE_MODULE", ModuleColor.Purple,
                TestReservations()), "texture module created");

            var nebulaSystem = viewModel.Document!.Systems.First(system => system.ShowNebula == 1 && system.Cluster is not null);
            var cluster = nebulaSystem.Cluster!;
            FindNode(viewModel, row => row is Cluster candidate && candidate.RowId == cluster.RowId).IsSelected = true;
            var sourceTexture = Path.Combine(FindTextureDirectory(), "Cluster01.jpg");
            True(viewModel.StageClusterTexture(cluster, viewModel.ActiveModule!, sourceTexture),
                "JPEG module texture is staged");
            var expectedPath = Path.Combine(viewModel.ActiveModule!.FolderPath!, "textures",
                $"Cluster_{cluster.RowId}_Cluster01.jpg");
            True(!File.Exists(expectedPath), "texture copy waits for Commit");
            True(viewModel.CurrentViewModel is ClusterViewModel { BackgroundTexture: not null },
                "staged Cluster texture previews immediately");
            var stagedTexture = ((ClusterViewModel)viewModel.CurrentViewModel!).BackgroundTexture;
            True(viewModel.CloneRow(cluster, new CloneContentRequest(100, "Cluster99", 0, "Shared Background Cluster", false)),
                "Cluster with the same Background value is cloned");
            True(viewModel.CurrentViewModel is ClusterViewModel sharedView &&
                 ReferenceEquals(stagedTexture, sharedView.BackgroundTexture),
                "matching Background value reuses the linked module texture");

            FindNode(viewModel, row => row is GalaxySystem system && system.RowId == nebulaSystem.RowId).IsSelected = true;
            var systemView = (SystemViewModel)viewModel.CurrentViewModel!;
            True(systemView.UsesNebulaBackground, "ShowNebula system uses Cluster texture");
            NearlyEqual(2, systemView.BackgroundScale, "nebula background is rendered at 200 percent");
            NotNull(systemView.BackgroundTexture, "nebula background texture resolves");

            True(viewModel.CommitPendingChanges(), "texture metadata commit succeeds");
            True(File.Exists(expectedPath), "texture is copied into module textures folder on Commit");
            var reloaded = new GalaxyMapModuleManifestStore().Load(viewModel.ActiveModule.FolderPath!);
            Equal("textures/Cluster_" + cluster.RowId + "_Cluster01.jpg",
                reloaded.ClusterTextureLinks[cluster.RowId], "manifest stores Cluster texture link");
            NotNull(new GalaxyMapTextureService(FindTextureDirectory()).GetModuleClusterTexture(reloaded, cluster.RowId),
                "committed module texture resolves independently of a Cluster row override");

            const string customPlanetPath = "BIOA_TEXTURE_MODULE_T.CustomPlanet01";
            const string customPlanetName = "CustomPlanet01";
            var materialPlanet = viewModel.Document.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var designer = viewModel.CreatePlanetDesigner(materialPlanet.Key);
            var originalTextureValues = designer.Groups.SelectMany(group => group.Fields)
                .Where(field => field.IsTexture)
                .ToDictionary(field => field.Definition.Id, field => field.Primary.Value);
            True(designer.LinkModuleTexture(
                    new PlanetTextureLinkRequest(
                        customPlanetPath,
                        sourceTexture,
                        PlanetTextureCategory.Continent | PlanetTextureCategory.Normals)),
                "Planet texture is staged with selected material categories");
            foreach (var field in designer.Groups.SelectMany(group => group.Fields).Where(field => field.IsTexture))
            {
                Equal(originalTextureValues[field.Definition.Id], field.Primary.Value,
                    $"linking a Planet texture preserves {field.Definition.Id}");
            }
            var stagedPlanetLink = viewModel.ActiveModule!.PlanetTextureLinks.Single();
            var stagedPlanetPath = Path.Combine(
                viewModel.ActiveModule.FolderPath!,
                stagedPlanetLink.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            True(!File.Exists(stagedPlanetPath), "Planet preview image waits for Commit");

            True(designer.Groups.Single(group => group.Name == "Continent / Landmass").Fields
                    .Where(field => field.IsTexture)
                    .All(field => field.TextureOptions.Contains(customPlanetName)),
                "Continent category exposes the linked Planet texture by object name");
            True(designer.Groups.Single(group => group.Name == "Normals").Fields
                    .Where(field => field.IsTexture)
                    .All(field => field.TextureOptions.Contains(customPlanetName)),
                "Normals category exposes the linked Planet texture by object name");
            True(designer.Groups.Single(group => group.Name == "Ocean").Fields
                    .Where(field => field.IsTexture)
                    .All(field => !field.TextureOptions.Contains(customPlanetName)),
                "unselected Ocean category hides the linked Planet texture");

            designer.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "ContinentMask01").Primary.Value = customPlanetName;
            True(designer.TryApply(), "linked Planet texture reference applies to the Planet 2DA draft");
            Equal(customPlanetPath,
                viewModel.Document!.PlanetsByRowId[materialPlanet.RowId].ExtraFields["ContinentMask01"],
                "selecting a Planet texture object name writes its full in-memory path");

            const string renamedPlanetPath = "BIOA_TEXTURE_MODULE_T.RenamedPlanet01";
            viewModel.TableViewer.SelectedTable = GalaxyMapTable.Planet;
            viewModel.TableViewer.RefreshIfNeeded(force: true);
            var continentColumn = viewModel.TableViewer.Columns.ToList().FindIndex(column =>
                column.Name.Equals("ContinentMask01", StringComparison.OrdinalIgnoreCase));
            var planetTableRow = viewModel.TableViewer.Rows.Single(row => row.Key == materialPlanet.Key);
            True(viewModel.TableViewer.CommitCellEdit(planetTableRow, continentColumn, renamedPlanetPath).Succeeded,
                "2DA table can rename a linked Planet texture reference");
            var renamedPlanetLink = viewModel.ActiveModule!.PlanetTextureLinks.Single();
            Equal(renamedPlanetPath, renamedPlanetLink.InMemoryPath,
                "2DA rename updates the linked Planet texture reference");
            Equal(stagedPlanetLink.RelativePath, renamedPlanetLink.RelativePath,
                "2DA rename keeps the linked local Planet image path");

            True(viewModel.CommitPendingChanges(), "Planet texture metadata commit succeeds");
            True(File.Exists(stagedPlanetPath), "Planet preview image is copied into the module on Commit");
            var reloadedPlanetModule = new GalaxyMapModuleManifestStore().Load(viewModel.ActiveModule.FolderPath!);
            Equal(renamedPlanetPath, reloadedPlanetModule.PlanetTextureLinks.Single().InMemoryPath,
                "renamed Planet texture relationship survives manifest reload");
        });
    }

    private static void SpectreExpansionModule(string folder)
    {
        var loader = new CsvGalaxyMapLoader();
        var module = new GalaxyMapModule(
            "Spectre Expansion Mod", "SEM", ModuleColor.Purple, folder,
            isReadOnly: true, loadOrder: 1,
            new ModuleIdReservations(
                new RowIdRange(50, 59),
                new RowIdRange(100, 109),
                new RowIdRange(8000, 8068),
                null,
                new RowIdRange(50, 62)));
        var workspace = new GalaxyMapWorkspace(loader.LoadBuiltInLayer(), [loader.LoadPartFolder(folder, module)]);
        var document = workspace.EffectiveDocument;

        Equal(27, document.Clusters.Count, "SEM effective Cluster count");
        Equal(53, document.Systems.Count, "SEM effective System count");
        Equal(302, document.Planets.Count, "SEM effective Planet count");
        Equal(7, document.PlotPlanets.Count, "SEM effective PlotPlanet count");
        Equal(106, document.Maps.Count, "SEM effective Map count");
        Equal(30, document.Relays.Count, "SEM effective Relay count");
        Equal("SEM", document.Relays.Single(row => row.RowId == 1).Origin!.ModuleTag,
            "SEM Relay row 1 overrides BASEGAME");
        Equal(500000, document.Relays.Single(row => row.RowId == 1).StartClusterEncoded,
            "SEM redirects one endpoint to Arcturus Stream");
        Equal(30000, document.Relays.Single(row => row.RowId == 1).EndClusterEncoded,
            "SEM keeps the Local Cluster endpoint");
        Equal(2, workspace.GetOverrideChain(new GalaxyMapRowKey(GalaxyMapTable.Relay, 1)).Count,
            "Relay redirect is a same-ID override");

        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        True(diagnostics.Any(item => item.Code == "PLOT-CODE-MISMATCH" && item.RowId == 8000),
            "SEM PlotPlanet mismatch is detected");
        True(diagnostics.Any(item => item.Code == "TYPE-PLANET-LEVEL-MISSING" && item.RowId == 8000),
            "SEM blank PlanetLevelType is detected");
        True(diagnostics.Any(item => item.Code == "ACTIVEWORLD-MISMATCH"),
            "SEM ActiveWorld inconsistencies are detected");
        True(diagnostics.Any(item => item.Code == "RELAY-DUPLICATE-PAIR"),
            "SEM duplicate Relay pair is detected");
    }

    private static void RealExports(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "GalaxyMap_*.csv").ToDictionary(
            path => Path.GetFileName(path), SnapshotFile, StringComparer.OrdinalIgnoreCase);

        var document = new CsvGalaxyMapLoader().LoadFolder(folder);
        Equal(17, document.Clusters.Count, "real Cluster count");
        Equal(43, document.Systems.Count, "real System count");
        Equal(233, document.Planets.Count, "real Planet count");
        Equal(6, document.PlotPlanets.Count, "real PlotPlanet count");
        Equal(106, document.Maps.Count, "real Map count");
        Equal(17, document.Relays.Count, "real Relay count");
        Equal(16, document.Relays.Count(relay => relay.IsResolved), "real resolved Relay count");

        SequenceEqual([1, 3, 6], document.Clusters.Take(3).Select(cluster => cluster.RowId), "real sparse Cluster IDs");
        Equal(11, document.Clusters[0].ExtraFields.Count, "real Cluster extra column count");
        Equal(10, document.Systems[0].ExtraFields.Count, "real System extra column count");
        Equal(111, document.Planets[0].ExtraFields.Count, "real Planet extra column count");
        Equal(6, document.PlotPlanets[0].ExtraFields.Count, "real PlotPlanet extra column count");

        var localCluster = document.ClustersByRowId[3];
        Equal(2, localCluster.Systems.Count, "Local Cluster system count");
        Equal(11, localCluster.Systems.Sum(system => system.Planets.Count), "Local Cluster object count");

        var sol = document.SystemsByRowId[4];
        Equal(11, sol.Planets.Count, "Sol object count");
        Equal(10, sol.Planets.Count(planet => planet.OrbitRing != 0), "Sol orbit ring count");

        var horseHead = document.ClustersByRowId[7];
        Equal(5, document.GetRelaysForCluster(horseHead).Count, "Horse Head incident Relay rows");
        Equal(4, document.GetRelaysForCluster(horseHead).Count(relay => relay.IsResolved),
            "Horse Head visible Relay connections");

        Equal("Cluster03.jpg",
            GalaxyMapTextureService.ResolveClusterAssetName(document.ClustersByRowId[1].Background)!,
            "Serpent uses its CSV-linked Cluster03 background");
        True(document.Clusters.All(cluster =>
                GalaxyMapTextureService.ResolveClusterAssetName(cluster.Background) is not null),
            "every real Cluster background reference resolves to an asset name");

        var citadel = document.PlanetsByRowId[1];
        NotNull(citadel.PlotPlanet, "Citadel PlotPlanet");
        Equal(10101, citadel.PlotPlanet!.Code, "Citadel PlotPlanet code");
        NotNull(citadel.LinkedMap, "Citadel Map");
        Equal("BIOA_STA00", citadel.LinkedMap!.MapName, "Citadel map name");
        Equal("start_NOR10_03", citadel.LinkedMap.StartPoint, "Citadel start point");

        var inspector = new PropertyInspectorViewModel();
        inspector.Inspect(citadel);
        var otherColumns = inspector.Sections
            .Where(section => section.Title is "Visibility and usability" or "Destination / unused internals" or
                "Legacy event routing" or "Advanced Planet fields")
            .SelectMany(section => section.Fields).ToArray();
        True(inspector.Sections.All(section => section.Title != "Planet appearance"),
            "real appearance columns are reserved for the Planet Designer");
        Equal(94, PlanetAppearanceSchema.Columns.Count, "real appearance schema column count");
        Equal(17, otherColumns.Length, "real nonappearance extra column count");

        var ilos = document.PlanetsByRowId[86];
        NotNull(ilos.PlotPlanet, "Ilos PlotPlanet");
        True(ilos.LinkedMap is null, "Ilos has no Map");

        foreach (var (fileName, before) in files)
        {
            var after = SnapshotFile(Path.Combine(folder, fileName));
            Equal(before, after, $"source file remains unchanged: {fileName}");
        }
    }

    private static void WpfViewsComposeAfterLoad()
    {
        WithFixture(folder =>
        {
            var application = new App();
            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var globalHighlight = (SolidColorBrush)application.FindResource(SystemColors.HighlightBrushKey);
            True(globalHighlight.Color != Colors.White, "global selection highlight follows dark theme");
            var comboItemStyle = (Style)application.FindResource(typeof(ComboBoxItem));
            var comboBackground = (SolidColorBrush)comboItemStyle.Setters.OfType<Setter>()
                .Single(setter => setter.Property == Control.BackgroundProperty).Value;
            True(comboBackground.Color != Colors.White, "ComboBox dropdown items use dark background");
            var combo = new ComboBox { Width = 200, Height = 30, ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
            combo.Style = (Style)application.FindResource(typeof(ComboBox));
            Compose(combo, application.Dispatcher);
            combo.ApplyTemplate();
            var dropToggle = FindVisualDescendants<ToggleButton>(combo).Single();
            True(dropToggle.ActualWidth > 190, "entire ComboBox face opens the dropdown");
            dropToggle.ApplyTemplate();
            var arrowHover = FindVisualDescendants<Border>(dropToggle).Single(border => border.Name == "ArrowHover");
            Equal(28d, arrowHover.ActualWidth, "collapsed ComboBox hover paint is confined to the arrow area");
            var comboHoverTrigger = dropToggle.Template.Triggers.OfType<Trigger>()
                .Single(trigger => trigger.Property == UIElement.IsMouseOverProperty);
            True(comboHoverTrigger.Setters.OfType<Setter>().All(setter => setter.TargetName == "ArrowHover"),
                "collapsed ComboBox hover cannot cover its selected label");
            var verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical };
            verticalScrollBar.Style = (Style)application.FindResource(typeof(ScrollBar));
            True(double.IsNaN(verticalScrollBar.Height), "vertical ScrollBar height remains unconstrained");
            Equal(12d, verticalScrollBar.Width, "vertical ScrollBar receives only a fixed width");
            var menuSeparatorStyle = (Style)application.FindResource(MenuItem.SeparatorStyleKey);
            Equal(new Thickness(0, 2, 0, 2), (Thickness)menuSeparatorStyle.Setters.OfType<Setter>()
                .Single(setter => setter.Property == FrameworkElement.MarginProperty).Value,
                "menu separator spans the context menu width");
            Exception? dispatcherFailure = null;
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                dispatcherFailure = eventArgs.Exception;
                eventArgs.Handled = true;
            };

            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(folder, "workspace.json")),
                (_, modules) => modules.Single(module => !module.IsReadOnly));
            True(viewModel.LoadFolder(folder), "fixture loads into MainViewModel");
            var window = new MainWindow
            {
                DataContext = viewModel,
                Width = 2048,
                Height = 1152
            };

            Compose(window, application.Dispatcher, 2048, 1152);
            var commitButton = (Button)window.FindName("CommitButton");
            True(commitButton.Parent is StackPanel commandGroup &&
                 commandGroup.Children.IndexOf(commitButton) == commandGroup.Children.Count - 1,
                "Commit sits directly after the left-side undo, redo and discard controls");
            viewModel.ToggleTableViewCommand.Execute(null);
            Compose(window, application.Dispatcher, 2048, 1152);
            var tableGrid = (DataGrid)window.FindName("TableGrid");
            Equal(Visibility.Visible, tableGrid.Visibility,
                "2DA mode replaces the galaxy panels with the merged table surface");
            Equal(3, viewModel.TableViewer.RowCount,
                "read-only reference folders project their live Cluster rows into 2DA mode");
            Equal(viewModel.TableViewer.ColumnCount, tableGrid.Columns.Count,
                "dynamic DataGrid columns follow the projected CSV schema");
            True(tableGrid.EnableColumnVirtualization,
                "wide 2DA tables virtualize off-screen columns");
            SequenceEqual(
                viewModel.TableViewer.Columns.Select(column => column.Name),
                tableGrid.Columns.Select(column => column.Header?.ToString() ?? string.Empty),
                "maximised Cluster tables preserve every projected column in canonical order");
            True(tableGrid.Columns.All(column =>
                    column.Visibility == Visibility.Visible &&
                    column.Width.IsAbsolute &&
                    column.Width.Value > 0),
                "maximised Cluster columns retain visible fixed widths");
            SequenceEqual(
                ["Cluster", "Relay", "System", "Planet", "PlotPlanet", "Map"],
                viewModel.TableViewer.Tabs.Select(tab => tab.Label),
                "2DA tabs follow the requested table order");
            True(!viewModel.TableViewer.IsEditingAvailable,
                "standalone reference folders remain read-only in the editable grid");
            viewModel.ToggleTableViewCommand.Execute(null);
            Compose(window, application.Dispatcher);

            var editableViewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(folder, "editable-workspace.json")),
                (_, modules) => modules.Single(module => !module.IsReadOnly));
            True(editableViewModel.LoadBuiltIn(), "BASEGAME loads for editable table composition");
            var tableModuleCreated = editableViewModel.CreateModule(
                    folder,
                    "Table Grid Edit",
                    "TABLE_GRID_EDIT",
                    ModuleColor.Cyan,
                    TestReservations());
            True(tableModuleCreated,
                $"writable module is available to the table grid ({editableViewModel.ErrorMessage} {editableViewModel.StatusMessage})");
            var editableWindow = new MainWindow { DataContext = editableViewModel };
            Compose(editableWindow, application.Dispatcher);
            editableViewModel.ToggleTableViewCommand.Execute(null);
            Compose(editableWindow, application.Dispatcher);
            var editableTableGrid = (DataGrid)editableWindow.FindName("TableGrid");
            True(editableViewModel.TableViewer.IsEditingAvailable,
                "table editing enables when the workspace contains a writable module");
            var editableColumn = editableTableGrid.Columns
                .OfType<DataGridTextColumn>()
                .Single(column => string.Equals(column.Header?.ToString(), "X", StringComparison.Ordinal));
            True(!editableColumn.IsReadOnly, "ordinary projected CSV columns remain editable");
            Equal(BindingMode.TwoWay, ((Binding)editableColumn.Binding).Mode,
                "editable table columns use a two-way presentation buffer instead of WPF's read-only OneWay mode");
            var selectedTrigger = editableColumn.CellStyle!.Triggers
                .OfType<Trigger>()
                .Single(trigger => trigger.Property == DataGridCell.IsSelectedProperty);
            Equal(Brushes.White, selectedTrigger.Setters.OfType<Setter>()
                    .Single(setter => setter.Property == DataGridCell.BorderBrushProperty).Value,
                "the active table cell receives a white outline distinct from staged cells");
            True(editableColumn.CellStyle.Triggers[editableColumn.CellStyle.Triggers.Count - 1] is DataTrigger errorTrigger &&
                 ((Binding)errorTrigger.Binding).Path.Path.EndsWith(".HasError", StringComparison.Ordinal),
                "table validation styling takes precedence while an invalid cell remains selected");
            True(((Binding)editableColumn.Binding).Path.Path.EndsWith(".EditValue", StringComparison.Ordinal),
                "the editable grid binding targets the mutable cell edit buffer");
            var editableColumnIndex = editableColumn.DisplayIndex;
            var editableRow = editableViewModel.TableViewer.Rows[0];
            var editedX = editableRow.Cells[editableColumnIndex].EditValue == "0.73" ? "0.27" : "0.73";
            var tableEdit = editableViewModel.TableViewer.CommitCellEdit(editableRow, editableColumnIndex, editedX);
            True(tableEdit.Succeeded,
                "the WPF table presentation routes an editable cell through the shared mutation workflow");

            editableWindow.Show();
            application.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            editableViewModel.TableViewer.SelectedTable = GalaxyMapTable.Planet;
            Compose(editableWindow, application.Dispatcher);
            var realisedPlanetCells = FindVisualDescendants<DataGridCell>(editableTableGrid).ToArray();
            True(realisedPlanetCells.Length > 0 && realisedPlanetCells.Length <
                 editableViewModel.TableViewer.RowCount * editableViewModel.TableViewer.ColumnCount,
                "the shown Planet sheet realises only its visible cell viewport");
            var tableScrollViewer = FindVisualDescendants<ScrollViewer>(editableTableGrid).First();
            tableScrollViewer.ScrollToRightEnd();
            application.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var lastPlanetColumn = editableTableGrid.Columns.Count - 1;
            True(FindVisualDescendants<DataGridCell>(editableTableGrid)
                    .Any(cell => cell.Column.DisplayIndex == lastPlanetColumn),
                "column virtualization realises the final Planet column after horizontal scrolling");
            editableWindow.DataContext = null;
            editableWindow.Close();

            var creationWindow = new PlanetCreationWindow();
            Compose(creationWindow, application.Dispatcher);
            var planetTemplateWindow = new PlanetTemplateWindow();
            Compose(planetTemplateWindow, application.Dispatcher);
            Equal(330d, planetTemplateWindow.Height,
                "Planet template dialog reserves enough vertical space for its description");
            Equal(80d, ((TextBox)planetTemplateWindow.FindName("DescriptionBox")).Height,
                "Planet template description editor reserves its full intended height");
            var templateBox = (ComboBox)creationWindow.FindName("TemplateBox");
            var landableContainer = (Border)creationWindow.FindName("LandableContainer");
            templateBox.SelectedItem = templateBox.Items.Cast<PlanetTemplateOption>()
                .Single(option => option.Value == PlanetCreationTemplate.AsteroidBelt);
            Compose(creationWindow, application.Dispatcher);
            Equal(Visibility.Collapsed, landableContainer.Visibility,
                "asteroid-belt template hides the landable-destination workflow");
            templateBox.SelectedItem = templateBox.Items.Cast<PlanetTemplateOption>()
                .Single(option => option.Value == PlanetCreationTemplate.GenericPlanet);
            Compose(creationWindow, application.Dispatcher);
            Equal(Visibility.Visible, landableContainer.Visibility,
                "landable-destination workflow returns for compatible templates");
            var landableWindow = new LandableDestinationWindow("BIOA_TEST", "Start", "Land", null, true);
            Compose(landableWindow, application.Dispatcher);
            var packedColorWindow = new ColorPickerWindow("1193046");
            Compose(packedColorWindow, application.Dispatcher);
            var packedPreview = (SolidColorBrush)((Border)packedColorWindow.FindName("Preview")).Background;
            Equal(Color.FromArgb(0xFF, 0x12, 0x34, 0x56), packedPreview.Color,
                "full packed-colour picker previews zero-alpha RGB values opaquely");
            var confirmationWindow = new ConfirmationWindow(
                "Confirm staged change", "Stage this change?", "Confirm", "Cancel");
            Compose(confirmationWindow, application.Dispatcher);
            Equal(((SolidColorBrush)application.FindResource("AppBackgroundBrush")).Color,
                ((SolidColorBrush)confirmationWindow.Background).Color,
                "confirmation dialogs use the application dark background");
            var shaderNameWindow = new PlanetShaderNameWindow(new PlanetShaderNameRequest(
                "Test Planet",
                123,
                new GalaxyMapModule(
                    "Test Module", "TEST_MODULE", ModuleColor.Cyan, null,
                    isReadOnly: false, loadOrder: 1, TestReservations()),
                "TEST_MODULE_Planet123",
                name => string.IsNullOrWhiteSpace(name) ? "Enter a Shader name." : null));
            Compose(shaderNameWindow, application.Dispatcher);
            Equal("TEST_MODULE_Planet123", shaderNameWindow.ShaderName,
                "Planet Shader prompt composes with its unique suggested name");
            var moveWindow = new MoveDestinationWindow(
                viewModel.Document!.Systems.First(),
                [new MoveDestinationOption(99, "Test Cluster", "Test Cluster • row 99", "System01", "System02")]);
            Compose(moveWindow, application.Dispatcher);
            Equal(1, ((ComboBox)moveWindow.FindName("DestinationBox")).Items.Count,
                "move destination dialog composes with collision preview data");
            True(viewModel.CurrentViewModel is GalaxyViewModel, "Galaxy view is active after load");
            var loadedGalaxy = (GalaxyViewModel)viewModel.CurrentViewModel!;
            NotNull(loadedGalaxy.BackgroundTexture, "packaged galaxy texture is available to the Galaxy view");
            Equal(PixelFormats.Bgr32, ((System.Windows.Media.Imaging.BitmapSource)loadedGalaxy.BackgroundTexture!).Format,
                "packaged galaxy texture ignores alpha");
            var mapSquare = (FrameworkElement)window.FindName("MapSquare");
            NearlyEqual(mapSquare.ActualWidth, mapSquare.ActualHeight, "MainWindow map viewport is square");

            var hierarchyTree = (TreeView)window.FindName("HierarchyTree");
            var activeSelection = (SolidColorBrush)hierarchyTree.FindResource(SystemColors.HighlightBrushKey);
            var inactiveSelection = (SolidColorBrush)hierarchyTree.FindResource(SystemColors.InactiveSelectionHighlightBrushKey);
            Equal(activeSelection.Color, inactiveSelection.Color,
                "hierarchy selection uses the same blue when the TreeView loses focus");

            var coordinateGrid = (CoordinateGridLayer)window.FindName("CoordinateGrid");
            Equal(Visibility.Collapsed, coordinateGrid.Visibility, "coordinate grid starts hidden");
            viewModel.ToggleCoordinateGridCommand.Execute(null);
            Compose(window, application.Dispatcher);
            Equal(Visibility.Visible, coordinateGrid.Visibility, "coordinate grid toggles visible");

            viewModel.ToggleDiagnosticsCommand.Execute(null);
            Compose(window, application.Dispatcher);
            True(viewModel.IsDiagnosticsPanelOpen, "warning details panel opens");
            var diagnosticsList = (ItemsControl)window.FindName("DiagnosticsList");
            Equal(viewModel.DiagnosticCount, diagnosticsList.Items.Count,
                "validation details list contains every diagnostic");
            viewModel.ToggleDiagnosticsCommand.Execute(null);

            var galaxy = (GalaxyViewModel)viewModel.CurrentViewModel!;
            var firstClusterNode = viewModel.HierarchyRoots[0].Children[0];
            galaxy.EnterClusterCommand.Execute(firstClusterNode);
            Compose(window, application.Dispatcher);
            True(viewModel.CurrentViewModel is ClusterViewModel, "Cluster view composes");

            var clusterView = (ClusterViewModel)viewModel.CurrentViewModel!;
            NotNull(clusterView.BackgroundTexture, "Cluster background resolves from its CSV value");
            var clusterControl = new ClusterView { DataContext = clusterView };
            Compose(clusterControl, application.Dispatcher);
            True(FindVisualDescendants<TextBlock>(clusterControl).Any(text =>
                    text.DataContext is HierarchyNodeViewModel { Item: GalaxySystem } &&
                    Math.Abs(text.FontSize - 14) < 0.001),
                "System map labels use the larger 14-point size");
            var originalBackground = clusterView.BackgroundTexture;
            clusterView.Cluster.Background = "BIOA_GalaxyMap_T.Cluster03";
            True(!ReferenceEquals(originalBackground, clusterView.BackgroundTexture),
                "editing Background refreshes the visible texture in memory");

            var actionStyle = (Style)window.FindResource("InspectorActionButtonStyle");
            var checkboxStyle = (Style)window.FindResource("InspectorCheckboxStyle");
            Equal(26d, (double)checkboxStyle.Setters.OfType<Setter>()
                    .Single(setter => setter.Property == FrameworkElement.MinHeightProperty).Value,
                "inspector checkboxes reserve the same row height as text editors");
            var alignmentProbe = new Button
            {
                Style = actionStyle,
                Content = "Relay action",
                Width = 240,
                Height = 40
            };
            alignmentProbe.Measure(new Size(240, 40));
            alignmentProbe.Arrange(new Rect(0, 0, 240, 40));
            alignmentProbe.ApplyTemplate();
            var presenter = FindVisualDescendants<ContentPresenter>(alignmentProbe).Single();
            Equal(HorizontalAlignment.Stretch, presenter.HorizontalAlignment,
                "Relay action template honours its left-aligned stretched content");

            var cluster = (ClusterViewModel)viewModel.CurrentViewModel!;
            cluster.EnterSystemCommand.Execute(firstClusterNode.Children[0]);
            Compose(window, application.Dispatcher);
            True(viewModel.CurrentViewModel is SystemViewModel, "System view composes");
            NotNull(((SystemViewModel)viewModel.CurrentViewModel!).BackgroundTexture,
                "System view receives the packaged stars01 background");
            var nebulaSystemId = viewModel.Document!.Systems.First(system => system.ShowNebula == 1).RowId;
            var nebulaSystemNode = FindNode(viewModel, row => row is GalaxySystem system && system.RowId == nebulaSystemId);
            nebulaSystemNode.IsSelected = true;
            var systemControl = new SystemView { DataContext = viewModel.CurrentViewModel };
            Compose(systemControl, application.Dispatcher);
            var systemSun = (Grid)systemControl.FindName("SystemSun");
            Equal(Visibility.Collapsed, systemSun.Visibility, "ShowNebula hides the System-view sun");
            True(FindVisualDescendants<TextBlock>(systemControl).Any(text =>
                    text.DataContext is HierarchyNodeViewModel { Item: Planet } &&
                    Math.Abs(text.FontSize - 14) < 0.001),
                "Planet map labels use the larger 14-point size");
            True(FindVisualDescendants<ContentPresenter>(systemControl).Any(presenter =>
                    presenter.DataContext is HierarchyNodeViewModel { Item: Planet } &&
                    Math.Abs(NormalizedCanvas.GetAnchorY(presenter) - 22) < 0.001),
                "planet symbols, rather than their label stacks, are anchored to orbit coordinates");
            NearlyEqual(mapSquare.ActualWidth, mapSquare.ActualHeight, "map remains square after navigation");
            Equal(Visibility.Visible, coordinateGrid.Visibility, "coordinate grid persists across navigation");

            var builtInViewModel = new MainViewModel(new CsvGalaxyMapLoader());
            True(builtInViewModel.LoadBuiltIn(), "embedded BASEGAME loads for live inspector composition");
            var builtInWindow = new MainWindow { DataContext = builtInViewModel };
            Compose(builtInWindow, application.Dispatcher);
            var builtInClusterNode = builtInViewModel.HierarchyRoots.Single().Children.First();
            builtInClusterNode.IsSelected = true;
            Compose(builtInWindow, application.Dispatcher);
            var builtInSystemNode = builtInClusterNode.Children.First();
            builtInSystemNode.IsSelected = true;
            Compose(builtInWindow, application.Dispatcher);
            var builtInPlanetNode = builtInSystemNode.Children.First();
            builtInPlanetNode.IsSelected = true;
            Compose(builtInWindow, application.Dispatcher);
            Equal(146d, PropertyInspectorViewModel.LabelColumnWidth.Value,
                "inspector property-name lane is widened by approximately twenty percent");

            var materialPlanet = builtInViewModel.Document!.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var designerWindow = new PlanetDesignerWindow(builtInViewModel.CreatePlanetDesigner(materialPlanet.Key));
            designerWindow.PrepareForFirstShow();
            var preparedDesignerContent = (FrameworkElement)designerWindow.Content;
            True(preparedDesignerContent.ActualWidth > 0 && preparedDesignerContent.ActualHeight > 0,
                "Planet Designer completes its expensive initial layout before becoming visible");
            True(PresentationSource.FromVisual(designerWindow) is null,
                "Planet Designer pre-layout does not expose an unpainted native window");
            designerWindow.Show();
            application.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            True(designerWindow.IsVisible,
                "Planet Designer survives its live WPF template and renderer lifecycle");
            var designerHandle = new System.Windows.Interop.WindowInteropHelper(designerWindow).Handle;
            var designerSource = System.Windows.Interop.HwndSource.FromHwnd(designerHandle);
            NotNull(designerSource?.CompositionTarget,
                "Planet Designer exposes its native composition target");
            Equal(Color.FromRgb(0x0A, 0x10, 0x18), designerSource!.CompositionTarget.BackgroundColor,
                "Planet Designer paints its native first frame navy instead of white");
            True(PumpDispatcherUntil(
                    application.Dispatcher,
                    () => designerWindow.ViewModel.PreviewImage is not null,
                    TimeSpan.FromSeconds(8)),
                "Planet Designer completes its asynchronous first live preview");
            var liveColorDialog = new ColorPickerWindow("-1") { Owner = designerWindow };
            liveColorDialog.Show();
            liveColorDialog.Activate();
            var framesBeforeColorPreview = designerWindow.Diagnostics.Snapshot().FramesPresented;
            var packedDesignerField = designerWindow.ViewModel.Groups.SelectMany(group => group.Fields)
                .First(field => field.IsPackedColor);
            packedDesignerField.Primary.Value = packedDesignerField.Primary.Value == "-1" ? "-16777216" : "-1";
            True(PumpDispatcherUntil(
                    application.Dispatcher,
                    () => designerWindow.Diagnostics.Snapshot().FramesPresented > framesBeforeColorPreview,
                    TimeSpan.FromSeconds(3)),
                "an owned colour picker leaves the Planet Designer live preview running");
            liveColorDialog.Close();
            var textureCombo = FindVisualDescendants<ComboBox>(designerWindow)
                .First(comboBox => comboBox.DataContext is PlanetAppearanceFieldViewModel { IsTexture: true });
            textureCombo.ApplyTemplate();
            var editableTextureText = (TextBox)textureCombo.Template.FindName("PART_EditableTextBox", textureCombo);
            Equal(((PlanetAppearanceFieldViewModel)textureCombo.DataContext).Primary.Value,
                editableTextureText.Text,
                "editable texture dropdown renders its current texture reference");
            var saveCurrentButton = FindVisualDescendants<Button>(designerWindow)
                .Single(button => Equals(button.Content, "Save current..."));
            True(saveCurrentButton.ActualHeight >= 30,
                "Save current template button has enough vertical space for its content");
            var presetTree = (TreeView)designerWindow.FindName("PresetTree");
            True(VirtualizingPanel.GetIsVirtualizing(presetTree) && ScrollViewer.GetCanContentScroll(presetTree),
                "appearance-base tree enables item-container virtualization through its scroll presenter");
            Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(presetTree),
                "appearance-base tree recycles off-screen item containers");
            Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(presetTree),
                "appearance-base tree retains smooth pixel scrolling while virtualized");
            var moduleTreeItems = FindVisualDescendants<TreeViewItem>(presetTree)
                .Where(item => item.DataContext is PlanetPresetModuleGroup)
                .ToArray();
            True(moduleTreeItems.Length > 0 && moduleTreeItems.All(item => !item.IsExpanded),
                "appearance-base module nodes are collapsed on first display");
            foreach (var moduleTreeItem in moduleTreeItems)
            {
                moduleTreeItem.IsExpanded = true;
            }
            Compose(designerWindow, application.Dispatcher);
            var presetScrollViewer = FindVisualDescendants<ScrollViewer>(presetTree).First();
            True(presetScrollViewer.ScrollableHeight > 0 &&
                 presetScrollViewer.ExtentHeight > presetScrollViewer.ViewportHeight,
                "appearance-base tree preserves a scrollable expanded extent while virtualized");
            var scrollBarCorner = FindVisualDescendants<Border>(presetScrollViewer)
                .Single(border => border.Name == "ScrollBarCorner");
            var scrollBarCornerBackground = scrollBarCorner.Background as SolidColorBrush;
            NotNull(scrollBarCornerBackground,
                "appearance-base scroll viewer explicitly paints its scrollbar junction");
            Equal(Color.FromRgb(0x0B, 0x13, 0x1B), scrollBarCornerBackground!.Color,
                "appearance-base scrollbar junction follows the dark scrollbar track");
            var moduleIcon = FindVisualDescendants<TextBlock>(presetTree)
                .First(text => text.DataContext is PlanetPresetModuleGroup && text.Text == "◆");
            var moduleGroup = (PlanetPresetModuleGroup)moduleIcon.DataContext;
            var expectedModuleBrush = (SolidColorBrush)new ModuleColorBrushConverter().Convert(
                moduleGroup.ModuleColor, typeof(Brush), null!, CultureInfo.InvariantCulture);
            Equal(expectedModuleBrush.Color, ((SolidColorBrush)moduleIcon.Foreground).Color,
                "appearance-base module icon uses the main-tree module colour");
            True(FindVisualDescendants<TextBlock>(presetTree)
                    .Any(text => text.DataContext is PlanetPresetClusterGroup && text.Text == "✦"),
                "appearance-base tree uses the main-tree cluster icon");
            True(FindVisualDescendants<TextBlock>(presetTree)
                    .Any(text => text.DataContext is PlanetPresetSystemGroup && text.Text == "⊙"),
                "appearance-base tree uses the main-tree system icon");
            var planetIcon = FindVisualDescendants<TextBlock>(presetTree)
                .First(text => text.DataContext is PlanetAppearancePreset && text.FontFamily.Source == "Segoe UI Symbol");
            var planetPreset = (PlanetAppearancePreset)planetIcon.DataContext;
            var planetTreeItem = FindVisualDescendants<TreeViewItem>(presetTree)
                .First(item => ReferenceEquals(item.DataContext, planetPreset));
            planetTreeItem.IsSelected = true;
            Compose(designerWindow, application.Dispatcher);
            True(ReferenceEquals(planetPreset, presetTree.SelectedItem),
                "appearance-base selection remains stable in the virtualized tree");
            presetScrollViewer.ScrollToEnd();
            Compose(designerWindow, application.Dispatcher);
            True(ReferenceEquals(planetPreset, presetTree.SelectedItem),
                "appearance-base selection survives recycling after scrolling off-screen");
            presetScrollViewer.ScrollToHome();
            Equal(new PlanetGlyphConverter().Convert(
                    planetPreset.VisualKind, typeof(string), null!, CultureInfo.InvariantCulture),
                planetIcon.Text,
                "appearance-base Planet icon follows the main-tree visual kind");
            True(FindVisualDescendants<Expander>(designerWindow).Count() < designerWindow.ViewModel.Groups.Count,
                "off-screen material groups remain virtualized during the first window paint");
            designerWindow.Close();
            True(designerWindow.ViewModel.Groups.SelectMany(group => group.Fields)
                    .Any(item => item.Editor == PlanetAppearanceEditorKind.Shader),
                "Planet Designer composes its dedicated Shader editor");
            True(designerWindow.ViewModel.PresetModules.Count > 0,
                "Planet Designer composes the workspace preset hierarchy");
            SequenceEqual(
                ["Identity", "Continent / Landmass", "Normals", "Ocean", "Beach / Silt", "City Emissive", "Atmosphere / Horizon", "Corona", "Lights"],
                designerWindow.ViewModel.Groups.Select(group => group.Name),
                "Planet Designer retains the standalone's curated material sections");
            True(designerWindow.ViewModel.Groups.All(group => group.ExpandedByDefault),
                "every material-parameter section starts expanded");
            var landmassMixer = designerWindow.ViewModel.Groups
                .Single(group => group.Name == "Continent / Landmass")
                .Fields.Single(field => field.Definition.Id == "Landmass_Mixer");
            SequenceEqual(["Beach transition", "Land threshold", "Silt transition"],
                landmassMixer.VisibleComponents.Select(component => component.Label),
                "landmass vector is presented as the standalone's three descriptive mixer sliders");
            True(designerWindow.ViewModel.PerformanceMode && Math.Abs(designerWindow.ViewModel.CloudSpeed - 1) < 0.001,
                "live preview defaults to 60fps mode and normal cloud speed");
            True(FindVisualDescendants<CheckBox>(designerWindow)
                    .Any(checkBox => Equals(checkBox.Content, "60fps Mode")),
                "live preview labels its frame-rate toggle as 60fps Mode");
            var copiedPreset = designerWindow.ViewModel.PresetModules
                .SelectMany(module => module.Clusters)
                .SelectMany(cluster => cluster.Systems)
                .SelectMany(system => system.Planets)
                .First(preset => preset.PlanetRowId != materialPlanet.RowId);
            var shaderBeforePaste = designerWindow.ViewModel.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "Shader").Primary.Value;
            designerWindow.ViewModel.CopyAppearance(copiedPreset);
            True(designerWindow.ViewModel.PasteAppearance(),
                "appearance tree clipboard can paste into the current designer");
            Equal(shaderBeforePaste,
                designerWindow.ViewModel.Groups.SelectMany(group => group.Fields)
                    .Single(field => field.Definition.Id == "Shader").Primary.Value,
                "appearance clipboard preserves the target Planet's Shader property");
            var textureField = designerWindow.ViewModel.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "ContinentMask01");
            textureField.Primary.Value = "TestPackage.CustomContinentTexture";
            textureField.Refresh();
            True(textureField.TextureOptions.Contains("TestPackage.CustomContinentTexture"),
                "texture dropdown keeps a newly loaded module texture available after refreshing the appearance base");

            if (dispatcherFailure is not null)
            {
                throw new InvalidOperationException($"WPF dispatcher failure: {dispatcherFailure.Message}", dispatcherFailure);
            }
        });
    }

    private static void Compose(
        FrameworkElement element,
        Dispatcher dispatcher,
        double width = 1440,
        double height = 860)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static bool PumpDispatcherUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        if (condition())
        {
            return true;
        }

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }

    private static HierarchyNodeViewModel FindNode(MainViewModel viewModel, Func<GalaxyMapRow, bool> predicate)
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

        return Flatten(viewModel.HierarchyRoots)
            .Single(node => node.Model is { } model && predicate(model));
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string FindTextureDirectory()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "LE1GalaxyMapEditor", "resources", "textures");
                if (File.Exists(Path.Combine(candidate, GalaxyMapTextureService.GalaxyAssetName)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the project texture resources.");
    }

    private static void WithFixture(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LE1GalaxyMapEditor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            CreateFixture(folder);
            test(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LE1GalaxyMapEditor.AuthoringTests", Guid.NewGuid().ToString("N"));
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

    private static GalaxyMapModule CreateTestModule(string folder, string tag, ModuleColor color)
        => new(
            tag.Replace('_', ' '), tag, color, folder,
            isReadOnly: false, loadOrder: 1, TestReservations());

    private static ModuleIdReservations TestReservations()
        => new(
            new RowIdRange(100, 199),
            new RowIdRange(1000, 1099),
            new RowIdRange(10000, 10099),
            new RowIdRange(1000, 1099),
            new RowIdRange(1000, 1099));

    private static ModuleIdReservations AlternateReservations()
        => new(
            new RowIdRange(200, 299),
            new RowIdRange(2000, 2099),
            new RowIdRange(20000, 20099),
            new RowIdRange(2000, 2099),
            new RowIdRange(2000, 2099));

    private static void CreateFixture(string folder)
    {
        WriteCsv(folder, "GalaxyMap_Cluster.csv",
            ["", "Label", "X", "Y", "Name", "NameText", "SphereSize", "background", "ExtraCluster"],
            [
                ["20", "Cluster21", "1", "1", "200", "Gemini, Sigma", "4", "BIOA_GalaxyMap_T.Cluster20", ""],
                ["1", "Cluster01", "0", "0", "100", "Serpent Nebula", "4.2", "BIOA_GalaxyMap_T.Cluster03", "kept"],
                ["6", "Cluster07", "0.5", "0.25", "700", "Horse Head Nebula", "4", "BIOA_GalaxyMap_T.Cluster12", "editable"]
            ]);

        WriteCsv(folder, "GalaxyMap_System.csv",
            ["", "Label", "Cluster", "X", "Y", "Name", "NameText", "Scale", "ShowNebula", "ExtraSystem"],
            [
                ["47", "System02", "20", "0.34", "0.69", "470", "Han", "1.1", "0", ""],
                ["1", "System01", "1", "0.5", "0.5", "10", "Widow", "0.1", "1", "x"],
                ["4", "System01", "6", "0.42", "0.56", "40", "Sol", "1", "1", "y"]
            ]);

        WriteCsv(folder, "GalaxyMap_Planet.csv",
            ["", "Label", "System", "X", "Y", "Name", "NameText", "ActiveWorld", "Description", "ButtonLabel",
                "Map", "Scale", "RingColor", "OrbitRing", "SystemLevelType", "PlanetLevelType", "Event", "ImageIndex",
                "ExtraPlanet", "Multiline"],
            [
                ["240", "Planet05", "47", "0.12", "0.28", "2400", "Patatanlis", "210205", "", "", "88", "4.7", "-1", "1", "0", "1", "Patatanlis", "-1", "plain", ""],
                ["1", "Planet01", "1", "0.35", "0.46", "135823", "Citadel", "10101", "", "", "0", "1", "-1", "0", "1", "4", "Land", "8", "quoted, value", "line one\r\nline two"],
                ["9", "Planet06", "4", "0.65", "0.14", "90", "Saturn", "0", "", "", "-1", "1", "-1", "2", "2", "1", "", "", "", ""]
            ]);

        WriteCsv(folder, "GalaxyMap_PlotPlanet.csv",
            ["", "Code", "Name", "NameText", "PlotExtra"],
            [["1", "10101", "135823", "Citadel", "linked"]]);

        WriteCsv(folder, "GalaxyMap_Map.csv",
            ["", "Map", "StartPoint"],
            [
                ["0", "BIOA_STA00", "start_NOR10_03"],
                ["88", "BIOA_TEST88", "start_TEST_00"]
            ]);

        WriteCsv(folder, "GalaxyMap_Relay.csv",
            ["", "StartCluster", "EndCluster"],
            [
                ["0", "10000", "70000"],
                ["1", "70000", "210000"],
                ["2", "70000", "40000"]
            ]);
    }

    private static void WriteCsv(string folder, string fileName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(EscapeCsv)));
        }

        File.WriteAllText(Path.Combine(folder, fileName), builder.ToString(), new UTF8Encoding(true));
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string SnapshotFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return $"{File.GetLastWriteTimeUtc(path):O}|{Convert.ToHexString(SHA256.HashData(bytes))}";
    }

    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Run(string name, Action test)
    {
        Console.WriteLine($"RUN   {name}");
        Console.Out.Flush();
        try
        {
            test();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine($"FAIL  {name}");
            Console.WriteLine($"      {exception.Message}");
        }
    }

    private static void Equal<T>(T expected, T actual, string description) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
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

    private static void NearlyEqual(double expected, double actual, string description)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
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

    private static void Throws<TException>(Action action, Func<string, bool> messagePredicate, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception) when (messagePredicate(exception.Message))
        {
            return;
        }

        throw new InvalidOperationException($"{description}: expected {typeof(TException).Name} with a matching message.");
    }
}
