using System.IO;
using System.Security.Cryptography;
using System.Text;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.Tests;

/// <summary>
/// Focused regression coverage for the optimisation pass. These checks deliberately
/// exercise observable contracts rather than implementation details so composition,
/// validation, snapshots and loading can be made incremental without changing results.
/// </summary>
internal static class OptimizationRegressionTests
{
    public static void Register(Action<string, Action> run)
    {
        run("Optimisation: deterministic override order", DeterministicOverrideOrder);
        run("Optimisation: reservations and allocator boundaries", ReservationsAndAllocatorBoundaries);
        run("Optimisation: partial CSV failure isolation", PartialCsvFailureIsolation);
        run("Optimisation: failed atomic replacement preserves commit", FailedAtomicReplacementPreservesCommit);
        run("Optimisation: shared links survive recomposition", SharedLinksSurviveRecomposition);
        run("Optimisation: Relay and ActiveWorld encoding boundaries", RelayAndActiveWorldEncodingBoundaries);
        run("Optimisation: corrupt workspace settings are contained", CorruptWorkspaceSettingsAreContained);
        run("Optimisation: layer clones are isolated", LayerClonesAreIsolated);
        run("Optimisation: CSV snapshots share no dirty state", CsvSnapshotsShareNoDirtyState);
    }

    private static void DeterministicOverrideOrder()
    {
        var baseLayer = CreateBaseLayer();
        var source = baseLayer.Clusters.Single();
        var alpha = CreateModule("ALPHA", loadOrder: 10, reservations: ModuleIdReservations.Empty);
        var zeta = CreateModule("ZETA", loadOrder: 10, reservations: ModuleIdReservations.Empty);
        var alphaLayer = new GalaxyMapLayer(alpha);
        var zetaLayer = new GalaxyMapLayer(zeta);

        var alphaRow = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, alpha);
        alphaRow.NameText = "Alpha override";
        alphaLayer.Upsert(alphaRow);
        var zetaRow = (Cluster)GalaxyMapRowCloner.CloneForOverride(source, zeta);
        zetaRow.NameText = "Zeta override";
        zetaLayer.Upsert(zetaRow);

        // Deliberately mount in reverse tag order. Equal-priority ties are resolved by tag,
        // not caller enumeration order, and the lexically later layer mounts on top.
        var workspace = new GalaxyMapWorkspace(baseLayer, [zetaLayer, alphaLayer]);
        var firstEffective = workspace.EffectiveDocument.ClustersByRowId[1];
        Equal("Zeta override", firstEffective.NameText, "equal-priority winner");
        SequenceEqual(
            [GalaxyMapModule.BaseGameTag, "ALPHA", "ZETA"],
            workspace.GetOverrideChain(firstEffective.Key).Select(row => row.Origin!.ModuleTag),
            "override-chain order");
        Equal("Original Cluster", source.NameText, "lower physical row is unchanged");

