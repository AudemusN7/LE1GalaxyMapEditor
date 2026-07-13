using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace LE1GalaxyMapEditor.Models;

public sealed partial class GalaxyMapDocument
{
    private readonly List<string> _baseWarnings = [];

    public string SourceFolder { get; internal set; } = string.Empty;
    public bool IsSourceReadOnly => true;

    public ObservableCollection<Cluster> Clusters { get; } = [];
    public ObservableCollection<GalaxySystem> Systems { get; } = [];
    public ObservableCollection<Planet> Planets { get; } = [];
    public ObservableCollection<PlotPlanetEntry> PlotPlanets { get; } = [];
    public ObservableCollection<MapEntry> Maps { get; } = [];
    public ObservableCollection<RelayConnection> Relays { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    public IReadOnlyDictionary<int, Cluster> ClustersByRowId { get; private set; }
        = new Dictionary<int, Cluster>();
    public IReadOnlyDictionary<int, GalaxySystem> SystemsByRowId { get; private set; }
        = new Dictionary<int, GalaxySystem>();
    public IReadOnlyDictionary<int, Planet> PlanetsByRowId { get; private set; }
        = new Dictionary<int, Planet>();
    public IReadOnlyDictionary<int, PlotPlanetEntry> PlotPlanetsByRowId { get; private set; }
        = new Dictionary<int, PlotPlanetEntry>();
    public IReadOnlyDictionary<int, MapEntry> MapsByRowId { get; private set; }
        = new Dictionary<int, MapEntry>();

    internal void AddBaseWarning(string warning) => _baseWarnings.Add(warning);

    public PlotPlanetEntry CreatePlotPlanetFor(Planet planet)
    {
        if (!Planets.Contains(planet))
        {
            throw new InvalidOperationException("The Planet does not belong to this document.");
        }

        var existing = PlotPlanets.FirstOrDefault(row => row.RowId == planet.RowId);
        if (existing is not null)
        {
            return existing;
        }

        if (Planets.Count(candidate => candidate.RowId == planet.RowId) != 1)
        {
            throw new InvalidOperationException($"Planet row ID {planet.RowId} is ambiguous and cannot receive PlotPlanet data.");
        }

        var plotPlanet = new PlotPlanetEntry
        {
            RowId = planet.RowId,
            Code = TryDerivePlotPlanetCode(planet, out var code) ? code : planet.ActiveWorld,
            Name = planet.Name,
            NameText = planet.NameText
        };
        SeedExtraColumns(plotPlanet, PlotPlanets.FirstOrDefault());
        PlotPlanets.Add(plotPlanet);
        return plotPlanet;
    }

    public MapEntry CreateMapFor(Planet planet)
    {
        if (!Planets.Contains(planet))
        {
            throw new InvalidOperationException("The Planet does not belong to this document.");
        }

        if (planet.LinkedMap is not null)
        {
            return planet.LinkedMap;
        }

        var rowId = planet.MapRowId >= 0 && Maps.All(map => map.RowId != planet.MapRowId)
            ? planet.MapRowId
            : NextRowId(Maps);
        var map = new MapEntry { RowId = rowId };
        SeedExtraColumns(map, Maps.FirstOrDefault());
        Maps.Add(map);
        planet.MapRowId = rowId;
        return map;
    }

    public IReadOnlyList<RelayConnection> GetRelaysForCluster(Cluster cluster)
        => Relays.Where(relay => ReferenceEquals(relay.StartCluster, cluster) || ReferenceEquals(relay.EndCluster, cluster))
            .ToArray();

    public bool TryGetRelayCode(Cluster cluster, out int code, out string error)
    {
        code = 0;
        error = string.Empty;
        var match = ClusterLabelPattern().Match(cluster.Label);
        if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var suffix))
        {
            error = $"Cluster label '{cluster.Label}' does not end in a numeric Cluster code.";
            return false;
        }

        if (suffix > int.MaxValue / 10_000)
        {
            error = $"Cluster label '{cluster.Label}' is too large to encode as a Relay endpoint.";
            return false;
        }

        code = suffix * 10_000;
        var matches = Clusters.Count(candidate =>
            ClusterLabelPattern().Match(candidate.Label) is { Success: true } candidateMatch &&
            int.TryParse(candidateMatch.Groups["number"].Value, out var candidateSuffix) &&
            candidateSuffix == suffix);
        if (matches != 1)
        {
            error = $"Cluster label code {code} is ambiguous across {matches} Clusters.";
            code = 0;
            return false;
        }

