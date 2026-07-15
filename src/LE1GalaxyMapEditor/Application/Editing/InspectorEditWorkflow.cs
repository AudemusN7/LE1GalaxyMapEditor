using System.Globalization;
using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Workflows.Editing;

public sealed record MoveParentChange(
    int ParentRowId,
    string ResultingLabel,
    NavigationTarget Destination,
    string SuccessMessage);

public sealed record InspectorEditResult(bool Handled, WorkflowResult? Result = null);

public interface IInspectorWorkflow
{
    bool IsManaged(GalaxyMapRow row, string propertyName, object? value);
    string? ValidateEdit(GalaxyMapRow row, string propertyName, object? value);
    InspectorEditResult ApplyEdit(
        GalaxyMapRow row,
        string propertyName,
        object? value,
        GalaxyMapModule target,
        HistoryPresentationState presentation);
}

public sealed class InspectorEditWorkflow(EditorSession session, EditSessionService edits) : IInspectorWorkflow
{
    public const string MoveParentProperty = "MoveParent";

    public bool IsManaged(GalaxyMapRow row, string propertyName, object? value)
    {
        var move = propertyName == MoveParentProperty && value is MoveParentChange;
        return row switch
        {
            _ when propertyName == "AvailabilityAlways" => true,
            Cluster => propertyName == nameof(Cluster.Label),
            GalaxySystem => propertyName is nameof(GalaxySystem.Label) or nameof(GalaxySystem.ClusterRowId) || move,
            Planet planet => propertyName is nameof(Planet.Label) or nameof(Planet.SystemRowId) or
                nameof(Planet.Name) or nameof(Planet.NameText) or nameof(Planet.SystemLevelType) ||
                planet.PlotPlanet is not null && IsPlotPlanetMirrorField(propertyName) || move,
            _ => false
        };
    }

    public bool TryGetCsvColumn(GalaxyMapRow row, string? propertyName, out string column)
    {
        column = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        const string extraPrefix = "ExtraFields[";
        if (propertyName.StartsWith(extraPrefix, StringComparison.Ordinal) && propertyName.EndsWith(']'))
        {
            column = propertyName[extraPrefix.Length..^1];
            return column.Length > 0;
        }

        column = row switch
        {
            Cluster when propertyName is nameof(Cluster.Label) or nameof(Cluster.X) or nameof(Cluster.Y) or
                nameof(Cluster.Name) or nameof(Cluster.NameText) or nameof(Cluster.SphereSize) => propertyName,
            Cluster when propertyName == nameof(Cluster.Background) => "Background",
            GalaxySystem when propertyName == nameof(GalaxySystem.ClusterRowId) => "Cluster",
            GalaxySystem when propertyName is nameof(GalaxySystem.Label) or nameof(GalaxySystem.X) or
                nameof(GalaxySystem.Y) or nameof(GalaxySystem.Name) or nameof(GalaxySystem.NameText) or
                nameof(GalaxySystem.Scale) or nameof(GalaxySystem.ShowNebula) => propertyName,
            Planet when propertyName == nameof(Planet.SystemRowId) => "System",
            Planet when propertyName == nameof(Planet.MapRowId) => "Map",
            Planet when propertyName is nameof(Planet.Label) or nameof(Planet.X) or nameof(Planet.Y) or
                nameof(Planet.Name) or nameof(Planet.NameText) or nameof(Planet.ActiveWorld) or
                nameof(Planet.Description) or nameof(Planet.ButtonLabel) or nameof(Planet.Scale) or
                nameof(Planet.RingColor) or nameof(Planet.OrbitRing) or nameof(Planet.SystemLevelType) or
                nameof(Planet.PlanetLevelType) or nameof(Planet.Event) or nameof(Planet.ImageIndex) => propertyName,
            PlotPlanetEntry when propertyName is nameof(PlotPlanetEntry.Code) or nameof(PlotPlanetEntry.Name) or
                nameof(PlotPlanetEntry.NameText) => propertyName,
            MapEntry when propertyName == nameof(MapEntry.MapName) => "Map",
            MapEntry when propertyName == nameof(MapEntry.StartPoint) => "StartPoint",
            RelayConnection when propertyName == nameof(RelayConnection.StartClusterEncoded) => "StartCluster",
            RelayConnection when propertyName == nameof(RelayConnection.EndClusterEncoded) => "EndCluster",
            _ => string.Empty
        };
        return column.Length > 0;
    }