        workspace.SetActiveModule(alpha);
        Equal("Zeta override", workspace.EffectiveDocument.ClustersByRowId[1].NameText,
            "active editing target does not change mount priority");
        workspace.Recompose();
        var recomposed = workspace.EffectiveDocument.ClustersByRowId[1];
        True(!ReferenceEquals(firstEffective, recomposed), "effective rows remain detached across recomposition");
        Equal("Zeta override", recomposed.NameText, "recomposition remains deterministic");

        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        Equal(1, diagnostics.Count(item => item.Code == "ID-BASEGAME-OVERRIDE"),
            "first module override is informational");
        Equal(1, diagnostics.Count(item => item.Code == "ID-MODULE-COLLISION"),
            "competing module override is diagnosed once");
        True(diagnostics.All(item => item.Code != "ID-OUTSIDE-RESERVATION" && item.Code != "ID-NO-RESERVATION"),
            "same-ID overrides do not require a new-row reservation");
    }

    private static void ReservationsAndAllocatorBoundaries()
    {
        var baseLayer = CreateBaseLayer();
        var alpha = CreateModule(
            "RANGE_ALPHA",
            loadOrder: 10,
            reservations: new ModuleIdReservations(Cluster: new RowIdRange(100, 101)));
        var beta = CreateModule(
            "RANGE_BETA",
            loadOrder: 20,
            reservations: new ModuleIdReservations(Cluster: new RowIdRange(101, 102)));
        var alphaLayer = new GalaxyMapLayer(alpha);
        alphaLayer.Add(NewCluster(100, "Cluster10", "Alpha 100"));
        alphaLayer.Add(NewCluster(101, "Cluster11", "Alpha 101"));
        var betaLayer = new GalaxyMapLayer(beta);
        betaLayer.Add(NewCluster(102, "Cluster12", "Beta 102"));
        betaLayer.Add(NewCluster(103, "Cluster13", "Outside reservation"));

        var workspace = new GalaxyMapWorkspace(baseLayer, [alphaLayer, betaLayer]);
        var allocator = new ModuleIdAllocator(workspace);
        True(!allocator.TryNextAvailable(beta, GalaxyMapTable.Cluster, out _),
            "IDs occupied by any mounted layer exhaust the inclusive reservation");
        Throws<InvalidOperationException>(
            () => allocator.NextAvailable(beta, GalaxyMapTable.Cluster),
            message => message.Contains("exhausted", StringComparison.OrdinalIgnoreCase),
            "exhausted range error");
        Throws<InvalidOperationException>(
            () => allocator.TryNextAvailable(beta, GalaxyMapTable.PlotPlanet, out _),
            message => message.Contains("Planet's existing row ID", StringComparison.Ordinal),
            "PlotPlanet allocation is forbidden");

        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        Equal(1, diagnostics.Count(item => item.Code == "MOD-RANGE-OVERLAP"),
            "inclusive one-ID range overlap");
        Equal(1, diagnostics.Count(item => item.Code == "ID-OUTSIDE-RESERVATION" && item.RowId == 103),
            "new row beyond reservation boundary");
    }

    private static void PartialCsvFailureIsolation()
    {
        WithTemporaryDirectory(folder =>
        {
            var loader = new CsvGalaxyMapLoader();
            var module = CreateModule(
                "PARTIAL_TEST",
                loadOrder: 10,
                reservations: new ModuleIdReservations(Cluster: new RowIdRange(100, 109)),
                folderPath: folder);

            var empty = loader.LoadPartFolder(folder, module);
            True(!empty.AllRows().Any(), "an empty module folder is a valid empty partial layer");

            var authoringLayer = new GalaxyMapLayer(module);
            authoringLayer.Add(NewCluster(100, "Cluster10", "Partial Cluster"));
            new GalaxyMapCsvWriter().WriteTable(authoringLayer, GalaxyMapTable.Cluster);
            var path = Path.Combine(folder, "GalaxyMap_Cluster_part.csv");
            var validBytes = File.ReadAllBytes(path);

            var loaded = loader.LoadPartFolder(folder, module);
            Equal(1, loaded.Clusters.Count, "one-table partial module loads");
            Equal(0, loaded.Systems.Count, "absent partial tables remain empty");
            Equal(100, loaded.Clusters.Single().RowId, "partial row ID");

            File.AppendAllText(path, "101,\"unterminated\r\n", new UTF8Encoding(false));
            Throws<GalaxyMapLoadException>(
                () => loader.LoadPartFolder(folder, module),
                message => message.Contains("Malformed CSV", StringComparison.OrdinalIgnoreCase),
                "malformed quoted record");

            File.WriteAllBytes(path, validBytes);
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            lines[0] = lines[0].Replace("Label", "Lable", StringComparison.Ordinal);
            File.WriteAllLines(path, lines, new UTF8Encoding(true));
            Throws<GalaxyMapLoadException>(
                () => loader.LoadPartFolder(folder, module),
                message => message.Contains("canonical Cluster columns", StringComparison.OrdinalIgnoreCase),
                "noncanonical partial schema");

            File.WriteAllBytes(path, validBytes);
            Equal(1, loader.LoadPartFolder(folder, module).Clusters.Count,
                "a failed parse does not poison a subsequent load");

            var missingFolder = Path.Combine(folder, "missing");
            var missingModule = CreateModule(
                "MISSING_PARTIAL",
                20,
                new ModuleIdReservations(Cluster: new RowIdRange(200, 209)),
                missingFolder);
            Throws<GalaxyMapLoadException>(
                () => loader.LoadPartFolder(missingFolder, missingModule),
                message => message.Contains("does not exist", StringComparison.OrdinalIgnoreCase),
                "missing module directory");
        });
    }

    private static void FailedAtomicReplacementPreservesCommit()
    {
        WithTemporaryDirectory(folder =>
        {
            var module = CreateModule(
                "ATOMIC_TEST",
                10,
                new ModuleIdReservations(Cluster: new RowIdRange(100, 109)),
                folder);
            var layer = new GalaxyMapLayer(module);
            var row = NewCluster(100, "Cluster10", "Committed name");
            layer.Add(row);
            var writer = new GalaxyMapCsvWriter();
            writer.WriteTable(layer, GalaxyMapTable.Cluster);

            var path = Path.Combine(folder, "GalaxyMap_Cluster_part.csv");
            var committedHash = SHA256.HashData(File.ReadAllBytes(path));
            row.NameText = "Uncommitted replacement";
            row.CsvSnapshot!.MarkDirty("NameText");

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ThrowsAny(
                    () => writer.WriteTable(layer, GalaxyMapTable.Cluster),
                    exception => exception is IOException or UnauthorizedAccessException,
                    "locked target rejects replacement");
            }

            SequenceEqual(committedHash, SHA256.HashData(File.ReadAllBytes(path)),
                "failed replacement leaves committed bytes untouched");
            Equal("Committed name", row.CsvSnapshot.GetOriginalValue("NameText")!,
                "failed replacement does not claim the new value was committed");
            True(row.CsvSnapshot.IsDirty("NameText"), "failed replacement preserves pending dirty state");
            True(!Directory.EnumerateFiles(folder, ".*.tmp").Any(), "failed replacement cleans its temporary file");

            writer.WriteTable(layer, GalaxyMapTable.Cluster);
            Equal("Uncommitted replacement", row.CsvSnapshot.GetOriginalValue("NameText")!,
                "a later successful commit advances the snapshot");
        });
    }

    private static void SharedLinksSurviveRecomposition()
    {
        var baseLayer = CreateBaseLayer();
        baseLayer.Maps.Add(new MapEntry { RowId = 5, MapName = "BIOA_SHARED", StartPoint = "start_shared" });
        baseLayer.Planets.Add(new Planet
        {
            RowId = 10,
            Label = "Planet01",
            SystemRowId = 1,
            Name = 100,
            NameText = "First",
            ActiveWorld = 10101,
            MapRowId = 5,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        });
        baseLayer.Planets.Add(new Planet
        {
            RowId = 11,
            Label = "Planet02",
            SystemRowId = 1,
            Name = 101,
            NameText = "Second",
            ActiveWorld = 10102,
            MapRowId = 5,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        });
        baseLayer.PlotPlanets.Add(new PlotPlanetEntry
        {
            RowId = 10,
            Code = 10101,
            Name = 100,
            NameText = "First"
        });

        var module = CreateModule("MAP_OVERRIDE", 10, ModuleIdReservations.Empty);
        var moduleLayer = new GalaxyMapLayer(module);
        var physicalMap = baseLayer.Maps.Single();
        var mapOverride = (MapEntry)GalaxyMapRowCloner.CloneForOverride(physicalMap, module);
        mapOverride.MapName = "BIOA_OVERRIDDEN_SHARED";
        moduleLayer.Add(mapOverride);
        var workspace = new GalaxyMapWorkspace(baseLayer, [moduleLayer]);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var document = workspace.EffectiveDocument;
            var first = document.PlanetsByRowId[10];
            var second = document.PlanetsByRowId[11];
            NotNull(first.LinkedMap, "first shared Map link");
            True(ReferenceEquals(first.LinkedMap, second.LinkedMap), "both Planets share the effective Map instance");
            Equal("BIOA_OVERRIDDEN_SHARED", first.LinkedMap!.MapName, "shared Map override is effective");
            Equal("MAP_OVERRIDE", first.LinkedMap.Origin!.ModuleTag, "shared Map provenance");
            NotNull(first.PlotPlanet, "same-ID PlotPlanet link");
            True(second.PlotPlanet is null, "PlotPlanet does not leak to another row");
            Equal(1, document.ClustersByRowId[1].Systems.Count, "Cluster children are not duplicated");
            Equal(2, document.SystemsByRowId[1].Planets.Count, "System children are not duplicated");
            workspace.Recompose();
        }

        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        Equal(1, diagnostics.Count(item => item.Code == "MAP-SHARED" && item.RowId == 5),
            "shared Map relationship is reported once");
    }

    private static void RelayAndActiveWorldEncodingBoundaries()
    {
        var document = new GalaxyMapDocument();
        var huge = NewCluster(1, "Cluster214748", "Largest Relay-safe Cluster");
        var ordinary = NewCluster(2, "Cluster01", "Ordinary Cluster");
        document.Clusters.Add(huge);
        document.Clusters.Add(ordinary);
        document.Systems.Add(new GalaxySystem
        {
            RowId = 1,
            Label = "System99",
            ClusterRowId = huge.RowId,
            NameText = "Boundary System",
            Scale = 1
        });
        document.Planets.Add(new Planet
        {
            RowId = 1,
            Label = "Planet99",
            SystemRowId = 1,
            NameText = "Overflowing ActiveWorld",
            ActiveWorld = 0,
            MapRowId = -1,
            Scale = 1,
            RingColor = -1,
            PlanetLevelType = 1
        });
        document.Relays.Add(new RelayConnection
        {
            RowId = 1,
            StartClusterEncoded = 2_147_480_000,
            EndClusterEncoded = 10_000
        });
        document.Relays.Add(new RelayConnection
        {
            RowId = 2,
            StartClusterEncoded = 10_000,
            EndClusterEncoded = 2_147_480_000
        });
        document.Relays.Add(new RelayConnection
        {
            RowId = 3,
            StartClusterEncoded = 10_001,
            EndClusterEncoded = 10_000
        });
        document.RebuildRelationships();

        True(document.TryGetRelayCode(huge, out var boundaryCode, out var error),
            $"largest Relay-safe label encodes: {error}");
        Equal(2_147_480_000, boundaryCode, "largest Relay-safe code");
        True(!document.TryAddRelay(ordinary, huge, out _, out var duplicateError),
            "reverse Relay duplicate is rejected");
        True(duplicateError.Contains("already", StringComparison.OrdinalIgnoreCase), "duplicate Relay explanation");

        var diagnostics = new GalaxyMapValidator().Validate(document);
        Equal(1, diagnostics.Count(item => item.Code == "ACTIVEWORLD-RANGE" && item.RowId == 1),
            "ActiveWorld overflow is diagnosed before integer arithmetic wraps");
        Equal(1, diagnostics.Count(item => item.Code == "RELAY-DUPLICATE-PAIR" && item.RowId == 2),
            "reversed Relay pair is diagnosed once");
        Equal(1, diagnostics.Count(item => item.Code == "RELAY-ENCODING" && item.RowId == 3),
            "non-multiple Relay endpoint is diagnosed");
    }

    private static void CorruptWorkspaceSettingsAreContained()
    {
        WithTemporaryDirectory(folder =>
        {
            var path = Path.Combine(folder, "workspace.json");
            var store = new GalaxyMapWorkspaceStore(path);
            Equal(0, store.Load().Modules.Count, "missing settings file means an empty workspace");

            File.WriteAllText(path, "{ definitely-not-json", new UTF8Encoding(false));
            Throws<GalaxyMapLoadException>(
                () => store.Load(),
                message => message.Contains("Could not read remembered modules", StringComparison.OrdinalIgnoreCase),
                "malformed settings JSON");

            File.WriteAllText(path, "{\"schemaVersion\":999,\"modules\":[]}", new UTF8Encoding(false));
            Throws<GalaxyMapLoadException>(
                () => store.Load(),
                message => message.Contains("unsupported schema version", StringComparison.OrdinalIgnoreCase),
                "future settings schema");

            var moduleFolder = Path.Combine(folder, "external-module");
            Directory.CreateDirectory(moduleFolder);
            var readOnlyModule = new GalaxyMapModule(
                "External Read Only",
                "EXTERNAL_READ_ONLY",
                ModuleColor.Green,
                moduleFolder,
                isReadOnly: true,
                loadOrder: 27,
                new ModuleIdReservations(Planet: new RowIdRange(8000, 8099)));
            store.Save([RememberedModule.FromModule(readOnlyModule)], activeModuleTag: null);
            var restored = store.Load().Modules.Single().UnmanifestedReadOnlyModule;
            NotNull(restored, "unmanifested read-only metadata is retained");
            Equal("EXTERNAL_READ_ONLY", restored!.Tag, "read-only module tag");
            Equal(27, restored.LoadOrder, "read-only module priority");
            Equal(new RowIdRange(8000, 8099), restored.Reservations.Planet!.Value,
                "read-only module reservation");
        });
    }

    private static void LayerClonesAreIsolated()
    {
        var module = CreateModule(
            "CLONE_ISOLATION",
            10,
            new ModuleIdReservations(Cluster: new RowIdRange(100, 109)));
        var source = new GalaxyMapLayer(module);
        var row = NewCluster(100, "Cluster10", "Source");
        row.AddExtraField("Custom", "original");
        var headers = new[] { "", "Label", "X", "Y", "Name", "NameText", "SphereSize", "Background", "Custom" };
        source.Add(row);
        source.SetSchema(new CsvTableSchema(GalaxyMapTable.Cluster, headers));
        source.SetSourceRowOrder(GalaxyMapTable.Cluster, [100]);

        var clone = GalaxyMapLayerCloner.Clone(source);
        var clonedRow = clone.Clusters.Single();
        clonedRow.NameText = "Clone";
        clonedRow.SetExtraField("Custom", "changed");

        Equal("Source", row.NameText, "row value is isolated");
        Equal("original", row.ExtraFields["Custom"], "extra-field dictionary is isolated");
        SequenceEqual(headers, clone.GetSchema(GalaxyMapTable.Cluster)!.Headers, "schema is preserved exactly");
        SequenceEqual([100], clone.GetSourceRowOrder(GalaxyMapTable.Cluster), "physical row order is preserved");
    }

    private static void CsvSnapshotsShareNoDirtyState()
    {
        var headers = new[] { "", "Label", "NameText", "Custom" };
        var values = new[] { "0042", "Planet01", "Exact, lexical value", "\"raw-looking\"" };
        var source = new CsvRowSnapshot("GalaxyMap_Planet_part.csv", 17, headers, values);
        source.MarkDirty("NameText");

        var clone = source.Clone();
        var cleanOverride = source.CloneForOverride();
        clone.MarkDirty("Custom");
        cleanOverride.MarkDirty(CsvRowSnapshot.RowIdColumnName);

        SequenceEqual(headers, clone.Headers, "clone preserves exact headers including blank Row ID header");
        SequenceEqual(values, clone.OriginalValues, "clone preserves exact lexical values");
        SequenceEqual(headers, cleanOverride.Headers, "override preserves exact headers");
        SequenceEqual(values, cleanOverride.OriginalValues, "override preserves exact lexical values");
        True(source.IsDirty("NameText") && !source.IsDirty("Custom") && !source.IsDirty(CsvRowSnapshot.RowIdColumnName),
            "source dirty set is independent");
        True(clone.IsDirty("NameText") && clone.IsDirty("Custom") && !clone.IsDirty(CsvRowSnapshot.RowIdColumnName),
            "normal clone starts with copied but independent dirty state");
        True(!cleanOverride.IsDirty("NameText") && !cleanOverride.IsDirty("Custom") &&
             cleanOverride.IsDirty(CsvRowSnapshot.RowIdColumnName),
            "override starts pristine and has independent dirty state");
        Equal("0042", cleanOverride.GetOriginalValue(CsvRowSnapshot.RowIdColumnName)!,
            "unnamed first header remains addressable as Row ID");
    }

    private static GalaxyMapLayer CreateBaseLayer()
    {
        var layer = new GalaxyMapLayer(GalaxyMapModule.BaseGame);
        layer.Add(NewCluster(1, "Cluster01", "Original Cluster"));
        layer.Add(new GalaxySystem
        {
            RowId = 1,
            Label = "System01",
            ClusterRowId = 1,
            X = 0.5,
            Y = 0.5,
            Name = 1,
            NameText = "Original System",
            Scale = 1,
            ShowNebula = 0
        });
        return layer;
    }

    private static Cluster NewCluster(int rowId, string label, string nameText)
        => new()
        {
            RowId = rowId,
            Label = label,
            X = 0.5,
            Y = 0.5,
            Name = rowId,
            NameText = nameText,
            SphereSize = 4,
            Background = "BIOA_GalaxyMap_T.Cluster01"
        };

    private static GalaxyMapModule CreateModule(
        string tag,
        int loadOrder,
        ModuleIdReservations reservations,
        string? folderPath = null)
        => new(
            tag.Replace('_', ' '),
            tag,
            ModuleColor.Cyan,
            folderPath,
            isReadOnly: false,
            loadOrder,
            reservations);

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LE1GalaxyMapEditor.RegressionTests", Guid.NewGuid().ToString("N"));
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

    private static void True(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{description}: expected true.");
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
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{description}: expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}");
        }

        throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private static void ThrowsAny(Action action, Func<Exception, bool> predicate, string description)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (predicate(exception))
        {
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{description}: unexpected {exception.GetType().Name}: {exception.Message}");
        }

        throw new InvalidOperationException($"{description}: expected an exception, but none was thrown.");
    }
}
