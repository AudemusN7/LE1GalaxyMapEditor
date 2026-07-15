using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;
using LE1GalaxyMapEditor.Workflows.Ports;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Infrastructure;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Presentation;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.Views;

namespace LE1GalaxyMapEditor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CsvGalaxyMapLoader _loader;
    private readonly GalaxyMapTextureService _textures;
    private readonly GalaxyMapModuleManifestStore _manifestStore = new();
    private readonly GalaxyMapWorkspaceStore _workspaceStore;
    private readonly IEditorDialogs _dialogs;
    private readonly ValidationCoordinator _validation;
    private readonly EditorSession _session = new();
    private readonly EditSessionService _edits;
    private readonly WorkspaceWorkflowService _workspaceWorkflows;
    private readonly RelayWorkflow _relay;
    private readonly ClusterTextureWorkflow _clusterTextures;
    private readonly PlanetRelationshipWorkflow _planetRelationships;
    private readonly InspectorEditWorkflow _inspectorEdits;
    private readonly RowAuthoringWorkflow _rowAuthoring;
    private readonly HierarchyNavigationCoordinator _navigation;

    private GalaxyMapWorkspace? _workspace;
    private GalaxyMapDocument? _document;
    private string _statusMessage = "Loading the built-in LE1 galaxy map.";
    private string _errorMessage = string.Empty;
    private bool _isCoordinateGridVisible;
    private bool _isShiftDragMode;
    private CoordinateDragSession? _coordinateDrag;
    private bool _isDiagnosticsPanelOpen;
    private bool _isApplying;

    public MainViewModel(
        CsvGalaxyMapLoader loader,
        GalaxyMapTextureService? textures = null,
        GalaxyMapWorkspaceStore? workspaceStore = null,
        Func<GalaxyMapRow, IReadOnlyList<GalaxyMapModule>, GalaxyMapModule?>? editTargetSelector = null,
        Func<string, bool>? confirmAction = null,
        IEditorDialogs? dialogs = null,
        IDeferredScheduler? deferredScheduler = null)
    {
        _loader = loader;
        _textures = textures ?? new GalaxyMapTextureService();
        _workspaceStore = workspaceStore ?? new GalaxyMapWorkspaceStore();
        _dialogs = dialogs ?? new WpfEditorDialogs(editTargetSelector, confirmAction);
        _validation = new ValidationCoordinator(deferredScheduler ?? new DispatcherDeferredScheduler());
        _validation.Completed += ValidationOnCompleted;
        _edits = new EditSessionService(_session, manifestStore: _manifestStore);
        _workspaceWorkflows = new WorkspaceWorkflowService(
            _session,
            _edits,
            _loader,
            _manifestStore,
            _workspaceStore);
        _relay = new RelayWorkflow(_session, _edits);
        _relay.StateChanged += RelayStateOnChanged;
        _clusterTextures = new ClusterTextureWorkflow(_session, _edits, _textures);
        _planetRelationships = new PlanetRelationshipWorkflow(_session, _edits);
        _inspectorEdits = new InspectorEditWorkflow(_session, _edits);
        _rowAuthoring = new RowAuthoringWorkflow(_session, _edits, _inspectorEdits);
        _session.Changed += SessionOnChanged;
        Inspector = new PropertyInspectorViewModel(new MainInspectorPresentationWorkflow(
            _session,
            _relay,
            () => HasActiveModule,
            BeginUserEdit,
            _inspectorEdits.ValidateEdit,
            ApplyManagedInspectorEdit,
            ExecuteInspectorAction));
        _navigation = new HierarchyNavigationCoordinator(
            _session,
            Inspector,
            _textures,
            GetClusterTexture,
            () => HasActiveModule,
            () => IsAddingRelay,
            HandlePendingRelaySelection,
            CloneRow,
            DeleteRow,
            MoveRowDialog,
            CanMoveOwnedRow,
            AddChildToHierarchyNode,
            ModelOnPropertyChanged,
            module => SetActiveModule(module));
        _navigation.Changed += NavigationOnChanged;

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
        ContextualAddCommand = new RelayCommand(AddForCurrentView, CanAddForCurrentView);
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
        UndoCommand = new RelayCommand(Undo, () => _edits.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _edits.CanRedo);
        DiscardChangesCommand = new RelayCommand(ConfirmDiscardChanges, () => HasPendingChanges);
        CancelRelayCommand = new RelayCommand(CancelRelayCreation);
    }

    public ObservableCollection<HierarchyNodeViewModel> HierarchyRoots => _navigation.HierarchyRoots;
    public BulkObservableCollection<GalaxyMapModule> MountedModules { get; } = [];
    public BulkObservableCollection<ModuleBarItemViewModel> ModuleBarItems { get; } = [];
    public ObservableCollection<RowInstanceTabViewModel> RowInstanceTabs => _navigation.RowInstanceTabs;
    public BulkObservableCollection<ValidationDiagnostic> ValidationDiagnostics { get; } = [];
    public PropertyInspectorViewModel Inspector { get; }

    public GalaxyMapWorkspace? Workspace
    {
        get => _workspace;
        private set
        {
            if (SetProperty(ref _workspace, value))
            {
                _session.Workspace = value;
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
    public EditorSession Session => _session;
    public bool HasPendingChanges => _session.Changes.HasChanges;
    public int PendingChangeCount => _session.Changes.Count;
    public string CommitButtonText => HasPendingChanges ? $"Commit ({PendingChangeCount})" : "Commit";
    public bool HasMultipleRowInstances => _navigation.HasMultipleRowInstances;
    public string ActiveModuleDisplay => ActiveModule is null
        ? "No active authoring module"
        : ActiveModule.Name;
    public ModuleColor ActiveModuleColor => ActiveModule?.Color ?? ModuleColor.BaseGameBlue;
    public object? CurrentViewModel => _navigation.CurrentViewModel;

    public bool HasContextualAddAction => CurrentViewModel is GalaxyViewModel or ClusterViewModel or SystemViewModel;
    public string ContextualAddButtonText => CurrentViewModel switch
    {
        GalaxyViewModel => "Add Cluster",
        ClusterViewModel => "Add System",
        SystemViewModel => "Add Planet/Object",
        _ => string.Empty
    };
    public string ContextualAddToolTip => CurrentViewModel switch
    {
        GalaxyViewModel => "Add a Cluster to the active module",
        ClusterViewModel => "Add a System to this Cluster",
        SystemViewModel => "Add a Planet or system object to this System",
        _ => string.Empty
    };

    public Cluster? CurrentCluster => _navigation.CurrentCluster;

    public GalaxySystem? CurrentSystem => _navigation.CurrentSystem;

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
    public string RelayLinkPrompt => _relay.Prompt;
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

    public Cluster? PendingRelaySource => _relay.Source;

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
    public RelayCommand ContextualAddCommand { get; }
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
        var result = _workspaceWorkflows.LoadBuiltIn();
        if (!result.Succeeded)
        {
            ErrorMessage = result.Error ?? result.Message;
            StatusMessage = "The built-in LE1 galaxy map could not be loaded.";
            return false;
        }

        Workspace = _session.Workspace;
        AttachWorkspace(Workspace!, result.Message);
        ErrorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Legacy full-export loading retained for fixture/reference inspection. It is
    /// always read-only and never becomes an authoring target.
    /// </summary>
    public bool LoadFolder(string folderPath)
    {
        var result = _workspaceWorkflows.LoadReferenceFolder(folderPath);
        if (result.Succeeded && _session.Document is { } document)
        {
            Workspace = null;
            RefreshMountedModules();
            AttachDocument(document, null, ViewContext.Galaxy);
            UpdateValidation();
            UpdateDocumentSummary(result.Message);
            ErrorMessage = string.Empty;
            RaiseCommandStates();
            return true;
        }

        ErrorMessage = result.Error ?? result.Message;
        StatusMessage = "The CSV folder could not be loaded. The current document was left unchanged.";
        return false;
    }

    public bool CreateModule(
        string parentFolder,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        var result = _workspaceWorkflows.CreateModule(parentFolder, name, tag, color, reservations, loadOrder);
        return ApplyWorkspaceResult(result);
    }

    public bool OpenExistingModule(string folderPath)
    {
        var result = _workspaceWorkflows.OpenExistingModule(folderPath);
        return ApplyWorkspaceResult(result);
    }

    public bool MountReadOnlyModule(
        string folderPath,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        var result = _workspaceWorkflows.MountReadOnlyModule(
            folderPath, name, tag, color, reservations, loadOrder);
        return ApplyWorkspaceResult(result);
    }

    public void ActivateHierarchyNode(HierarchyNodeViewModel node)
        => _navigation.ActivateHierarchyNode(node);

    public void SelectMapNode(HierarchyNodeViewModel node)
        => _navigation.SelectMapNode(node);

    private void CreateModuleDialog()
    {
        var parent = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var result = _dialogs.ConfigureModule(new ModuleSetupDialogRequest(
            SelectParentFolder: true,
            FolderPath: parent,
            SuggestedName: "New Galaxy Map Module",
            SuggestedTag: "NEW_GALAXY_MAP_MODULE",
            SuggestedReservations: WorkspaceWorkflowService.DefaultReservations(),
            SuggestedLoadOrder: _workspaceWorkflows.NextLoadOrder()));
        if (result is not null)
        {
            CreateModule(result.FolderPath, result.Name, result.Tag, result.Color, result.Reservations, result.LoadOrder);
        }
    }

    private void OpenModuleDialog()
    {
        var folder = _dialogs.PickModuleFolder();
        if (folder is null)
        {
            return;
        }

        if (File.Exists(Path.Combine(folder, GalaxyMapModuleManifestStore.FileName)))
        {
            OpenExistingModule(folder);
            return;
        }

        try
        {
            var name = new DirectoryInfo(folder).Name;
            var result = _dialogs.ConfigureModule(new ModuleSetupDialogRequest(
                SelectParentFolder: false,
                FolderPath: folder,
                SuggestedName: name,
                SuggestedTag: WorkspaceWorkflowService.SuggestedTag(name),
                SuggestedReservations: _workspaceWorkflows.InferReservations(folder),
                SuggestedLoadOrder: _workspaceWorkflows.NextLoadOrder()));
            if (result is not null)
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

        var result = _dialogs.ConfigureModule(new ModuleSetupDialogRequest(
            SelectParentFolder: false,
            FolderPath: module.FolderPath,
            SuggestedName: module.Name,
            SuggestedTag: module.Tag,
            SuggestedReservations: module.Reservations,
            SuggestedLoadOrder: module.LoadOrder,
            IsEditing: true,
            SuggestedColor: module.Color,
            CanSetActive: !module.IsReadOnly,
            IsActive: ReferenceEquals(module, ActiveModule),
            SetActiveAction: () => SetActiveModule(module),
            UnlinkAction: () => UnlinkModule(module)));
        if (result is null)
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
        var result = _workspaceWorkflows.SetActiveModule(module);
        if (result.Succeeded)
        {
            OnActiveModuleChanged();
            StatusMessage = result.Message;
            ErrorMessage = string.Empty;
            return true;
        }

        return Fail(result.Error ?? result.Message);
    }

    public bool UnlinkModule(GalaxyMapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (Workspace is null || module.IsBaseGame ||
            Workspace.ModuleLayers.All(layer => !ReferenceEquals(layer.Module, module)))
        {
            return false;
        }

        var hasStagedChanges = _session.Changes.ContainsModule(module.Tag);
        var stagedWarning = hasStagedChanges
            ? "\n\nThis module has staged changes. Unlinking it will discard those changes."
            : string.Empty;
        if (!Confirm(
                $"Unlink {module.Name} [{module.Tag}] from this workspace? Its folder and files will not be deleted.{stagedWarning}"))
        {
            StatusMessage = "Module unlink cancelled.";
            return false;
        }

        var result = _workspaceWorkflows.UnlinkModule(module);
        if (!result.Succeeded)
        {
            OnActiveModuleChanged();
            return Fail(result.Error ?? result.Message);
        }

        if (string.Equals(_navigation.PreferredInstanceTag, module.Tag, StringComparison.OrdinalIgnoreCase))
        {
            _navigation.RestoreInspectionState(null, false);
        }

        RefreshWorkspace(null, ViewContext.Galaxy, result.Message, recompose: false);
        NotifyPendingChanges();
        ErrorMessage = string.Empty;
        return true;
    }

    public bool UpdateModuleMetadata(
        GalaxyMapModule module,
        string name,
        string tag,
        ModuleColor color,
        int loadOrder,
        ModuleIdReservations reservations)
    {
        var oldTag = module.Tag;
        var result = _workspaceWorkflows.UpdateModuleMetadata(
            module,
            name,
            tag,
            color,
            loadOrder,
            reservations,
            CaptureHistoryPresentation());
        if (!result.Succeeded)
        {
            return Fail(result.Error ?? result.Message);
        }

        if (string.Equals(_navigation.PreferredInstanceTag, oldTag, StringComparison.OrdinalIgnoreCase))
        {
            _navigation.PreferredInstanceTag = tag;
        }

        RefreshWorkspace(result.SelectionKey, CaptureView(), result.Message, recompose: false);
        return true;
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

    private void AddCluster()
        => ApplyMutationResult(_rowAuthoring.CreateCluster(CaptureHistoryPresentation()));

    private bool CanAddForCurrentView() => HasActiveModule && CurrentViewModel switch
    {
        GalaxyViewModel => true,
        ClusterViewModel => CurrentCluster is not null,
        SystemViewModel => CurrentSystem is not null,
        _ => false
    };

    private void AddForCurrentView()
    {
        switch (CurrentViewModel)
        {
            case GalaxyViewModel:
                AddCluster();
                break;
            case ClusterViewModel when CurrentCluster is not null:
                AddSystem();
                break;
            case SystemViewModel when CurrentSystem is not null:
                AddPlanet();
                break;
        }
    }

    private void AddChildToHierarchyNode(HierarchyNodeViewModel node)
    {
        switch (node)
        {
            case { IsGalaxyRoot: true }:
                _navigation.ActivateHierarchyNode(node);
                NavigateGalaxy();
                AddCluster();
                break;
            case { Item: Cluster cluster }:
                _navigation.ActivateHierarchyNode(node);
                NavigateCluster(cluster);
                AddSystem();
                break;
            case { Item: GalaxySystem system }:
                _navigation.ActivateHierarchyNode(node);
                NavigateSystem(system);
                AddPlanet();
                break;
        }
    }

    private void AddSystem()
    {
        if (CurrentCluster is not { } cluster)
        {
            return;
        }

        ApplyMutationResult(_rowAuthoring.CreateSystem(cluster, CaptureHistoryPresentation()));
    }

    private void AddPlanet()
    {
        if (CurrentSystem is not { } system)
        {
            return;
        }

        var request = _dialogs.CreatePlanet();
        if (request is null)
        {
            return;
        }

        var destination = request.Destination is null
            ? null
            : new LandableDestinationChange(
                request.Destination.MapName,
                request.Destination.StartPoint,
                request.Destination.Event,
                request.Destination.ButtonLabel,
                request.Destination.AddPlotPlanet);
        ApplyMutationResult(_rowAuthoring.CreatePlanet(
            system,
            new PlanetCreationChange(request.Template, request.NameText, request.Name, request.Scale, destination),
            CaptureHistoryPresentation()));
    }

    private void CloneRow(GalaxyMapRow source)
    {
        CloneDefaults defaults;
        try
        {
            defaults = _rowAuthoring.GetCloneDefaults(source);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
            return;
        }

        if (_dialogs.ConfigureClone(source, defaults.RowId, defaults.Label) is { } request)
        {
            CloneRow(source, request);
        }
    }

    public bool CloneRow(GalaxyMapRow source, CloneContentRequest request)
    {
        var result = _rowAuthoring.Clone(
            source,
            new CloneRowChange(
                request.RowId,
                request.Label,
                request.Name,
                request.NameText,
                request.CloneChildren),
            CaptureHistoryPresentation());
        if (!result.Succeeded)
        {
            return Fail(result.Error ?? result.Message);
        }
        ApplyMutationResult(result);
        return true;
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
            var target = ResolveWritableTarget(row);
            if (target is null)
            {
                return false;
            }
            layer = Workspace.ModuleLayers.First(candidate =>
                string.Equals(candidate.Module.Tag, target.Tag, StringComparison.OrdinalIgnoreCase));
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

        var result = _rowAuthoring.StageCoordinates(
            session.Row,
            session.Layer,
            finalX,
            finalY,
            CaptureHistoryPresentation(session.Row.Key, session.View));
        if (!result.Succeeded)
        {
            return Fail(result.Error ?? result.Message);
        }

        RefreshWorkspace(
            result.SelectionKey,
            session.View,
            result.Message,
            recompose: false,
            preserveHierarchy: true,
            refreshModules: false,
            deferValidation: true);
        OnActiveModuleChanged();
        ErrorMessage = string.Empty;
        return true;
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
        var result = _edits.Commit();
        if (!result.Succeeded)
        {
            NotifyPendingChanges();
            return Fail(result.Error ?? result.Message);
        }

        if (Workspace is null || string.IsNullOrEmpty(result.Message))
        {
            return true;
        }

        try
        {
            _workspaceWorkflows.RememberCurrentWorkspace();
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return Fail($"Module files were committed, but the remembered workspace could not be updated: {exception.Message}");
        }
        RefreshWorkspace(_navigation.SelectedKey, CaptureView(), result.Message, recompose: false);
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

        _edits.DiscardChangeState();
        _navigation.RestoreInspectionState(null, false);
        NotifyPendingChanges();
        LoadBuiltIn();
        RestoreRememberedModules();
        StatusMessage = "Discarded all uncommitted changes.";
    }

    public bool RestoreRememberedModules()
    {
        var result = _workspaceWorkflows.RestoreRememberedModules();
        if (_session.Workspace is null)
        {
            return Fail(result.Error ?? result.Message);
        }

        Workspace = _session.Workspace;
        RefreshWorkspace(null, ViewContext.Galaxy, result.Message, recompose: false);
        ErrorMessage = result.Error ?? string.Empty;
        return result.Succeeded;
    }

    public bool RefreshRememberedWorkspace()
    {
        if (HasPendingChanges && !Confirm(
                "Refreshing reloads BASEGAME and every module remembered in workspace.json. Discard all uncommitted changes?"))
        {
            StatusMessage = "Refresh cancelled; staged changes were left intact.";
            return false;
        }

        _edits.DiscardChangeState();
        _navigation.RestoreInspectionState(null, false);
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

        return _dialogs.ChooseEditTarget(row, candidates, ActiveModule);
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

    private void SessionOnChanged(object? sender, SessionChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(_workspace, _session.Workspace))
        {
            Workspace = _session.Workspace;
        }

        NotifyPendingChanges();
        NotifyHistoryChanged();
    }

    private void NavigationOnChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(CurrentCluster));
        OnPropertyChanged(nameof(CurrentSystem));
        OnPropertyChanged(nameof(HasCurrentCluster));
        OnPropertyChanged(nameof(HasCurrentSystem));
        OnPropertyChanged(nameof(HasMultipleRowInstances));
        OnPropertyChanged(nameof(HasContextualAddAction));
        OnPropertyChanged(nameof(ContextualAddButtonText));
        OnPropertyChanged(nameof(ContextualAddToolTip));
        NavigateClusterCommand.RaiseCanExecuteChanged();
        AddSystemCommand.RaiseCanExecuteChanged();
        AddPlanetCommand.RaiseCanExecuteChanged();
        ContextualAddCommand.RaiseCanExecuteChanged();
    }

    private void RelayStateOnChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(PendingRelaySource));
        OnPropertyChanged(nameof(IsAddingRelay));
        OnPropertyChanged(nameof(RelayLinkPrompt));
    }

    private void ApplyMutationResult(WorkflowResult result)
    {
        if (!result.Succeeded)
        {
            if (result.Error is not null)
            {
                Fail(result.Error);
            }
            else
            {
                StatusMessage = result.Message;
            }
            return;
        }

        var navigation = result.Navigation ?? NavigationTarget.Galaxy;
        RefreshWorkspace(
            result.SelectionKey,
            new ViewContext(navigation.ClusterRowId, navigation.SystemRowId),
            result.Message,
            recompose: false);
        OnActiveModuleChanged();
        ErrorMessage = string.Empty;
    }

    private void ExecuteInspectorAction(GalaxyMapRow row, InspectorActionDescriptor action)
    {
        switch (action.Id)
        {
            case InspectorActionId.LinkClusterTexture when row is Cluster cluster:
                LinkClusterTexture(cluster);
                break;
            case InspectorActionId.ConfigureLandableDestination when row is Planet planet:
                ConfigureLandableDestination(planet);
                break;
            case InspectorActionId.AddPlotPlanet when row is Planet planet:
                AddPlotPlanet(planet);
                break;
            case InspectorActionId.AddLinkedMap when row is Planet planet:
                AddLinkedMap(planet);
                break;
            case InspectorActionId.DeleteLinkedPlotPlanet when row is Planet planet:
                DeleteLinkedPlotPlanet(planet);
                break;
            case InspectorActionId.DeleteLinkedMap when row is Planet planet:
                DeleteLinkedMap(planet);
                break;
            case InspectorActionId.BeginRelayCreation when row is Cluster cluster:
                BeginRelayCreation(cluster);
                break;
            case InspectorActionId.CancelRelayEdit:
                CancelRelayCreation();
                break;
            case InspectorActionId.RedirectRelay when row is Cluster cluster &&
                                                       action.Payload is RelayConnection relay:
                BeginRelayRedirect(cluster, relay);
                break;
            case InspectorActionId.RemoveRelay when action.Payload is RelayConnection relay:
                RemoveRelay(relay);
                break;
        }
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

        if (_navigation.TrySelect(row.Key))
        {
        }
        else if (row is PlotPlanetEntry && _navigation.TrySelect(
                     new GalaxyMapRowKey(GalaxyMapTable.Planet, row.RowId)))
        {
        }
        else if (row is MapEntry && Document.Planets.FirstOrDefault(planet => planet.MapRowId == row.RowId) is { } linkedPlanet &&
                 _navigation.TrySelect(linkedPlanet.Key))
        {
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

    private bool ApplyWorkspaceResult(WorkflowResult result)
    {
        if (!result.Succeeded || _session.Workspace is null)
        {
            return Fail(result.Error ?? result.Message);
        }

        Workspace = _session.Workspace;
        var navigation = result.Navigation ?? NavigationTarget.Galaxy;
        RefreshWorkspace(
            result.SelectionKey,
            new ViewContext(navigation.ClusterRowId, navigation.SystemRowId),
            result.Message,
            recompose: false);
        ErrorMessage = string.Empty;
        return true;
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
        _relay.Cancel();
        Document = document;
        _navigation.AttachDocument(
            document,
            selectionKey,
            new NavigationTarget(view.ClusterRowId, view.SystemRowId),
            preserveHierarchy);

        NavigateGalaxyCommand.RaiseCanExecuteChanged();
    }

    private bool HandlePendingRelaySelection(HierarchyNodeViewModel node)
    {
        if (!_relay.State.IsActive)
        {
            return false;
        }

        if (node.Item is Cluster target)
        {
            ApplyMutationResult(_relay.AcceptTarget(target, CaptureHistoryPresentation()));
            return true;
        }

        _relay.Cancel();
        return false;
    }

    private void NavigateGalaxy() => _navigation.NavigateGalaxy();

    private void NavigateCluster(Cluster cluster) => _navigation.NavigateCluster(cluster);

    private void NavigateSystem(GalaxySystem system) => _navigation.NavigateSystem(system);

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

        if (_dialogs.PickClusterTexture() is { } sourcePath)
        {
            StageClusterTexture(cluster, target, sourcePath);
        }
    }

    public bool StageClusterTexture(Cluster cluster, GalaxyMapModule target, string sourcePath)
    {
        var result = _clusterTextures.Stage(
            cluster,
            target,
            sourcePath,
            CaptureHistoryPresentation(cluster.Key));
        if (!result.Succeeded)
        {
            return Fail(result.Error ?? result.Message);
        }

        _navigation.PreferredInstanceTag = ActiveModule?.Tag;
        RefreshWorkspace(result.SelectionKey, CaptureView(), result.Message, recompose: false);
        return true;
    }

    private System.Windows.Media.ImageSource? GetClusterTexture(Cluster cluster)
    {
        var source = _clusterTextures.ResolveSource(cluster, Document);
        if (source.PendingContents is not null && source.CacheKey is not null)
        {
            return _textures.LoadTextureBytes(source.CacheKey, source.PendingContents);
        }

        if (source.Module is not null && source.LinkedClusterRowId is { } linkedClusterRowId &&
            _textures.GetModuleClusterTexture(source.Module, linkedClusterRowId) is { } moduleTexture)
        {
            return moduleTexture;
        }

        return _textures.GetClusterTexture(source.Background);
    }

    private void AddPlotPlanet(Planet planet)
    {
        var target = ResolveWritableTarget(planet);
        if (target is null)
        {
            return;
        }

        ApplyMutationResult(_planetRelationships.AddPlotPlanet(
            planet,
            target,
            CaptureHistoryPresentation(
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId))));
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

        var request = _dialogs.ConfigureLandableDestination(new LandableDestinationDefaults(
            planet.LinkedMap?.MapName ?? string.Empty,
            planet.LinkedMap?.StartPoint ?? string.Empty,
            planet.Event,
            planet.ButtonLabel,
            planet.PlotPlanet is null));
        if (request is null)
        {
            return;
        }

        var target = ResolveWritableTarget(planet);
        if (target is null)
        {
            return;
        }
        ApplyMutationResult(_planetRelationships.ConfigureLandableDestination(
            planet,
            target,
            new LandableDestinationChange(
                request.MapName,
                request.StartPoint,
                request.Event,
                request.ButtonLabel,
                request.AddPlotPlanet),
            CaptureHistoryPresentation(
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId))));
    }

    private void AddLinkedMap(Planet planet)
    {
        var target = ResolveWritableTarget(planet);
        if (target is null)
        {
            return;
        }

        ApplyMutationResult(_planetRelationships.AddLinkedMap(
            planet,
            target,
            CaptureHistoryPresentation(
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId))));
    }

    private void DeleteLinkedPlotPlanet(Planet planet)
    {
        if (Workspace is null || planet.PlotPlanet is not { } linked || !Confirm($"Delete PlotPlanet row {linked.RowId}?")) return;
        ApplyMutationResult(_planetRelationships.DeleteLinkedPlotPlanet(
            planet,
            CaptureHistoryPresentation(
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId))));
    }

    private void DeleteLinkedMap(Planet planet)
    {
        if (Workspace is null || planet.LinkedMap is not { } linked || !Confirm($"Delete Map row {linked.RowId} and clear its Planet link?")) return;
        var target = ResolveWritableTarget(planet);
        if (target is null) return;
        ApplyMutationResult(_planetRelationships.DeleteLinkedMap(
            planet,
            target,
            CaptureHistoryPresentation(
                planet.Key,
                new ViewContext(planet.System?.ClusterRowId, planet.SystemRowId))));
    }

    private GalaxyMapLayer? MovableOwningLayer(GalaxyMapRow row)
        => _rowAuthoring.MovableOwningLayer(row);

    public bool CanMoveOwnedRow(GalaxyMapRow row)
        => row is GalaxySystem or Planet;

    private void MoveRowDialog(GalaxyMapRow source)
    {
        if (Document is null || source is not (GalaxySystem or Planet))
        {
            return;
        }

        var target = ResolveWritableTarget(source);
        if (target is null)
        {
            return;
        }

        IReadOnlyList<MoveDestinationChoice> choices;
        try
        {
            choices = _rowAuthoring.GetMoveDestinations(source);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            Fail(exception.Message);
            return;
        }

        if (choices.Count == 0)
        {
            Fail($"No alternative {(source is GalaxySystem ? "Clusters" : "Systems")} are available.");
            return;
        }

        var options = choices.Select(choice => new MoveDestinationOption(
            choice.RowId,
            choice.DisplayName,
            choice.Detail,
            choice.CurrentLabel,
            choice.ResultingLabel)).ToArray();
        if (_dialogs.ChooseMoveDestination(source, options) is { } destination)
        {
            MoveRow(source, destination.RowId, target);
        }
    }

    public bool MoveRow(GalaxyMapRow source, int destinationParentRowId)
    {
        var target = ResolveWritableTarget(source);
        return target is not null && MoveRow(source, destinationParentRowId, target);
    }

    private bool MoveRow(GalaxyMapRow source, int destinationParentRowId, GalaxyMapModule target)
    {
        var result = _rowAuthoring.Move(
            source,
            destinationParentRowId,
            target,
            CaptureHistoryPresentation(source.Key, CaptureView()));
        if (!result.Succeeded)
        {
            return Fail(result.Error ?? result.Message);
        }
        ApplyMutationResult(result);
        return true;
    }

    private void DeleteRow(GalaxyMapRow row)
    {
        if (!Confirm($"Delete {row.Table} row {row.RowId} ({RowDisplayName(row)})?"))
        {
            return;
        }
        ApplyMutationResult(_rowAuthoring.Delete(row, CaptureHistoryPresentation()));
    }

    private void BeginRelayCreation(Cluster source)
    {
        var result = _relay.BeginCreation(source);
        if (!result.Succeeded)
        {
            Fail(result.Error ?? result.Message);
            return;
        }

        ErrorMessage = string.Empty;
        NavigateGalaxy();
        _navigation.TrySelectWithoutNavigation(source.Key);
        _navigation.RefreshInspector(source);
        StatusMessage = result.Message;
    }

    private void BeginRelayRedirect(Cluster source, RelayConnection relay)
    {
        var targetModule = ResolveWritableTarget(relay);
        if (targetModule is null)
        {
            return;
        }

        var result = _relay.BeginRedirect(source, relay, targetModule);
        if (!result.Succeeded)
        {
            Fail(result.Error ?? result.Message);
            return;
        }

        OnActiveModuleChanged();
        ErrorMessage = string.Empty;
        NavigateGalaxy();
        _navigation.TrySelectWithoutNavigation(source.Key);
        _navigation.RefreshInspector(source);
        StatusMessage = result.Message;
    }

    private void CancelRelayCreation()
    {
        if (_relay.Cancel() is not { } sourceKey)
        {
            return;
        }

        NavigateGalaxy();
        if (Workspace?.Resolve(sourceKey) is Cluster source)
        {
            _navigation.TrySelectWithoutNavigation(source.Key);
            _navigation.RefreshInspector(source);
        }
        UpdateDocumentSummary(null);
    }

    private void RemoveRelay(RelayConnection relay)
    {
        if (_navigation.SelectedRow is not Cluster selectedCluster)
        {
            Fail("Create or open a writable module before removing Relay connections.");
            return;
        }

        ApplyMutationResult(_relay.Remove(
            relay,
            selectedCluster,
            CaptureHistoryPresentation(selectedCluster.Key, ViewContext.Galaxy)));
    }

    private bool ApplyManagedInspectorEdit(GalaxyMapRow inspectedRow, string propertyName, object? value)
    {
        if (!_inspectorEdits.IsManaged(inspectedRow, propertyName, value))
        {
            return false;
        }

        var target = ResolveWritableTarget(inspectedRow);
        if (target is null)
        {
            _edits.CancelUserEdit();
            return true;
        }

        var edit = _inspectorEdits.ApplyEdit(
            inspectedRow,
            propertyName,
            value,
            target,
            CaptureHistoryPresentation(inspectedRow.Key));
        if (!edit.Handled)
        {
            return false;
        }

        if (edit.Result is not null)
        {
            if (!edit.Result.Succeeded)
            {
                _edits.CancelUserEdit();
            }
            ApplyMutationResult(edit.Result);
        }
        return true;
    }
    private void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isApplying || _edits.IsApplying || sender is not GalaxyMapRow row ||
            !_inspectorEdits.TryGetCsvColumn(row, eventArgs.PropertyName, out _))
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

        var view = CaptureView();
        var selectionKey = _navigation.SelectedKey ?? row.Key;
        var physicalLayer = Workspace.Layers.FirstOrDefault(candidate => ReferenceEquals(candidate.Find(row.Key), row));
        var targetModule = physicalLayer?.Module ?? ResolveWritableTarget(row);
        if (targetModule is null)
        {
            _edits.CancelUserEdit();
            Workspace.Recompose();
            RefreshWorkspace(selectionKey, view, null, recompose: false);
            if (!HasError)
            {
                StatusMessage = "Edit cancelled; the source row was left unchanged.";
            }
            return;
        }

        var result = _inspectorEdits.ApplyScalarEdit(
            row,
            eventArgs.PropertyName!,
            targetModule,
            CaptureHistoryPresentation(selectionKey, view));
        if (!result.Succeeded)
        {
            RefreshWorkspace(selectionKey, view, null, recompose: false);
            Fail(result.Error ?? result.Message);
            return;
        }

        _navigation.PreferredInstanceTag = targetModule.Tag;
        RefreshWorkspace(
            selectionKey,
            view,
            result.Message,
            recompose: false,
            preserveHierarchy: true,
            refreshModules: false,
            deferValidation: true);
        OnActiveModuleChanged();
        ErrorMessage = string.Empty;
    }

    private void UpdateValidation()
    {
        _validation.Cancel();
        ApplyValidation(_validation.Validate(Workspace, Document, _workspaceWorkflows.StartupDiagnostics));
    }

    private void ApplyValidation(ValidationSnapshot snapshot)
    {
        ValidationDiagnostics.ReplaceAll(snapshot.Diagnostics);

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
        => _validation.Schedule(
            () => _validation.Validate(Workspace, Document, _workspaceWorkflows.StartupDiagnostics),
            status);

    private void ValidationOnCompleted(object? sender, ValidationCompletedEventArgs eventArgs)
    {
        ApplyValidation(eventArgs.Snapshot);
        UpdateDocumentSummary(eventArgs.DeferredStatus);
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
                var isDirty = _session.Changes.ContainsModule(module.Tag);
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
        ContextualAddCommand.RaiseCanExecuteChanged();
        _navigation.RaiseNodeCommandStates();
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

    private bool Fail(string message)
    {
        ErrorMessage = message;
        StatusMessage = message;
        return false;
    }

    private static bool IsExpectedOperationFailure(Exception exception)
        => exception is GalaxyMapLoadException or IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or OverflowException;

    private readonly record struct ViewContext(int? ClusterRowId, int? SystemRowId)
    {
        public static ViewContext Galaxy => new(null, null);
    }

    private sealed record CoordinateDragSession(
        GalaxyMapRow Row,
        GalaxyMapLayer Layer,
        double OriginalX,
        double OriginalY,
        ViewContext View);

    private void BeginUserEdit()
        => _edits.BeginUserEdit(CaptureHistoryPresentation());

    private HistoryPresentationState CaptureHistoryPresentation(
        GalaxyMapRowKey? selectionKey = null,
        ViewContext? view = null)
    {
        var context = view ?? CaptureView();
        return new HistoryPresentationState(
            selectionKey ?? _navigation.SelectedKey,
            new NavigationTarget(context.ClusterRowId, context.SystemRowId),
            _navigation.PreferredInstanceTag,
            _navigation.InspectPhysicalInstance);
    }

    private void Undo()
    {
        var result = _edits.Undo(CaptureHistoryPresentation());
        ApplyHistoryRestore(result);
    }

    private void Redo()
    {
        var result = _edits.Redo(CaptureHistoryPresentation());
        ApplyHistoryRestore(result);
    }

    private void ApplyHistoryRestore(HistoryRestoreResult result)
    {
        if (!result.Succeeded || result.Presentation is not { } presentation)
        {
            return;
        }

        Workspace = _session.Workspace;
        _navigation.RestoreInspectionState(
            presentation.PreferredInstanceTag,
            presentation.InspectPhysicalInstance);
        RefreshWorkspace(
            presentation.SelectionKey,
            new ViewContext(presentation.Navigation.ClusterRowId, presentation.Navigation.SystemRowId),
            result.Message,
            recompose: false);
        NotifyPendingChanges();
        NotifyHistoryChanged();
    }

    private void ConfirmDiscardChanges()
    {
        if (Confirm("Discard every uncommitted change?"))
        {
            DiscardPendingChanges();
        }
    }

    private bool Confirm(string message)
        => _dialogs.Confirm(message);

    private void NotifyHistoryChanged()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }
}