    public string? ValidateEdit(GalaxyMapRow row, string propertyName, object? value)
    {
        var document = session.Document;
        if (document is null)
        {
            return null;
        }

        propertyName = NormalizePropertyName(row, propertyName);
        if (propertyName is nameof(Cluster.X) or nameof(Cluster.Y) && value is double coordinate &&
            coordinate is < 0 or > 1)
        {
            return "Enter a value from 0 to 1.";
        }

        if (TryExtraColumn(propertyName, out var extraColumn))
        {
            var token = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if ((extraColumn.EndsWith("Conditional", StringComparison.OrdinalIgnoreCase) ||
                 extraColumn.EndsWith("Parameter", StringComparison.OrdinalIgnoreCase) &&
                 (extraColumn.StartsWith("Visible", StringComparison.OrdinalIgnoreCase) ||
                  extraColumn.StartsWith("Usable", StringComparison.OrdinalIgnoreCase))) &&
                token is not "0" and not "1")
            {
                return $"{extraColumn} must be 0 or 1.";
            }
            if (extraColumn.EndsWith("Function", StringComparison.OrdinalIgnoreCase) &&
                (extraColumn.StartsWith("Visible", StringComparison.OrdinalIgnoreCase) ||
                 extraColumn.StartsWith("Usable", StringComparison.OrdinalIgnoreCase)) &&
                (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var function) || function < 0))
            {
                return $"{extraColumn} must be a non-negative whole number.";
            }
            return null;
        }

