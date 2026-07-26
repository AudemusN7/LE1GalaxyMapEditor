using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.Tests;

internal static class GalaxyMapIdentityContractTests
{
    public static void Register(Action<string, Action> run)
    {
        run("Identity: strict label syntax and ranges", StrictLabelSyntaxAndRanges);
        run("Identity: ActiveWorld and Relay encoding boundaries", EncodingBoundaries);
        run("Identity: GalaxyMapDocument Relay resolution and ambiguity", RelayResolutionAndAmbiguity);
        run("Identity: syntax and range diagnostics remain distinct", SyntaxAndRangeDiagnosticsRemainDistinct);
        run("Identity: authoring policy remains separate from existing-data validity", AuthoringPolicyRemainsSeparate);
    }

    private static void StrictLabelSyntaxAndRanges()
    {
        AssertParsed("Cluster01", GalaxyMapIdentityKind.Cluster, 1);
        AssertParsed("cluster21", GalaxyMapIdentityKind.Cluster, 21);
        AssertParsed("CLUSTER99", GalaxyMapIdentityKind.Cluster, 99);
        AssertParsed("System1", GalaxyMapIdentityKind.System, 1);
        AssertParsed("sYsTeM09", GalaxyMapIdentityKind.System, 9);
        AssertParsed("Planet1", GalaxyMapIdentityKind.Planet, 1);
        AssertParsed("PLANET99", GalaxyMapIdentityKind.Planet, 99);

        foreach (var (label, kind) in new[]
                 {
                     ("", GalaxyMapIdentityKind.Cluster),
                     ("Cluster", GalaxyMapIdentityKind.Cluster),
                     ("Cluster+22", GalaxyMapIdentityKind.Cluster),
                     ("Cluster-22", GalaxyMapIdentityKind.Cluster),
                     (" Cluster22", GalaxyMapIdentityKind.Cluster),
                     ("Cluster22 ", GalaxyMapIdentityKind.Cluster),
                     ("Cluster 22", GalaxyMapIdentityKind.Cluster),
                     ("System01x", GalaxyMapIdentityKind.System),
                     ("Planet01", GalaxyMapIdentityKind.System)
                 })
        {
            Equal(GalaxyMapLabelParseStatus.Malformed,
                GalaxyMapIdentity.ParseLabelSyntax(label, kind).Status,
                $"{label} is malformed for {kind}");
        }

        foreach (var (label, kind, suffix) in new[]
                 {
                     ("Cluster0", GalaxyMapIdentityKind.Cluster, 0),
                     ("Cluster100", GalaxyMapIdentityKind.Cluster, 100),
                     ("System10", GalaxyMapIdentityKind.System, 10),
                     ("Planet100", GalaxyMapIdentityKind.Planet, 100)
                 })
        {
            var parsed = GalaxyMapIdentity.ParseLabelSyntax(label, kind);
            Equal(GalaxyMapLabelParseStatus.Parsed, parsed.Status, $"{label} has valid syntax");
            Equal(suffix, parsed.Suffix, $"{label} suffix");
            True(!GalaxyMapIdentity.IsValidExistingSuffix(kind, suffix), $"{label} is out of range");
        }

        Equal(GalaxyMapLabelParseStatus.NumericOverflow,
            GalaxyMapIdentity.ParseLabelSyntax("Cluster2147483648", GalaxyMapIdentityKind.Cluster).Status,
            "Int32 overflow remains distinct from malformed syntax");
    }

    private static void EncodingBoundaries()
    {
        True(GalaxyMapIdentity.TryDeriveActiveWorld(
                "Cluster99", "System09", "Planet99", out var activeWorld),
            "maximum ActiveWorld label chain is encodable");
        Equal(990_999, activeWorld, "maximum ActiveWorld value");
        True(!GalaxyMapIdentity.TryDeriveActiveWorld(
                "Cluster99", "System10", "Planet99", out _),
            "out-of-range System is not encodable");
        True(GalaxyMapIdentity.TryEncodeClusterRelayEndpoint("cluster21", out var relayCode),
            "case-insensitive Cluster label is Relay-encodable");
        Equal(210_000, relayCode, "Cluster21 Relay code");
        True(!GalaxyMapIdentity.TryEncodeClusterRelayEndpoint("Cluster100", out _),
            "out-of-range Cluster is not Relay-encodable");
    }

