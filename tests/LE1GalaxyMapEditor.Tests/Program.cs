using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Converters;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;

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
        Run("Asteroid belts use a distinct visual", AsteroidBeltsUseDistinctVisual);
        Run("Object scale uses the compressed visual curve", ObjectScaleUsesCompressedCurve);
        Run("Planet templates use verified structural defaults", PlanetTemplateDefaults);
        Run("Inspector metadata and type ranges", InspectorMetadataAndTypeRanges);
        Run("Square viewport and coordinate grid definitions", SquareViewportAndGridDefinitions);
        Run("Texture mapping ignores PNG alpha", TextureMappingIgnoresPngAlpha);
        Run("Hierarchy navigation semantics", HierarchyNavigationSemantics);
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
        Run("Module Cluster textures and nebula systems", ModuleTexturesAndNebulaSystems);
        Run("Clone delete and staged history", CloneDeleteAndHistory);
        Run("Module-owned rows move between parents", ModuleOwnedRowsMoveBetweenParents);
        Run("Shift drag stages rounded coordinates", ShiftDragStagesRoundedCoordinates);
        Run("Managed identity edits cascade to dependent rows", ManagedIdentityEditsCascade);
        Run("Special property editors and packed colours", SpecialPropertyEditorsAndColors);
        Run("Structured validation errors and warnings", StructuredValidationErrorsAndWarnings);
        OptimizationRegressionTests.Register(Run);

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
        Equal(43, document.Systems.Count, "built-in System count");
        Equal(233, document.Planets.Count, "built-in Planet count");
        Equal(6, document.PlotPlanets.Count, "built-in PlotPlanet count");
        Equal(106, document.Maps.Count, "built-in Map count");
        Equal(17, document.Relays.Count, "built-in Relay count");
        Equal(16, document.Relays.Count(relay => relay.IsResolved), "built-in resolved Relay count");
        Equal("BIOA_GalaxyMap_T.Cluster03", document.ClustersByRowId[1].Background,
            "built-in Serpent background reference");
        NotNull(document.PlanetsByRowId[1].PlotPlanet, "built-in PlotPlanet relationship");
        NotNull(document.PlanetsByRowId[1].LinkedMap, "built-in Map relationship");

        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GalaxyMap_Cluster.csv"] = "7BB1FEDCF4E3A5D0B7B86BF99427144F44B567821D42B37203EA452BF079C129",
            ["GalaxyMap_Map.csv"] = "517BCE5E4979269A8A74D731A329DAA8CB6BBB63FCEC3E4A06C270C4F4BEEA0D",
            ["GalaxyMap_Planet.csv"] = "AE9BCBDF6F8810EF3AB777CD511E304F495A133DF6777112EBBBB8DA377A2AA5",
            ["GalaxyMap_PlotPlanet.csv"] = "F85D0F1598FBD5BD3CFE103588071088A206226FE22673FBC33C60C79C510C24",
            ["GalaxyMap_Relay.csv"] = "5FE5B6B706D7DA1DD250C483962C07559C97D9E5726F406F06BE4DF2471CB373",
            ["GalaxyMap_System.csv"] = "2D9C9ADBEEEB4FBFBD176F84C7B43E1D8003D259128D2008863425FB84F47226"
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
        var appearance = inspector.Sections.Single(section => section.Title == "Planet appearance");
        var destination = inspector.Sections.Single(section => section.Title == "Destination / unused internals");
        var advanced = inspector.Sections.Single(section => section.Title == "Advanced Planet fields");
        SequenceEqual(
            ["Shader", "Horizon_Atmosphere_Intensity", "Corona_ColorA"],
            appearance.Fields.Select(field => field.Name),
            "appearance field range");
        SequenceEqual(["ExitMap"], destination.Fields.Select(field => field.Name), "destination/internal fields");
        SequenceEqual(["AfterAppearance"], advanced.Fields.Select(field => field.Name), "advanced nonappearance fields");

        appearance.Fields[0].Value = "ChangedShader";
        Equal("ChangedShader", planet.ExtraFields["Shader"], "appearance value remains editable");
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

    private static void ObjectScaleUsesCompressedCurve()
    {
        NearlyEqual(0.75, ObjectScaleConverter.Calculate(0), "zero scale clamps to the visible minimum");
        NearlyEqual(0.75, ObjectScaleConverter.Calculate(0.5), "minimum scale displays at three-quarter size");
        NearlyEqual(1, ObjectScaleConverter.Calculate(1), "default scale remains unchanged");
        NearlyEqual(3, ObjectScaleConverter.Calculate(8), "maximum scale displays at triple size");
        NearlyEqual(3, ObjectScaleConverter.Calculate(80), "oversized values clamp to the visual maximum");
        True(ObjectScaleConverter.Calculate(2) > 1 && ObjectScaleConverter.Calculate(2) < 2,
            "intermediate scale is compressed rather than linear");
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
        var beltInspector = new PropertyInspectorViewModel(
            addMap: _ => { },
            configureLandableDestination: _ => { });
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
            Equal(1, viewModel.WarningCount, "initial unresolved Relay warning");
            viewModel.ToggleWarningsCommand.Execute(null);
            True(viewModel.IsWarningsPanelOpen, "warning details can be opened");

            var cluster07 = viewModel.Document!.ClustersByRowId[6];
            FindNode(viewModel, row => ReferenceEquals(row, cluster07)).IsSelected = true;
            var relaySection = viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections");
            Equal(3, relaySection.Actions.Count(action => action.Label.StartsWith("Break connection", StringComparison.Ordinal)),
                "all incident Relays are manageable, including unresolved rows");
            relaySection.Actions.Single(action => action.Label.Contains("unresolved", StringComparison.OrdinalIgnoreCase))
                .Command.Execute(null);
            Equal(2, viewModel.Document.Relays.Count, "breaking a Relay removes its row in memory");
            Equal(0, viewModel.WarningCount, "breaking unresolved Relay clears warning");
            True(!viewModel.IsWarningsPanelOpen, "warning panel closes when warnings are gone");

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

        True(document.TryAddRelay(start, end, out var relay, out var error),
            $"Relay row can be added: {error}");
        document.RebuildRelationships();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        True(CountRelayPixels(layer) > 0, "adding to the existing collection redraws the Relay line");

        True(document.RemoveRelay(relay!), "Relay row can be removed");
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
                    new RowIdRange(50, 79)));
            var store = new GalaxyMapModuleManifestStore();
            store.Save(module);
            var loaded = store.Load(folder);

            Equal(module.Name, loaded.Name, "manifest name");
            Equal(module.Tag, loaded.Tag, "manifest tag");
            Equal(module.Color, loaded.Color, "manifest colour");
            Equal(module.LoadOrder, loaded.LoadOrder, "manifest load order");
            Equal(module.Reservations.Planet!.Value, loaded.Reservations.Planet!.Value, "manifest Planet range");
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

            var folder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.HasPendingChanges, "new rows remain staged");
            True(!File.Exists(Path.Combine(folder, "GalaxyMap_Cluster_part.csv")), "no automatic Cluster write");
            True(viewModel.CommitPendingChanges(), "manual row-creation commit succeeds");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Cluster_part.csv")), "Cluster part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_System_part.csv")), "System part written");
            True(File.Exists(Path.Combine(folder, "GalaxyMap_Planet_part.csv")), "Planet part written");
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

            True(!viewModel.MoveRow(sourceSystem, targetCluster.RowId), "BASEGAME System cannot move directly");
            True(viewModel.ErrorMessage.Contains("Clone", StringComparison.OrdinalIgnoreCase),
                "BASEGAME move explains the clone requirement");
            var baseNode = FindNode(viewModel, row => row is GalaxySystem system && system.RowId == sourceSystem.RowId);
            True(baseNode.SupportsParentMove, "System context menu supports parent moves");
            True(!baseNode.CanMoveToParent && !baseNode.MoveCommand!.CanExecute(null),
                "BASEGAME System move command is disabled");

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
            True(!viewModel.BeginCoordinateDrag(baseCluster), "BASEGAME coordinates cannot be dragged directly");
            True(viewModel.ErrorMessage.Contains("Clone", StringComparison.OrdinalIgnoreCase),
                "BASEGAME drag explains the clone requirement");
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
        var workspace = new GalaxyMapWorkspace(loader.LoadBuiltInLayer(), [layer]);
        var diagnostics = new GalaxyMapValidator().Validate(workspace);

        True(diagnostics.Any(item => item.Code == "ID-OUTSIDE-RESERVATION" && item.Severity == ValidationSeverity.Error),
            "out-of-range ID error");
        True(diagnostics.Any(item => item.Code == "LABEL-CLUSTER" && item.RowId == 150), "label typo error");
        True(diagnostics.Any(item => item.Code == "COORDINATE-OFF-CANVAS" && item.Severity == ValidationSeverity.Warning),
            "off-canvas coordinate warning");
        True(diagnostics.Any(item => item.Code == "VALUE-NONPOSITIVE-SCALE"), "invisible-size warning");
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
            True(restored.LoadBuiltIn(), "second BASEGAME load");
            True(restored.RestoreRememberedModules(), "remembered workspace restores cleanly");
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
            True(missing.LoadBuiltIn(), "missing-path BASEGAME load");
            True(!missing.RestoreRememberedModules(), "missing module flags startup failure");
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
            // Built-in Cluster backgrounds are now JPG, while imported module
            // textures deliberately retain PNG support.
            var sourceTexture = Path.Combine(FindTextureDirectory(), GalaxyMapTextureService.SystemAssetName);
            True(viewModel.StageClusterTexture(cluster, viewModel.ActiveModule!, sourceTexture),
                "module texture is staged");
            var expectedPath = Path.Combine(viewModel.ActiveModule!.FolderPath!, "textures",
                $"Cluster_{cluster.RowId}_stars01.png");
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
            Equal("textures/Cluster_" + cluster.RowId + "_stars01.png",
                reloaded.ClusterTextureLinks[cluster.RowId], "manifest stores Cluster texture link");
            NotNull(new GalaxyMapTextureService(FindTextureDirectory()).GetModuleClusterTexture(reloaded, cluster.RowId),
                "committed module texture resolves independently of a Cluster row override");
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
        var appearance = inspector.Sections.Single(section => section.Title == "Planet appearance");
        var otherColumns = inspector.Sections
            .Where(section => section.Title is "Visibility and usability" or "Destination / unused internals" or
                "Legacy event routing" or "Advanced Planet fields")
            .SelectMany(section => section.Fields).ToArray();
        Equal(94, appearance.Fields.Count, "real appearance column count");
        Equal("Shader", appearance.Fields[0].Name, "real first appearance column");
        Equal("Corona_ColorA", appearance.Fields[^1].Name, "real last appearance column");
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

            var viewModel = new MainViewModel(new CsvGalaxyMapLoader());
            True(viewModel.LoadFolder(folder), "fixture loads into MainViewModel");
            var window = new MainWindow { DataContext = viewModel };

            Compose(window, application.Dispatcher);
            var creationWindow = new PlanetCreationWindow();
            Compose(creationWindow, application.Dispatcher);
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

            viewModel.ToggleWarningsCommand.Execute(null);
            Compose(window, application.Dispatcher);
            True(viewModel.IsWarningsPanelOpen, "warning details panel opens");
            var diagnosticsList = (ItemsControl)window.FindName("DiagnosticsList");
            Equal(viewModel.DiagnosticCount, diagnosticsList.Items.Count,
                "validation details list contains every diagnostic");
            viewModel.ToggleWarningsCommand.Execute(null);

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

            if (dispatcherFailure is not null)
            {
                throw new InvalidOperationException($"WPF dispatcher failure: {dispatcherFailure.Message}", dispatcherFailure);
            }
        });
    }

    private static void Compose(FrameworkElement element, Dispatcher dispatcher)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(1440, 860));
        element.Arrange(new Rect(0, 0, 1440, 860));
        element.UpdateLayout();
        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
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