        switch (row)
        {
            case Cluster cluster when propertyName == nameof(Cluster.Label):
            {
                var label = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!TryLabelSuffix(label, "Cluster", out var suffix) || suffix <= 0 || suffix > int.MaxValue / 10_000)
                {
                    return "Use Cluster followed by a positive number that can be encoded as a Relay endpoint.";
                }
                if (document.Clusters.Any(candidate => candidate.RowId != cluster.RowId &&
                        TryLabelSuffix(candidate.Label, "Cluster", out var other) && other == suffix))
                {
                    return $"Cluster code {suffix} is already used by another Cluster.";
                }
                break;
            }
            case GalaxySystem system when propertyName is nameof(GalaxySystem.Label) or nameof(GalaxySystem.ClusterRowId):
            {
                var label = propertyName == nameof(GalaxySystem.Label)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : system.Label;
                var clusterRowId = propertyName == nameof(GalaxySystem.ClusterRowId)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : system.ClusterRowId;
                if (!document.ClustersByRowId.ContainsKey(clusterRowId))
                {
                    return $"Cluster row {clusterRowId} is not available.";
                }
                if (!TryLabelSuffix(label, "System", out var suffix) || suffix is <= 0 or > 99)
                {
                    return "Use System followed by a number from 01 to 99.";
                }
                if (document.Systems.Any(candidate => candidate.RowId != system.RowId &&
                        candidate.ClusterRowId == clusterRowId &&
                        TryLabelSuffix(candidate.Label, "System", out var other) && other == suffix))
                {
                    return $"System code {suffix:00} is already used in that Cluster.";
                }
                break;
            }
            case Planet planet when propertyName is nameof(Planet.Label) or nameof(Planet.SystemRowId):
            {
                var label = propertyName == nameof(Planet.Label)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : planet.Label;
                var systemRowId = propertyName == nameof(Planet.SystemRowId)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : planet.SystemRowId;
                if (!document.SystemsByRowId.TryGetValue(systemRowId, out var system) || system.Cluster is null)
                {
                    return $"System row {systemRowId} is not available or has no valid Cluster.";
                }
                if (!TryLabelSuffix(label, "Planet", out var suffix) || suffix is <= 0 or > 99)
                {
                    return "Use Planet followed by a number from 01 to 99.";
                }
                if (document.Planets.Any(candidate => candidate.RowId != planet.RowId &&
                        candidate.SystemRowId == systemRowId &&
                        TryLabelSuffix(candidate.Label, "Planet", out var other) && other == suffix))
                {
                    return $"Planet code {suffix:00} is already used in that System.";
                }
                if (!TryCalculateActiveWorld(system.Cluster.Label, system.Label, label, out var activeWorld) ||
                    document.Planets.Any(candidate => candidate.RowId != planet.RowId && candidate.ActiveWorld == activeWorld))
                {
                    return "That label chain cannot produce a unique, valid ActiveWorld value.";
                }
                break;
            }
            case Planet when propertyName == nameof(Planet.MapRowId):
            {
                var mapRowId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (mapRowId < -1 || mapRowId >= 0 && !document.MapsByRowId.ContainsKey(mapRowId))
                {
                    return "Choose No linked Map or an available Map row.";
                }
                break;
            }
            case Planet when propertyName == nameof(Planet.PlanetLevelType) && value is null:
                return "PlanetLevelType is required; choose a value from 0 to 7.";
            case Planet when propertyName == nameof(Planet.RingColor) && value is long color &&
                             color is < int.MinValue or > uint.MaxValue:
                return "RingColor must fit a signed or unsigned packed 32-bit colour value.";
            case PlotPlanetEntry plot when propertyName == nameof(PlotPlanetEntry.Code):
            {
                if (!document.PlanetsByRowId.TryGetValue(plot.RowId, out var planet) ||
                    Convert.ToInt32(value, CultureInfo.InvariantCulture) != planet.ActiveWorld)
                {
                    return "Code must equal the linked Planet's ActiveWorld value.";
                }
                break;
            }
            case RelayConnection relay when propertyName is nameof(RelayConnection.StartClusterEncoded) or nameof(RelayConnection.EndClusterEncoded):
            {
                var start = propertyName == nameof(RelayConnection.StartClusterEncoded)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : relay.StartClusterEncoded;
                var end = propertyName == nameof(RelayConnection.EndClusterEncoded)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : relay.EndClusterEncoded;
                var changedEndpoint = propertyName == nameof(RelayConnection.StartClusterEncoded) ? start : end;
                if (!RelayEndpointResolves(document, changedEndpoint))
                {
                    return "The selected Relay endpoint must resolve to one available Cluster.";
                }
                if (start == end)
                {
                    return "A Relay cannot connect a Cluster to itself.";
                }
                if (document.Relays.Any(candidate => candidate.RowId != relay.RowId &&
                        (candidate.StartClusterEncoded == start && candidate.EndClusterEncoded == end ||
                         candidate.StartClusterEncoded == end && candidate.EndClusterEncoded == start)))
                {
                    return "Those Clusters already have a Relay connection.";
                }
                break;
            }
        }

        return null;
    }

    public WorkflowResult ApplyScalarEdit(
        GalaxyMapRow row,
        string propertyName,
        GalaxyMapModule target,
        HistoryPresentationState presentation)
    {
        var workspace = session.Workspace;
        if (workspace is null || !TryGetCsvColumn(row, propertyName, out var column))
        {
            return WorkflowResult.Failure("The edited property is not backed by a galaxy-map CSV column.");
        }

        var physicalLayer = workspace.Layers.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Find(row.Key), row));
        if (physicalLayer is not null)
        {
            if (physicalLayer.Module.IsReadOnly || physicalLayer.Module.IsBaseGame)
            {
                workspace.Recompose();
                return WorkflowResult.Failure($"{physicalLayer.Module.Tag} is read-only.");
            }

            return edits.CompleteObservedEdit(row, physicalLayer.Module, column, presentation);
        }

        workspace.SetActiveModule(target);
        var layer = workspace.ActiveLayer!;
        var existing = layer.Find(row.Key);
        var physical = existing is null
            ? GalaxyMapRowCloner.CloneForOverride(row, target)
            : GalaxyMapRowCloner.Clone(row);
        physical.Origin = new GalaxyMapRowOrigin(target, workspace.GetOverrideChain(row.Key).Any(candidate =>
            !string.Equals(candidate.Origin?.ModuleTag, target.Tag, StringComparison.OrdinalIgnoreCase)));
        GalaxyMapRowAuthoring.EnsureSnapshot(physical).MarkDirty(column);
        return edits.ExecuteMutation(new EditMutationRequest(
            [row.Key],
            [row.Table],
            () => layer.Upsert(physical),
            presentation,
            $"Staged {row.Table} row {row.RowId} in {target.Tag}.",
            IsStructural: false));
    }

    public InspectorEditResult ApplyEdit(
        GalaxyMapRow inspectedRow,
        string propertyName,
        object? value,
        GalaxyMapModule target,
        HistoryPresentationState presentation)
    {
        var workspace = session.Workspace;
        var document = session.Document;
        if (workspace is null || document is null || !IsManaged(inspectedRow, propertyName, value))
        {
            return new InspectorEditResult(false);
        }

        if (ValidateEdit(inspectedRow, propertyName, value) is { } validationError)
        {
            return new InspectorEditResult(true, WorkflowResult.Failure(validationError));
        }

        var moveRequest = propertyName == MoveParentProperty ? value as MoveParentChange : null;
        var layer = workspace.ModuleLayers.First(candidate =>
            string.Equals(candidate.Module.Tag, target.Tag, StringComparison.OrdinalIgnoreCase));
        workspace.SetActiveModule(target);

        var staged = new Dictionary<GalaxyMapRowKey, GalaxyMapRow>();
        T Stage<T>(T source) where T : GalaxyMapRow
        {
            if (staged.TryGetValue(source.Key, out var already))
            {
                return (T)already;
            }

            var existing = layer.Find(source.Key);
            var copy = existing is not null
                ? GalaxyMapRowCloner.Clone((T)existing)
                : GalaxyMapRowCloner.CloneForOverride(source, target);
            copy.Origin = new GalaxyMapRowOrigin(target, workspace.GetOverrideChain(source.Key).Any(candidate =>
                !string.Equals(candidate.Origin?.ModuleTag, target.Tag, StringComparison.OrdinalIgnoreCase)));
            staged[source.Key] = copy;
            return (T)copy;
        }

        static void Dirty(GalaxyMapRow row, params string[] columns)
        {
            var snapshot = GalaxyMapRowAuthoring.EnsureSnapshot(row);
            foreach (var column in columns)
            {
                snapshot.MarkDirty(column);
            }
        }

        void UpdatePlanetIdentity(Planet source, string clusterLabel, string systemLabel, string planetLabel)
        {
            if (!TryCalculateActiveWorld(clusterLabel, systemLabel, planetLabel, out var activeWorld))
            {
                return;
            }

            var planet = Stage(source);
            planet.ActiveWorld = activeWorld;
            Dirty(planet, "ActiveWorld");
            if (source.PlotPlanet is { } sourcePlot)
            {
                var plot = Stage(sourcePlot);
                plot.Code = activeWorld;
                Dirty(plot, "Code");
            }
        }

        switch (inspectedRow)
        {
            case GalaxyMapRow availabilityRow when propertyName == "AvailabilityAlways" && value is string[] availabilityFields:
            {
                var stagedRow = Stage(availabilityRow);
                foreach (var field in availabilityFields)
                {
                    var token = field.EndsWith("Function", StringComparison.OrdinalIgnoreCase) ? "974" : "1";
                    stagedRow.SetExtraField(field, token);
                    Dirty(stagedRow, field);
                }

                if (availabilityRow is Planet sourcePlanet && sourcePlanet.PlotPlanet is { } sourcePlot)
                {
                    var plot = Stage(sourcePlot);
                    foreach (var field in availabilityFields.Where(field =>
                                 !field.StartsWith("UsablePlanet", StringComparison.OrdinalIgnoreCase)))
                    {
                        var token = field.EndsWith("Function", StringComparison.OrdinalIgnoreCase) ? "974" : "1";
                        plot.SetExtraField(field, token);
                        Dirty(plot, field);
                    }
                }
                break;
            }
            case Cluster inspectedCluster when value is string newLabel:
            {
                if (!TryLabelSuffix(newLabel, "Cluster", out var newClusterNumber) ||
                    newClusterNumber <= 0 || newClusterNumber > int.MaxValue / 10_000)
                {
                    return new InspectorEditResult(false);
                }

                var effectiveCluster = document.ClustersByRowId.GetValueOrDefault(inspectedCluster.RowId) ?? inspectedCluster;
                var cluster = Stage(inspectedCluster);
                cluster.Label = newLabel;
                Dirty(cluster, "Label");
                foreach (var system in effectiveCluster.Systems)
                foreach (var planet in system.Planets)
                {
                    UpdatePlanetIdentity(planet, newLabel, system.Label, planet.Label);
                }

                var newCode = checked(newClusterNumber * 10_000);
                foreach (var sourceRelay in document.Relays.Where(relay =>
                             relay.StartCluster?.RowId == effectiveCluster.RowId ||
                             relay.EndCluster?.RowId == effectiveCluster.RowId))
                {
                    var relay = Stage(sourceRelay);
                    if (sourceRelay.StartCluster?.RowId == effectiveCluster.RowId)
                    {
                        relay.StartClusterEncoded = newCode;
                        Dirty(relay, "StartCluster");
                    }
                    if (sourceRelay.EndCluster?.RowId == effectiveCluster.RowId)
                    {
                        relay.EndClusterEncoded = newCode;
                        Dirty(relay, "EndCluster");
                    }
                }
                break;
            }
            case GalaxySystem inspectedSystem:
            {
                var effectiveSystem = document.SystemsByRowId.GetValueOrDefault(inspectedSystem.RowId) ?? inspectedSystem;
                var system = Stage(inspectedSystem);
                var clusterRowId = moveRequest?.ParentRowId ?? (propertyName == nameof(GalaxySystem.ClusterRowId)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : inspectedSystem.ClusterRowId);
                var systemLabel = moveRequest?.ResultingLabel ?? (propertyName == nameof(GalaxySystem.Label)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : inspectedSystem.Label);
                if (!TryLabelSuffix(systemLabel, "System", out var systemNumber) || systemNumber is <= 0 or > 99 ||
                    !document.ClustersByRowId.TryGetValue(clusterRowId, out var parentCluster))
                {
                    return new InspectorEditResult(false);
                }

                system.Label = systemLabel;
                system.ClusterRowId = clusterRowId;
                Dirty(system, moveRequest is not null
                    ? ["Label", "Cluster"]
                    : [propertyName == nameof(GalaxySystem.Label) ? "Label" : "Cluster"]);
                foreach (var planet in effectiveSystem.Planets)
                {
                    UpdatePlanetIdentity(planet, parentCluster.Label, systemLabel, planet.Label);
                }
                break;
            }
            case Planet inspectedPlanet:
            {
                var effectivePlanet = document.PlanetsByRowId.GetValueOrDefault(inspectedPlanet.RowId) ?? inspectedPlanet;
                var planet = Stage(inspectedPlanet);
                if (TryExtraColumn(propertyName, out var mirroredColumn) && IsPlotPlanetMirrorField(propertyName))
                {
                    var token = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    planet.SetExtraField(mirroredColumn, token);
                    Dirty(planet, mirroredColumn);
                    if (effectivePlanet.PlotPlanet is { } sourcePlot)
                    {
                        var plot = Stage(sourcePlot);
                        plot.SetExtraField(mirroredColumn, token);
                        Dirty(plot, mirroredColumn);
                    }
                    break;
                }
                if (propertyName == nameof(Planet.SystemLevelType))
                {
                    planet.SystemLevelType = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    Dirty(planet, "SystemLevelType");
                    if (planet.SystemLevelType != 2 && planet.RingColor != -1)
                    {
                        planet.RingColor = -1;
                        Dirty(planet, "RingColor");
                    }
                    break;
                }
                if (propertyName == nameof(Planet.Name))
                {
                    planet.Name = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    Dirty(planet, "Name");
                    if (effectivePlanet.PlotPlanet is { } sourcePlot)
                    {
                        var plot = Stage(sourcePlot);
                        plot.Name = planet.Name;
                        Dirty(plot, "Name");
                    }
                    break;
                }
                if (propertyName == nameof(Planet.NameText))
                {
                    planet.NameText = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    Dirty(planet, "NameText");
                    if (effectivePlanet.PlotPlanet is { } sourcePlot)
                    {
                        var plot = Stage(sourcePlot);
                        plot.NameText = planet.NameText;
                        Dirty(plot, "NameText");
                    }
                    break;
                }

                var systemRowId = moveRequest?.ParentRowId ?? (propertyName == nameof(Planet.SystemRowId)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : inspectedPlanet.SystemRowId);
                var planetLabel = moveRequest?.ResultingLabel ?? (propertyName == nameof(Planet.Label)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : inspectedPlanet.Label);
                if (!document.SystemsByRowId.TryGetValue(systemRowId, out var parentSystem) || parentSystem.Cluster is null ||
                    !TryCalculateActiveWorld(parentSystem.Cluster.Label, parentSystem.Label, planetLabel, out var activeWorld))
                {
                    return new InspectorEditResult(false);
                }

                planet.Label = planetLabel;
                planet.SystemRowId = systemRowId;
                planet.ActiveWorld = activeWorld;
                Dirty(planet, moveRequest is not null
                    ? ["Label", "System", "ActiveWorld"]
                    : [propertyName == nameof(Planet.Label) ? "Label" : "System", "ActiveWorld"]);
                if (effectivePlanet.PlotPlanet is { } linkedPlot)
                {
                    var plot = Stage(linkedPlot);
                    plot.Code = activeWorld;
                    Dirty(plot, "Code");
                }
                break;
            }
        }

        if (staged.Count == 0)
        {
            return new InspectorEditResult(false);
        }

        var mutationPresentation = moveRequest is null
            ? presentation
            : presentation with { Navigation = moveRequest.Destination };
        var result = edits.ExecuteMutation(new EditMutationRequest(
            staged.Keys.ToArray(),
            staged.Values.Select(row => row.Table).ToArray(),
            () =>
            {
                foreach (var stagedRow in staged.Values)
                {
                    layer.Upsert(stagedRow);
                }
            },
            mutationPresentation,
            moveRequest?.SuccessMessage ??
            $"Updated {inspectedRow.Table} row {inspectedRow.RowId} and {staged.Count - 1} dependent row(s).",
            IsStructural: true));
        return new InspectorEditResult(true, result);
    }

    public static bool TryLabelSuffix(string label, string prefix, out int suffix)
    {
        suffix = 0;
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(label[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out suffix);
    }

    public static bool IsPlotPlanetMirrorField(string propertyName)
        => TryExtraColumn(propertyName, out var column) && column is
            "VisibleConditional" or "VisibleFunction" or "VisibleParameter" or
            "UsableConditional" or "UsableFunction" or "UsableParameter";

    public static bool TryExtraColumn(string propertyName, out string column)
    {
        const string prefix = "ExtraFields[";
        if (propertyName.StartsWith(prefix, StringComparison.Ordinal) && propertyName.EndsWith(']'))
        {
            column = propertyName[prefix.Length..^1];
            return column.Length > 0;
        }
        column = string.Empty;
        return false;
    }

    private static string NormalizePropertyName(GalaxyMapRow row, string propertyName)
        => (row, propertyName) switch
        {
            (GalaxySystem, "Cluster") => nameof(GalaxySystem.ClusterRowId),
            (Planet, "System") => nameof(Planet.SystemRowId),
            (Planet, "Map") => nameof(Planet.MapRowId),
            (RelayConnection, "StartCluster") => nameof(RelayConnection.StartClusterEncoded),
            (RelayConnection, "EndCluster") => nameof(RelayConnection.EndClusterEncoded),
            _ => propertyName
        };

    private static bool RelayEndpointResolves(GalaxyMapDocument document, int encoded)
    {
        if (encoded <= 0 || encoded % 10_000 != 0)
        {
            return false;
        }

        return document.Clusters.Count(cluster =>
            TryLabelSuffix(cluster.Label, "Cluster", out var suffix) &&
            suffix > 0 && suffix <= int.MaxValue / 10_000 &&
            suffix * 10_000 == encoded) == 1;
    }

    private static bool TryCalculateActiveWorld(
        string clusterLabel,
        string systemLabel,
        string planetLabel,
        out int activeWorld)
    {
        activeWorld = 0;
        if (!TryLabelSuffix(clusterLabel, "Cluster", out var cluster) || cluster <= 0 ||
            !TryLabelSuffix(systemLabel, "System", out var system) || system is <= 0 or > 99 ||
            !TryLabelSuffix(planetLabel, "Planet", out var planet) || planet is <= 0 or > 99)
        {
            return false;
        }

        var calculated = (long)cluster * 10_000 + system * 100L + planet;
        if (calculated > int.MaxValue)
        {
            return false;
        }
        activeWorld = (int)calculated;
        return true;
    }
}