    private static void RelayResolutionAndAmbiguity()
    {
        var document = new GalaxyMapDocument();
        var first = new Cluster { RowId = 1, Label = "cluster1", NameText = "First" };
        var second = new Cluster { RowId = 2, Label = "Cluster02", NameText = "Second" };
        var relay = new RelayConnection
        {
            RowId = 1,
            StartClusterEncoded = 10_000,
            EndClusterEncoded = 20_000
        };
        document.Clusters.Add(first);
        document.Clusters.Add(second);
        document.Relays.Add(relay);
        document.RebuildRelationships();

        True(relay.IsResolved, "document resolves Relay endpoints through shared identity encoding");
        True(document.TryGetRelayCode(first, out var code, out _),
            "document returns a unique Relay code");
        Equal(10_000, code, "document Relay code");

        document.Clusters.Add(new Cluster { RowId = 3, Label = "CLUSTER01", NameText = "Duplicate" });
        document.RebuildRelationships();

        True(!document.TryGetRelayCode(first, out _, out var error) &&
             error.Contains("ambiguous", StringComparison.OrdinalIgnoreCase),
            "duplicate Cluster suffix makes Relay code ambiguous");
        True(relay.StartCluster is null,
            "ambiguous Cluster suffix prevents relationship resolution");
        True(document.Warnings.Any(warning =>
                warning.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)),
            "document records Relay ambiguity warning");
    }

    private static void AuthoringPolicyRemainsSeparate()
    {
        var parsed = GalaxyMapIdentity.ParseLabelSyntax("Cluster21", GalaxyMapIdentityKind.Cluster);
        Equal(GalaxyMapLabelParseStatus.Parsed, parsed.Status, "Cluster21 has valid existing-data syntax");
        True(GalaxyMapIdentity.IsValidExistingSuffix(GalaxyMapIdentityKind.Cluster, parsed.Suffix),
            "Cluster21 is valid existing data");

        var baseLayer = new CsvGalaxyMapLoader().LoadBuiltInLayer();
        var module = new GalaxyMapModule(
            "Identity policy",
            "IDENTITY_POLICY",
            ModuleColor.Cyan,
            folderPath: null,
            isReadOnly: false,
            loadOrder: 10,
            new ModuleIdReservations(Cluster: new RowIdRange(100, 199)));
        var layer = new GalaxyMapLayer(module);
        var workspace = new GalaxyMapWorkspace(baseLayer, [layer]);
        workspace.SetActiveModule(module);

        Throws<InvalidOperationException>(
            () => new GalaxyMapRowFactory(workspace).CreateCluster(label: "Cluster21"),
            "authoring still rejects the vanilla Cluster range");
        Throws<InvalidOperationException>(
            () => new GalaxyMapRowFactory(workspace).CreateCluster(label: " Cluster22 "),
            "authoring deliberately rejects surrounding label whitespace");
    }

    private static void SyntaxAndRangeDiagnosticsRemainDistinct()
    {
        var document = new GalaxyMapDocument();
        var malformed = new Cluster { RowId = 1, Label = "ClusterNope", NameText = "Malformed" };
        var overflow = new Cluster
        {
            RowId = 2,
            Label = "Cluster2147483648",
            NameText = "Overflow"
        };
        document.Clusters.Add(malformed);
        document.Clusters.Add(overflow);
        document.RebuildRelationships();

        var diagnostics = new GalaxyMapValidator().Validate(document);
        True(diagnostics.Any(item =>
                item.RowId == malformed.RowId && item.Code == "LABEL-CLUSTER"),
            "malformed syntax keeps the syntax diagnostic");
        True(diagnostics.Any(item =>
                item.RowId == overflow.RowId && item.Code == "LABEL-CLUSTER-RANGE"),
            "numeric overflow follows the range diagnostic path");
        True(!document.TryGetRelayCode(malformed, out _, out var malformedError) &&
             malformedError.Contains("does not end", StringComparison.OrdinalIgnoreCase),
            "document reports malformed Relay label syntax");
        True(!document.TryGetRelayCode(overflow, out _, out var overflowError) &&
             overflowError.Contains("too large", StringComparison.OrdinalIgnoreCase),
            "document reports numeric overflow as an excessive Relay code");
    }

    private static void AssertParsed(string label, GalaxyMapIdentityKind kind, int suffix)
    {
        var parsed = GalaxyMapIdentity.ParseLabelSyntax(label, kind);
        Equal(GalaxyMapLabelParseStatus.Parsed, parsed.Status, $"{label} parse status");
        Equal(suffix, parsed.Suffix, $"{label} suffix");
        True(GalaxyMapIdentity.IsValidExistingSuffix(kind, suffix), $"{label} existing-data range");
    }

    private static void True(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void Throws<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}.");
    }
}
