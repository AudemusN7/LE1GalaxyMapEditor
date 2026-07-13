using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LE1GalaxyMapEditor.Infrastructure;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.ViewModels;

public enum InspectorEditorKind { Text, Checkbox, Dropdown, Color }

public sealed record InspectorFieldOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class InspectorFieldViewModel : ObservableObject
{
    private readonly Func<string, string?> _apply;
    private string _value;
    private string? _validationError;

    public InspectorFieldViewModel(
        string name,
        string value,
        bool isMain,
        bool isReadOnly,
        Func<string, string?> apply,
        InspectorEditorKind editorKind = InspectorEditorKind.Text,
        IReadOnlyList<InspectorFieldOption>? options = null,
        GalaxyMapPropertyMetadata? metadata = null)
    {
        Name = name;
        DisplayName = metadata?.DisplayName ?? name;
        Description = metadata?.Description ?? string.Empty;
        _value = value;
        IsMain = isMain;
        IsReadOnly = isReadOnly;
        _apply = apply;
        EditorKind = editorKind;
        Options = options ?? [];
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string ToolTipText => string.IsNullOrWhiteSpace(ValidationError)
        ? Description
        : string.IsNullOrWhiteSpace(Description) ? ValidationError! : $"{Description}\n\nValidation: {ValidationError}";
    public bool IsMain { get; }
    public bool IsReadOnly { get; }
    public bool IsEditable => !IsReadOnly;
    public InspectorEditorKind EditorKind { get; }
    public IReadOnlyList<InspectorFieldOption> Options { get; }
    public bool IsTextEditor => EditorKind is InspectorEditorKind.Text or InspectorEditorKind.Color;
    public bool IsCheckboxEditor => EditorKind == InspectorEditorKind.Checkbox;
    public bool IsDropdownEditor => EditorKind == InspectorEditorKind.Dropdown;
    public bool IsColorEditor => EditorKind == InspectorEditorKind.Color;
    public bool? IsChecked
    {
        get => Value == "1";
        set => Value = value == true ? "1" : "0";
    }
    public InspectorFieldOption? SelectedOption
    {
        get => Options.FirstOrDefault(option => option.Value == Value);
        set { if (value is not null) Value = value.Value; }
    }
    public Brush ColorPreview
    {
        get
        {
            if (!long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var packed))
                return Brushes.Transparent;
            if (Name.Equals("RingColor", StringComparison.OrdinalIgnoreCase) && packed == -1)
                return Brushes.Transparent;
            var argb = unchecked((uint)packed);
            return new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (IsReadOnly)
            {
                return;
            }

            if (!SetProperty(ref _value, value))
            {
                return;
            }

            ValidationError = _apply(value);
            OnPropertyChanged(nameof(IsChecked));
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(ColorPreview));
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (SetProperty(ref _validationError, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ValidationError);
}

public sealed class InspectorActionViewModel
{
    public InspectorActionViewModel(
        string label,
        Action execute,
        string detail = "",
        bool isPrimary = false,
        bool isDestructive = false)
    {
        Label = label;
        Detail = detail;
        IsPrimary = isPrimary;
        IsDestructive = isDestructive;
        Command = new RelayCommand(execute);
    }

    public string Label { get; }
    public string Detail { get; }
    public bool IsPrimary { get; }
    public bool IsDestructive { get; }
    public RelayCommand Command { get; }
}

public sealed class InspectorSectionViewModel : ObservableObject
{
    private bool _isExpanded;

    public InspectorSectionViewModel(
        string title,
        IEnumerable<InspectorFieldViewModel>? fields = null,
        IEnumerable<InspectorActionViewModel>? actions = null,
        string detail = "",
        bool isExpanded = true)
    {
        Title = title;
        Fields = new ObservableCollection<InspectorFieldViewModel>(fields ?? []);
        Actions = new ObservableCollection<InspectorActionViewModel>(actions ?? []);
        Detail = detail;
        _isExpanded = isExpanded;
    }

    public string Title { get; }
    public string Detail { get; }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public ObservableCollection<InspectorFieldViewModel> Fields { get; }
    public ObservableCollection<InspectorActionViewModel> Actions { get; }
}

public sealed class PropertyInspectorViewModel : ObservableObject
{
    public static GridLength LabelColumnWidth { get; } = new(146);

    private readonly Action<Planet>? _addPlotPlanet;
    private readonly Action<Planet>? _addMap;
    private readonly Func<Cluster, IReadOnlyList<RelayConnection>>? _getClusterRelays;
    private readonly Action<Cluster>? _beginRelayCreation;
    private readonly Action<RelayConnection>? _removeRelay;
    private readonly Action<Cluster, RelayConnection>? _beginRelayRedirect;
    private readonly Func<RelayConnection, bool>? _canBreakRelay;
    private readonly Action<Cluster>? _linkClusterTexture;
    private readonly Action<Planet>? _deleteLinkedPlotPlanet;
    private readonly Action<Planet>? _deleteLinkedMap;
    private readonly Action? _beforeEdit;
    private readonly Func<GalaxyMapRow, string, object?, bool>? _managedEdit;
    private readonly Action<Planet>? _configureLandableDestination;
    private readonly Func<IReadOnlyList<InspectorFieldOption>> _clusterOptions;
    private readonly Func<IReadOnlyList<InspectorFieldOption>> _systemOptions;
    private readonly Func<IReadOnlyList<InspectorFieldOption>> _mapOptions;
    private readonly Func<IReadOnlyList<InspectorFieldOption>> _relayClusterOptions;
    private readonly Func<Cluster, bool>? _isRelaySource;
    private readonly Action? _cancelRelayCreation;
    private readonly Func<bool> _canEdit;
    private string _title = "Nothing selected";
    private string _subtitle = "Select an item in the hierarchy or on the map.";
    private bool _hasSelection;
    private bool _currentEditable = true;
    private GalaxyMapTable _currentTable;

    public PropertyInspectorViewModel(
        Action<Planet>? addPlotPlanet = null,
        Action<Planet>? addMap = null,
        Func<Cluster, IReadOnlyList<RelayConnection>>? getClusterRelays = null,
        Action<Cluster>? beginRelayCreation = null,
        Action<RelayConnection>? removeRelay = null,
        Func<Cluster, bool>? isRelaySource = null,
        Action? cancelRelayCreation = null,
        Func<bool>? canEdit = null,
        Action<Cluster, RelayConnection>? beginRelayRedirect = null,
        Func<RelayConnection, bool>? canBreakRelay = null,
        Action<Cluster>? linkClusterTexture = null,
        Action<Planet>? deleteLinkedPlotPlanet = null,
        Action<Planet>? deleteLinkedMap = null,
        Action? beforeEdit = null,
        Func<GalaxyMapRow, string, object?, bool>? managedEdit = null,
        Action<Planet>? configureLandableDestination = null,
        Func<IReadOnlyList<InspectorFieldOption>>? clusterOptions = null,
        Func<IReadOnlyList<InspectorFieldOption>>? systemOptions = null,
        Func<IReadOnlyList<InspectorFieldOption>>? mapOptions = null,
        Func<IReadOnlyList<InspectorFieldOption>>? relayClusterOptions = null)
    {
        _addPlotPlanet = addPlotPlanet;
        _addMap = addMap;
        _getClusterRelays = getClusterRelays;
        _beginRelayCreation = beginRelayCreation;
        _removeRelay = removeRelay;
        _beginRelayRedirect = beginRelayRedirect;
        _canBreakRelay = canBreakRelay;
        _linkClusterTexture = linkClusterTexture;
        _deleteLinkedPlotPlanet = deleteLinkedPlotPlanet;
        _deleteLinkedMap = deleteLinkedMap;
        _beforeEdit = beforeEdit;
        _managedEdit = managedEdit;
        _configureLandableDestination = configureLandableDestination;
        _clusterOptions = clusterOptions ?? (() => []);
        _systemOptions = systemOptions ?? (() => []);
        _mapOptions = mapOptions ?? (() => []);
        _relayClusterOptions = relayClusterOptions ?? (() => []);
        _isRelaySource = isRelaySource;
        _cancelRelayCreation = cancelRelayCreation;
        _canEdit = canEdit ?? (() => true);
    }

    public ObservableCollection<InspectorSectionViewModel> Sections { get; } = [];
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
    public bool HasSelection { get => _hasSelection; private set => SetProperty(ref _hasSelection, value); }

    public void InspectGalaxy()
    {
        Sections.Clear();
        HasSelection = true;
        Title = "The Milky Way";
        Subtitle = "Galaxy overview";
    }

    public void Inspect(GalaxyMapRow? row, bool? isEditable = null)
    {
        Sections.Clear();
        HasSelection = row is not null;
        _currentEditable = isEditable ?? _canEdit();
        _currentTable = row?.Table ?? default;

        if (row is null)
        {
            Title = "Nothing selected";
            Subtitle = "Select an item in the hierarchy or on the map.";
            return;
        }

        switch (row)
        {
            case Cluster cluster:
                Title = cluster.DisplayName;
                Subtitle = DescribeRow(cluster, "Cluster");
                AddCluster(cluster);
                break;
            case GalaxySystem system:
                Title = system.DisplayName;
                Subtitle = DescribeRow(system, "System");
                AddSystem(system);
                break;
            case Planet planet:
                Title = planet.DisplayName;
                Subtitle = DescribeRow(planet, "Planet / system object");
                AddPlanet(planet);
                break;
            case PlotPlanetEntry plotPlanet:
                Title = plotPlanet.NameText;
                Subtitle = DescribeRow(plotPlanet, "PlotPlanet");
                AddPlotPlanet(plotPlanet, "PlotPlanet", true);
                break;
            case MapEntry map:
                Title = map.MapName;
                Subtitle = DescribeRow(map, "Map");
                AddMap(map, "Map", true);
                break;
            case RelayConnection relay:
                Title = $"Relay {relay.RowId}";
                Subtitle = DescribeRow(relay, relay.IsResolved ? "Resolved relay connection" : "Unresolved relay connection");
                AddRelay(relay);
                break;
        }
    }

    private void AddCluster(Cluster row)
    {
        Sections.Add(new InspectorSectionViewModel("Cluster", [
            Int("Row ID", () => row.RowId, value => row.RowId = value),
            Text("Label", () => row.Label, value => SetManaged(row, nameof(Cluster.Label), value, () => row.Label = value)),
            Int("Name", () => row.Name, value => SetManaged(row, nameof(Cluster.Name), value, () => row.Name = value)),
            Text("NameText", () => row.NameText, value => SetManaged(row, nameof(Cluster.NameText), value, () => row.NameText = value)),
            Number("X", () => row.X, value => row.X = value),
            Number("Y", () => row.Y, value => row.Y = value),
            Number("SphereSize", () => row.SphereSize, value => row.SphereSize = value),
            Text("Background", () => row.Background, value => row.Background = value)
        ], detail: "Identity, placement and the most frequently edited Cluster properties."));
        var clusterAppearance = new[] { "Colour", "Colour2", "NebularDensity", "CloudTile", "SphereIntensity" };
        var availability = AvailabilityFields(includeUseButton: false);
        AddExtraFields(row, "Cluster appearance", clusterAppearance,
            detail: "Visual parameters. Several effects remain experimentally documented.");
        AddExtraFields(row, "Visibility and usability", availability,
            detail: "Independent three-part rules. Always = 1 / 974 / 1.");
        AddRemainingExtraFields(row, "Advanced Cluster fields", clusterAppearance.Concat(availability), isExpanded: false);
        if (_linkClusterTexture is not null)
        {
            Sections.Add(new InspectorSectionViewModel("Cluster background texture", actions:
            [
                new InspectorActionViewModel(
                    "Link module texture…",
                    () => _linkClusterTexture(row),
                    "Choose a PNG to stage inside the target module's textures folder.",
                    isPrimary: true)
            ]));
        }
        AddClusterRelays(row);
    }

    private void AddSystem(GalaxySystem row)
    {
        Sections.Add(new InspectorSectionViewModel("System", [
            Int("Row ID", () => row.RowId, value => row.RowId = value),
            Text("Label", () => row.Label, value => SetManaged(row, nameof(GalaxySystem.Label), value, () => row.Label = value)),
            Int("Name", () => row.Name, value => SetManaged(row, nameof(GalaxySystem.Name), value, () => row.Name = value)),
            Text("NameText", () => row.NameText, value => SetManaged(row, nameof(GalaxySystem.NameText), value, () => row.NameText = value)),
            Dropdown("Cluster", () => row.ClusterRowId, value => SetManaged(row, nameof(GalaxySystem.ClusterRowId), value, () => row.ClusterRowId = value), _clusterOptions()),
            Number("X", () => row.X, value => row.X = value),
            Number("Y", () => row.Y, value => row.Y = value),
            Number("Scale", () => row.Scale, value => row.Scale = value),
            Checkbox("ShowNebula", () => row.ShowNebula, value => row.ShowNebula = value)
        ], detail: "Identity, parent relationship, placement and System canvas behaviour."));
        var systemAppearance = new[] { "Colour", "Colour2", "FlareTint" };
        var availability = AvailabilityFields(includeUseButton: false);
        AddExtraFields(row, "System appearance", systemAppearance,
            detail: "Packed visual colours; their exact rendered targets remain unverified.");
        AddExtraFields(row, "Visibility and usability", availability,
            detail: "Independent three-part rules. Always = 1 / 974 / 1.");
        AddRemainingExtraFields(row, "Advanced / unused System fields", systemAppearance.Concat(availability), isExpanded: false);
    }

    private void AddPlanet(Planet row)
    {
        Sections.Add(new InspectorSectionViewModel("Planet", [
            Int("Row ID", () => row.RowId, value => row.RowId = value),
            Text("Label", () => row.Label, value => SetManaged(row, nameof(Planet.Label), value, () => row.Label = value)),
            Int("Name", () => row.Name, value => SetManaged(row, nameof(Planet.Name), value, () => row.Name = value)),
            Text("NameText", () => row.NameText, value => SetManaged(row, nameof(Planet.NameText), value, () => row.NameText = value)),
            Dropdown("System", () => row.SystemRowId, value => SetManaged(row, nameof(Planet.SystemRowId), value, () => row.SystemRowId = value), _systemOptions()),
            ReadOnlyInt("ActiveWorld", () => row.ActiveWorld),
            Dropdown("Map", () => row.MapRowId, value => row.MapRowId = value, _mapOptions())
        ], detail: "Identity and managed relationships. ActiveWorld is derived from the numbered label chain."));
        Sections.Add(new InspectorSectionViewModel("System-view display", [
            Number("X", () => row.X, value => row.X = value),
            Number("Y", () => row.Y, value => row.Y = value),
            Number("Scale", () => row.Scale, value => row.Scale = value),
            Dropdown("OrbitRing", () => row.OrbitRing, value => row.OrbitRing = value,
                [new("0", "None"), new("1", "Orbit ring"), new("2", "Asteroid belt")]),
            Dropdown("SystemLevelType", () => row.SystemLevelType, value => SetManaged(row, nameof(Planet.SystemLevelType), value, () => row.SystemLevelType = value),
                [new("0", "Planet"), new("1", "Anomaly / ship"), new("2", "Ringed planet"), new("3", "Mass relay"), new("4", "Fuel depot"), new("5", "Sun")]),
            NullableDropdown("PlanetLevelType", () => row.PlanetLevelType, value => row.PlanetLevelType = value,
                [new("0", "None"), new("1", "Planet"), new("2", "Anomaly"), new("3", "Planet + anomaly (broken)"), new("4", "Citadel"), new("5", "Prefab (broken)"), new("6", "Planet + ring"), new("7", "2D image (broken)")]),
            Color("RingColor", () => row.RingColor, value => row.RingColor = value),
            NullableInt("ImageIndex", () => row.ImageIndex, value => row.ImageIndex = value)
        ], detail: "What the object looks like on the System map and after selection."));
        Sections.Add(new InspectorSectionViewModel("Text and interaction", [
            NullableInt("Description", () => row.Description, value => row.Description = value),
            NullableInt("ButtonLabel", () => row.ButtonLabel, value => row.ButtonLabel = value),
            Text("Event", () => row.Event, value => row.Event = value)
        ], detail: "Localised text, use-button behaviour and the Kismet Remote Event."));
        AddPlanetExtraFields(row);

        var relationshipActions = new List<InspectorActionViewModel>();
        var isAsteroidBelt = row.OrbitRing == 2;
        if (!isAsteroidBelt && _configureLandableDestination is not null)
        {
            relationshipActions.Add(new InspectorActionViewModel(
                row.LinkedMap is null ? "Configure landable destination…" : "Edit landable destination…",
                () => _configureLandableDestination(row),
                "Creates or updates the linked Map, StartPoint, Remote Event and optional use-button TLK.",
                isPrimary: true));
        }
        if (row.PlotPlanet is null && _addPlotPlanet is not null)
        {
            relationshipActions.Add(new InspectorActionViewModel(
                "Add PlotPlanet properties",
                () => _addPlotPlanet(row),
                "Creates a PlotPlanet _part row in the active module using this Planet's row ID.",
                isPrimary: true));
        }

        if (!isAsteroidBelt && row.LinkedMap is null && _addMap is not null)
        {
            relationshipActions.Add(new InspectorActionViewModel(
                "Add linked Map",
                () => _addMap(row),
                "Creates a blank linked Map row for manual configuration.",
                isPrimary: false));
        }

        if (relationshipActions.Count > 0)
        {
            Sections.Add(new InspectorSectionViewModel("Optional relationships", actions: relationshipActions));
        }

        if (row.PlotPlanet is not null)
        {
            AddPlotPlanet(row.PlotPlanet, "Linked PlotPlanet", false);
            if (_deleteLinkedPlotPlanet is not null)
                Sections.Add(new InspectorSectionViewModel("Linked PlotPlanet actions", actions:
                    [new("Delete linked PlotPlanet", () => _deleteLinkedPlotPlanet(row), "Stages removal of the module-owned linked row.", isDestructive: true)]));
        }

        if (row.LinkedMap is not null)
        {
            AddMap(row.LinkedMap, "Linked Map", false);
            if (_deleteLinkedMap is not null)
                Sections.Add(new InspectorSectionViewModel("Linked Map actions", actions:
                    [new("Delete linked Map", () => _deleteLinkedMap(row), "Stages removal of the module-owned Map and clears the Planet link.", isDestructive: true)]));
        }
    }

    private void AddPlotPlanet(PlotPlanetEntry row, string sectionTitle, bool standalone)
    {
        var fields = standalone
            ? new[]
            {
                Int("Row ID", () => row.RowId, value => row.RowId = value, true),
                Int("Code", () => row.Code, value => row.Code = value, true),
                Int("Name", () => row.Name, value => row.Name = value, true),
                Text("NameText", () => row.NameText, value => row.NameText = value, true)
            }
            :
            [
                ReadOnlyInt("Row ID", () => row.RowId),
                ReadOnlyInt("Code", () => row.Code),
                ReadOnlyInt("Name", () => row.Name),
                ReadOnlyText("NameText", () => row.NameText)
            ];
        Sections.Add(new InspectorSectionViewModel(sectionTitle, fields,
            detail: standalone ? "Standalone PlotPlanet row." : "Mirrored from the linked Planet and maintained automatically."));
        AddExtraFields(row, standalone ? "PlotPlanet visibility and usability" : "Linked PlotPlanet visibility and usability",
            AvailabilityFields(includeUseButton: false), detail: "Vanilla linked rows mirror the Planet rules.", forceReadOnly: !standalone);
        AddRemainingExtraFields(row, standalone ? "Advanced PlotPlanet fields" : "Advanced linked PlotPlanet fields",
            AvailabilityFields(includeUseButton: false), isExpanded: false, forceReadOnly: !standalone);
    }

    private void AddMap(MapEntry row, string sectionTitle, bool standalone)
    {
        Sections.Add(new InspectorSectionViewModel(sectionTitle, [
            Int("Row ID", () => row.RowId, value => row.RowId = value, standalone),
            Text("Map", () => row.MapName, value => row.MapName = value, standalone),
            Text("StartPoint", () => row.StartPoint, value => row.StartPoint = value, standalone)
        ]));
        AddExtraFields(row, standalone ? "Other Map columns" : "Other linked Map columns");
    }

    private void AddRelay(RelayConnection row)
    {
        Sections.Add(new InspectorSectionViewModel("Relay", [
            Int("Row ID", () => row.RowId, value => row.RowId = value),
            Dropdown("StartCluster", () => row.StartClusterEncoded, value => row.StartClusterEncoded = value, _relayClusterOptions()),
            Dropdown("EndCluster", () => row.EndClusterEncoded, value => row.EndClusterEncoded = value, _relayClusterOptions())
        ]));
        AddExtraFields(row, "Other Relay columns");
    }

    private void AddClusterRelays(Cluster cluster)
    {
        if (_getClusterRelays is null || _beginRelayCreation is null || _removeRelay is null)
        {
            return;
        }

        var actions = new List<InspectorActionViewModel>();
        foreach (var relay in _getClusterRelays(cluster))
        {
            var otherCluster = ReferenceEquals(relay.StartCluster, cluster) ? relay.EndCluster : relay.StartCluster;
            var otherCode = ReferenceEquals(relay.StartCluster, cluster)
                ? relay.EndClusterEncoded
                : relay.StartClusterEncoded;
            var destination = otherCluster?.DisplayName ?? $"unresolved Cluster{otherCode / 10_000:00} ({otherCode})";
            if (_beginRelayRedirect is not null)
            {
                actions.Add(new InspectorActionViewModel(
                    $"Redirect connection from {destination}…",
                    () => _beginRelayRedirect(cluster, relay),
                    $"Overrides Relay row {relay.RowId}; then click its new destination Cluster.",
                    isPrimary: true));
            }

            if (_canBreakRelay?.Invoke(relay) ?? true)
            {
                actions.Add(new InspectorActionViewModel(
                    $"Break connection to {destination}",
                    () => _removeRelay(relay),
                    $"Removes active-module Relay row {relay.RowId}.",
                    isDestructive: true));
            }
        }

        if (_isRelaySource?.Invoke(cluster) == true && _cancelRelayCreation is not null)
        {
            actions.Add(new InspectorActionViewModel(
                "Cancel relay edit",
                _cancelRelayCreation,
                "Return to normal Cluster selection."));
        }
        else
        {
            actions.Add(new InspectorActionViewModel(
                "Add relay connection…",
                () => _beginRelayCreation(cluster),
                "Then click another Cluster on the Galaxy view.",
                isPrimary: true));
        }

        Sections.Add(new InspectorSectionViewModel("Relay connections", actions: actions));
    }

    private void AddPlanetExtraFields(Planet row)
    {
        var names = row.ExtraFieldOrder.ToArray();
        var availability = AvailabilityFields(includeUseButton: true);
        var legacyEvents = new[]
        {
            "EventCondition", "EventFunction", "EventParameter", "EventTransition",
            "EventTransitionParameter", "EventMessage"
        };
        var destinationInternals = new[] { "ExitMap", "PlanetRotation" };
        var appearanceStart = Array.FindIndex(names,
            name => string.Equals(name, "Shader", StringComparison.OrdinalIgnoreCase));
        var appearanceEnd = Array.FindIndex(names,
            name => string.Equals(name, "Corona_ColorA", StringComparison.OrdinalIgnoreCase));
        var appearance = appearanceStart >= 0 && appearanceEnd >= appearanceStart
            ? names.Skip(appearanceStart).Take(appearanceEnd - appearanceStart + 1).ToArray()
            : [];

        AddExtraFields(row, "Visibility and usability", availability,
            detail: "Visibility, object interaction and use-button rules are independent three-part rules.");
        AddExtraFields(row, "Destination / unused internals", destinationInternals, isExpanded: false,
            detail: "Rare or unverified fields kept available without cluttering routine editing.");
        AddExtraFields(row, "Legacy event routing", legacyEvents, isExpanded: false,
            detail: "Unused by vanilla; Remote Event handles normal destination behaviour.");
        AddExtraFields(row, "Planet appearance", appearance, isExpanded: false,
            detail: "Shader and material parameters. Preserved exactly when cloning.");
        AddRemainingExtraFields(row, "Advanced Planet fields",
            availability.Concat(destinationInternals).Concat(legacyEvents).Concat(appearance), isExpanded: false);
    }

    private void AddExtraFields(GalaxyMapRow row, string title)
        => AddExtraFields(row, title, row.ExtraFieldOrder);

    private void AddExtraFields(
        GalaxyMapRow row,
        string title,
        IEnumerable<string> fieldNames,
        bool isExpanded = true,
        string detail = "",
        bool forceReadOnly = false)
    {
        var names = fieldNames
            .Where(row.ExtraFields.ContainsKey)
            .ToArray();
        var fields = names
            .Select(name => new InspectorFieldViewModel(name, row.ExtraFields[name], false, forceReadOnly || !_currentEditable, value =>
            {
                _beforeEdit?.Invoke();
                SetManaged(row, $"ExtraFields[{name}]", value, () => row.SetExtraField(name, value));
                return null;
            }, ExtraEditor(name), ExtraOptions(name), GalaxyMapPropertyCatalog.Get(row.Table, name)))
            .ToArray();

        if (fields.Length > 0)
        {
            var actions = new List<InspectorActionViewModel>();
            if (!forceReadOnly && names.All(IsAvailabilityField))
            {
                actions.Add(new InspectorActionViewModel(
                    "Set these rules to Always",
                    () => SetAvailabilityAlways(row, names),
                    "Writes each independent triplet as 1 / 974 / 1.",
                    isPrimary: false));
            }
            Sections.Add(new InspectorSectionViewModel(title, fields, actions, detail, isExpanded));
        }
    }

    private void AddRemainingExtraFields(
        GalaxyMapRow row,
        string title,
        IEnumerable<string> excluded,
        bool isExpanded = false,
        bool forceReadOnly = false)
    {
        var excludedSet = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddExtraFields(row, title, row.ExtraFieldOrder.Where(name => !excludedSet.Contains(name)), isExpanded,
            "Raw columns retained for compatibility with imported tables and unusual mods.", forceReadOnly);
    }

    private static string[] AvailabilityFields(bool includeUseButton)
    {
        var fields = new List<string>
        {
            "VisibleConditional", "VisibleFunction", "VisibleParameter",
            "UsableConditional", "UsableFunction", "UsableParameter"
        };
        if (includeUseButton)
        {
            fields.AddRange(["UsablePlanetConditional", "UsablePlanetFunction", "UsablePlanetParameter"]);
        }
        return fields.ToArray();
    }

    private void SetManaged<T>(GalaxyMapRow row, string propertyName, T value, Action fallback)
    {
        if (_managedEdit?.Invoke(row, propertyName, value) != true)
        {
            fallback();
        }
    }

    private void SetAvailabilityAlways(GalaxyMapRow row, IReadOnlyList<string> fields)
    {
        _beforeEdit?.Invoke();
        if (_managedEdit?.Invoke(row, "AvailabilityAlways", fields.ToArray()) == true)
        {
            return;
        }

        foreach (var field in fields)
        {
            row.SetExtraField(field, field.EndsWith("Function", StringComparison.OrdinalIgnoreCase) ? "974" : "1");
        }
    }

    private static bool IsAvailabilityField(string name)
        => name.StartsWith("Visible", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("Usable", StringComparison.OrdinalIgnoreCase);

    private InspectorFieldViewModel Text(
        string name, Func<string> getter, Action<string> setter, bool isMain = true)
        => new(name, getter(), isMain, IsReadOnly(name), value =>
        {
            _beforeEdit?.Invoke();
            setter(value);
            return null;
        }, metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel ReadOnlyText(string name, Func<string> getter, bool isMain = true)
        => new(name, getter(), isMain, true, _ => null,
            metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Int(
        string name, Func<int> getter, Action<int> setter, bool isMain = true)
        => new(name, getter().ToString(CultureInfo.InvariantCulture), isMain, IsReadOnly(name), value =>
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return "Enter a whole number.";
            }

            _beforeEdit?.Invoke(); setter(parsed);
            return null;
        }, metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel ReadOnlyInt(string name, Func<int> getter, bool isMain = true)
        => new(name, getter().ToString(CultureInfo.InvariantCulture), isMain, true, _ => null,
            metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel NullableInt(
        string name, Func<int?> getter, Action<int?> setter, bool isMain = true)
        => new(name, getter()?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, isMain, IsReadOnly(name), value =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _beforeEdit?.Invoke(); setter(null);
                return null;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return "Enter a whole number or leave this blank.";
            }

            _beforeEdit?.Invoke(); setter(parsed);
            return null;
        }, metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Long(
        string name, Func<long> getter, Action<long> setter, bool isMain = true)
        => new(name, getter().ToString(CultureInfo.InvariantCulture), isMain, IsReadOnly(name), value =>
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return "Enter a whole number.";
            }

            _beforeEdit?.Invoke(); setter(parsed);
            return null;
        }, metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Number(
        string name, Func<double> getter, Action<double> setter, bool isMain = true)
        => new(name, GalaxyMapNumber.FormatDisplay(getter()), isMain, IsReadOnly(name), value =>
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed))
            {
                return "Enter a number using a decimal point.";
            }

            if (!GalaxyMapNumber.HasSupportedPrecision(parsed))
            {
                return "Enter a value with no more than two decimal places.";
            }

            _beforeEdit?.Invoke(); setter(parsed);
            return null;
        }, metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Checkbox(string name, Func<int> getter, Action<int> setter)
        => new(name, getter() == 0 ? "0" : "1", true, IsReadOnly(name), value =>
        { _beforeEdit?.Invoke(); setter(value == "1" ? 1 : 0); return null; }, InspectorEditorKind.Checkbox,
            metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Dropdown(string name, Func<int> getter, Action<int> setter, IReadOnlyList<InspectorFieldOption> options)
        => new(name, getter().ToString(CultureInfo.InvariantCulture), true, IsReadOnly(name), value =>
        { if (!int.TryParse(value, out var parsed)) return "Choose a value."; _beforeEdit?.Invoke(); setter(parsed); return null; }, InspectorEditorKind.Dropdown, options,
            GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel NullableDropdown(string name, Func<int?> getter, Action<int?> setter, IReadOnlyList<InspectorFieldOption> options)
        => new(name, getter()?.ToString(CultureInfo.InvariantCulture) ?? "", true, IsReadOnly(name), value =>
        { if (value.Length == 0) { _beforeEdit?.Invoke(); setter(null); return null; } if (!int.TryParse(value, out var parsed)) return "Choose a value."; _beforeEdit?.Invoke(); setter(parsed); return null; }, InspectorEditorKind.Dropdown, options,
            GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private InspectorFieldViewModel Color(string name, Func<long> getter, Action<long> setter)
        => new(name, getter().ToString(CultureInfo.InvariantCulture), true, IsReadOnly(name), value =>
        { if (!long.TryParse(value, out var parsed)) return "Enter a packed 32-bit colour."; _beforeEdit?.Invoke(); setter(parsed); return null; }, InspectorEditorKind.Color,
            metadata: GalaxyMapPropertyCatalog.Get(_currentTable, name));

    private static InspectorEditorKind ExtraEditor(string name)
        => name.EndsWith("Conditional", StringComparison.OrdinalIgnoreCase) ||
           (name.EndsWith("Parameter", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("Visible", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Usable", StringComparison.OrdinalIgnoreCase)))
            ? InspectorEditorKind.Checkbox
            : IsPackedColor(name) ? InspectorEditorKind.Color : InspectorEditorKind.Text;

    private static IReadOnlyList<InspectorFieldOption> ExtraOptions(string name) => [];
    private static bool IsPackedColor(string name) => name.Equals("Colour", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Colour2", StringComparison.OrdinalIgnoreCase) || name.Equals("FlareTint", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SunColor0", StringComparison.OrdinalIgnoreCase) || name.Equals("SunColor1", StringComparison.OrdinalIgnoreCase) || name.Equals("SunColor2", StringComparison.OrdinalIgnoreCase);

    private bool IsReadOnly(string fieldName)
        => string.Equals(fieldName, "Row ID", StringComparison.OrdinalIgnoreCase) || !_currentEditable;

    private static string DescribeRow(GalaxyMapRow row, string kind)
    {
        var origin = row.Origin;
        var moduleTag = origin?.ModuleTag ?? GalaxyMapModule.BaseGameTag;
        var overrideText = origin?.OverridesLowerLayer == true ? " \u2022 override" : string.Empty;
        return $"{kind} \u2022 row {row.RowId} \u2022 {moduleTag}{overrideText}";
    }
}
