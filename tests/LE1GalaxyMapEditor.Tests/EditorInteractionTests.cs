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
        Equal("975", GalaxyMapDefaults.ExtraValue(GalaxyMapTable.Planet, "UsablePlanetFunction"),
            "new Planet has no use button by default");

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
        Equal("975", hidden.ExtraFields["UsablePlanetFunction"], "hidden anomaly has no use button");
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
        True(inspector.Sections.All(section => section.Title != "Text and interaction"),
            "Planet inspector no longer has a Text and interaction category");
        var planetSection = inspector.Sections.Single(section => section.Title == "Planet");
        Equal("Description", planetSection.Fields[4].Name,
            "Description follows Internal name in the Planet category");
        SequenceEqual(["ButtonLabel", "Event"], availability.Fields.Take(2).Select(field => field.Name),
            "Use button and Remote event lead Visibility and usability");
        availability.IsExpanded = false;
        True(!availability.IsExpanded, "inspector section expansion state supports WPF's two-way Expander binding");
        availability.IsExpanded = true;
        var always = availability.Actions.Single(action => action.Label == "Set these rules to Always");
        always.Command.Execute(null);
        Equal("1", planet.ExtraFields["VisibleConditional"], "Always preset enables the condition");
        Equal("974", planet.ExtraFields["VisibleFunction"], "Always preset uses utility function 974");
        Equal("1", planet.ExtraFields["VisibleParameter"], "Always preset enables the independent parameter");
    }

    private static void InvalidManagedIdentitiesBecomeEditable()
    {
        var planet = new Planet { RowId = 99, Label = "Planet01", ActiveWorld = 7 };
        var repairWorkflow = new RepairInspectorWorkflow("Row ID", "ActiveWorld");
        var inspector = new PropertyInspectorViewModel(repairWorkflow);
        inspector.Inspect(planet);

        var fields = inspector.Sections.SelectMany(section => section.Fields).ToArray();
        var rowId = fields.Single(field => field.Name == "Row ID");
        var activeWorld = fields.Single(field => field.Name == "ActiveWorld");
        True(rowId.IsEditable, "an erroneous Row ID is editable in the inspector");
        True(activeWorld.IsEditable, "an erroneous ActiveWorld is editable in the inspector");

        rowId.Value = "100";
        activeWorld.Value = "10101";
        Equal(100, planet.RowId, "Row ID repair is applied through the managed edit boundary");
        Equal(10101, planet.ActiveWorld, "ActiveWorld repair is applied without deriving a replacement");

        repairWorkflow.ClearDiagnostics();
        inspector.Inspect(planet);
        fields = inspector.Sections.SelectMany(section => section.Fields).ToArray();
        True(fields.Single(field => field.Name == "Row ID").IsReadOnly,
            "a valid Row ID returns to read-only");
        True(fields.Single(field => field.Name == "ActiveWorld").IsReadOnly,
            "a valid ActiveWorld returns to read-only");
    }

    private static void PlotPlanetMismatchOffersManualRepairs()
    {
        var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
        var relationshipModule = new GalaxyMapModule(
            "Relationship Repair", "RELATIONSHIP_REPAIR", ModuleColor.Magenta, folderPath: null,
            isReadOnly: false, loadOrder: 1, TestReservations());
        var relationshipLayer = new GalaxyMapLayer(relationshipModule);
        relationshipLayer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Planet));
        relationshipLayer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.PlotPlanet));
        var mismatchedPlanet = new Planet
        {
            RowId = 10000,
            Label = "Planet98",
            SystemRowId = 1,
            Name = 123,
            NameText = "Intended Planet",
            ActiveWorld = 10198,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        };
        var mismatchedPlot = new PlotPlanetEntry
        {
            RowId = 10000,
            Code = 10197,
            Name = 456,
            NameText = "Wrong PlotPlanet"
        };
        var intendedPlanet = new Planet
        {
            RowId = 10002,
            Label = "Planet97",
            SystemRowId = 1,
            Name = 456,
            NameText = "Wrong PlotPlanet",
            ActiveWorld = 10197,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        };
        var invalidIdentityPlanet = new Planet
        {
            RowId = 10003,
            Label = "Planet96",
            SystemRowId = 1,
            NameText = "Invalid ActiveWorld",
            ActiveWorld = 7,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        };
        GalaxyMapRowAuthoring.PrepareNewRow(relationshipLayer, mismatchedPlanet);
        GalaxyMapRowAuthoring.PrepareNewRow(relationshipLayer, mismatchedPlot);
        GalaxyMapRowAuthoring.PrepareNewRow(relationshipLayer, intendedPlanet);
        GalaxyMapRowAuthoring.PrepareNewRow(relationshipLayer, invalidIdentityPlanet);
        relationshipLayer.Add(mismatchedPlanet);
        relationshipLayer.Add(mismatchedPlot);
        relationshipLayer.Add(intendedPlanet);
        relationshipLayer.Add(invalidIdentityPlanet);
        var relationshipWorkspace = new GalaxyMapWorkspace(baseLayer, [relationshipLayer]);
        relationshipWorkspace.SetActiveModule(relationshipModule);
        var relationshipSession = new EditorSession(relationshipWorkspace);
        var relationshipEdits = new EditSessionService(relationshipSession);
        IReadOnlyList<ValidationDiagnostic> relationshipDiagnostics =
            new GalaxyMapValidator().Validate(relationshipWorkspace);
        var relationshipRepairs = new ValidationRepairPolicy(() => relationshipDiagnostics);
        var relationshipEditWorkflow = new InspectorEditWorkflow(
            relationshipSession, relationshipEdits, relationshipRepairs.CanRepair);
        var presentationWorkflow = new MainInspectorPresentationWorkflow(
            relationshipSession,
            new RelayWorkflow(relationshipSession, relationshipEdits),
            relationshipRepairs,
            () => true,
            () => { },
            relationshipEditWorkflow.ValidateEdit,
            (_, _, _) => false,
            (_, _) => { });
        var relationshipInspector = new PropertyInspectorViewModel(presentationWorkflow);
        relationshipInspector.Inspect(relationshipWorkspace.EffectiveDocument.PlanetsByRowId[10000]);

        var planetRowId = relationshipInspector.Sections.Single(section => section.Title == "Planet")
            .Fields.Single(field => field.Name == "Row ID");
        var plotRowId = relationshipInspector.Sections.Single(section => section.Title == "Linked PlotPlanet")
            .Fields.Single(field => field.Name == "Row ID");
        True(planetRowId.IsEditable,
            "a PlotPlanet mismatch unlocks the Planet side of the shared Row ID relationship");
        True(plotRowId.IsEditable,
            "a PlotPlanet mismatch unlocks the PlotPlanet side of the shared Row ID relationship");

        var tablePresentation = new HistoryPresentationState(
            mismatchedPlot.Key, NavigationTarget.Galaxy, relationshipModule.Tag, true);
        var tableViewer = new TableViewerViewModel(
            new TableProjectionService(relationshipSession),
            (key, column, token) => relationshipEditWorkflow.ApplyTableCellEdit(
                (GalaxyMapRow)relationshipWorkspace.Resolve(key)!,
                column,
                token,
                relationshipModule,
                tablePresentation),
            () => true,
            relationshipRepairs);
        tableViewer.SelectedTable = GalaxyMapTable.Planet;
        var tableRowIdColumn = tableViewer.Columns.ToList().FindIndex(column =>
            column.Name == CsvRowSnapshot.RowIdColumnName);
        var planetTableRow = tableViewer.Rows.Single(row => row.Key == mismatchedPlanet.Key);
        True(!tableViewer.IsCellReadOnly(planetTableRow, tableRowIdColumn),
            "the 2DA Planet Row ID cell unlocks for the relationship repair");
        var planetSideRepair = tableViewer.CommitCellEdit(planetTableRow, tableRowIdColumn, "10001");
        True(planetSideRepair.Succeeded,
            "the 2DA table can choose the Planet side of the ambiguous Row ID repair");
        NotNull(relationshipLayer.Find(new GalaxyMapRowKey(GalaxyMapTable.Planet, 10001)),
            "the Planet-side choice stages the Planet under its selected ID");
        True(relationshipEdits.Undo(tablePresentation).Succeeded,
            "the Planet-side repair participates in shared undo history");
        relationshipWorkspace = relationshipSession.Workspace!;
        relationshipModule = relationshipSession.ActiveModule!;
        relationshipLayer = relationshipWorkspace.ActiveLayer!;
        relationshipDiagnostics = new GalaxyMapValidator().Validate(relationshipWorkspace);
        tableViewer.RefreshIfNeeded(force: true);

        var activeWorldColumn = tableViewer.Columns.ToList().FindIndex(column =>
            column.Name == nameof(Planet.ActiveWorld));
        var invalidIdentityTableRow = tableViewer.Rows.Single(row => row.Key == invalidIdentityPlanet.Key);
        True(!tableViewer.IsCellReadOnly(invalidIdentityTableRow, activeWorldColumn),
            "the 2DA ActiveWorld cell unlocks when validation marks that identity invalid");
        var activeWorldRepair = tableViewer.CommitCellEdit(
            invalidIdentityTableRow, activeWorldColumn, "10196");
        True(activeWorldRepair.Succeeded, "the 2DA table can manually repair ActiveWorld");
        Equal(10196, relationshipSession.Workspace!.EffectiveDocument.PlanetsByRowId[10003].ActiveWorld,
            "the table stages the manually selected ActiveWorld without deriving another value");

        tableViewer.SelectedTable = GalaxyMapTable.PlotPlanet;
        var plotTableRow = tableViewer.Rows.Single(row => row.Key == mismatchedPlot.Key);
        True(!tableViewer.IsCellReadOnly(plotTableRow, tableRowIdColumn),
            "the 2DA PlotPlanet Row ID cell unlocks for the relationship repair");
        var tableRepair = tableViewer.CommitCellEdit(plotTableRow, tableRowIdColumn, "10002");
        True(tableRepair.Succeeded, "the 2DA table can re-key the mismatched PlotPlanet manually");
        NotNull(relationshipLayer.Find(new GalaxyMapRowKey(GalaxyMapTable.PlotPlanet, 10002)),
            "the table repair stages the PlotPlanet under its intended ID");

        relationshipDiagnostics = new GalaxyMapValidator().Validate(relationshipWorkspace);
        tableViewer.RefreshIfNeeded(force: true);
        var repairedPlotTableRow = tableViewer.Rows.Single(row =>
            row.Key == new GalaxyMapRowKey(GalaxyMapTable.PlotPlanet, 10002));
        True(tableViewer.IsCellReadOnly(repairedPlotTableRow, tableRowIdColumn),
            "the corrected 2DA PlotPlanet Row ID cell returns to read-only");
        tableViewer.SelectedTable = GalaxyMapTable.Planet;
        var repairedIdentityTableRow = tableViewer.Rows.Single(row => row.Key == invalidIdentityPlanet.Key);
        True(tableViewer.IsCellReadOnly(repairedIdentityTableRow, activeWorldColumn),
            "the corrected 2DA ActiveWorld cell returns to read-only");
    }

    private static void RowIdRepairIsTransactional()
    {
        var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Identity Repair", "IDENTITY_REPAIR", ModuleColor.Cyan, folderPath: null,
            isReadOnly: false, loadOrder: 1, TestReservations());
        var layer = new GalaxyMapLayer(module);
        layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(GalaxyMapTable.Cluster));
        var invalidCluster = new Cluster
        {
            RowId = 999,
            Label = "Cluster98",
            NameText = "Repair me",
            SphereSize = 1
        };
        GalaxyMapRowAuthoring.PrepareNewRow(layer, invalidCluster);
        layer.Add(invalidCluster);
        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        workspace.SetActiveModule(module);
        var session = new EditorSession(workspace);
        var edits = new EditSessionService(session);
        var editWorkflow = new InspectorEditWorkflow(session, edits);
        var inspected = workspace.EffectiveDocument.ClustersByRowId[999];
        var edit = editWorkflow.ApplyEdit(
            inspected,
            nameof(GalaxyMapRow.RowId),
            100,
            module,
            new HistoryPresentationState(inspected.Key, NavigationTarget.Galaxy, module.Tag, true));

        True(edit.Handled && edit.Result?.Succeeded == true, "Row ID repair is a managed transaction");
        True(layer.Find(new GalaxyMapRowKey(GalaxyMapTable.Cluster, 999)) is null,
            "Row ID repair removes the invalid physical key");
        var repaired = layer.Find(new GalaxyMapRowKey(GalaxyMapTable.Cluster, 100));
        NotNull(repaired, "Row ID repair inserts the corrected physical key");
        True(repaired!.CsvSnapshot!.IsDirty(CsvRowSnapshot.RowIdColumnName),
            "Row ID repair marks the unnamed CSV identity column dirty");
        Equal(new GalaxyMapRowKey(GalaxyMapTable.Cluster, 100), edit.Result!.SelectionKey!.Value,
            "Row ID repair navigates to the corrected key");
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
        var decoded = textures.LoadTextureBytes(
            "retained-stars-background",
            File.ReadAllBytes(Path.Combine(FindTextureDirectory(), "stars_bg.jpg")));
        NotNull(decoded, "retained non-game background texture loads");
        Equal(PixelFormats.Bgr32, decoded!.Format, "decoded textures ignore source alpha");
        True(decoded.IsFrozen, "decoded texture is safe to share across threads");
        True(textures.CanDecodeImageBytes(
                File.ReadAllBytes(Path.Combine(FindTextureDirectory(), "stars_bg.jpg"))),
            "validation uses the production decoder without requiring a cache key");
        True(!textures.CanDecodeImageBytes([0, 1, 2, 3]),
            "validation rejects bytes the production decoder cannot read");
        True(GalaxyMapTextureService.IsSupportedImagePath("preview.TIFF"),
            "texture staging shares its supported extension set");
        True(!GalaxyMapTextureService.IsSupportedImagePath("preview.dds"),
            "unsupported staging extension is rejected");
        True(ReferenceEquals(
                decoded,
                textures.LoadTextureBytes(
                    "retained-stars-background",
                    File.ReadAllBytes(Path.Combine(FindTextureDirectory(), "stars_bg.jpg")))),
            "decoded texture is cached");
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
}
