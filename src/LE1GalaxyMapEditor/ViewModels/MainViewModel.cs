using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Infrastructure;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.Views;
using Microsoft.Win32;

namespace LE1GalaxyMapEditor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaximumHistoryEntries = 50;
    private const long MaximumHistoryBytes = 64L * 1024 * 1024;

    private static readonly GalaxyMapTable[] ReservableTables =
    [
        GalaxyMapTable.Cluster,
        GalaxyMapTable.System,
        GalaxyMapTable.Planet,
        GalaxyMapTable.Map,
        GalaxyMapTable.Relay
    ];

    private readonly CsvGalaxyMapLoader _loader;
    private readonly GalaxyMapTextureService _textures;
    private readonly GalaxyMapCsvWriter _writer = new();
    private readonly GalaxyMapModuleManifestStore _manifestStore = new();
    private readonly GalaxyMapValidator _validator = new();
    private readonly GalaxyMapWorkspaceStore _workspaceStore;
    private readonly Func<GalaxyMapRow, IReadOnlyList<GalaxyMapModule>, GalaxyMapModule?>? _editTargetSelector;
    private readonly Func<string, bool>? _confirmAction;
    private readonly Stack<WorkspaceEditState> _undoStates = [];
    private readonly Stack<WorkspaceEditState> _redoStates = [];
    private readonly Dictionary<string, HashSet<GalaxyMapTable>> _dirtyTables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyModuleMetadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string ModuleTag, int ClusterRowId), PendingTexture> _pendingTextures = [];
    private readonly List<ValidationDiagnostic> _startupDiagnostics = [];
    private readonly List<RememberedModule> _missingRememberedModules = [];
    private readonly Dictionary<GalaxyMapRow, HierarchyNodeViewModel> _nodes =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GalaxyMapRowKey, HierarchyNodeViewModel> _nodesByKey = [];

    private GalaxyMapWorkspace? _workspace;
    private GalaxyMapDocument? _document;
    private object? _currentViewModel;
    private HierarchyNodeViewModel? _selectedNode;
    private HierarchyNodeViewModel? _galaxyRoot;
    private Cluster? _currentCluster;
    private GalaxySystem? _currentSystem;
    private string _statusMessage = "Loading the built-in LE1 galaxy map.";
    private string _errorMessage = string.Empty;
    private Cluster? _pendingRelaySource;
    private RelayConnection? _pendingRelayReplacement;
    private GalaxyMapModule? _pendingRelayTargetModule;
    private bool _isCoordinateGridVisible;
    private bool _isShiftDragMode;
    private CoordinateDragSession? _coordinateDrag;
    private bool _isDiagnosticsPanelOpen;
    private bool _isApplying;
    private GalaxyMapRow? _inspectedPhysicalRow;
    private string? _preferredInstanceTag;
    private bool _inspectPhysicalInstance;
    private bool _isRestoringWorkspace;
    private bool _editSnapshotCaptured;
    private DispatcherTimer? _validationTimer;
    private string? _deferredValidationStatus;

    public MainViewModel(
        CsvGalaxyMapLoader loader,
        GalaxyMapTextureService? textures = null,
        GalaxyMapWorkspaceStore? workspaceStore = null,
        Func<GalaxyMapRow, IReadOnlyList<GalaxyMapModule>, GalaxyMapModule?>? editTargetSelector = null,
        Func<string, bool>? confirmAction = null)
    {
        _loader = loader;
        _textures = textures ?? new GalaxyMapTextureService();
        _workspaceStore = workspaceStore ?? new GalaxyMapWorkspaceStore();
        _editTargetSelector = editTargetSelector;
        _confirmAction = confirmAction;
        Inspector = new PropertyInspectorViewModel(
            AddPlotPlanet,
            AddLinkedMap,
            cluster => Document?.GetRelaysForCluster(cluster) ?? [],
            BeginRelayCreation,
            RemoveRelay,
            cluster => ReferenceEquals(PendingRelaySource, cluster),
            CancelRelayCreation,
            () => HasActiveModule,
            BeginRelayRedirect,
            CanBreakRelay,
            LinkClusterTexture,
            DeleteLinkedPlotPlanet,
            DeleteLinkedMap,
            BeginUserEdit,
            ApplyManagedInspectorEdit,
            ConfigureLandableDestination,
            ClusterInspectorOptions,
            SystemInspectorOptions,
            MapInspectorOptions,
            RelayClusterInspectorOptions);

        CreateModuleCommand = new RelayCommand(CreateModuleDialog, () => HasDocument);
        OpenModuleCommand = new RelayCommand(OpenModuleDialog, () => HasDocument);
        OpenFolderCommand = OpenModuleCommand;
        RefreshRememberedWorkspaceCommand = new RelayCommand(
            () => RefreshRememberedWorkspace(),
            () => HasDocument);
        DismissErrorCommand = new RelayCommand(
            () => ErrorMessage = string.Empty,
            () => HasError);
        AddClusterCommand = new RelayCommand(AddCluster, () => HasActiveModule);
        AddSystemCommand = new RelayCommand(AddSystem, () => HasActiveModule && CurrentCluster is not null);
        AddPlanetCommand = new RelayCommand(AddPlanet, () => HasActiveModule && CurrentSystem is not null);
        NavigateGalaxyCommand = new RelayCommand(NavigateGalaxy, () => HasDocument);
        NavigateClusterCommand = new RelayCommand(
            () =>
            {
                if (CurrentCluster is not null)
                {
                    NavigateCluster(CurrentCluster);
                }
            },
            () => CurrentCluster is not null);
        ToggleCoordinateGridCommand = new RelayCommand(ToggleCoordinateGrid);
        ToggleDiagnosticsCommand = new RelayCommand(ToggleDiagnostics);
        ToggleWarningsCommand = ToggleDiagnosticsCommand;
        NavigateDiagnosticCommand = new RelayCommand<ValidationDiagnostic>(NavigateToDiagnostic);
        CommitCommand = new RelayCommand(() => CommitPendingChanges(), () => HasPendingChanges);
        UndoCommand = new RelayCommand(Undo, () => _undoStates.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redoStates.Count > 0);
        DiscardChangesCommand = new RelayCommand(ConfirmDiscardChanges, () => HasPendingChanges);
        CancelRelayCommand = new RelayCommand(CancelRelayCreation);
    }

    public ObservableCollection<HierarchyNodeViewModel> HierarchyRoots { get; } = [];
    public BulkObservableCollection<GalaxyMapModule> MountedModules { get; } = [];
    public BulkObservableCollection<ModuleBarItemViewModel> ModuleBarItems { get; } = [];
    public ObservableCollection<RowInstanceTabViewModel> RowInstanceTabs { get; } = [];
    public BulkObservableCollection<ValidationDiagnostic> ValidationDiagnostics { get; } = [];
    public PropertyInspectorViewModel Inspector { get; }

    public GalaxyMapWorkspace? Workspace
    {
        get => _workspace;
        private set
        {
            if (SetProperty(ref _workspace, value))
            {
                OnPropertyChanged(nameof(ActiveModule));
                OnPropertyChanged(nameof(HasActiveModule));
                OnPropertyChanged(nameof(ActiveModuleDisplay));
                OnPropertyChanged(nameof(ActiveModuleColor));
            }
        }
    }

    public GalaxyMapDocument? Document
    {
        get => _document;
        private set
        {
            if (SetProperty(ref _document, value))
            {
                OnPropertyChanged(nameof(HasDocument));
                OnPropertyChanged(nameof(SourceFolder));
            }
        }
    }

    public GalaxyMapModule? ActiveModule => Workspace?.ActiveModule;
    public bool HasActiveModule => ActiveModule is { IsReadOnly: false, IsBaseGame: false };
    public bool HasPendingChanges => _dirtyTables.Count > 0 || _dirtyModuleMetadata.Count > 0 || _pendingTextures.Count > 0;
    public int PendingChangeCount => _dirtyTables.Values.Sum(tables => tables.Count) +
                                     _dirtyModuleMetadata.Count + _pendingTextures.Count;
    public string CommitButtonText => HasPendingChanges ? $"Commit ({PendingChangeCount})" : "Commit";
    public bool HasMultipleRowInstances => RowInstanceTabs.Count > 1;
    public string ActiveModuleDisplay => ActiveModule is null
        ? "No active authoring module"
        : ActiveModule.Name;
    public ModuleColor ActiveModuleColor => ActiveModule?.Color ?? ModuleColor.BaseGameBlue;
    public object? CurrentViewModel { get => _currentViewModel; private set => SetProperty(ref _currentViewModel, value); }

    public Cluster? CurrentCluster
    {
        get => _currentCluster;
        private set
        {
            if (SetProperty(ref _currentCluster, value))
            {
                OnPropertyChanged(nameof(HasCurrentCluster));
                NavigateClusterCommand.RaiseCanExecuteChanged();
                AddSystemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public GalaxySystem? CurrentSystem
    {
        get => _currentSystem;
        private set
        {
            if (SetProperty(ref _currentSystem, value))
            {
                OnPropertyChanged(nameof(HasCurrentSystem));
                AddPlanetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasDocument => Document is not null;
    public bool HasCurrentCluster => CurrentCluster is not null;
    public bool HasCurrentSystem => CurrentSystem is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasDiagnostics => ValidationDiagnostics.Count > 0;
    public int DiagnosticCount => ValidationDiagnostics.Count;
    public int ValidationErrorCount => ValidationDiagnostics.Count(item => item.Severity == ValidationSeverity.Error);
    public int ValidationWarningCount => ValidationDiagnostics.Count(item => item.Severity == ValidationSeverity.Warning);

    // Compatibility aliases retained for the prototype's earlier bindings/tests.
    public bool HasWarnings => HasDiagnostics;
    public int WarningCount => DiagnosticCount;
    public bool IsWarningsPanelOpen => IsDiagnosticsPanelOpen;
    public string WarningsText => string.Join(Environment.NewLine, ValidationDiagnostics.Select(item => item.ToString()));

    public bool IsAddingRelay => PendingRelaySource is not null;
    public string RelayLinkPrompt => PendingRelaySource is null
        ? string.Empty
        : _pendingRelayReplacement is null
            ? $"Select another Cluster to connect to {PendingRelaySource.DisplayName}."
            : $"Select the new destination for Relay row {_pendingRelayReplacement.RowId} from {PendingRelaySource.DisplayName}.";
    public string SourceFolder => Workspace is not null
        ? Workspace.ModuleLayers.Count == 0
            ? CsvGalaxyMapLoader.BuiltInSourceName
            : string.Join(" + ", Workspace.Layers.Select(layer => layer.Module.Tag))
        : Document?.SourceFolder ?? "No galaxy map loaded";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                DismissErrorCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Cluster? PendingRelaySource
    {
        get => _pendingRelaySource;
        private set
        {
            if (SetProperty(ref _pendingRelaySource, value))
            {
                OnPropertyChanged(nameof(IsAddingRelay));
                OnPropertyChanged(nameof(RelayLinkPrompt));
            }
        }
    }

    public bool IsCoordinateGridVisible
    {
        get => _isCoordinateGridVisible;
        private set
        {
            if (SetProperty(ref _isCoordinateGridVisible, value))
            {
                OnPropertyChanged(nameof(CoordinateGridButtonText));
                OnPropertyChanged(nameof(IsCoordinateOverlayVisible));
            }
        }
    }

    public bool IsShiftDragMode
    {
        get => _isShiftDragMode;
        private set
        {
            if (SetProperty(ref _isShiftDragMode, value))
            {
                OnPropertyChanged(nameof(IsCoordinateOverlayVisible));
            }
        }
    }

    public bool IsCoordinateOverlayVisible => IsCoordinateGridVisible || IsShiftDragMode;
    public bool HasActiveCoordinateDrag => _coordinateDrag is not null;
    public string CoordinateGridButtonText => IsCoordinateGridVisible ? "Hide coordinate grid" : "Show coordinate grid";

    public bool IsDiagnosticsPanelOpen
    {
        get => _isDiagnosticsPanelOpen;
        private set
        {
            if (SetProperty(ref _isDiagnosticsPanelOpen, value))
            {
                OnPropertyChanged(nameof(IsWarningsPanelOpen));
            }
        }
    }

    public RelayCommand CreateModuleCommand { get; }
    public RelayCommand OpenModuleCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RefreshRememberedWorkspaceCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public RelayCommand AddClusterCommand { get; }
    public RelayCommand AddSystemCommand { get; }
    public RelayCommand AddPlanetCommand { get; }
    public RelayCommand NavigateGalaxyCommand { get; }
    public RelayCommand NavigateClusterCommand { get; }
    public RelayCommand ToggleCoordinateGridCommand { get; }
    public RelayCommand ToggleDiagnosticsCommand { get; }
    public RelayCommand ToggleWarningsCommand { get; }
    public RelayCommand<ValidationDiagnostic> NavigateDiagnosticCommand { get; }
    public RelayCommand CommitCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DiscardChangesCommand { get; }
    public RelayCommand CancelRelayCommand { get; }

    public bool LoadBuiltIn()
    {
        try
        {
            var workspace = new GalaxyMapWorkspace(_loader.LoadBuiltInLayer());
            AttachWorkspace(workspace, "Loaded the read-only BASEGAME galaxy map.");
            ErrorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            ErrorMessage = exception.Message;
            StatusMessage = "The built-in LE1 galaxy map could not be loaded.";
            return false;
        }
    }

    /// <summary>
    /// Legacy full-export loading retained for fixture/reference inspection. It is
    /// always read-only and never becomes an authoring target.
    /// </summary>
    public bool LoadFolder(string folderPath)
    {
        try
        {
            var document = _loader.LoadFolder(folderPath);
            Workspace = null;
            RefreshMountedModules();
            AttachDocument(document, null, ViewContext.Galaxy);
            UpdateValidation();
            UpdateDocumentSummary("Loaded a read-only Legendary Explorer export folder.");
            ErrorMessage = string.Empty;
            RaiseCommandStates();
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            ErrorMessage = exception.Message;
            StatusMessage = "The CSV folder could not be loaded. The current document was left unchanged.";
            return false;
        }
    }

    public bool CreateModule(
        string parentFolder,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        if (Workspace is null)
        {
            return Fail("Load BASEGAME before creating an authoring module.");
        }

        try
        {
            ValidateNewModuleRanges(reservations);
            var folderName = SafeFolderName(name, tag);
            var moduleFolder = Path.Combine(Path.GetFullPath(parentFolder), folderName);
            if (Directory.Exists(moduleFolder) && Directory.EnumerateFileSystemEntries(moduleFolder).Any())
            {
                throw new InvalidOperationException(
                    $"The module folder already exists and is not empty: {moduleFolder}");
            }

            Directory.CreateDirectory(moduleFolder);
            var module = new GalaxyMapModule(
                name,
                tag,
                color,
                moduleFolder,
                isReadOnly: false,
                loadOrder: loadOrder ?? NextLoadOrder(),
                reservations);
            EnsureUniqueTag(module.Tag);
            _manifestStore.Save(module);

            var layer = new GalaxyMapLayer(module);
            foreach (var table in Enum.GetValues<GalaxyMapTable>())
            {
                layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(table));
            }

            Workspace.Mount(layer);
            Workspace.SetActiveModule(module);
            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            RefreshWorkspace(null, ViewContext.Galaxy,
                $"Created authoring module {module.Name} [{module.Tag}]. Changes will remain staged until Commit.",
                recompose: false);
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    public bool OpenExistingModule(string folderPath)
    {
        if (Workspace is null)
        {
            return Fail("Load BASEGAME before opening a module.");
        }

        try
        {
            var module = _manifestStore.Load(folderPath);
            EnsureUniqueTag(module.Tag);
            var layer = _loader.LoadPartFolder(folderPath, module);
            Workspace.Mount(layer);
            if (!module.IsReadOnly)
            {
                Workspace.SetActiveModule(module);
            }

            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            RefreshWorkspace(null, ViewContext.Galaxy,
                module.IsReadOnly
                    ? $"Mounted read-only module {module.Name} [{module.Tag}]."
                    : $"Opened authoring module {module.Name} [{module.Tag}]. Live CSV editing is active.",
                recompose: false);
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    public bool MountReadOnlyModule(
        string folderPath,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        if (Workspace is null)
        {
            return Fail("Load BASEGAME before mounting a module.");
        }

        try
        {
            var module = new GalaxyMapModule(
                name,
                tag,
                color,
                folderPath,
                isReadOnly: true,
                loadOrder: loadOrder ?? NextLoadOrder(),
                reservations);
            EnsureUniqueTag(module.Tag);
            var layer = _loader.LoadPartFolder(folderPath, module);
            Workspace.Mount(layer);
            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            RefreshWorkspace(null, ViewContext.Galaxy,
                $"Mounted read-only module {module.Name} [{module.Tag}] above the lower layers.",
                recompose: false);
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    public void ActivateHierarchyNode(HierarchyNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var belongsToCurrentHierarchy = node.IsGalaxyRoot
            ? ReferenceEquals(node, _galaxyRoot)
            : node.Model is { } model &&
              _nodes.TryGetValue(model, out var currentNode) &&
              ReferenceEquals(node, currentNode);
        if (belongsToCurrentHierarchy)
        {
            SelectFromHierarchy(node);
        }
    }

    public void SelectMapNode(HierarchyNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Model is { } model &&
            _nodes.TryGetValue(model, out var currentNode) &&
            ReferenceEquals(node, currentNode))
        {
            SelectFromMap(node);
        }
    }

    private void CreateModuleDialog()
    {
        var parent = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog = new ModuleSetupWindow(
            selectParentFolder: true,
            folderPath: parent,
            suggestedName: "New Galaxy Map Module",
            suggestedTag: "NEW_GALAXY_MAP_MODULE",
            suggestedReservations: DefaultReservations(),
            suggestedLoadOrder: NextLoadOrder())
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            CreateModule(result.FolderPath, result.Name, result.Tag, result.Color, result.Reservations, result.LoadOrder);
        }
    }

    private void OpenModuleDialog()
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Open or mount a galaxy-map module folder",
            Multiselect = false
        };
        if (folderDialog.ShowDialog() != true)
        {
            return;
        }

        var folder = folderDialog.FolderName;
        if (File.Exists(Path.Combine(folder, GalaxyMapModuleManifestStore.FileName)))
        {
            OpenExistingModule(folder);
            return;
        }

        try
        {
            var name = new DirectoryInfo(folder).Name;
            var dialog = new ModuleSetupWindow(
                selectParentFolder: false,
                folderPath: folder,
                suggestedName: name,
                suggestedTag: SuggestedTag(name),
                suggestedReservations: InferReservations(folder),
                suggestedLoadOrder: NextLoadOrder())
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.Result is { } result)
            {
                MountReadOnlyModule(folder, result.Name, result.Tag, result.Color, result.Reservations, result.LoadOrder);
            }
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    private void EditModule(GalaxyMapModule module)
    {
        if (Workspace is null || module.IsBaseGame || module.FolderPath is null)
        {
            return;
        }

        var dialog = new ModuleSetupWindow(
            selectParentFolder: false,
            folderPath: module.FolderPath,
            suggestedName: module.Name,
            suggestedTag: module.Tag,
            suggestedReservations: module.Reservations,
            suggestedLoadOrder: module.LoadOrder,
            isEditing: true,
            suggestedColor: module.Color,
            canSetActive: !module.IsReadOnly,
            isActive: ReferenceEquals(module, ActiveModule),
            setActiveAction: () => SetActiveModule(module),
            unlinkAction: () => UnlinkModule(module))
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
        {
            return;
        }

        try
        {
            UpdateModuleMetadata(module, result.Name, result.Tag, result.Color, result.LoadOrder, result.Reservations);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    public bool SetActiveModule(GalaxyMapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (Workspace is null || module.IsBaseGame || module.IsReadOnly)
        {
            return false;
        }

        try
        {
            Workspace.SetActiveModule(module);
            OnActiveModuleChanged();
            RememberCurrentWorkspace();
            StatusMessage = $"{module.Name} [{module.Tag}] is now the active editing module.";
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    public bool UnlinkModule(GalaxyMapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (Workspace is null || module.IsBaseGame ||
            Workspace.ModuleLayers.All(layer => !ReferenceEquals(layer.Module, module)))
        {
            return false;
        }

        var hasStagedChanges = _dirtyTables.ContainsKey(module.Tag) ||
                               _dirtyModuleMetadata.Contains(module.Tag) ||
                               _pendingTextures.Keys.Any(key =>
                                   string.Equals(key.ModuleTag, module.Tag, StringComparison.OrdinalIgnoreCase));
        var stagedWarning = hasStagedChanges
            ? "\n\nThis module has staged changes. Unlinking it will discard those changes."
            : string.Empty;
        if (!Confirm(
                $"Unlink {module.Name} [{module.Tag}] from this workspace? Its folder and files will not be deleted.{stagedWarning}"))
        {
            StatusMessage = "Module unlink cancelled.";
            return false;
        }

        var layer = Workspace.ModuleLayers.Single(candidate => ReferenceEquals(candidate.Module, module));
        var wasActive = ReferenceEquals(module, ActiveModule);
        try
        {
            Workspace.Unmount(module);
            if (wasActive)
            {
                var fallback = Workspace.Modules
                    .Where(candidate => !candidate.IsReadOnly)
                    .OrderByDescending(candidate => candidate.LoadOrder)
                    .ThenBy(candidate => candidate.Tag, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                Workspace.SetActiveModule(fallback);
            }

            try
            {
                RememberCurrentWorkspace();
            }
            catch
            {
                Workspace.Mount(layer);
                if (wasActive)
                {
                    Workspace.SetActiveModule(module);
                }

                throw;
            }

            _dirtyTables.Remove(module.Tag);
            _dirtyModuleMetadata.Remove(module.Tag);
            foreach (var key in _pendingTextures.Keys.Where(key =>
                         string.Equals(key.ModuleTag, module.Tag, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _pendingTextures.Remove(key);
            }

            if (string.Equals(_preferredInstanceTag, module.Tag, StringComparison.OrdinalIgnoreCase))
            {
                _preferredInstanceTag = null;
                _inspectPhysicalInstance = false;
            }

            ClearHistory();
            RefreshWorkspace(null, ViewContext.Galaxy,
                $"Unlinked {module.Name} [{module.Tag}] from the workspace. Module files were left untouched.",
                recompose: false);
            NotifyPendingChanges();
            ErrorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            OnActiveModuleChanged();
            return Fail($"The module could not be unlinked: {exception.Message}");
        }
    }

    public bool UpdateModuleMetadata(
        GalaxyMapModule module,
        string name,
        string tag,
        ModuleColor color,
        int loadOrder,
        ModuleIdReservations reservations)
    {
        if (Workspace is null || module.IsBaseGame)
        {
            return false;
        }

        try
        {
            EnsureUndoSnapshot();
            var replacement = module.With(name, tag, color, loadOrder, reservations);
            Workspace.ReplaceModule(module, replacement);
            MigrateDirtyModuleTag(module.Tag, replacement.Tag);
            MarkMetadataDirty(replacement);
            _preferredInstanceTag = string.Equals(_preferredInstanceTag, module.Tag, StringComparison.OrdinalIgnoreCase)
                ? replacement.Tag
                : _preferredInstanceTag;
            RefreshWorkspace(_selectedNode?.Model?.Key, CaptureView(),
                $"Staged metadata changes for {replacement.Name} [{replacement.Tag}].",
                recompose: false);
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    private void MigrateDirtyModuleTag(string oldTag, string newTag)
    {
        if (string.Equals(oldTag, newTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_dirtyTables.Remove(oldTag, out var tables))
        {
            _dirtyTables[newTag] = tables;
        }

        if (_dirtyModuleMetadata.Remove(oldTag))
        {
            _dirtyModuleMetadata.Add(newTag);
        }

        foreach (var pair in _pendingTextures.Where(pair =>
                     string.Equals(pair.Key.ModuleTag, oldTag, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _pendingTextures.Remove(pair.Key);
            _pendingTextures[(newTag, pair.Key.ClusterRowId)] = pair.Value;
        }
    }

    private void RememberCurrentWorkspace()
    {
        if (Workspace is null || _isRestoringWorkspace)
        {
            return;
        }

        var mounted = Workspace.Modules
            .OrderBy(module => module.LoadOrder)
            .ThenBy(module => module.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(RememberedModule.FromModule)
            .ToList();
        foreach (var missing in _missingRememberedModules.Where(missing => mounted.All(module =>
                     !string.Equals(module.FolderPath, missing.FolderPath, StringComparison.OrdinalIgnoreCase))))
        {
            mounted.Add(missing);
        }

        _workspaceStore.Save(mounted, ActiveModule?.Tag);
    }

    private void ClearStartupIssueFor(string tag, string? folderPath = null)
    {
        _missingRememberedModules.RemoveAll(module =>
            string.Equals(module.DiagnosticTag, tag, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(folderPath) &&
             string.Equals(module.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)));
        _startupDiagnostics.RemoveAll(item =>
            string.Equals(item.ModuleTag, tag, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(folderPath) &&
             item.Code.StartsWith("WORKSPACE-MODULE-", StringComparison.Ordinal) &&
             item.Message.Contains(folderPath, StringComparison.OrdinalIgnoreCase)));
        if (_startupDiagnostics.Count == 0)
        {
            ErrorMessage = string.Empty;
        }
    }

    private void OnActiveModuleChanged()
    {
        OnPropertyChanged(nameof(ActiveModule));
        OnPropertyChanged(nameof(HasActiveModule));
        OnPropertyChanged(nameof(ActiveModuleDisplay));
        OnPropertyChanged(nameof(ActiveModuleColor));
        RaiseCommandStates();
        RefreshModuleBarItems();
    }

    private ModuleIdReservations InferReservations(string folder)
    {
        if (Workspace is null)
        {
            return ModuleIdReservations.Empty;
        }

        var preview = new GalaxyMapModule(
            "Module preview",
            $"PREVIEW_{Guid.NewGuid():N}"[..16],
            ModuleColor.Magenta,
            folder,
            isReadOnly: true,
            loadOrder: NextLoadOrder(),
            ModuleIdReservations.Empty);
        var layer = _loader.LoadPartFolder(folder, preview);

        RowIdRange? NewRange(GalaxyMapTable table)
        {
            var newIds = layer.Rows(table)
                .Where(row => Workspace.Resolve(row.Key) is null)
                .Select(row => row.RowId)
                .OrderBy(rowId => rowId)
                .ToArray();
            return newIds.Length == 0 ? null : new RowIdRange(newIds[0], newIds[^1]);
        }

        return new ModuleIdReservations(
            NewRange(GalaxyMapTable.Cluster),
            NewRange(GalaxyMapTable.System),
            NewRange(GalaxyMapTable.Planet),
            NewRange(GalaxyMapTable.Map),
            NewRange(GalaxyMapTable.Relay));
    }

    private void AddCluster()
        => CreateFactoryRow(
            GalaxyMapTable.Cluster,
            factory => factory.CreateCluster(),
            row => new ViewContext(row.RowId, null),
            "Created a new Cluster in the active module.");

    private void AddSystem()
    {
        if (CurrentCluster is not { } cluster)
        {
            return;
        }

        CreateFactoryRow(
            GalaxyMapTable.System,
            factory => factory.CreateSystem(cluster),
            row => new ViewContext(((GalaxySystem)row).ClusterRowId, row.RowId),
            "Created a new System in the active module.");
    }

    private void AddPlanet()
    {
        if (CurrentSystem is not { } system || Workspace?.ActiveLayer is not { } layer)
        {
            return;
        }

        PlanetCreationRequest? request;
        if (Application.Current?.MainWindow is null)
        {
            var defaults = new Planet();
            GalaxyMapDefaults.ApplyPlanetTemplate(defaults, PlanetCreationTemplate.GenericPlanet);
            request = new PlanetCreationRequest(
                PlanetCreationTemplate.GenericPlanet,
                GalaxyMapDefaults.DefaultPlanetName(PlanetCreationTemplate.GenericPlanet),
                0,
                defaults.Scale,
                null);
        }
        else
        {
            var dialog = new PlanetCreationWindow { Owner = Application.Current.MainWindow };
            request = dialog.ShowDialog() == true ? dialog.Result : null;
        }
        if (request is null)
        {
            return;
        }

        var existing = new Dictionary<GalaxyMapTable, HashSet<int>>
        {
            [GalaxyMapTable.Planet] = layer.Planets.Select(row => row.RowId).ToHashSet(),
            [GalaxyMapTable.Map] = layer.Maps.Select(row => row.RowId).ToHashSet(),
            [GalaxyMapTable.PlotPlanet] = layer.PlotPlanets.Select(row => row.RowId).ToHashSet()
        };
        try
        {
            EnsureUndoSnapshot();
            _isApplying = true;
            var effective = new GalaxyMapRowFactory(Workspace).CreatePlanet(
                system, request.Template, request.NameText, request.Name, request.Scale);
            var planet = (Planet)(layer.Find(effective.Key) ?? throw new InvalidOperationException("The new Planet row was not added to the active module."));
            var tables = new HashSet<GalaxyMapTable> { GalaxyMapTable.Planet };

            if (request.Template != PlanetCreationTemplate.AsteroidBelt && request.Destination is { } destination)
            {
                var mapId = new ModuleIdAllocator(Workspace).NextAvailable(layer.Module, GalaxyMapTable.Map);
                var map = new MapEntry
                {
                    RowId = mapId,
                    MapName = destination.MapName,
                    StartPoint = destination.StartPoint
                };
                PrepareNewRow(layer, map);
                layer.Upsert(map);
                planet.MapRowId = mapId;
                planet.Event = destination.Event;
                planet.ButtonLabel = destination.ButtonLabel;
                DirtyColumns(planet, "Map", "Event", "ButtonLabel");
                tables.Add(GalaxyMapTable.Map);

                if (destination.AddPlotPlanet)
                {
                    layer.Upsert(CreatePlotPlanetRow(layer, planet));
                    tables.Add(GalaxyMapTable.PlotPlanet);
                }
            }

            foreach (var table in tables)
            {
                MarkTableDirty(layer.Module, table);
            }
            RefreshWorkspace(planet.Key, new ViewContext(system.ClusterRowId, system.RowId),
                $"Staged: created {request.Template} Planet row {planet.RowId}.");
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            foreach (var (table, ids) in existing)
            foreach (var row in layer.Rows(table).Where(row => !ids.Contains(row.RowId)).ToArray())
            {
                layer.Remove(row);
            }
            Workspace.Recompose();
            RefreshWorkspace(null, new ViewContext(system.ClusterRowId, system.RowId), null, recompose: false);
            Fail(exception.Message);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void CloneRow(GalaxyMapRow source)
    {
        if (Workspace is null || ActiveModule is null) { Fail("Create or select a writable module before cloning content."); return; }
        try
        {
            var suggestedId = new ModuleIdAllocator(Workspace).NextAvailable(ActiveModule, source.Table);
            var prefix = source is Cluster ? "Cluster" : source is GalaxySystem ? "System" : "Planet";
            var suggestedLabel = NextLabel(prefix, source switch
            {
                GalaxySystem s => s.Cluster?.Systems.Select(x => x.Label) ?? [],
                Planet p => p.System?.Planets.Select(x => x.Label) ?? [],
                _ => Document?.Clusters.Select(x => x.Label) ?? []
            });
            var dialog = new CloneContentWindow(source, suggestedId, suggestedLabel) { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() == true && dialog.Result is { } request) CloneRow(source, request);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception)) { Fail(exception.Message); }
    }

    public bool CloneRow(GalaxyMapRow source, CloneContentRequest request)
    {
        if (Workspace?.ActiveLayer is not { } layer || ActiveModule is null) return Fail("A writable active module is required.");
        try
        {
            ValidateNewId(source.Table, request.RowId);
            if (string.IsNullOrWhiteSpace(request.Label) || string.IsNullOrWhiteSpace(request.NameText))
                throw new InvalidOperationException("A unique label and display name are required.");
            var duplicateLabel = source switch
            {
                Cluster => Document!.Clusters.Any(row => string.Equals(row.Label, request.Label, StringComparison.OrdinalIgnoreCase)),
                GalaxySystem system => system.Cluster!.Systems.Any(row => string.Equals(row.Label, request.Label, StringComparison.OrdinalIgnoreCase)),
                Planet planet => planet.System!.Planets.Any(row => string.Equals(row.Label, request.Label, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (duplicateLabel) throw new InvalidOperationException($"The label '{request.Label}' is already used in this scope.");

            var planned = new Dictionary<GalaxyMapTable, HashSet<int>>();
            int Next(GalaxyMapTable table)
            {
                var range = ActiveModule.Reservations.GetRange(table) ?? throw new InvalidOperationException($"No reserved {table} range exists.");
                var occupied = Workspace.Layers.SelectMany(x => x.Rows(table)).Select(x => x.RowId).ToHashSet();
                if (planned.TryGetValue(table, out var already)) occupied.UnionWith(already);
                for (long candidate = range.Start; candidate <= range.End; candidate++) if (!occupied.Contains((int)candidate))
                { (planned.GetValueOrDefault(table) ?? (planned[table] = [])).Add((int)candidate); return (int)candidate; }
                throw new InvalidOperationException($"The reserved {table} range is exhausted.");
            }

            var created = new List<GalaxyMapRow>();
            GalaxyMapRow root;
            if (source is Cluster sourceCluster)
            {
                var cluster = GalaxyMapRowCloner.Clone(sourceCluster); SetIdentity(cluster, request);
                PrepareNewRow(layer, cluster); created.Add(cluster); root = cluster;
                if (request.CloneChildren)
                {
                    var systemIndex = 1;
                    foreach (var sourceSystem in sourceCluster.Systems)
                    {
                        var system = GalaxyMapRowCloner.Clone(sourceSystem); system.RowId = Next(GalaxyMapTable.System);
                        system.Label = $"System{systemIndex++:D2}"; system.ClusterRowId = cluster.RowId; PrepareNewRow(layer, system); created.Add(system);
                        ClonePlanets(sourceSystem, system, cluster.Label, created, layer, Next);
                    }
                }
            }
            else if (source is GalaxySystem sourceSystem)
            {
                var system = GalaxyMapRowCloner.Clone(sourceSystem); SetIdentity(system, request); PrepareNewRow(layer, system); created.Add(system); root = system;
                if (request.CloneChildren) ClonePlanets(sourceSystem, system, sourceSystem.Cluster?.Label ?? "", created, layer, Next);
            }
            else if (source is Planet sourcePlanet)
            {
                var planet = GalaxyMapRowCloner.Clone(sourcePlanet); SetIdentity(planet, request);
                planet.ActiveWorld = DeriveActiveWorld(sourcePlanet.System?.Cluster?.Label, sourcePlanet.System?.Label, planet.Label);
                ClonePlanetLinks(sourcePlanet, planet, created, layer, Next); PrepareNewRow(layer, planet); created.Insert(0, planet); root = planet;
            }
            else throw new InvalidOperationException("Only Clusters, Systems and Planets can be cloned.");

            var rootView = root switch
            {
                Cluster c => new ViewContext(c.RowId, null),
                GalaxySystem s => new ViewContext(s.ClusterRowId, s.RowId),
                Planet p => new ViewContext((source as Planet)?.System?.ClusterRowId, p.SystemRowId),
                _ => ViewContext.Galaxy
            };
            ExecuteLayerMutation(created.Select(x => x.Key), () => { foreach (var row in created) layer.Upsert(row); },
                created.Select(x => x.Table), root.Key,
                rootView,
                $"Cloned {source.Table} row {source.RowId} as row {root.RowId} with {created.Count - 1} child/link row(s).");
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception)) { return Fail(exception.Message); }
    }

    private static void ClonePlanets(GalaxySystem source, GalaxySystem target, string clusterLabel, List<GalaxyMapRow> created,
        GalaxyMapLayer layer, Func<GalaxyMapTable, int> next)
    {
        var planetIndex = 1;
        foreach (var old in source.Planets)
        {
            var planet = GalaxyMapRowCloner.Clone(old); planet.RowId = next(GalaxyMapTable.Planet); planet.Label = $"Planet{planetIndex++:D2}";
            planet.SystemRowId = target.RowId; planet.ActiveWorld = DeriveActiveWorld(clusterLabel, target.Label, planet.Label);
            ClonePlanetLinks(old, planet, created, layer, next); PrepareNewRow(layer, planet); created.Add(planet);
        }
    }

    private static void ClonePlanetLinks(Planet source, Planet target, List<GalaxyMapRow> created, GalaxyMapLayer layer, Func<GalaxyMapTable, int> next)
    {
        if (source.PlotPlanet is { } oldPlot)
        {
            var plot = GalaxyMapRowCloner.Clone(oldPlot); plot.RowId = target.RowId; plot.Code = target.ActiveWorld; plot.Name = target.Name; plot.NameText = target.NameText;
            PrepareNewRow(layer, plot); created.Add(plot);
        }
        if (source.LinkedMap is { } oldMap)
        {
            var map = GalaxyMapRowCloner.Clone(oldMap); map.RowId = next(GalaxyMapTable.Map); target.MapRowId = map.RowId; PrepareNewRow(layer, map); created.Add(map);
        }
        else target.MapRowId = -1;
    }

    private void ValidateNewId(GalaxyMapTable table, int rowId)
    {
        var range = ActiveModule!.Reservations.GetRange(table);
        if (range is null || !range.Value.Contains(rowId)) throw new InvalidOperationException($"Row ID {rowId} is outside {ActiveModule.Tag}'s reserved {table} range ({range?.ToString() ?? "none"}).");
        if (Workspace!.Layers.SelectMany(layer => layer.Rows(table)).Any(row => row.RowId == rowId)) throw new InvalidOperationException($"{table} row ID {rowId} is already in use.");
    }

    private static void SetIdentity(GalaxyMapRow row, CloneContentRequest request)
    {
        row.RowId = request.RowId;
        switch (row)
        {
            case Cluster c: c.Label = request.Label; c.Name = request.Name; c.NameText = request.NameText; break;
            case GalaxySystem s: s.Label = request.Label; s.Name = request.Name; s.NameText = request.NameText; break;
            case Planet p: p.Label = request.Label; p.Name = request.Name; p.NameText = request.NameText; break;
        }
    }

    private static string NextLabel(string prefix, IEnumerable<string> labels)
    {
        var max = labels.Select(label => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(label[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty().Max();
        return $"{prefix}{max + 1:D2}";
    }

    private static int DeriveActiveWorld(string? cluster, string? system, string planet)
    {
        static int Suffix(string? value) { var digits = new string((value ?? "").Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()); return int.TryParse(digits, out var n) ? n : 0; }
        return checked(Suffix(cluster) * 10_000 + Suffix(system) * 100 + Suffix(planet));
    }

    private void CreateFactoryRow(
        GalaxyMapTable table,
        Func<GalaxyMapRowFactory, GalaxyMapRow> create,
        Func<GalaxyMapRow, ViewContext> view,
        string message)
    {
        if (Workspace?.ActiveLayer is not { } layer)
        {
            Fail("Create or open a writable module before adding rows.");
            return;
        }

        var existingIds = layer.Rows(table).Select(row => row.RowId).ToHashSet();
        GalaxyMapRow? created = null;
        try
        {
            EnsureUndoSnapshot();
            _isApplying = true;
            created = create(new GalaxyMapRowFactory(Workspace));
            MarkTableDirty(layer.Module, table);
            RefreshWorkspace(created.Key, view(created), $"Staged: {message}", recompose: false);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            foreach (var row in layer.Rows(table).Where(row => !existingIds.Contains(row.RowId)).ToArray())
            {
                layer.Remove(row);
            }

            Workspace.Recompose();
            RefreshWorkspace(null, CaptureView(), null, recompose: false);
            Fail(exception.Message);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void ToggleCoordinateGrid() => IsCoordinateGridVisible = !IsCoordinateGridVisible;

    public void SetShiftDragMode(bool enabled)
        => IsShiftDragMode = enabled;

    public bool BeginCoordinateDrag(GalaxyMapRow row)
    {
        if (Workspace is null || row is not (Cluster or GalaxySystem or Planet))
        {
            return false;
        }

        if (_coordinateDrag is not null)
        {
            CompleteCoordinateDrag(cancel: true);
        }

        var layer = MovableOwningLayer(row);
        if (layer is null)
        {
            return Fail($"Clone this {row.Table} into a writable module before moving it.");
        }

        var (x, y) = Coordinates(row);
        _coordinateDrag = new CoordinateDragSession(row, layer, x, y, CaptureView());
        OnPropertyChanged(nameof(HasActiveCoordinateDrag));
        ErrorMessage = string.Empty;
        StatusMessage = $"Dragging {RowDisplayName(row)}. Release Shift or the mouse button to stage its coordinates.";
        return true;
    }

    public Point PreviewCoordinateDrag(GalaxyMapRow row, Point normalizedPosition)
    {
        var rounded = CoordinateGridLayer.RoundNormalizedPosition(normalizedPosition);
        if (_coordinateDrag is not { } session || session.Row.Key != row.Key)
        {
            return rounded;
        }

        try
        {
            _isApplying = true;
            SetCoordinates(session.Row, rounded.X, rounded.Y);
            if (CurrentViewModel is MapViewModelBase map)
            {
                map.Refresh();
            }
        }
        finally
        {
            _isApplying = false;
        }

        return rounded;
    }

    public bool CompleteCoordinateDrag(bool cancel = false)
    {
        if (_coordinateDrag is not { } session)
        {
            return false;
        }

        var (finalX, finalY) = Coordinates(session.Row);
        try
        {
            _isApplying = true;
            SetCoordinates(session.Row, session.OriginalX, session.OriginalY);
            if (CurrentViewModel is MapViewModelBase map)
            {
                map.Refresh();
            }
        }
        finally
        {
            _isApplying = false;
            _coordinateDrag = null;
            OnPropertyChanged(nameof(HasActiveCoordinateDrag));
        }

        if (cancel)
        {
            StatusMessage = $"Cancelled coordinate move for {RowDisplayName(session.Row)}.";
            return true;
        }

        if (finalX.Equals(session.OriginalX) && finalY.Equals(session.OriginalY))
        {
            StatusMessage = $"{RowDisplayName(session.Row)} stayed at {finalX:0.00}, {finalY:0.00}.";
            return true;
        }

        if (Workspace is null || session.Layer.Find(session.Row.Key) is not { } physical)
        {
            return Fail("The module-owned row disappeared before its coordinate move could be staged.");
        }

        Workspace.SetActiveModule(session.Layer.Module);
        OnActiveModuleChanged();
        var replacement = GalaxyMapRowCloner.Clone(physical);
        SetCoordinates(replacement, finalX, finalY);
        DirtyColumns(replacement, "X", "Y");
        ExecuteLayerMutation(
            [replacement.Key],
            () => session.Layer.Upsert(replacement),
            [replacement.Table],
            replacement.Key,
            session.View,
            $"Moved {RowDisplayName(replacement)} to X {finalX:0.00}, Y {finalY:0.00}.",
            preserveHierarchy: true,
            refreshModules: false,
            deferValidation: true);
        return !HasError;
    }

    private static (double X, double Y) Coordinates(GalaxyMapRow row) => row switch
    {
        Cluster cluster => (cluster.X, cluster.Y),
        GalaxySystem system => (system.X, system.Y),
        Planet planet => (planet.X, planet.Y),
        _ => throw new ArgumentException($"{row.Table} rows do not have map coordinates.", nameof(row))
    };

    private static void SetCoordinates(GalaxyMapRow row, double x, double y)
    {
        switch (row)
        {
            case Cluster cluster:
                cluster.X = x;
                cluster.Y = y;
                break;
            case GalaxySystem system:
                system.X = x;
                system.Y = y;
                break;
            case Planet planet:
                planet.X = x;
                planet.Y = y;
                break;
            default:
                throw new ArgumentException($"{row.Table} rows do not have map coordinates.", nameof(row));
        }
    }

    private static string RowDisplayName(GalaxyMapRow row) => row switch
    {
        Cluster cluster => cluster.DisplayName,
        GalaxySystem system => system.DisplayName,
        Planet planet => planet.DisplayName,
        _ => $"{row.Table} row {row.RowId}"
    };

    private void ToggleDiagnostics()
    {
        IsDiagnosticsPanelOpen = HasDiagnostics && !IsDiagnosticsPanelOpen;
    }

    public bool CommitPendingChanges()
    {
        if (Workspace is null || !HasPendingChanges)
        {
            return true;
        }

        var failedModules = new List<string>();
        var tags = _dirtyTables.Keys
            .Concat(_dirtyModuleMetadata)
            .Concat(_pendingTextures.Keys.Select(key => key.ModuleTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var tag in tags)
        {
            var layer = Workspace.ModuleLayers.FirstOrDefault(candidate =>
                string.Equals(candidate.Module.Tag, tag, StringComparison.OrdinalIgnoreCase));
            if (layer is null)
            {
                failedModules.Add($"{tag}: module is no longer mounted");
                continue;
            }

            try
            {
                foreach (var pending in _pendingTextures.Where(pair =>
                             string.Equals(pair.Key.ModuleTag, tag, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value))
                {
                    var targetPath = GalaxyMapTextureService.ResolveModuleTexturePath(layer.Module, pending.RelativePath)
                        ?? throw new InvalidOperationException($"Invalid module texture path '{pending.RelativePath}'.");
                    AtomicFileWriter.Write(targetPath, pending.Contents);
                }

                if (_dirtyTables.TryGetValue(tag, out var tables) && tables.Count > 0)
                {
                    _writer.WriteTables(layer, tables);
                }

                if (!layer.Module.IsReadOnly &&
                    (_dirtyModuleMetadata.Contains(tag) || _pendingTextures.Keys.Any(key =>
                        string.Equals(key.ModuleTag, tag, StringComparison.OrdinalIgnoreCase))))
                {
                    _manifestStore.Save(layer.Module);
                }

                _dirtyTables.Remove(tag);
                _dirtyModuleMetadata.Remove(tag);
                foreach (var key in _pendingTextures.Keys.Where(key =>
                             string.Equals(key.ModuleTag, tag, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    _pendingTextures.Remove(key);
                }
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                failedModules.Add($"{tag}: {exception.Message}");
            }
        }

        if (failedModules.Count > 0)
        {
            Fail("Some changes could not be committed: " + string.Join(" | ", failedModules));
            NotifyPendingChanges();
            return false;
        }

        try
        {
            RememberCurrentWorkspace();
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail($"Module files were committed, but the remembered workspace could not be updated: {exception.Message}");
        }
        Workspace.Recompose();
        ClearHistory();
        RefreshWorkspace(_selectedNode?.Model?.Key, CaptureView(), "Committed all staged module changes to CSV.", recompose: false);
        ErrorMessage = string.Empty;
        NotifyPendingChanges();
        return true;
    }

    public void DiscardPendingChanges()
    {
        if (!HasPendingChanges)
        {
            return;
        }

        _dirtyTables.Clear();
        _dirtyModuleMetadata.Clear();
        _pendingTextures.Clear();
        _preferredInstanceTag = null;
        ClearHistory();
        NotifyPendingChanges();
        LoadBuiltIn();
        RestoreRememberedModules();
        StatusMessage = "Discarded all uncommitted changes.";
    }

    public bool RestoreRememberedModules()
    {
        if (Workspace is null)
        {
            return false;
        }

        try
        {
            _isRestoringWorkspace = true;
            _startupDiagnostics.Clear();
            _missingRememberedModules.Clear();
            var remembered = _workspaceStore.Load();
            var loadedLayers = new List<GalaxyMapLayer>();
            var loadedTags = Workspace.Layers.Select(layer => layer.Module.Tag)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in remembered.Modules)
            {
                if (!Directory.Exists(entry.FolderPath))
                {
                    _missingRememberedModules.Add(entry);
                    _startupDiagnostics.Add(new ValidationDiagnostic(
                        "WORKSPACE-MODULE-MISSING",
                        ValidationSeverity.Error,
                        $"Remembered module folder is missing: {entry.FolderPath}",
                        entry.DiagnosticTag));
                    continue;
                }

                try
                {
                    var manifestPath = Path.Combine(entry.FolderPath, GalaxyMapModuleManifestStore.FileName);
                    var module = File.Exists(manifestPath)
                        ? _manifestStore.Load(entry.FolderPath)
                        : entry.UnmanifestedReadOnlyModule?.ToModule(entry.FolderPath)
                          ?? throw new GalaxyMapLoadException(
                              $"The remembered module folder has no {GalaxyMapModuleManifestStore.FileName}: {entry.FolderPath}");
                    if (!loadedTags.Add(module.Tag))
                    {
                        throw new InvalidOperationException($"A module tagged {module.Tag} is already mounted.");
                    }

                    loadedLayers.Add(_loader.LoadPartFolder(entry.FolderPath, module));
                }
                catch (Exception exception) when (IsExpectedOperationFailure(exception))
                {
                    _startupDiagnostics.Add(new ValidationDiagnostic(
                        "WORKSPACE-MODULE-LOAD",
                        ValidationSeverity.Error,
                        $"Remembered module could not be loaded: {exception.Message}",
                        entry.DiagnosticTag));
                }
            }

            Workspace.MountRange(loadedLayers);

            var active = Workspace.Modules.FirstOrDefault(module => !module.IsReadOnly &&
                string.Equals(module.Tag, remembered.ActiveModuleTag, StringComparison.OrdinalIgnoreCase))
                ?? Workspace.Modules.Where(module => !module.IsReadOnly).OrderByDescending(module => module.LoadOrder).FirstOrDefault();
            if (active is not null)
            {
                Workspace.SetActiveModule(active);
            }

            var startupError = _startupDiagnostics.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, _startupDiagnostics.Select(item => item.Message));
            RefreshWorkspace(null, ViewContext.Galaxy,
                Workspace.ModuleLayers.Count == 0
                    ? "No remembered modules were mounted."
                    : $"Restored {Workspace.ModuleLayers.Count} remembered module(s).",
                recompose: false);
            ErrorMessage = startupError;
            return _startupDiagnostics.Count == 0;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
        finally
        {
            _isRestoringWorkspace = false;
        }
    }

    public bool RefreshRememberedWorkspace()
    {
        if (HasPendingChanges && !Confirm(
                "Refreshing reloads BASEGAME and every module remembered in workspace.json. Discard all uncommitted changes?"))
        {
            StatusMessage = "Refresh cancelled; staged changes were left intact.";
            return false;
        }

        _dirtyTables.Clear();
        _dirtyModuleMetadata.Clear();
        _pendingTextures.Clear();
        _preferredInstanceTag = null;
        _inspectPhysicalInstance = false;
        ClearHistory();
        NotifyPendingChanges();
        if (!LoadBuiltIn())
        {
            return false;
        }

        var restoredCleanly = RestoreRememberedModules();
        if (restoredCleanly)
        {
            StatusMessage = Workspace?.ModuleLayers.Count > 0
                ? $"Refreshed BASEGAME and {Workspace.ModuleLayers.Count} remembered module(s); validation is up to date."
                : "Refreshed BASEGAME; no remembered modules were configured.";
        }

        return restoredCleanly;
    }

    private GalaxyMapModule? ChooseEditTarget(GalaxyMapRow row)
    {
        if (Workspace is null)
        {
            return null;
        }

        var candidates = Workspace.Modules.Where(module => !module.IsReadOnly && !module.IsBaseGame)
            .OrderByDescending(module => module.LoadOrder)
            .ThenBy(module => module.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            Fail("Create or open a writable module before editing source rows.");
            return null;
        }

        if (_editTargetSelector is not null)
        {
            return _editTargetSelector(row, candidates);
        }

        if (Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            var dialog = new ModuleTargetWindow(candidates, ActiveModule) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.SelectedModule : null;
        }

        return ActiveModule is not null && candidates.Contains(ActiveModule)
            ? ActiveModule
            : candidates[0];
    }

    private GalaxyMapModule? ResolveWritableTarget(GalaxyMapRow row)
    {
        if (Workspace is null)
        {
            return null;
        }

        if (row.Origin?.Module is { IsReadOnly: false, IsBaseGame: false } origin)
        {
            return Workspace.Modules.FirstOrDefault(module =>
                string.Equals(module.Tag, origin.Tag, StringComparison.OrdinalIgnoreCase));
        }

        return ChooseEditTarget(row);
    }

    private void MarkTableDirty(GalaxyMapModule module, GalaxyMapTable table)
    {
        if (!_dirtyTables.TryGetValue(module.Tag, out var tables))
        {
            tables = [];
            _dirtyTables[module.Tag] = tables;
        }

        if (tables.Add(table))
        {
            NotifyPendingChanges();
        }
    }

    private void MarkTablesDirty(GalaxyMapModule module, IEnumerable<GalaxyMapTable> dirtyTables)
    {
        if (!_dirtyTables.TryGetValue(module.Tag, out var tables))
        {
            tables = [];
            _dirtyTables[module.Tag] = tables;
        }

        var changed = false;
        foreach (var table in dirtyTables)
        {
            changed |= tables.Add(table);
        }

        if (changed)
        {
            NotifyPendingChanges();
        }
    }

    private void MarkMetadataDirty(GalaxyMapModule module)
    {
        _dirtyModuleMetadata.Add(module.Tag);
        NotifyPendingChanges();
    }

    private void NotifyPendingChanges()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PendingChangeCount));
        OnPropertyChanged(nameof(CommitButtonText));
        CommitCommand.RaiseCanExecuteChanged();
        DiscardChangesCommand.RaiseCanExecuteChanged();
        RefreshModuleBarItems();
    }

    private void NavigateToDiagnostic(ValidationDiagnostic? diagnostic)
    {
        if (diagnostic?.RowId is not { } rowId ||
            !Enum.TryParse<GalaxyMapTable>(diagnostic.TableName, ignoreCase: true, out var table) ||
            Document is null)
        {
            return;
        }

        GalaxyMapRow? row = Workspace?.Resolve(new GalaxyMapRowKey(table, rowId)) ?? table switch
        {
            GalaxyMapTable.Cluster => Document.ClustersByRowId.GetValueOrDefault(rowId),
            GalaxyMapTable.System => Document.SystemsByRowId.GetValueOrDefault(rowId),
            GalaxyMapTable.Planet => Document.PlanetsByRowId.GetValueOrDefault(rowId),
            GalaxyMapTable.PlotPlanet => Document.PlotPlanetsByRowId.GetValueOrDefault(rowId),
            GalaxyMapTable.Map => Document.MapsByRowId.GetValueOrDefault(rowId),
            GalaxyMapTable.Relay => Document.Relays.FirstOrDefault(candidate => candidate.RowId == rowId),
            _ => null
        };
        if (row is null)
        {
            return;
        }

        if (_nodesByKey.TryGetValue(row.Key, out var directNode))
        {
            SelectFromHierarchy(directNode);
        }
        else if (row is PlotPlanetEntry && _nodesByKey.TryGetValue(
                     new GalaxyMapRowKey(GalaxyMapTable.Planet, row.RowId), out var planetNode))
        {
            SelectFromHierarchy(planetNode);
        }
        else if (row is MapEntry && Document.Planets.FirstOrDefault(planet => planet.MapRowId == row.RowId) is { } linkedPlanet &&
                 _nodesByKey.TryGetValue(linkedPlanet.Key, out var linkedPlanetNode))
        {
            SelectFromHierarchy(linkedPlanetNode);
        }
        else
        {
            NavigateGalaxy();
            Inspector.Inspect(row);
        }

        StatusMessage = $"Selected {diagnostic.Location}: {diagnostic.Message}";
    }

    private void AttachWorkspace(GalaxyMapWorkspace workspace, string status)
    {
        Workspace = workspace;
        RefreshWorkspace(null, ViewContext.Galaxy, status, recompose: false);
        RaiseCommandStates();
    }

    private void RefreshWorkspace(
        GalaxyMapRowKey? selectionKey,
        ViewContext view,
        string? status,
        bool recompose = true,
        bool preserveHierarchy = false,
        bool refreshModules = true,
        bool deferValidation = false)
    {
        if (Workspace is null)
        {
            return;
        }

        if (recompose)
        {
            Workspace.Recompose();
        }
        if (refreshModules)
        {
            RefreshMountedModules();
        }
        AttachDocument(Workspace.EffectiveDocument, selectionKey, view, preserveHierarchy);
        if (deferValidation)
        {
            ScheduleValidation(status);
        }
        else
        {
            UpdateValidation();
        }
        UpdateDocumentSummary(status);
        if (refreshModules)
        {
            OnActiveModuleChanged();
        }
        OnPropertyChanged(nameof(SourceFolder));
        RaiseCommandStates();
    }

    private void AttachDocument(
        GalaxyMapDocument document,
        GalaxyMapRowKey? selectionKey,
        ViewContext view,
        bool preserveHierarchy = false)
    {
        PendingRelaySource = null;
        _pendingRelayReplacement = null;
        _pendingRelayTargetModule = null;
        if (Document is not null)
        {
            foreach (var row in AllRows(Document))
            {
                row.PropertyChanged -= ModelOnPropertyChanged;
            }
        }

        var canPreserveHierarchy = preserveHierarchy && CanRetargetHierarchy(document);
        Document = document;
        foreach (var row in AllRows(document))
        {
            row.PropertyChanged += ModelOnPropertyChanged;
        }

        if (canPreserveHierarchy)
        {
            RetargetHierarchy(document);
        }
        else
        {
            BuildHierarchy();
        }
        if (view.SystemRowId is { } systemId &&
            document.SystemsByRowId.TryGetValue(systemId, out var system) &&
            system.Cluster is not null)
        {
            NavigateSystem(system);
        }
        else if (view.ClusterRowId is { } clusterId &&
                 document.ClustersByRowId.TryGetValue(clusterId, out var cluster))
        {
            NavigateCluster(cluster);
        }
        else
        {
            NavigateGalaxy();
        }

        if (selectionKey is { } key && _nodesByKey.TryGetValue(key, out var node))
        {
            SelectNodeCore(node);
        }
        else
        {
            ClearRowInstanceTabs();
            Inspector.Inspect(null);
        }

        NavigateGalaxyCommand.RaiseCanExecuteChanged();
    }

    private bool CanRetargetHierarchy(GalaxyMapDocument document)
    {
        if (_galaxyRoot is null || HierarchyRoots.Count != 1 || !ReferenceEquals(HierarchyRoots[0], _galaxyRoot))
        {
            return false;
        }

        var systems = document.Clusters.SelectMany(cluster => cluster.Systems).ToArray();
        var planets = systems.SelectMany(system => system.Planets).ToArray();
        if (_nodesByKey.Count != document.Clusters.Count + systems.Length + planets.Length)
        {
            return false;
        }

        foreach (var cluster in document.Clusters)
        {
            if (!_nodesByKey.TryGetValue(cluster.Key, out var node) || !ReferenceEquals(node.Parent, _galaxyRoot))
            {
                return false;
            }
        }

        foreach (var system in systems)
        {
            if (system.Cluster is null || !_nodesByKey.TryGetValue(system.Key, out var node) ||
                node.Parent?.Model?.Key != system.Cluster.Key)
            {
                return false;
            }
        }

        foreach (var planet in planets)
        {
            if (planet.System is null || !_nodesByKey.TryGetValue(planet.Key, out var node) ||
                node.Parent?.Model?.Key != planet.System.Key)
            {
                return false;
            }
        }

        return true;
    }

    private void RetargetHierarchy(GalaxyMapDocument document)
    {
        _nodes.Clear();
        foreach (var row in document.Clusters.Cast<GalaxyMapRow>()
                     .Concat(document.Clusters.SelectMany(cluster => cluster.Systems))
                     .Concat(document.Clusters.SelectMany(cluster => cluster.Systems).SelectMany(system => system.Planets)))
        {
            var node = _nodesByKey[row.Key];
            node.UpdateItem(row, Workspace?.GetOverrideChain(row.Key).Count ?? 1, CanMoveOwnedRow(row));
            _nodes[row] = node;
        }
    }

    private void BuildHierarchy()
    {
        DisposeHierarchy();
        if (Document is null)
        {
            return;
        }

        _galaxyRoot = HierarchyNodeViewModel.CreateGalaxyRoot(SelectFromHierarchy);
        HierarchyRoots.Add(_galaxyRoot);
        foreach (var cluster in Document.Clusters)
        {
            var clusterNode = CreateNode(cluster, _galaxyRoot);
            _galaxyRoot.Children.Add(clusterNode);
            foreach (var system in cluster.Systems)
            {
                var systemNode = CreateNode(system, clusterNode);
                clusterNode.Children.Add(systemNode);
                foreach (var planet in system.Planets)
                {
                    systemNode.Children.Add(CreateNode(planet, systemNode));
                }
            }
        }
        _selectedNode = null;
    }

    private HierarchyNodeViewModel CreateNode(GalaxyMapRow row, HierarchyNodeViewModel? parent = null)
    {
        var instanceCount = Workspace?.GetOverrideChain(row.Key).Count ?? 1;
        var node = new HierarchyNodeViewModel(
            row,
            SelectFromHierarchy,
            parent,
            instanceCount,
            CloneRow,
            DeleteRow,
            MoveRowDialog,
            CanMoveOwnedRow(row));
        _nodes[row] = node;
        _nodesByKey[row.Key] = node;
        return node;
    }

    private void SelectFromHierarchy(HierarchyNodeViewModel node)
    {
        if (HandlePendingRelaySelection(node))
        {
            return;
        }

        if (_selectedNode?.Model?.Key != node.Model?.Key)
        {
            _preferredInstanceTag = null;
            _inspectPhysicalInstance = false;
        }
        SelectNodeCore(node);
        if (node.IsGalaxyRoot)
        {
            NavigateGalaxy();
            return;
        }

        switch (node.Item)
        {
            case Cluster cluster:
                NavigateCluster(cluster);
                break;
            case GalaxySystem system:
                NavigateSystem(system);
                break;
            case Planet planet when planet.System is not null:
                NavigateSystem(planet.System);
                break;
        }
    }

    private void SelectFromMap(HierarchyNodeViewModel node)
    {
        if (!HandlePendingRelaySelection(node))
        {
            if (_selectedNode?.Model?.Key != node.Model?.Key)
            {
                _preferredInstanceTag = null;
                _inspectPhysicalInstance = false;
            }
            SelectNodeCore(node);
        }
    }

    private bool HandlePendingRelaySelection(HierarchyNodeViewModel node)
    {
        if (PendingRelaySource is not { } source)
        {
            return false;
        }

        if (node.Item is Cluster target)
        {
            if (_pendingRelayReplacement is { } replacement)
            {
                CompleteRelayRedirect(source, replacement, target);
            }
            else
            {
                CompleteRelayCreation(source, target);
            }
            return true;
        }

        CancelRelayCreation();
        return false;
    }

    private void SelectNodeCore(HierarchyNodeViewModel node)
    {
        if (!ReferenceEquals(_selectedNode, node))
        {
            var previous = _selectedNode;
            _selectedNode = node;
            previous?.SetSelectedSilently(false);
        }

        if (!node.IsSelected)
        {
            node.SetSelectedSilently(true);
        }

        node.ExpandAncestors();
        if (node.IsGalaxyRoot)
        {
            ClearRowInstanceTabs();
            Inspector.InspectGalaxy();
        }
        else
        {
            ShowInspectorForRow(node.Model!);
        }
    }

    private void ShowInspectorForRow(GalaxyMapRow effectiveRow)
    {
        if (_inspectedPhysicalRow is not null)
        {
            _inspectedPhysicalRow.PropertyChanged -= ModelOnPropertyChanged;
            _inspectedPhysicalRow = null;
        }

        RowInstanceTabs.Clear();
        var chain = Workspace?.GetOverrideChain(effectiveRow.Key) ?? [];
        var effectiveTag = effectiveRow.Origin?.ModuleTag ?? GalaxyMapModule.BaseGameTag;
        var selectedTag = chain.Any(row => string.Equals(row.Origin?.ModuleTag, _preferredInstanceTag,
                StringComparison.OrdinalIgnoreCase))
            ? _preferredInstanceTag!
            : effectiveTag;
        foreach (var instance in chain)
        {
            if (instance.Origin?.Module is not { } module)
            {
                continue;
            }

            RowInstanceTabs.Add(new RowInstanceTabViewModel(
                module,
                string.Equals(module.Tag, effectiveTag, StringComparison.OrdinalIgnoreCase),
                string.Equals(module.Tag, selectedTag, StringComparison.OrdinalIgnoreCase),
                selected => SelectRowInstance(effectiveRow.Key, selected)));
        }

        OnPropertyChanged(nameof(HasMultipleRowInstances));
        if (_inspectPhysicalInstance)
        {
            var physical = chain.FirstOrDefault(row =>
                string.Equals(row.Origin?.ModuleTag, selectedTag, StringComparison.OrdinalIgnoreCase));
            if (physical is not null)
            {
                var isWritablePhysical = physical.Origin?.Module is { IsReadOnly: false, IsBaseGame: false };
                var inspectionRow = isWritablePhysical ? physical : GalaxyMapRowCloner.Clone(physical);
                HydrateInspectionRelationships(inspectionRow, effectiveRow);
                _inspectedPhysicalRow = inspectionRow;
                var canCreateOverride = Workspace?.Modules.Any(module => !module.IsReadOnly) == true;
                if (isWritablePhysical || canCreateOverride)
                {
                    inspectionRow.PropertyChanged += ModelOnPropertyChanged;
                }

                Inspector.Inspect(inspectionRow, isWritablePhysical || canCreateOverride);
                return;
            }
        }

        _preferredInstanceTag = effectiveTag;
        Inspector.Inspect(effectiveRow, Workspace?.Modules.Any(module => !module.IsReadOnly) == true);
    }

    private void SelectRowInstance(GalaxyMapRowKey key, GalaxyMapModule module)
    {
        if (Workspace?.Resolve(key) is not { } effective)
        {
            return;
        }

        _preferredInstanceTag = module.Tag;
        _inspectPhysicalInstance = true;
        if (!module.IsReadOnly && !module.IsBaseGame)
        {
            Workspace.SetActiveModule(module);
            OnActiveModuleChanged();
        }

        ShowInspectorForRow(effective);
    }

    private void ClearRowInstanceTabs()
    {
        if (_inspectedPhysicalRow is not null)
        {
            _inspectedPhysicalRow.PropertyChanged -= ModelOnPropertyChanged;
            _inspectedPhysicalRow = null;
        }

        RowInstanceTabs.Clear();
        OnPropertyChanged(nameof(HasMultipleRowInstances));
    }

    private static void HydrateInspectionRelationships(GalaxyMapRow physical, GalaxyMapRow effective)
    {
        switch (physical, effective)
        {
            case (GalaxySystem physicalSystem, GalaxySystem effectiveSystem):
                physicalSystem.Cluster = effectiveSystem.Cluster;
                break;
            case (Planet physicalPlanet, Planet effectivePlanet):
                physicalPlanet.System = effectivePlanet.System;
                physicalPlanet.PlotPlanet = effectivePlanet.PlotPlanet;
                physicalPlanet.LinkedMap = effectivePlanet.LinkedMap;
                break;
            case (RelayConnection physicalRelay, RelayConnection effectiveRelay):
                physicalRelay.StartCluster = effectiveRelay.StartCluster;
                physicalRelay.EndCluster = effectiveRelay.EndCluster;
                break;
        }
    }

    private void EnterCluster(HierarchyNodeViewModel node)
    {
        if (!IsAddingRelay && node.Item is Cluster cluster)
        {
            SelectNodeCore(node);
            NavigateCluster(cluster);
        }
    }

    private void EnterSystem(HierarchyNodeViewModel node)
    {
        if (node.Item is GalaxySystem system)
        {
            SelectNodeCore(node);
            NavigateSystem(system);
        }
    }

    private void NavigateGalaxy()
    {
        if (Document is null)
        {
            CurrentViewModel = null;
            CurrentCluster = null;
            CurrentSystem = null;
            return;
        }

        CurrentCluster = null;
        CurrentSystem = null;
        var clusterNodes = (IReadOnlyList<HierarchyNodeViewModel>?)_galaxyRoot?.Children ?? [];
        CurrentViewModel = new GalaxyViewModel(
            clusterNodes,
            Document.Relays,
            SelectFromMap,
            EnterCluster,
            IsAddingRelay,
            _textures.GetGalaxyTexture());
    }

    private void NavigateCluster(Cluster cluster)
    {
        if (!_nodes.TryGetValue(cluster, out var clusterNode))
        {
            return;
        }

        CurrentCluster = cluster;
        CurrentSystem = null;
        CurrentViewModel = new ClusterViewModel(
            cluster,
            clusterNode.Children,
            SelectFromMap,
            EnterSystem,
            GetClusterTexture(cluster));
    }

    private void NavigateSystem(GalaxySystem system)
    {
        if (!_nodes.TryGetValue(system, out var systemNode))
        {
            return;
        }

        CurrentCluster = system.Cluster;
        CurrentSystem = system;
        var usesNebula = system.ShowNebula == 1 && system.Cluster is not null;
        CurrentViewModel = new SystemViewModel(
            system,
            systemNode.Children,
            SelectFromMap,
            usesNebula ? GetClusterTexture(system.Cluster!) : _textures.GetSystemTexture(),
            usesNebula);
    }

    private void LinkClusterTexture(Cluster cluster)
    {
        if (Workspace is null)
        {
            return;
        }

        var target = cluster.Origin?.Module is { IsReadOnly: false, IsBaseGame: false } writable
            ? Workspace.Modules.FirstOrDefault(module =>
                string.Equals(module.Tag, writable.Tag, StringComparison.OrdinalIgnoreCase))
            : ChooseEditTarget(cluster);
        if (target is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose Cluster background texture",
            Filter = "PNG textures (*.png)|*.png",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            StageClusterTexture(cluster, target, dialog.FileName);
        }
    }

    public bool StageClusterTexture(Cluster cluster, GalaxyMapModule target, string sourcePath)
    {
        if (Workspace is null || target.IsReadOnly || target.IsBaseGame || target.FolderPath is null)
        {
            return Fail("Cluster textures must be stored in a writable module.");
        }

        try
        {
            var contents = File.ReadAllBytes(sourcePath);
            var fileName = Path.GetFileName(sourcePath);
            if (!string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase) ||
                _textures.LoadTextureBytes($"validate:{target.Tag}:{cluster.RowId}:{Guid.NewGuid():N}", contents) is null)
            {
                throw new InvalidOperationException("Choose a valid PNG texture.");
            }

            var relativePath = $"textures/Cluster_{cluster.RowId}_{fileName}";
            EnsureUndoSnapshot();
            var links = new Dictionary<int, string>(target.ClusterTextureLinks)
            {
                [cluster.RowId] = relativePath
            };
            var replacement = target.With(clusterTextureLinks: links);
            Workspace.ReplaceModule(target, replacement);
            _pendingTextures[(replacement.Tag, cluster.RowId)] = new PendingTexture(relativePath, contents, Guid.NewGuid().ToString("N"));
            Workspace.SetActiveModule(replacement);
            MarkMetadataDirty(replacement);
            _preferredInstanceTag = replacement.Tag;
            RefreshWorkspace(cluster.Key, CaptureView(),
                $"Staged module texture {fileName} for {cluster.DisplayName} in {replacement.Tag}.",
                recompose: false);
            return true;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    private System.Windows.Media.ImageSource? GetClusterTexture(Cluster cluster)
    {
        if (Workspace is not null)
        {
            foreach (var layer in Workspace.Layers.Reverse())
            {
                var module = layer.Module;
                if (module.IsBaseGame)
                {
                    continue;
                }

                if (_pendingTextures.TryGetValue((module.Tag, cluster.RowId), out var pending))
                {
                    return _textures.LoadTextureBytes(
                        $"pending:{module.Tag}:{cluster.RowId}:{pending.CacheKey}", pending.Contents);
                }

                var moduleTexture = _textures.GetModuleClusterTexture(module, cluster.RowId);
                if (moduleTexture is not null)
                {
                    return moduleTexture;
                }

                // A Background value names the visual asset, so Clusters which
                // share that value should share an imported replacement too.
                foreach (var linkedClusterId in module.ClusterTextureLinks.Keys)
                {
                    if (Document?.ClustersByRowId.GetValueOrDefault(linkedClusterId) is not { } linkedCluster ||
                        !string.Equals(linkedCluster.Background, cluster.Background, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (_pendingTextures.TryGetValue((module.Tag, linkedClusterId), out var sharedPending))
                    {
                        return _textures.LoadTextureBytes(
                            $"pending:{module.Tag}:{linkedClusterId}:{sharedPending.CacheKey}", sharedPending.Contents);
                    }

                    var sharedTexture = _textures.GetModuleClusterTexture(module, linkedClusterId);
                    if (sharedTexture is not null)
                    {
                        return sharedTexture;
                    }
                }
            }
        }

        return _textures.GetClusterTexture(cluster.Background);
    }

    private void AddPlotPlanet(Planet planet)
    {
        var target = ResolveWritableTarget(planet);
        if (Workspace is null || target is null || planet.PlotPlanet is not null)
        {
            Fail("Create or open a writable module before adding PlotPlanet data.");
            return;
        }

        Workspace.SetActiveModule(target);
        OnActiveModuleChanged();
        var layer = Workspace.ActiveLayer!;

        var plot = CreatePlotPlanetRow(layer, planet);
        ExecuteLayerMutation(
            [plot.Key],
            () => layer.Upsert(plot),
            [GalaxyMapTable.PlotPlanet],
            planet.Key,
            new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId),
            $"Added PlotPlanet row {plot.RowId} to the active module.");
    }

    private void ConfigureLandableDestination(Planet planet)
    {
        if (Workspace is null)
        {
            return;
        }
        if (planet.OrbitRing == 2)
        {
            Fail("Asteroid belts cannot be configured as landable destinations.");
            return;
        }

        var dialog = new LandableDestinationWindow(
            planet.LinkedMap?.MapName ?? string.Empty,
            planet.LinkedMap?.StartPoint ?? string.Empty,
            planet.Event,
            planet.ButtonLabel,
            planet.PlotPlanet is null)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } request)
        {
            return;
        }

        var target = ResolveWritableTarget(planet);
        if (target is null)
        {
            return;
        }
        Workspace.SetActiveModule(target);
        OnActiveModuleChanged();
        var layer = Workspace.ActiveLayer!;

        try
        {
            var planetOverride = layer.Find(planet.Key) is Planet existingPlanet
                ? GalaxyMapRowCloner.Clone(existingPlanet)
                : GalaxyMapRowCloner.CloneForOverride(planet, target) as Planet
                  ?? throw new InvalidOperationException("The Planet override could not be created.");
            planetOverride.Origin = new GalaxyMapRowOrigin(target, Workspace.GetOverrideChain(planet.Key).Any(candidate =>
                !string.Equals(candidate.Origin?.ModuleTag, target.Tag, StringComparison.OrdinalIgnoreCase)));

            MapEntry map;
            var mapIsShared = planet.LinkedMap is not null &&
                              Document?.Planets.Any(other => other.RowId != planet.RowId && other.MapRowId == planet.MapRowId) == true;
            if (planet.LinkedMap is { } linkedMap && !mapIsShared)
            {
                map = layer.Find(linkedMap.Key) is MapEntry existingMap
                    ? GalaxyMapRowCloner.Clone(existingMap)
                    : GalaxyMapRowCloner.CloneForOverride(linkedMap, target) as MapEntry
                      ?? throw new InvalidOperationException("The Map override could not be created.");
                map.Origin = new GalaxyMapRowOrigin(target, Workspace.GetOverrideChain(linkedMap.Key).Any(candidate =>
                    !string.Equals(candidate.Origin?.ModuleTag, target.Tag, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var mapId = new ModuleIdAllocator(Workspace).NextAvailable(target, GalaxyMapTable.Map);
                map = planet.LinkedMap is { } sharedMap ? GalaxyMapRowCloner.Clone(sharedMap) : new MapEntry();
                map.RowId = mapId;
                PrepareNewRow(layer, map);
            }

            map.MapName = request.MapName;
            map.StartPoint = request.StartPoint;
            DirtyColumns(map, "Map", "StartPoint");
            planetOverride.MapRowId = map.RowId;
            planetOverride.Event = request.Event;
            planetOverride.ButtonLabel = request.ButtonLabel;
            DirtyColumns(planetOverride, "Map", "Event", "ButtonLabel");

            PlotPlanetEntry? plot = null;
            if (request.AddPlotPlanet && planet.PlotPlanet is null)
            {
                plot = CreatePlotPlanetRow(layer, planetOverride);
            }

            var affected = new List<GalaxyMapRowKey> { planet.Key, map.Key };
            var tables = new List<GalaxyMapTable> { GalaxyMapTable.Planet, GalaxyMapTable.Map };
            if (plot is not null)
            {
                affected.Add(plot.Key);
                tables.Add(GalaxyMapTable.PlotPlanet);
            }

            ExecuteLayerMutation(
                affected,
                () =>
                {
                    layer.Upsert(map);
                    layer.Upsert(planetOverride);
                    if (plot is not null) layer.Upsert(plot);
                },
                tables,
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId),
                $"Configured landable destination for Planet row {planet.RowId}.");
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    private static PlotPlanetEntry CreatePlotPlanetRow(GalaxyMapLayer layer, Planet planet)
    {
        var plot = new PlotPlanetEntry
        {
            RowId = planet.RowId,
            Code = planet.ActiveWorld,
            Name = planet.Name,
            NameText = planet.NameText
        };
        PrepareNewRow(layer, plot);
        foreach (var column in new[]
                 {
                     "VisibleConditional", "VisibleFunction", "VisibleParameter",
                     "UsableConditional", "UsableFunction", "UsableParameter"
                 })
        {
            if (planet.ExtraFields.TryGetValue(column, out var value))
            {
                plot.SetExtraField(column, value);
            }
        }
        return plot;
    }

    private static void DirtyColumns(GalaxyMapRow row, params string[] columns)
    {
        var snapshot = EnsureSnapshot(row);
        foreach (var column in columns)
        {
            snapshot.MarkDirty(column);
        }
    }

    private void AddLinkedMap(Planet planet)
    {
        var target = ResolveWritableTarget(planet);
        if (Workspace is null || target is null || planet.LinkedMap is not null)
        {
            Fail("Create or open a writable module before adding a Map link.");
            return;
        }

        Workspace.SetActiveModule(target);
        OnActiveModuleChanged();
        var layer = Workspace.ActiveLayer!;

        try
        {
            var mapId = new ModuleIdAllocator(Workspace).NextAvailable(target, GalaxyMapTable.Map);
            var map = new MapEntry
            {
                RowId = mapId,
                MapName = "BIOA_NEW_MAP",
                StartPoint = string.Empty
            };
            PrepareNewRow(layer, map);

            var planetOverride = layer.Find(planet.Key) is null
                ? GalaxyMapRowCloner.CloneForOverride(planet, target)
                : GalaxyMapRowCloner.Clone(planet);
            planetOverride.Origin = new GalaxyMapRowOrigin(target, Workspace.GetOverrideChain(planet.Key).Count > 0);
            ((Planet)planetOverride).MapRowId = mapId;
            EnsureSnapshot(planetOverride).MarkDirty("Map");

            ExecuteLayerMutation(
                [map.Key, planet.Key],
                () =>
                {
                    layer.Upsert(map);
                    layer.Upsert(planetOverride);
                },
                [GalaxyMapTable.Map, GalaxyMapTable.Planet],
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId),
                $"Created Map row {mapId} and linked Planet row {planet.RowId}.");
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    private void DeleteLinkedPlotPlanet(Planet planet)
    {
        if (Workspace is null || planet.PlotPlanet is not { } linked || !Confirm($"Delete PlotPlanet row {linked.RowId}?")) return;
        var layer = WritableOwningLayer(linked);
        if (layer is null) { Fail("Only a PlotPlanet row owned by a writable module can be deleted safely."); return; }
        Workspace.SetActiveModule(layer.Module);
        ExecuteLayerMutation([linked.Key], () => layer.Remove(layer.Find(linked.Key)!), [GalaxyMapTable.PlotPlanet],
            planet.Key, new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId), $"Deleted linked PlotPlanet row {linked.RowId}.");
    }

    private void DeleteLinkedMap(Planet planet)
    {
        if (Workspace is null || planet.LinkedMap is not { } linked || !Confirm($"Delete Map row {linked.RowId} and clear its Planet link?")) return;
        var mapLayer = WritableOwningLayer(linked);
        var planetLayer = ResolveWritableTarget(planet) is { } target
            ? Workspace.ModuleLayers.First(layer => string.Equals(layer.Module.Tag, target.Tag, StringComparison.OrdinalIgnoreCase)) : null;
        if (mapLayer is null || planetLayer is null || !ReferenceEquals(mapLayer, planetLayer))
        { Fail("The Map and editable Planet instance must belong to the same writable module before the link can be deleted safely."); return; }
        Workspace.SetActiveModule(planetLayer.Module);
        var replacement = planetLayer.Find(planet.Key) is { } existing ? GalaxyMapRowCloner.Clone((Planet)existing) : GalaxyMapRowCloner.CloneForOverride(planet, planetLayer.Module) as Planet;
        replacement!.MapRowId = -1; EnsureSnapshot(replacement).MarkDirty("Map");
        ExecuteLayerMutation([linked.Key, planet.Key], () => { mapLayer.Remove(mapLayer.Find(linked.Key)!); planetLayer.Upsert(replacement); },
            [GalaxyMapTable.Map, GalaxyMapTable.Planet], planet.Key,
            new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId), $"Deleted linked Map row {linked.RowId}.");
    }

    private GalaxyMapLayer? WritableOwningLayer(GalaxyMapRow row)
        => Workspace?.ModuleLayers.FirstOrDefault(layer => !layer.Module.IsReadOnly && layer.Find(row.Key) is not null &&
            string.Equals(layer.Module.Tag, row.Origin?.ModuleTag, StringComparison.OrdinalIgnoreCase));

    private GalaxyMapLayer? MovableOwningLayer(GalaxyMapRow row)
    {
        var layer = WritableOwningLayer(row);
        return layer is not null && Workspace?.GetOverrideChain(row.Key).Count == 1
            ? layer
            : null;
    }

    public bool CanMoveOwnedRow(GalaxyMapRow row)
        => row is Cluster or GalaxySystem or Planet && MovableOwningLayer(row) is not null;

    private void MoveRowDialog(GalaxyMapRow source)
    {
        if (Document is null || source is not (GalaxySystem or Planet))
        {
            return;
        }

        if (!CanMoveOwnedRow(source))
        {
            Fail($"Clone this {source.Table} into a writable module before moving it.");
            return;
        }

        try
        {
            var options = source switch
            {
                GalaxySystem system => Document.Clusters
                    .Where(cluster => cluster.RowId != system.ClusterRowId)
                    .OrderBy(cluster => cluster.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(cluster => (Cluster: cluster, ResultingLabel: FindAvailableScopedLabel(
                        "System", system.Label, cluster.Systems.Select(candidate => candidate.Label))))
                    .Where(option => option.ResultingLabel is not null &&
                        TryLabelSuffix(option.Cluster.Label, "Cluster", out var clusterNumber) && clusterNumber > 0)
                    .Select(option => new MoveDestinationOption(
                        option.Cluster.RowId,
                        option.Cluster.DisplayName,
                        $"{option.Cluster.DisplayName} • Cluster row {option.Cluster.RowId}",
                        system.Label,
                        option.ResultingLabel!))
                    .ToArray(),
                Planet planet => Document.Systems
                    .Where(system => system.RowId != planet.SystemRowId)
                    .OrderBy(system => system.Cluster?.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(system => system.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(system => (System: system, ResultingLabel: FindAvailableScopedLabel(
                        "Planet", planet.Label, system.Planets.Select(candidate => candidate.Label))))
                    .Where(option => option.ResultingLabel is not null && option.System.Cluster is not null &&
                        TryCalculateActiveWorld(
                            option.System.Cluster.Label,
                            option.System.Label,
                            option.ResultingLabel!,
                            out _))
                    .Select(option => new MoveDestinationOption(
                        option.System.RowId,
                        $"{option.System.Cluster?.DisplayName ?? "Missing Cluster"} / {option.System.DisplayName}",
                        $"{option.System.DisplayName} • System row {option.System.RowId}",
                        planet.Label,
                        option.ResultingLabel!))
                    .ToArray(),
                _ => []
            };

            if (options.Length == 0)
            {
                Fail($"No alternative {(source is GalaxySystem ? "Clusters" : "Systems")} are available.");
                return;
            }

            var dialog = new MoveDestinationWindow(source, options)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.Result is { } destination)
            {
                MoveRow(source, destination.RowId);
            }
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    public bool MoveRow(GalaxyMapRow source, int destinationParentRowId)
    {
        if (Document is null || Workspace is null || source is not (GalaxySystem or Planet))
        {
            return false;
        }

        var layer = MovableOwningLayer(source);
        if (layer is null)
        {
            return Fail($"Clone this {source.Table} into a writable module before moving it.");
        }

        var hasForeignDependency = source switch
        {
            GalaxySystem system => system.Planets.Any(planet =>
                !IsEffectiveRowOwnedBy(layer, planet) ||
                planet.PlotPlanet is { } plot && !IsEffectiveRowOwnedBy(layer, plot)),
            Planet planet => planet.PlotPlanet is { } plot && !IsEffectiveRowOwnedBy(layer, plot),
            _ => false
        };
        if (hasForeignDependency)
        {
            var guidance = source is GalaxySystem
                ? "Clone the System with all of its children into one module before moving it."
                : "Clone the Planet so its linked PlotPlanet row is owned by the same module before moving it.";
            return Fail(
                $"{source.Table} row {source.RowId} has dependent Planet or PlotPlanet rows owned by another layer. " +
                $"{guidance} This keeps the identity update atomic.");
        }

        try
        {
            MoveParentRequest request;
            switch (source)
            {
                case GalaxySystem system when Document.ClustersByRowId.TryGetValue(destinationParentRowId, out var cluster):
                {
                    if (system.ClusterRowId == cluster.RowId)
                    {
                        return Fail($"{system.DisplayName} is already in {cluster.DisplayName}.");
                    }

                    var label = AvailableScopedLabel(
                        "System",
                        system.Label,
                        cluster.Systems.Where(candidate => candidate.RowId != system.RowId).Select(candidate => candidate.Label));
                    if (!TryLabelSuffix(cluster.Label, "Cluster", out var clusterNumber) || clusterNumber <= 0 ||
                        !TryLabelSuffix(label, "System", out var systemNumber) || systemNumber is <= 0 or > 99 ||
                        system.Planets.Any(planet => !TryCalculateActiveWorld(
                            cluster.Label,
                            label,
                            planet.Label,
                            out _)))
                    {
                        return Fail("The destination labels cannot produce valid ActiveWorld IDs for every child Planet.");
                    }
                    request = new MoveParentRequest(
                        cluster.RowId,
                        label,
                        new ViewContext(cluster.RowId, null),
                        MoveSummary(system.DisplayName, cluster.DisplayName, system.Label, label));
                    break;
                }
                case Planet planet when Document.SystemsByRowId.TryGetValue(destinationParentRowId, out var system):
                {
                    if (system.Cluster is null)
                    {
                        return Fail($"System row {system.RowId} has no valid parent Cluster.");
                    }
                    if (planet.SystemRowId == system.RowId)
                    {
                        return Fail($"{planet.DisplayName} is already in {system.DisplayName}.");
                    }

                    var label = AvailableScopedLabel(
                        "Planet",
                        planet.Label,
                        system.Planets.Where(candidate => candidate.RowId != planet.RowId).Select(candidate => candidate.Label));
                    if (!TryCalculateActiveWorld(system.Cluster.Label, system.Label, label, out _))
                    {
                        return Fail("The destination labels cannot produce a valid ActiveWorld ID for this Planet.");
                    }
                    request = new MoveParentRequest(
                        system.RowId,
                        label,
                        new ViewContext(system.ClusterRowId, system.RowId),
                        MoveSummary(planet.DisplayName, system.DisplayName, planet.Label, label));
                    break;
                }
                default:
                    return Fail($"Destination row {destinationParentRowId} is not a valid parent for this {source.Table}.");
            }

            Workspace.SetActiveModule(layer.Module);
            OnActiveModuleChanged();
            return ApplyManagedInspectorEdit(source, MoveParentRequest.PropertyName, request);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail(exception.Message);
        }
    }

    private static string MoveSummary(string item, string destination, string oldLabel, string newLabel)
        => string.Equals(oldLabel, newLabel, StringComparison.OrdinalIgnoreCase)
            ? $"Moved {item} to {destination}."
            : $"Moved {item} to {destination} and changed {oldLabel} to {newLabel} to avoid a label collision.";

    private static bool IsEffectiveRowOwnedBy(GalaxyMapLayer layer, GalaxyMapRow row)
        => layer.Find(row.Key) is not null &&
           string.Equals(row.Origin?.ModuleTag, layer.Module.Tag, StringComparison.OrdinalIgnoreCase);

    private static string AvailableScopedLabel(string prefix, string preferred, IEnumerable<string> existingLabels)
        => FindAvailableScopedLabel(prefix, preferred, existingLabels) ??
           throw new InvalidOperationException($"The destination already uses all 99 available {prefix} labels.");

    private static string? FindAvailableScopedLabel(string prefix, string preferred, IEnumerable<string> existingLabels)
    {
        var used = existingLabels
            .Select(label => TryLabelSuffix(label, prefix, out var suffix) ? suffix : 0)
            .Where(suffix => suffix is > 0 and <= 99)
            .ToHashSet();
        if (TryLabelSuffix(preferred, prefix, out var preferredSuffix) &&
            preferredSuffix is > 0 and <= 99 && !used.Contains(preferredSuffix))
        {
            return preferred;
        }

        for (var suffix = 1; suffix <= 99; suffix++)
        {
            if (!used.Contains(suffix))
            {
                return $"{prefix}{suffix:D2}";
            }
        }

        return null;
    }

    private void DeleteRow(GalaxyMapRow row)
    {
        if (Workspace is null || !Confirm($"Delete {row.Table} row {row.RowId} ({row switch { Cluster c => c.DisplayName, GalaxySystem s => s.DisplayName, Planet p => p.DisplayName, _ => row.Table.ToString() }})?")) return;
        var layer = WritableOwningLayer(row);
        if (layer is null)
        {
            Fail("BASEGAME and read-only module rows cannot be deleted: the 2DA partial format has no safe deletion tombstone.");
            return;
        }

        Workspace.SetActiveModule(layer.Module);
        var rows = new List<GalaxyMapRow> { layer.Find(row.Key)! };
        var removesEntity = Workspace.GetOverrideChain(row.Key).Count == 1;
        if (removesEntity && row is Cluster cluster)
        {
            var systems = layer.Systems.Where(s => s.ClusterRowId == cluster.RowId).ToArray();
            rows.AddRange(systems);
            rows.AddRange(layer.Planets.Where(p => systems.Any(s => s.RowId == p.SystemRowId)));
            if (Document?.TryGetRelayCode(cluster, out var relayCode, out _) == true)
                rows.AddRange(layer.Relays.Where(relay => relay.StartClusterEncoded == relayCode || relay.EndClusterEncoded == relayCode));
        }
        else if (removesEntity && row is GalaxySystem system) rows.AddRange(layer.Planets.Where(p => p.SystemRowId == system.RowId));
        foreach (var planet in rows.OfType<Planet>().ToArray())
        {
            if (layer.PlotPlanets.FirstOrDefault(p => p.RowId == planet.RowId) is { } plot) rows.Add(plot);
            if (layer.Maps.FirstOrDefault(m => m.RowId == planet.MapRowId) is { } map &&
                !layer.Planets.Any(p => p.RowId != planet.RowId && p.MapRowId == map.RowId)) rows.Add(map);
        }
        var tables = rows.Select(r => r.Table).Distinct().ToArray();
        ExecuteLayerMutation(rows.Select(r => r.Key), () => { foreach (var item in rows.Distinct().ToArray()) layer.Remove(item); }, tables,
            null, row is Cluster ? ViewContext.Galaxy : new ViewContext((row as GalaxySystem)?.ClusterRowId ?? (row as Planet)?.System?.ClusterRowId, null),
            $"Deleted {row.Table} row {row.RowId} and {rows.Count - 1} owned child/link row(s).");
    }

    private void BeginRelayCreation(Cluster source)
    {
        if (Document is null || Workspace?.ActiveLayer is null)
        {
            Fail("Create or open a writable module before adding Relay connections.");
            return;
        }

        if (!Document.TryGetRelayCode(source, out _, out var error))
        {
            StatusMessage = error;
            return;
        }

        _pendingRelayReplacement = null;
        _pendingRelayTargetModule = ActiveModule;
        PendingRelaySource = source;
        ErrorMessage = string.Empty;
        NavigateGalaxy();
        if (_nodes.TryGetValue(source, out var sourceNode))
        {
            SelectNodeCore(sourceNode);
        }

        ShowInspectorForRow(source);
        StatusMessage = RelayLinkPrompt;
    }

    private void BeginRelayRedirect(Cluster source, RelayConnection relay)
    {
        if (Document is null || Workspace?.ActiveLayer is null)
        {
            Fail("Create or open a writable module before redirecting Relay connections.");
            return;
        }

        if (!Document.TryGetRelayCode(source, out var sourceCode, out var error))
        {
            Fail(error);
            return;
        }

        if (relay.StartClusterEncoded != sourceCode && relay.EndClusterEncoded != sourceCode)
        {
            Fail($"Relay row {relay.RowId} is not connected to {source.DisplayName}.");
            return;
        }

        var targetModule = ResolveWritableTarget(relay);
        if (targetModule is null)
        {
            return;
        }

        Workspace.SetActiveModule(targetModule);
        OnActiveModuleChanged();
        _pendingRelayReplacement = relay;
        _pendingRelayTargetModule = targetModule;
        PendingRelaySource = source;
        ErrorMessage = string.Empty;
        NavigateGalaxy();
        if (_nodes.TryGetValue(source, out var sourceNode))
        {
            SelectNodeCore(sourceNode);
        }

        ShowInspectorForRow(source);
        StatusMessage = RelayLinkPrompt;
    }

    private void CompleteRelayRedirect(Cluster source, RelayConnection relay, Cluster target)
    {
        var targetModule = _pendingRelayTargetModule;
        if (Document is null || Workspace is null || targetModule is null)
        {
            return;
        }

        Workspace.SetActiveModule(targetModule);
        var layer = Workspace.ActiveLayer!;

        if (ReferenceEquals(source, target))
        {
            StatusMessage = "A Relay connection cannot loop back to the same Cluster.";
            return;
        }

        if (!Document.TryGetRelayCode(source, out var sourceCode, out var error) ||
            !Document.TryGetRelayCode(target, out var targetCode, out error))
        {
            StatusMessage = error;
            return;
        }

        if (Document.Relays.Any(candidate => candidate.RowId != relay.RowId &&
                ((candidate.StartClusterEncoded == sourceCode && candidate.EndClusterEncoded == targetCode) ||
                 (candidate.StartClusterEncoded == targetCode && candidate.EndClusterEncoded == sourceCode))))
        {
            StatusMessage = $"{source.DisplayName} and {target.DisplayName} already have a Relay connection.";
            return;
        }

        var physical = layer.Find(relay.Key) is null
            ? GalaxyMapRowCloner.CloneForOverride(relay, targetModule)
            : GalaxyMapRowCloner.Clone(relay);
        string dirtyColumn;
        if (((RelayConnection)physical).StartClusterEncoded == sourceCode)
        {
            ((RelayConnection)physical).EndClusterEncoded = targetCode;
            dirtyColumn = "EndCluster";
        }
        else if (((RelayConnection)physical).EndClusterEncoded == sourceCode)
        {
            ((RelayConnection)physical).StartClusterEncoded = targetCode;
            dirtyColumn = "StartCluster";
        }
        else
        {
            Fail($"Relay row {relay.RowId} no longer contains {source.DisplayName}'s endpoint code.");
            return;
        }

        physical.Origin = new GalaxyMapRowOrigin(targetModule, Workspace.GetOverrideChain(relay.Key).Any(
            candidate => !string.Equals(candidate.Origin?.ModuleTag, targetModule.Tag, StringComparison.OrdinalIgnoreCase)));
        EnsureSnapshot(physical).MarkDirty(dirtyColumn);
        _pendingRelayReplacement = null;
        _pendingRelayTargetModule = null;
        PendingRelaySource = null;
        ExecuteLayerMutation(
            [relay.Key],
            () => layer.Upsert(physical),
            [GalaxyMapTable.Relay],
            source.Key,
            ViewContext.Galaxy,
            $"Redirected Relay row {relay.RowId} from {source.DisplayName} to {target.DisplayName}.");
    }

    private void CompleteRelayCreation(Cluster source, Cluster target)
    {
        if (Document is null || Workspace?.ActiveLayer is not { } layer || ActiveModule is null)
        {
            return;
        }

        if (ReferenceEquals(source, target))
        {
            StatusMessage = "A Cluster cannot have a Relay connection to itself.";
            return;
        }

        if (!Document.TryGetRelayCode(source, out var startCode, out var error) ||
            !Document.TryGetRelayCode(target, out var endCode, out error))
        {
            StatusMessage = error;
            return;
        }

        if (Document.Relays.Any(relay =>
                (relay.StartClusterEncoded == startCode && relay.EndClusterEncoded == endCode) ||
                (relay.StartClusterEncoded == endCode && relay.EndClusterEncoded == startCode)))
        {
            StatusMessage = $"{source.DisplayName} and {target.DisplayName} already have a Relay connection.";
            return;
        }

        try
        {
            var rowId = new ModuleIdAllocator(Workspace).NextAvailable(ActiveModule, GalaxyMapTable.Relay);
            var relay = new RelayConnection
            {
                RowId = rowId,
                StartClusterEncoded = startCode,
                EndClusterEncoded = endCode
            };
            PrepareNewRow(layer, relay);
            _pendingRelayReplacement = null;
            _pendingRelayTargetModule = null;
            PendingRelaySource = null;
            ExecuteLayerMutation(
                [relay.Key],
                () => layer.Upsert(relay),
                [GalaxyMapTable.Relay],
                source.Key,
                ViewContext.Galaxy,
                $"Added Relay connection from {source.DisplayName} to {target.DisplayName}.");
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
        }
    }

    private void CancelRelayCreation()
    {
        if (PendingRelaySource is not { } source)
        {
            return;
        }

        PendingRelaySource = null;
        _pendingRelayReplacement = null;
        _pendingRelayTargetModule = null;
        NavigateGalaxy();
        if (_nodes.TryGetValue(source, out var sourceNode))
        {
            SelectNodeCore(sourceNode);
        }

        ShowInspectorForRow(source);
        UpdateDocumentSummary(null);
    }

    private void RemoveRelay(RelayConnection relay)
    {
        if (Workspace?.ActiveLayer is not { } layer || _selectedNode?.Item is not Cluster selectedCluster)
        {
            Fail("Create or open a writable module before removing Relay connections.");
            return;
        }

        var physical = layer.Find(relay.Key);
        var chain = Workspace.GetOverrideChain(relay.Key);
        if (physical is null || chain.Count != 1)
        {
            Fail(
                "This Relay comes from BASEGAME or a mounted source module. The 2DA partial format has no verified " +
                "deletion tombstone, so the editor will not invent one and risk corrupting the runtime table.");
            return;
        }

        var destination = ReferenceEquals(relay.StartCluster, selectedCluster)
            ? relay.EndCluster?.DisplayName ?? $"code {relay.EndClusterEncoded}"
            : relay.StartCluster?.DisplayName ?? $"code {relay.StartClusterEncoded}";
        ExecuteLayerMutation(
            [relay.Key],
            () => layer.Remove(physical),
            [GalaxyMapTable.Relay],
            selectedCluster.Key,
            ViewContext.Galaxy,
            $"Removed Relay connection from {selectedCluster.DisplayName} to {destination}.");
    }

    private bool CanBreakRelay(RelayConnection relay)
        => Workspace?.ActiveLayer?.Find(relay.Key) is not null &&
           Workspace.GetOverrideChain(relay.Key).Count == 1;

    private void ExecuteLayerMutation(
        IEnumerable<GalaxyMapRowKey> affectedKeys,
        Action mutation,
        IEnumerable<GalaxyMapTable> tables,
        GalaxyMapRowKey? selectionKey,
        ViewContext view,
        string successMessage,
        bool preserveHierarchy = false,
        bool refreshModules = true,
        bool deferValidation = false)
    {
        if (Workspace?.ActiveLayer is not { } layer)
        {
            Fail("A writable active module is required for this edit.");
            return;
        }

        var keys = affectedKeys.Distinct().ToArray();
        var touchedTables = tables.Distinct().ToArray();
        var backups = keys.ToDictionary(
            key => key,
            key => layer.Find(key) is { } row ? GalaxyMapRowCloner.Clone(row) : null);

        try
        {
            EnsureUndoSnapshot();
            _isApplying = true;
            mutation();
            MarkTablesDirty(layer.Module, touchedTables);
            RefreshWorkspace(selectionKey, view, $"Staged: {successMessage}",
                preserveHierarchy: preserveHierarchy,
                refreshModules: refreshModules,
                deferValidation: deferValidation);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            RestoreRows(layer, backups);
            Workspace.Recompose();
            RefreshWorkspace(selectionKey, view, null, recompose: false);
            Fail(exception.Message);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private bool ApplyManagedInspectorEdit(GalaxyMapRow inspectedRow, string propertyName, object? value)
    {
        if (Workspace is null || Document is null)
        {
            return false;
        }

        var moveRequest = propertyName == MoveParentRequest.PropertyName ? value as MoveParentRequest : null;
        var isManaged = inspectedRow switch
        {
            _ when propertyName == "AvailabilityAlways" => true,
            Cluster => propertyName == nameof(Cluster.Label),
            GalaxySystem => propertyName is nameof(GalaxySystem.Label) or nameof(GalaxySystem.ClusterRowId) ||
                            moveRequest is not null,
            Planet planet => propertyName is nameof(Planet.Label) or nameof(Planet.SystemRowId) or
                nameof(Planet.Name) or nameof(Planet.NameText) or nameof(Planet.SystemLevelType) ||
                planet.PlotPlanet is not null && IsPlotPlanetMirrorField(propertyName) ||
                moveRequest is not null,
            _ => false
        };
        if (!isManaged)
        {
            return false;
        }

        var target = ResolveWritableTarget(inspectedRow);
        if (target is null)
        {
            return true;
        }

        var layer = Workspace.ModuleLayers.First(candidate =>
            string.Equals(candidate.Module.Tag, target.Tag, StringComparison.OrdinalIgnoreCase));
        Workspace.SetActiveModule(target);
        OnActiveModuleChanged();

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
            copy.Origin = new GalaxyMapRowOrigin(target, Workspace.GetOverrideChain(source.Key).Any(candidate =>
                !string.Equals(candidate.Origin?.ModuleTag, target.Tag, StringComparison.OrdinalIgnoreCase)));
            staged[source.Key] = copy;
            return (T)copy;
        }

        void Dirty(GalaxyMapRow row, params string[] columns)
        {
            var snapshot = EnsureSnapshot(row);
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
                    foreach (var field in availabilityFields.Where(field => !field.StartsWith("UsablePlanet", StringComparison.OrdinalIgnoreCase)))
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
                    return false;
                }

                var effectiveCluster = Document.ClustersByRowId.GetValueOrDefault(inspectedCluster.RowId) ?? inspectedCluster;
                var cluster = Stage(inspectedCluster);
                cluster.Label = newLabel;
                Dirty(cluster, "Label");

                foreach (var system in effectiveCluster.Systems)
                foreach (var planet in system.Planets)
                {
                    UpdatePlanetIdentity(planet, newLabel, system.Label, planet.Label);
                }

                var newCode = checked(newClusterNumber * 10_000);
                foreach (var sourceRelay in Document.Relays.Where(relay =>
                             relay.StartCluster?.RowId == effectiveCluster.RowId || relay.EndCluster?.RowId == effectiveCluster.RowId))
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
                var effectiveSystem = Document.SystemsByRowId.GetValueOrDefault(inspectedSystem.RowId) ?? inspectedSystem;
                var system = Stage(inspectedSystem);
                var clusterRowId = moveRequest?.ParentRowId ?? (propertyName == nameof(GalaxySystem.ClusterRowId)
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : inspectedSystem.ClusterRowId);
                var systemLabel = moveRequest?.ResultingLabel ?? (propertyName == nameof(GalaxySystem.Label)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : inspectedSystem.Label);
                if (!TryLabelSuffix(systemLabel, "System", out var systemNumber) || systemNumber is <= 0 or > 99 ||
                    !Document.ClustersByRowId.TryGetValue(clusterRowId, out var parentCluster))
                {
                    return false;
                }

                system.Label = systemLabel;
                system.ClusterRowId = clusterRowId;
                if (moveRequest is not null)
                {
                    Dirty(system, "Label", "Cluster");
                }
                else
                {
                    Dirty(system, propertyName == nameof(GalaxySystem.Label) ? "Label" : "Cluster");
                }
                foreach (var planet in effectiveSystem.Planets)
                {
                    UpdatePlanetIdentity(planet, parentCluster.Label, systemLabel, planet.Label);
                }
                break;
            }
            case Planet inspectedPlanet:
            {
                var effectivePlanet = Document.PlanetsByRowId.GetValueOrDefault(inspectedPlanet.RowId) ?? inspectedPlanet;
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
                if (!Document.SystemsByRowId.TryGetValue(systemRowId, out var parentSystem) || parentSystem.Cluster is null ||
                    !TryCalculateActiveWorld(parentSystem.Cluster.Label, parentSystem.Label, planetLabel, out var activeWorld))
                {
                    return false;
                }

                planet.Label = planetLabel;
                planet.SystemRowId = systemRowId;
                planet.ActiveWorld = activeWorld;
                if (moveRequest is not null)
                {
                    Dirty(planet, "Label", "System", "ActiveWorld");
                }
                else
                {
                    Dirty(planet, propertyName == nameof(Planet.Label) ? "Label" : "System", "ActiveWorld");
                }
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
            return false;
        }

        ExecuteLayerMutation(
            staged.Keys,
            () =>
            {
                foreach (var stagedRow in staged.Values)
                {
                    layer.Upsert(stagedRow);
                }
            },
            staged.Values.Select(stagedRow => stagedRow.Table),
            inspectedRow.Key,
            moveRequest?.DestinationView ?? CaptureView(),
            moveRequest?.SuccessMessage ??
            $"Updated {inspectedRow.Table} row {inspectedRow.RowId} and {staged.Count - 1} dependent row(s).");
        return true;
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

    private static bool TryLabelSuffix(string label, string prefix, out int suffix)
    {
        suffix = 0;
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(label[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out suffix);
    }

    private static bool IsPlotPlanetMirrorField(string propertyName)
        => TryExtraColumn(propertyName, out var column) && column is
            "VisibleConditional" or "VisibleFunction" or "VisibleParameter" or
            "UsableConditional" or "UsableFunction" or "UsableParameter";

    private static bool TryExtraColumn(string propertyName, out string column)
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

    private IReadOnlyList<InspectorFieldOption> ClusterInspectorOptions()
        => Document?.Clusters
            .OrderBy(cluster => cluster.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(cluster => new InspectorFieldOption(
                cluster.RowId.ToString(CultureInfo.InvariantCulture),
                $"{cluster.DisplayName} • row {cluster.RowId}"))
            .ToArray() ?? [];

    private IReadOnlyList<InspectorFieldOption> SystemInspectorOptions()
        => Document?.Systems
            .OrderBy(system => system.Cluster?.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(system => system.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(system => new InspectorFieldOption(
                system.RowId.ToString(CultureInfo.InvariantCulture),
                $"{system.Cluster?.DisplayName ?? "Missing Cluster"} / {system.DisplayName} • row {system.RowId}"))
            .ToArray() ?? [];

    private IReadOnlyList<InspectorFieldOption> MapInspectorOptions()
    {
        var options = new List<InspectorFieldOption> { new("-1", "No linked Map") };
        if (Document is not null)
        {
            options.AddRange(Document.Maps.OrderBy(map => map.RowId).Select(map => new InspectorFieldOption(
                map.RowId.ToString(CultureInfo.InvariantCulture),
                $"{(string.IsNullOrWhiteSpace(map.MapName) ? "Unnamed Map" : map.MapName)} • row {map.RowId}")));
        }
        return options;
    }

    private IReadOnlyList<InspectorFieldOption> RelayClusterInspectorOptions()
        => Document?.Clusters
            .Select(cluster => (Cluster: cluster, Valid: TryLabelSuffix(cluster.Label, "Cluster", out var suffix), Suffix: suffix))
            .Where(item => item.Valid && item.Suffix > 0 && item.Suffix <= int.MaxValue / 10_000)
            .OrderBy(item => item.Cluster.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new InspectorFieldOption(
                (item.Suffix * 10_000).ToString(CultureInfo.InvariantCulture),
                $"{item.Cluster.DisplayName} • {item.Suffix * 10_000}"))
            .ToArray() ?? [];

    private void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isApplying || sender is not GalaxyMapRow row || !TryCsvColumn(row, eventArgs.PropertyName, out var column))
        {
            return;
        }

        if (Workspace is null)
        {
            if (CurrentViewModel is MapViewModelBase legacyMap)
            {
                legacyMap.Refresh();
            }

            if (row is Cluster changedCluster &&
                eventArgs.PropertyName == nameof(Cluster.Background) &&
                CurrentViewModel is ClusterViewModel clusterView &&
                ReferenceEquals(clusterView.Cluster, changedCluster))
            {
                clusterView.UpdateBackgroundTexture(_textures.GetClusterTexture(changedCluster.Background));
            }

            return;
        }

        var physicalLayer = Workspace.Layers.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Find(row.Key), row));
        if (physicalLayer is not null)
        {
            if (physicalLayer.Module.IsReadOnly || physicalLayer.Module.IsBaseGame)
            {
                var readOnlyView = CaptureView();
                Workspace.Recompose();
                RefreshWorkspace(_selectedNode?.Model?.Key ?? row.Key, readOnlyView, null, recompose: false);
                Fail($"{physicalLayer.Module.Tag} is read-only.");
                return;
            }

            EnsureSnapshot(row).MarkDirty(column);
            MarkTableDirty(physicalLayer.Module, row.Table);
            _preferredInstanceTag = physicalLayer.Module.Tag;
            var physicalView = CaptureView();
            Workspace.Recompose();
            RefreshWorkspace(_selectedNode?.Model?.Key ?? row.Key, physicalView,
                $"Staged {row.Table} row {row.RowId} in {physicalLayer.Module.Tag}.",
                recompose: false,
                preserveHierarchy: true,
                refreshModules: false,
                deferValidation: true);
            _editSnapshotCaptured = false;
            return;
        }

        var targetModule = row.Origin?.IsBaseGame == true
            ? ChooseEditTarget(row)
            : row.Origin?.Module is { IsReadOnly: false, IsBaseGame: false } originModule
                ? Workspace.Modules.FirstOrDefault(module =>
                    string.Equals(module.Tag, originModule.Tag, StringComparison.OrdinalIgnoreCase))
                : ChooseEditTarget(row);
        if (targetModule is null)
        {
            var view = CaptureView();
            var selectedKey = _selectedNode?.Model?.Key ?? row.Key;
            Workspace.Recompose();
            RefreshWorkspace(selectedKey, view, null, recompose: false);
            if (!HasError)
            {
                StatusMessage = "Edit cancelled; the source row was left unchanged.";
            }
            return;
        }

        Workspace.SetActiveModule(targetModule);
        OnActiveModuleChanged();
        var layer = Workspace.ActiveLayer!;

        var viewContext = CaptureView();
        var selectionKey = _selectedNode?.Model?.Key ?? row.Key;
        var existing = layer.Find(row.Key);
        GalaxyMapRow physical = existing is null
            ? GalaxyMapRowCloner.CloneForOverride(row, targetModule)
            : GalaxyMapRowCloner.Clone(row);
        physical.Origin = new GalaxyMapRowOrigin(targetModule, Workspace.GetOverrideChain(row.Key).Any(
            candidate => !string.Equals(candidate.Origin?.ModuleTag, targetModule.Tag, StringComparison.OrdinalIgnoreCase)));
        EnsureSnapshot(physical).MarkDirty(column);
        _preferredInstanceTag = targetModule.Tag;

        ExecuteLayerMutation(
            [row.Key],
            () => layer.Upsert(physical),
            [row.Table],
            selectionKey,
            viewContext,
            $"{row.Table} row {row.RowId} in {targetModule.Tag}.",
            preserveHierarchy: true,
            refreshModules: false,
            deferValidation: true);
    }

    private void UpdateValidation()
    {
        _validationTimer?.Stop();
        _deferredValidationStatus = null;
        var diagnostics = Workspace is not null
            ? _validator.Validate(Workspace)
            : Document is not null
                ? _validator.Validate(Document)
                : [];
        ValidationDiagnostics.ReplaceAll(_startupDiagnostics.Concat(diagnostics));

        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticCount));
        OnPropertyChanged(nameof(ValidationErrorCount));
        OnPropertyChanged(nameof(ValidationWarningCount));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(WarningsText));
        if (!HasDiagnostics)
        {
            IsDiagnosticsPanelOpen = false;
        }
    }

    private void ScheduleValidation(string? status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || !dispatcher.CheckAccess())
        {
            UpdateValidation();
            return;
        }

        _deferredValidationStatus = status;
        if (_validationTimer is null)
        {
            _validationTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(250),
                DispatcherPriority.Background,
                DeferredValidationOnTick,
                dispatcher);
            _validationTimer.Stop();
        }

        _validationTimer.Stop();
        _validationTimer.Start();
    }

    private void DeferredValidationOnTick(object? sender, EventArgs eventArgs)
    {
        var status = _deferredValidationStatus;
        _validationTimer?.Stop();
        _deferredValidationStatus = null;
        UpdateValidation();
        UpdateDocumentSummary(status);
    }

    private void UpdateDocumentSummary(string? leadingMessage)
    {
        if (Document is null)
        {
            return;
        }

        var validation = ValidationErrorCount == 0 && ValidationWarningCount == 0
            ? "No validation errors or warnings."
            : $"Validation: {ValidationErrorCount} error(s), {ValidationWarningCount} warning(s).";
        var counts = $"{Document.Clusters.Count} clusters, {Document.Systems.Count} systems, " +
                     $"{Document.Planets.Count} planets and {Document.Relays.Count} relays.";
        StatusMessage = string.IsNullOrWhiteSpace(leadingMessage)
            ? $"{counts} {validation}"
            : $"{leadingMessage} {counts} {validation}";
    }

    private void RefreshMountedModules()
    {
        if (Workspace is null)
        {
            MountedModules.ReplaceAll([]);
            ModuleBarItems.ReplaceAll([]);
            return;
        }

        MountedModules.ReplaceAll(Workspace.Layers.Select(layer => layer.Module));
        RefreshModuleBarItems();
    }

    private void RefreshModuleBarItems()
    {
        if (Workspace is null)
        {
            ModuleBarItems.ReplaceAll([]);
            return;
        }

        ModuleBarItems.ReplaceAll(Workspace.Modules.OrderBy(module => module.LoadOrder)
            .ThenBy(module => module.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(module =>
            {
                var isDirty = _dirtyTables.ContainsKey(module.Tag) ||
                              _dirtyModuleMetadata.Contains(module.Tag) ||
                              _pendingTextures.Keys.Any(key =>
                                  string.Equals(key.ModuleTag, module.Tag, StringComparison.OrdinalIgnoreCase));
                return new ModuleBarItemViewModel(
                    module,
                    ReferenceEquals(module, ActiveModule),
                    isDirty,
                    EditModule);
            }));
    }

    private void RaiseCommandStates()
    {
        CreateModuleCommand.RaiseCanExecuteChanged();
        OpenModuleCommand.RaiseCanExecuteChanged();
        RefreshRememberedWorkspaceCommand.RaiseCanExecuteChanged();
        AddClusterCommand.RaiseCanExecuteChanged();
        AddSystemCommand.RaiseCanExecuteChanged();
        AddPlanetCommand.RaiseCanExecuteChanged();
        NavigateGalaxyCommand.RaiseCanExecuteChanged();
        NavigateClusterCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        DiscardChangesCommand.RaiseCanExecuteChanged();
    }

    private ViewContext CaptureView() => CurrentViewModel switch
    {
        SystemViewModel systemView => new ViewContext(systemView.System.ClusterRowId, systemView.System.RowId),
        ClusterViewModel clusterView => new ViewContext(clusterView.Cluster.RowId, null),
        _ => ViewContext.Galaxy
    };

    private void ValidateNewModuleRanges(ModuleIdReservations reservations)
    {
        if (Workspace is null)
        {
            return;
        }

        foreach (var existing in Workspace.Modules)
        {
            foreach (var table in ReservableTables)
            {
                var candidate = reservations.GetRange(table);
                var current = existing.Reservations.GetRange(table);
                if (candidate is { } left && current is { } right && left.Overlaps(right))
                {
                    throw new InvalidOperationException(
                        $"The proposed {table} range {left} overlaps {existing.Tag}'s reserved range {right}.");
                }
            }
        }
    }

    private void EnsureUniqueTag(string tag)
    {
        if (Workspace is not null && Workspace.Layers.Any(layer =>
                string.Equals(layer.Module.Tag, tag, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A module tagged {tag} is already mounted.");
        }
    }

    private int NextLoadOrder()
        => Workspace is null ? 1 : Workspace.Layers.Max(layer => layer.Module.LoadOrder) + 1;

    private static ModuleIdReservations DefaultReservations()
        => new(
            new RowIdRange(100, 199),
            new RowIdRange(1_000, 1_999),
            new RowIdRange(10_000, 19_999),
            new RowIdRange(1_000, 1_999),
            new RowIdRange(1_000, 1_999));

    private static void PrepareNewRow(GalaxyMapLayer layer, GalaxyMapRow row)
    {
        var schema = CsvGalaxyMapLoader.GetCanonicalSchema(row.Table);
        layer.SetSchema(schema);
        var known = KnownColumns(row.Table);
        foreach (var header in schema.Headers.Skip(1).Where(header => !known.Contains(header)))
        {
            if (!row.ExtraFields.ContainsKey(header))
            {
                row.AddExtraField(header, GalaxyMapDefaults.ExtraValue(row.Table, header));
            }
        }

        row.Origin = new GalaxyMapRowOrigin(layer.Module, OverridesLowerLayer: false);
        var values = schema.Headers.Select((_, index) => index == 0
            ? row.RowId.ToString(CultureInfo.InvariantCulture)
            : string.Empty).ToArray();
        var snapshot = new CsvRowSnapshot(
            $"GalaxyMap_{row.Table}_part.csv",
            sourceRowNumber: 0,
            schema.Headers,
            values);
        for (var index = 0; index < schema.Headers.Count; index++)
        {
            snapshot.MarkDirty(index == 0 ? CsvRowSnapshot.RowIdColumnName : schema.Headers[index]);
        }

        row.CsvSnapshot = snapshot;
    }

    private static IReadOnlySet<string> KnownColumns(GalaxyMapTable table) => table switch
    {
        GalaxyMapTable.Cluster => Fields("Label", "X", "Y", "Name", "NameText", "SphereSize", "Background"),
        GalaxyMapTable.System => Fields("Label", "Cluster", "X", "Y", "Name", "NameText", "Scale", "ShowNebula"),
        GalaxyMapTable.Planet => Fields("Label", "System", "X", "Y", "Name", "NameText", "ActiveWorld",
            "Description", "ButtonLabel", "Map", "Scale", "RingColor", "OrbitRing", "SystemLevelType",
            "PlanetLevelType", "Event", "ImageIndex"),
        GalaxyMapTable.PlotPlanet => Fields("Code", "Name", "NameText"),
        GalaxyMapTable.Map => Fields("Map", "StartPoint"),
        GalaxyMapTable.Relay => Fields("StartCluster", "EndCluster"),
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
    };

    private static HashSet<string> Fields(params string[] fields)
        => new(fields, StringComparer.OrdinalIgnoreCase);

    private static CsvRowSnapshot EnsureSnapshot(GalaxyMapRow row)
        => row.CsvSnapshot ?? throw new InvalidOperationException(
            $"{row.Table} row {row.RowId} has no source snapshot and cannot be written safely.");

    private static void RestoreRows(
        GalaxyMapLayer layer,
        IReadOnlyDictionary<GalaxyMapRowKey, GalaxyMapRow?> backups)
    {
        foreach (var (key, backup) in backups)
        {
            if (layer.Find(key) is { } current)
            {
                layer.Remove(current);
            }

            if (backup is not null)
            {
                layer.Upsert(backup);
            }
        }
    }

    private static bool TryCsvColumn(GalaxyMapRow row, string? propertyName, out string column)
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

    private bool Fail(string message)
    {
        ErrorMessage = message;
        StatusMessage = message;
        return false;
    }

    private static bool IsExpectedOperationFailure(Exception exception)
        => exception is GalaxyMapLoadException or IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or OverflowException;

    private static string SafeFolderName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Trim())
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim(' ', '.');
        return result.Length == 0 ? SuggestedTag(fallback) : result;
    }

    private static string SuggestedTag(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_', '-') is { Length: > 0 } tag ? tag : "MODULE";
    }

    private void DisposeHierarchy()
    {
        foreach (var root in HierarchyRoots)
        {
            root.Dispose();
        }

        HierarchyRoots.Clear();
        _nodes.Clear();
        _nodesByKey.Clear();
        _galaxyRoot = null;
    }

    private static IEnumerable<GalaxyMapRow> AllRows(GalaxyMapDocument document)
        => document.Clusters.Cast<GalaxyMapRow>()
            .Concat(document.Systems)
            .Concat(document.Planets)
            .Concat(document.PlotPlanets)
            .Concat(document.Maps)
            .Concat(document.Relays);

    private readonly record struct ViewContext(int? ClusterRowId, int? SystemRowId)
    {
        public static ViewContext Galaxy => new(null, null);
    }

    private sealed record MoveParentRequest(
        int ParentRowId,
        string ResultingLabel,
        ViewContext DestinationView,
        string SuccessMessage)
    {
        public const string PropertyName = "MoveParent";
    }

    private sealed record CoordinateDragSession(
        GalaxyMapRow Row,
        GalaxyMapLayer Layer,
        double OriginalX,
        double OriginalY,
        ViewContext View);

    private sealed record PendingTexture(string RelativePath, byte[] Contents, string CacheKey);

    private sealed record WorkspaceEditState(
        IReadOnlyList<GalaxyMapLayer> Layers,
        string? ActiveModuleTag,
        IReadOnlyDictionary<string, HashSet<GalaxyMapTable>> DirtyTables,
        IReadOnlySet<string> DirtyMetadata,
        IReadOnlyDictionary<(string ModuleTag, int ClusterRowId), PendingTexture> PendingTextures,
        GalaxyMapRowKey? SelectionKey,
        ViewContext View,
        string? PreferredInstanceTag,
        bool InspectPhysicalInstance,
        long ApproximateBytes);

    private void BeginUserEdit()
    {
        if (Workspace is null || _editSnapshotCaptured)
        {
            return;
        }

        PushHistoryState(_undoStates, CaptureEditState());
        _redoStates.Clear();
        _editSnapshotCaptured = true;
        NotifyHistoryChanged();
    }

    private void EnsureUndoSnapshot()
    {
        if (_editSnapshotCaptured)
        {
            _editSnapshotCaptured = false;
            return;
        }

        if (Workspace is null)
        {
            return;
        }

        PushHistoryState(_undoStates, CaptureEditState());
        _redoStates.Clear();
        NotifyHistoryChanged();
    }

    private WorkspaceEditState CaptureEditState()
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var layers = Workspace!.ModuleLayers.Select(GalaxyMapLayerCloner.Clone).ToArray();
        var dirtyTables = _dirtyTables.ToDictionary(
            pair => pair.Key,
            pair => new HashSet<GalaxyMapTable>(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var dirtyMetadata = new HashSet<string>(_dirtyModuleMetadata, StringComparer.OrdinalIgnoreCase);
        var pendingTextures = _pendingTextures.ToDictionary(pair => pair.Key,
            pair => new PendingTexture(pair.Value.RelativePath, pair.Value.Contents.ToArray(), pair.Value.CacheKey));
        var allocatedBytes = Math.Max(1, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        return new WorkspaceEditState(
            layers,
            ActiveModule?.Tag,
            dirtyTables,
            dirtyMetadata,
            pendingTextures,
            _selectedNode?.Model?.Key,
            CaptureView(),
            _preferredInstanceTag,
            _inspectPhysicalInstance,
            allocatedBytes);
    }

    private static void PushHistoryState(Stack<WorkspaceEditState> stack, WorkspaceEditState state)
    {
        stack.Push(state);
        if (stack.Count <= MaximumHistoryEntries && stack.Sum(item => item.ApproximateBytes) <= MaximumHistoryBytes)
        {
            return;
        }

        var newestFirst = stack.ToArray();
        var retained = new List<WorkspaceEditState>(Math.Min(newestFirst.Length, MaximumHistoryEntries));
        long retainedBytes = 0;
        foreach (var candidate in newestFirst)
        {
            if (retained.Count >= MaximumHistoryEntries ||
                retained.Count > 0 && retainedBytes + candidate.ApproximateBytes > MaximumHistoryBytes)
            {
                break;
            }

            retained.Add(candidate);
            retainedBytes += candidate.ApproximateBytes;
        }

        stack.Clear();
        for (var index = retained.Count - 1; index >= 0; index--)
        {
            stack.Push(retained[index]);
        }
    }

    private void RestoreEditState(WorkspaceEditState state, string status)
    {
        if (Workspace is null)
        {
            return;
        }

        // A history state is removed from its stack before restoration, so its
        // detached layers can be transferred directly into the new workspace.
        // Cloning them a second time doubled undo allocations for no benefit.
        var restored = new GalaxyMapWorkspace(Workspace.BaseLayer, state.Layers);
        var active = restored.Modules.FirstOrDefault(module =>
            string.Equals(module.Tag, state.ActiveModuleTag, StringComparison.OrdinalIgnoreCase));
        if (active is { IsReadOnly: false })
        {
            restored.SetActiveModule(active);
        }

        Workspace = restored;
        _dirtyTables.Clear();
        foreach (var pair in state.DirtyTables)
        {
            _dirtyTables[pair.Key] = new HashSet<GalaxyMapTable>(pair.Value);
        }
        _dirtyModuleMetadata.Clear();
        foreach (var tag in state.DirtyMetadata) _dirtyModuleMetadata.Add(tag);
        _pendingTextures.Clear();
        foreach (var pair in state.PendingTextures) _pendingTextures[pair.Key] = pair.Value;
        _preferredInstanceTag = state.PreferredInstanceTag;
        _inspectPhysicalInstance = state.InspectPhysicalInstance;
        _editSnapshotCaptured = false;
        RefreshWorkspace(state.SelectionKey, state.View, status, recompose: false);
        NotifyPendingChanges();
        NotifyHistoryChanged();
    }

    private void Undo()
    {
        if (Workspace is null || _undoStates.Count == 0) return;
        PushHistoryState(_redoStates, CaptureEditState());
        RestoreEditState(_undoStates.Pop(), "Undid the last staged change.");
    }

    private void Redo()
    {
        if (Workspace is null || _redoStates.Count == 0) return;
        PushHistoryState(_undoStates, CaptureEditState());
        RestoreEditState(_redoStates.Pop(), "Redid the staged change.");
    }

    private void ConfirmDiscardChanges()
    {
        if (Confirm("Discard every uncommitted change?"))
        {
            DiscardPendingChanges();
        }
    }

    private bool Confirm(string message)
        => _confirmAction?.Invoke(message) ??
           (Application.Current?.MainWindow is { } owner && MessageBox.Show(owner, message,
               "Confirm staged change", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    private void ClearHistory()
    {
        _undoStates.Clear();
        _redoStates.Clear();
        _editSnapshotCaptured = false;
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }
}