        return true;
    }

    public bool TryAddRelay(
        Cluster source,
        Cluster target,
        out RelayConnection? relay,
        out string error)
    {
        relay = null;
        if (ReferenceEquals(source, target))
        {
            error = "A Cluster cannot have a Relay connection to itself.";
            return false;
        }

        if (!TryGetRelayCode(source, out var startCode, out error) ||
            !TryGetRelayCode(target, out var endCode, out error))
        {
            return false;
        }

        if (Relays.Any(candidate =>
                (candidate.StartClusterEncoded == startCode && candidate.EndClusterEncoded == endCode) ||
                (candidate.StartClusterEncoded == endCode && candidate.EndClusterEncoded == startCode)))
        {
            error = $"{source.DisplayName} and {target.DisplayName} already have a Relay connection.";
            return false;
        }

        relay = new RelayConnection
        {
            RowId = NextRowId(Relays),
            StartClusterEncoded = startCode,
            EndClusterEncoded = endCode
        };
        SeedExtraColumns(relay, Relays.FirstOrDefault());
        Relays.Add(relay);
        error = string.Empty;
        return true;
    }

    public bool RemoveRelay(RelayConnection relay) => Relays.Remove(relay);

    public void RebuildRelationships()
    {
        Warnings.Clear();
        foreach (var warning in _baseWarnings)
        {
            Warnings.Add(warning);
        }

        ClustersByRowId = BuildIndex(Clusters, "Cluster");
        SystemsByRowId = BuildIndex(Systems, "System");
        PlanetsByRowId = BuildIndex(Planets, "Planet");
        PlotPlanetsByRowId = BuildIndex(PlotPlanets, "PlotPlanet");
        MapsByRowId = BuildIndex(Maps, "Map");

        foreach (var cluster in Clusters)
        {
            cluster.Systems.Clear();
        }

        foreach (var system in Systems)
        {
            system.Cluster = null;
            system.Planets.Clear();
            if (ClustersByRowId.TryGetValue(system.ClusterRowId, out var cluster))
            {
                system.Cluster = cluster;
                cluster.Systems.Add(system);
            }
            else
            {
                Warnings.Add($"System row {system.RowId} references missing Cluster row {system.ClusterRowId}.");
            }
        }

        foreach (var planet in Planets)
        {
            planet.System = null;
            planet.PlotPlanet = null;
            planet.LinkedMap = null;

            if (SystemsByRowId.TryGetValue(planet.SystemRowId, out var system))
            {
                planet.System = system;
                system.Planets.Add(planet);
            }
            else
            {
                Warnings.Add($"Planet row {planet.RowId} references missing System row {planet.SystemRowId}.");
            }

            if (PlotPlanetsByRowId.TryGetValue(planet.RowId, out var plotPlanet))
            {
                planet.PlotPlanet = plotPlanet;
            }

            if (planet.MapRowId >= 0)
            {
                if (MapsByRowId.TryGetValue(planet.MapRowId, out var map))
                {
                    planet.LinkedMap = map;
                }
                else
                {
                    Warnings.Add($"Planet row {planet.RowId} references missing Map row {planet.MapRowId}.");
                }
            }
        }

        foreach (var plotPlanet in PlotPlanets)
        {
            if (!PlanetsByRowId.ContainsKey(plotPlanet.RowId))
            {
                Warnings.Add($"PlotPlanet row {plotPlanet.RowId} has no Planet with the same row ID.");
            }
        }

        RelinkRelays();
    }

    private Dictionary<int, T> BuildIndex<T>(IEnumerable<T> rows, string tableName) where T : GalaxyMapRow
    {
        var result = new Dictionary<int, T>();
        foreach (var row in rows)
        {
            if (!result.TryAdd(row.RowId, row))
            {
                Warnings.Add($"{tableName} contains duplicate row ID {row.RowId}; the first row is used for links.");
            }
        }

        return result;
    }

    private void RelinkRelays()
    {
        var candidates = new Dictionary<int, List<Cluster>>();
        foreach (var cluster in Clusters)
        {
            var match = ClusterLabelPattern().Match(cluster.Label);
            if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var suffix))
            {
                Warnings.Add($"Cluster row {cluster.RowId} label '{cluster.Label}' cannot be used to resolve Relay endpoints.");
                continue;
            }

            if (suffix > int.MaxValue / 10_000)
            {
                Warnings.Add($"Cluster row {cluster.RowId} label '{cluster.Label}' is too large to encode as a Relay endpoint.");
                continue;
            }

            var encoded = suffix * 10_000;
            if (!candidates.TryGetValue(encoded, out var list))
            {
                list = [];
                candidates[encoded] = list;
            }

            list.Add(cluster);
        }

        var relayLookup = new Dictionary<int, Cluster>();
        foreach (var (encoded, matchingClusters) in candidates)
        {
            if (matchingClusters.Count == 1)
            {
                relayLookup[encoded] = matchingClusters[0];
            }
            else
            {
                Warnings.Add($"Relay code {encoded} is ambiguous across {matchingClusters.Count} Cluster labels.");
            }
        }

        foreach (var relay in Relays)
        {
            relay.StartCluster = relayLookup.GetValueOrDefault(relay.StartClusterEncoded);
            relay.EndCluster = relayLookup.GetValueOrDefault(relay.EndClusterEncoded);

            if (!relay.IsResolved)
            {
                var missing = relay.StartCluster is null ? relay.StartClusterEncoded : relay.EndClusterEncoded;
                Warnings.Add($"Relay row {relay.RowId} references an unavailable Cluster label code {missing}.");
            }
        }
    }

    [GeneratedRegex("^Cluster(?<number>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClusterLabelPattern();

    private bool TryDerivePlotPlanetCode(Planet planet, out int code)
    {
        code = 0;
        if (planet.System?.Cluster is not { } cluster ||
            !TryParseLabelSuffix(cluster.Label, "Cluster", out var clusterNumber) ||
            !TryParseLabelSuffix(planet.System.Label, "System", out var systemNumber) ||
            !TryParseLabelSuffix(planet.Label, "Planet", out var planetNumber))
        {
            return false;
        }

        var calculated = ((long)clusterNumber * 10_000) + ((long)systemNumber * 100) + planetNumber;
        if (calculated is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        code = (int)calculated;
        return true;
    }

    private static bool TryParseLabelSuffix(string label, string prefix, out int number)
    {
        number = 0;
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(label[prefix.Length..], out number);
    }

    private static int NextRowId<T>(IEnumerable<T> rows) where T : GalaxyMapRow
    {
        var existing = rows.Select(row => row.RowId).ToArray();
        return existing.Length == 0 ? 0 : checked(existing.Max() + 1);
    }

    private static void SeedExtraColumns(GalaxyMapRow target, GalaxyMapRow? template)
    {
        if (template is null)
        {
            return;
        }

        foreach (var name in template.ExtraFieldOrder)
        {
            target.AddExtraField(name, string.Empty);
        }
    }
}
