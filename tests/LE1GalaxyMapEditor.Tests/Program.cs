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
    private sealed record TestCase(string Name, string Category, Action Body);
    private static readonly IReadOnlyList<TestCase> AllCases = BuildCases();

    public static IEnumerable<object[]> Cases(string category) => AllCases
        .Where(test => test.Category == category)
        .Select(test => new object[] { test.Name });

    public static void Run(string name)
        => AllCases.Single(test => test.Name == name).Body();

    private static IReadOnlyList<TestCase> BuildCases()
    {
        LegendaryExplorerCoreService.Initialize(TaskScheduler.Current);
        var tests = new List<TestCase>();
        void Add(string category, string name, Action body) => tests.Add(new TestCase(name, category, body));
        void Fast(string name, Action body) => Add("Fast", name, body);
        void Integration(string name, Action body) => Add("Integration", name, body);
        void Pcc(string name, Action body) => Add("Pcc", name, body);
        void Wpf(string name, Action body) => Add("Wpf", name, body);

        Fast("Synthetic CSV load and linking", SyntheticCsvLoadAndLinking);
        Integration("Deployed vanilla CSV data", EmbeddedVanillaCsvData);
        Pcc("LEC blank PCC template", LecBlankPccTemplate);
        if (HasVanillaTexturePackage()) Pcc("Vanilla PCC planet texture extraction", VanillaPccPlanetTextureExtraction);
        Pcc("Transactional PCC table commit", TransactionalPccTableCommit);
        Integration("DLC PCC discovery and profile persistence", DlcPccDiscoveryAndProfiles);
        Integration("Profile workspace PCC relink and forget", ProfileWorkspacePccRelink);
        Integration("LE1 TLK cache diagnostics and StrRef schema", TlkCacheDiagnosticsAndStrRefSchema);
        Fast("Invariant numeric parsing", InvariantNumericParsing);
        Fast("Inspector field parsing", InspectorEditsModel);
        Fast("Compact map-number formatting", CompactMapNumberFormatting);
        Fast("Planet appearance columns are categorized", PlanetAppearanceColumnsAreCategorized);
        Fast("Planet appearance codec, presets and templates", PlanetAppearanceCodecPresetsAndTemplates);
        Fast("Guarded Planet appearance randomiser", GuardedPlanetAppearanceRandomizer);
        Integration("Planet Designer workflow and Shader guard", PlanetDesignerWorkflowAndShaderGuard);
        Integration("Planet Designer BASEGAME override prompts", PlanetDesignerBaseGameOverridePrompts);
        Integration("Planet preview renderer production assets", PlanetPreviewRendererProductionAssets);
        Fast("Asteroid belts use a distinct visual", AsteroidBeltsUseDistinctVisual);
        Fast("Map markers preserve object scale while resizing", MapMarkersPreserveObjectScaleWhileResizing);
        Fast("Planet templates use verified structural defaults", PlanetTemplateDefaults);
        Fast("Inspector metadata and type ranges", InspectorMetadataAndTypeRanges);
        Fast("Invalid managed identities become temporarily editable", InvalidManagedIdentitiesBecomeEditable);
        Fast("PlotPlanet mismatches offer row-aware manual repairs", PlotPlanetMismatchOffersManualRepairs);
        Integration("Row ID repairs move physical identities transactionally", RowIdRepairIsTransactional);
        Wpf("Square viewport and coordinate grid definitions", SquareViewportAndGridDefinitions);
        Fast("Texture mapping ignores PNG alpha", TextureMappingIgnoresPngAlpha);
        Integration("Hierarchy navigation semantics", HierarchyNavigationSemantics);
        Integration("Contextual add actions follow the active view", ContextualAddActionsFollowActiveView);
        Wpf("Relay layer observes collection changes", RelayLayerObservesCollectionChanges);
        Fast("Duplicate row IDs are rejected", DuplicateRowIdsAreRejected);
        Fast("Missing table is reported", MissingTableIsReported);
        Fast("Effective BASEGAME rows are detached", EffectiveBaseGameRowsAreDetached);
        Integration("Module manifest round-trip", ModuleManifestRoundTrip);
        Fast("Partial layers override deterministically", PartialLayersOverrideDeterministically);
        Integration("Atomic partial CSV writer contract", AtomicPartialCsvWriterContract);
        Integration("MainViewModel writes full-row overrides", MainViewModelWritesFullRowOverrides);
        Integration("Commit preview describes and protects staged writes", CommitPreviewDescribesAndProtectsStagedWrites);
        Integration("Scalar edits preserve hierarchy identity", ScalarEditsPreserveHierarchyIdentity);
        Integration("Reserved-range row creation", ReservedRangeRowCreation);
        Integration("Galaxy-map label and ActiveWorld limits", GalaxyMapIdentityLimitsAreEnforced);
        Integration("Cluster creation requires a coordinated global label", ClusterCreationRequiresGlobalLabel);
        Integration("Partial module reservations", PartialModuleReservations);
        Integration("PlotPlanet and Map persistence", PlotPlanetAndMapPersistence);
        Integration("Inherited Relay rows redirect by override", InheritedRelayRedirectPersistence);
        Integration("Remembered module workspace and missing paths", RememberedModuleWorkspace);
        Integration("Unlinking a module preserves its files", ModuleUnlinkPreservesFiles);
        Integration("Open and unlink workspace membership waits for reviewed Commit", ModuleMembershipWaitsForCommit);
        Integration("Mount priority and row-instance comparison", MountPriorityAndRowInstances);
        Integration("Duplicate row delete follows the active module", DuplicateRowDeleteFollowsActiveModule);
        Integration("Module Cluster textures and nebula systems", ModuleTexturesAndNebulaSystems);
        Integration("Clone delete and staged history", CloneDeleteAndHistory);
        Integration("Module-owned rows move between parents", ModuleOwnedRowsMoveBetweenParents);
        Integration("Shift drag stages rounded coordinates", ShiftDragStagesRoundedCoordinates);
        Integration("Managed identity edits cascade to dependent rows", ManagedIdentityEditsCascade);
        Fast("Special property editors and packed colours", SpecialPropertyEditorsAndColors);
        Fast("Structured validation errors and warnings", StructuredValidationErrorsAndWarnings);
        CoreRegressionTests.Register(Fast);
        PersistenceSafetyTests.Register(Integration);
        WorkspaceLifecycleTests.Register(Integration);
        GalaxyMapIdentityContractTests.Register(Fast);
        Integration("Edit transaction rollback and history contract", EditTransactionRollbackAndHistoryContract);
        Integration("Merged table projection follows the editor session", TableProjectionFollowsEditorSession);
        Integration("2DA dirty highlights clear after commit", TableDirtyHighlightsClearAfterCommit);
        Integration("2DA table cells use existing edit workflows", TableCellEditingUsesExistingWorkflows);
        Wpf("Application views compose", WpfViewsComposeAfterLoad);

        var realFolder = Environment.GetEnvironmentVariable("LE1_GALAXYMAP_CSV_FOLDER");
        if (!string.IsNullOrWhiteSpace(realFolder)) Pcc("Supplied Legendary Explorer exports", () => RealExports(realFolder));
        var semFolder = Environment.GetEnvironmentVariable("LE1_GALAXYMAP_SEM_FOLDER");
        if (!string.IsNullOrWhiteSpace(semFolder)) Integration("Spectre Expansion Mod partial mount", () => SpectreExpansionModule(semFolder));
        return tests;
    }

    private static string FindTextureDirectory()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "LE1GalaxyMapEditor",
                    "resources",
                    "planet-designer",
                    "Textures");
                if (File.Exists(Path.Combine(candidate, "stars_bg.jpg")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the project texture resources.");
    }

    private static bool HasVanillaTexturePackage()
    {
        var cookedPath = LegendaryExplorerCore.GameFilesystem.LE1Directory.CookedPCPath;
        return !string.IsNullOrWhiteSpace(cookedPath) &&
               File.Exists(Path.Combine(cookedPath, "BIOA_NOR10_03_GM_LAY.pcc"));
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
            new RowIdRange(3000, 3099),
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

    private sealed class RepairInspectorWorkflow(params string[] invalidColumns) : IInspectorPresentationWorkflow
    {
        private readonly HashSet<string> _invalidColumns = new(invalidColumns, StringComparer.OrdinalIgnoreCase);

        public bool CanEdit => true;
        public bool CanRepairIdentity(GalaxyMapRow row, string columnName)
            => _invalidColumns.Contains(columnName);
        public void ClearDiagnostics() => _invalidColumns.Clear();
        public void BeginEdit() { }
        public string? ValidateEdit(GalaxyMapRow row, string propertyName, object? value) => null;
        public bool ApplyManagedEdit(GalaxyMapRow row, string propertyName, object? value)
        {
            if (propertyName != nameof(GalaxyMapRow.RowId))
            {
                return false;
            }

            row.RowId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        public IReadOnlyList<InspectorFieldOption> GetOptions(InspectorOptionSet optionSet) => [];
        public IReadOnlyList<InspectorActionDescriptor> GetActions(GalaxyMapRow row) => [];
        public void ExecuteAction(GalaxyMapRow row, InspectorActionDescriptor action) { }
    }
}
