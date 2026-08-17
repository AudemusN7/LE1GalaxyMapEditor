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

    private static void GuardedPlanetAppearanceRandomizer()
    {
        var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
        var source = baseLayer.Planets.First(planet =>
            PlanetAppearanceCodec.IsAppearanceCapable(planet) &&
            !string.IsNullOrWhiteSpace(planet.ExtraFields.GetValueOrDefault("Shader")));
        var original = PlanetAppearanceCodec.Decode(source);
        var first = PlanetAppearanceRandomizer.Generate(original, baseLayer.Planets, 1701);
        var repeated = PlanetAppearanceRandomizer.Generate(original, baseLayer.Planets, 1701);

        Equal(original.Shader, first.Appearance.Shader,
            "randomisation preserves the target Planet Shader identity");
        Equal(first.DonorName, repeated.DonorName,
            "a randomisation seed selects the same donor appearance");
        SequenceEqual(
            PlanetAppearanceSchema.Columns.Select(column => first.Appearance[column]),
            PlanetAppearanceSchema.Columns.Select(column => repeated.Appearance[column]),
            "a randomisation seed reproduces every generated material value");
        True(PlanetAppearanceCodec.ChangedColumns(original, first.Appearance).Count > 20,
            "randomisation replaces a substantial visual appearance rather than nudging one control");

        var exceedsFormerTenPercentBand = false;
        foreach (var seed in Enumerable.Range(0, 64))
        {
            var randomisation = PlanetAppearanceRandomizer.Generate(original, baseLayer.Planets, seed);
            var generated = randomisation.Appearance;
            var donor = PlanetAppearanceCodec.Decode(
                baseLayer.Planets.Single(planet => planet.RowId == randomisation.DonorRowId));
            Equal(original.Shader, generated.Shader, $"seed {seed} keeps the Shader name");
            foreach (var property in PlanetAppearanceSchema.Properties.Where(property =>
                         property.Editor is PlanetAppearanceEditorKind.Scalar or
                             PlanetAppearanceEditorKind.ColorVector or
                             PlanetAppearanceEditorKind.MixerVector))
            {
                foreach (var column in property.Columns)
                {
                    True(PlanetAppearanceCodec.TryParseFloat(generated[column], out _),
                        $"seed {seed} emits a finite value for {column}");
                }
            }

            double Value(string column) => double.Parse(generated[column], CultureInfo.InvariantCulture);
            double Luminance(string prefix) =>
                0.2126 * Value(prefix + "R") +
                0.7152 * Value(prefix + "G") +
                0.0722 * Value(prefix + "B");
            double MixerSum(string prefix) =>
                "RGB".Sum(component => Math.Max(0, Value(prefix + component)));
            double PackedLuminance(string column)
            {
                var packed = unchecked((uint)long.Parse(generated[column], CultureInfo.InvariantCulture));
                double Linear(uint component)
                {
                    var value = component / 255d;
                    return value <= 0.04045
                        ? value / 12.92
                        : Math.Pow((value + 0.055) / 1.055, 2.4);
                }

                return 0.2126 * Linear((packed >> 16) & 0xff) +
                       0.7152 * Linear((packed >> 8) & 0xff) +
                       0.0722 * Linear(packed & 0xff);
            }

            foreach (var column in new[]
                     {
                         "Bump_Amount", "Atmosphere_Min", "Atmosphere_Tile_U",
                         "Atmosphere_Tile_V", "Emissive_Twinkle_Multiplier",
                         "Normal_Map_Tile", "City_Emissive_Tile",
                         "Horizon_Atmosphere_Falloff"
                     })
            {
                var donorValue = double.Parse(donor[column], CultureInfo.InvariantCulture);
                if (donorValue <= 0)
                {
                    continue;
                }

                var ratio = Value(column) / donorValue;
                True(ratio is >= 0.649 and <= 1.351,
                    $"seed {seed} keeps {column} inside the 35 percent variation budget");
                exceedsFormerTenPercentBand |= ratio is < 0.899 or > 1.101;
            }

            var maskWeight = new[] { "Continent_Mask_Mixer", "Continent_Mask_Mixer02" }
                .Sum(prefix => MixerSum(prefix));
            True(maskWeight > 0, $"seed {seed} retains usable continent-mask weights");
            True(Value("Landmass_MixerG") / maskWeight is >= 0 and <= 0.601,
                $"seed {seed} keeps land coverage inside the guarded ratio");
            True(Value("Landmass_MixerR") / maskWeight is >= 0 and <= 0.851,
                $"seed {seed} keeps beach width inside the guarded ratio");
            True(Value("Landmass_MixerB") / maskWeight is >= 0 and <= 0.551,
                $"seed {seed} keeps silt width inside the guarded ratio");

            True(Luminance("Atmosphere_Color") * MixerSum("Atmosphere_Mixer") <= 45.001,
                $"seed {seed} stays within the atmosphere energy envelope");
            True(Luminance("Horizon_Atmosphere_Color") * Value("Horizon_Atmosphere_Intensity") <= 7.101,
                $"seed {seed} stays within the horizon energy envelope");
            True(Luminance("Corona_Color") * Value("Opacity") <= 3.251,
                $"seed {seed} stays within the corona energy envelope");
            True(Luminance("City_Emissive_Color") * MixerSum("City_Emissive_Mixer") <= 3.251,
                $"seed {seed} stays within the city-emissive energy envelope");
            True(PackedLuminance("SunColor1") * Value("Brightness1") <= 3.201,
                $"seed {seed} stays within the first-light energy envelope");
            True(PackedLuminance("SunColor2") * Value("Brightness2") <= 106.01,
                $"seed {seed} stays within the second-light energy envelope");
            True(new[]
                    {
                        "Beach_Color", "Continent_Color", "Continent_Color_Alt",
                        "Ocean_Color", "Ocean_Color_Alt", "Silt_Color"
                    }.Max(Luminance) <= 1.101,
                $"seed {seed} prevents HDR overbleed from the surface palette");
        }

        var customLinks = new[]
        {
            new PlanetTextureLink(
                "random-continent", "BIOA_RANDOM_T.CustomContinent", "textures/continent.png",
                PlanetTextureCategory.Continent),
            new PlanetTextureLink(
                "random-normal", "BIOA_RANDOM_T.CustomNormal", "textures/normal.png",
                PlanetTextureCategory.Normals),
            new PlanetTextureLink(
                "random-ocean", "BIOA_RANDOM_T.CustomOcean", "textures/ocean.png",
                PlanetTextureCategory.Ocean),
            new PlanetTextureLink(
                "random-city", "BIOA_RANDOM_T.CustomCity", "textures/city.png",
                PlanetTextureCategory.CityEmissive),
            new PlanetTextureLink(
                "random-atmosphere", "BIOA_RANDOM_T.CustomAtmosphere", "textures/atmosphere.png",
                PlanetTextureCategory.Atmosphere)
        };
        var validCustomSlots = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [customLinks[0].InMemoryPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "ContinentMask01", "ContinentMask02", "Continent_Texture" },
            [customLinks[1].InMemoryPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Normal_Map" },
            [customLinks[2].InMemoryPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Ocean_Texture" },
            [customLinks[3].InMemoryPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "City_Emissive" },
            [customLinks[4].InMemoryPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "AtmosphereMaster" }
        };
        var customTexturesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textureColumns = validCustomSlots.Values.SelectMany(columns => columns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var seed in Enumerable.Range(0, 64))
        {
            var randomisation = PlanetAppearanceRandomizer.Generate(
                original,
                baseLayer.Planets,
                seed,
                customLinks);
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in textureColumns)
            {
                var path = randomisation.Appearance[column];
                if (!validCustomSlots.TryGetValue(path, out var validColumns))
                {
                    continue;
                }

                True(validColumns.Contains(column),
                    $"seed {seed} uses {path} only in a linked texture category");
                usedPaths.Add(path);
                customTexturesSeen.Add(path);
            }

            True(usedPaths.SetEquals(randomisation.CustomTexturePaths),
                $"seed {seed} reports precisely the linked custom textures it selected");
        }

        Equal(customLinks.Length, customTexturesSeen.Count,
            "the deterministic custom-texture sweep reaches every linked material category");
        True(exceedsFormerTenPercentBand,
            "the expanded randomiser produces scalar variation beyond the former ten percent band");
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
            var packageTextureOptionCalls = 0;
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
                _ => WorkflowResult.Failure("Texture linking is not used by this test."),
                _ => null,
                new PlanetAppearanceTemplateStore(navigationTemplateFolder),
                packageTextureOptions: () =>
                {
                    packageTextureOptionCalls++;
                    return ["BIOA_GXM10_T.GXM_ContinentMask01"];
                });
            Equal(1, packageTextureOptionCalls,
                "Planet Designer enumerates package texture choices once per session");
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
            var originalNavigationBump = navigationBump.Primary.Value;
            navigationBump.Primary.Value = "0.8125";
            True(navigationViewModel.UndoCommand.CanExecute(null),
                "a dirty Planet Designer property edit immediately enables Undo");
            navigationViewModel.UndoCommand.Execute(null);
            Equal(originalNavigationBump, navigationBump.Primary.Value,
                "Planet Designer Undo restores the preceding draft property value");
            True(navigationViewModel.RedoCommand.CanExecute(null),
                "undoing a Planet Designer property edit enables Redo");
            navigationViewModel.RedoCommand.Execute(null);
            Equal("0.8125", navigationBump.Primary.Value,
                "Planet Designer Redo restores the draft property edit");
            navigationViewModel.BeginDraftPropertyEdit();
            navigationBump.Primary.Value = "0.7";
            navigationBump.Primary.Value = "0.6";
            navigationBump.Primary.Value = "0.5";
            navigationViewModel.EndDraftPropertyEdit();
            navigationViewModel.UndoCommand.Execute(null);
            Equal("0.8125", navigationBump.Primary.Value,
                "one Undo restores the value from before a complete slider drag");
            navigationViewModel.RedoCommand.Execute(null);
            Equal("0.5", navigationBump.Primary.Value,
                "one Redo restores the final value from a complete slider drag");
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
        var sharedTexture = new PlanetPreviewTextureSource(
            "test-stars",
            File.ReadAllBytes(Path.Combine(FindTextureDirectory(), "stars_bg.jpg")));
        using var renderer = new PlanetPreviewRenderer(
            320,
            180,
            materialTextureResolver: _ => sharedTexture);
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

    private static void VanillaPccPlanetTextureExtraction()
    {
        var references = PlanetAppearanceSchema.Properties
            .SelectMany(property => property.TextureOptions ?? [])
            .Append("BIOA_GXM10_T.GXM_CoronaGradient")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var textureService = new GalaxyMapTextureService();
        var textures = Task.Run(() => textureService.GetPackageTextureData([], references))
            .GetAwaiter().GetResult();
        Equal(references.Length, textures.Count,
            "every renderer GXM texture resolves from BIOA_NOR10_03_GM_LAY.pcc");
        foreach (var reference in references)
        {
            True(textures.TryGetValue(reference, out var texture) &&
                 texture.Contents.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                $"{reference} decodes to PNG data");
        }

        var sources = textures.ToDictionary(
            item => item.Key,
            item => new PlanetPreviewTextureSource(item.Value.CacheKey, item.Value.Contents),
            StringComparer.OrdinalIgnoreCase);
        var earth = new CsvGalaxyMapLoader().LoadBuiltIn().Planets.Single(planet =>
            planet.ExtraFields.GetValueOrDefault("Shader") == "GXM_Earth");
        var material = PlanetAppearanceCodec.ToRenderMaterial(PlanetAppearanceCodec.Decode(earth));
        using var renderer = new PlanetPreviewRenderer(
            320,
            180,
            materialTextureResolver: reference => sources.GetValueOrDefault(reference));
        var frame = renderer.Render(material, new());
        Equal(0, frame.MissingTextures.Count,
            "Earth renders from package textures without the stars fallback");
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
}
