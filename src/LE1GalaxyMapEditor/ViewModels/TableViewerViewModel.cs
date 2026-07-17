using System.Collections.ObjectModel;
using LE1GalaxyMapEditor.Infrastructure;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Queries;

namespace LE1GalaxyMapEditor.ViewModels;

public sealed class TableViewerViewModel : ObservableObject
{
    private static readonly GalaxyMapTable[] TabOrder =
    [
        GalaxyMapTable.Cluster,
        GalaxyMapTable.Relay,
        GalaxyMapTable.System,
        GalaxyMapTable.Planet,
        GalaxyMapTable.PlotPlanet,
        GalaxyMapTable.Map
    ];

    private readonly TableProjectionService _projection;
    private readonly Func<GalaxyMapRowKey, string, string, WorkflowResult> _applyEdit;
    private readonly Func<bool> _canEdit;
    private IReadOnlyList<TableColumn> _columns = [];
    private GalaxyMapTable _selectedTable = GalaxyMapTable.Cluster;
    private bool _needsRefresh = true;
    private bool _isEditingAvailable;
    private long _sessionRevision;

    public TableViewerViewModel(
        TableProjectionService projection,
        Func<GalaxyMapRowKey, string, string, WorkflowResult> applyEdit,
        Func<bool> canEdit)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _applyEdit = applyEdit ?? throw new ArgumentNullException(nameof(applyEdit));
        _canEdit = canEdit ?? throw new ArgumentNullException(nameof(canEdit));
        Tabs = new ObservableCollection<TableTabViewModel>(TabOrder.Select(table =>
            new TableTabViewModel(table, () => SelectedTable = table)));
        UpdateTabSelection();
    }

    public ObservableCollection<TableTabViewModel> Tabs { get; }
    public BulkObservableCollection<TableRowViewModel> Rows { get; } = [];
    public IReadOnlyList<TableColumn> Columns
    {
        get => _columns;
        private set
        {
            _columns = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColumnCount));
        }
    }

    public GalaxyMapTable SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (!SetProperty(ref _selectedTable, value))
            {
                return;
            }

            UpdateTabSelection();
            Invalidate();
            RefreshIfNeeded();
            OnPropertyChanged(nameof(Title));
        }
    }

    public int RowCount => Rows.Count;
    public int ColumnCount => Columns.Count;
    public string Title => $"GalaxyMap_{SelectedTable}";
    public long SessionRevision => _sessionRevision;
    public bool IsEditingAvailable
    {
        get => _isEditingAvailable;
        private set
        {
            if (SetProperty(ref _isEditingAvailable, value))
            {
                OnPropertyChanged(nameof(EditingHint));
            }
        }
    }

    public string EditingHint => IsEditingAvailable
        ? "Double-click or press F2 to edit • Enter/Tab applies • Esc cancels • Shift + wheel scrolls horizontally"
        : "Read-only preview • Create or open a writable module to edit • Shift + wheel scrolls horizontally";

    public void Invalidate(ChangeImpact? impact = null)
    {
        if (impact is null || impact.IsStructural || impact.Tables.Contains(SelectedTable))
        {
            _needsRefresh = true;
        }
    }

    public void RefreshIfNeeded(bool force = false)
    {
        if (!force && !_needsRefresh)
        {
            return;
        }

        var snapshot = _projection.Project(SelectedTable);
        // Column generation queries this value synchronously when Columns changes.
        // Publish edit availability first so writable columns are not created read-only.
        var canEdit = _canEdit();
        var editingAvailabilityChanged = IsEditingAvailable != canEdit;
        IsEditingAvailable = canEdit;
        if (editingAvailabilityChanged || !Columns.SequenceEqual(snapshot.Columns))
        {
            Columns = snapshot.Columns;
        }
        Rows.ReplaceAll(snapshot.Rows.Select(row => ProjectRow(row, snapshot.Columns)));
        _sessionRevision = snapshot.SessionRevision;
        _needsRefresh = false;
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(SessionRevision));
        OnPropertyChanged(nameof(EditingHint));
    }

    public bool IsColumnReadOnly(TableColumn column)
        => !IsEditingAvailable ||
           string.Equals(column.Name, CsvRowSnapshot.RowIdColumnName, StringComparison.OrdinalIgnoreCase) ||
           SelectedTable == GalaxyMapTable.Planet &&
           string.Equals(column.Name, "ActiveWorld", StringComparison.OrdinalIgnoreCase);

    public WorkflowResult CommitCellEdit(TableRowViewModel row, int columnIndex, string token)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count || columnIndex >= row.Cells.Count)
        {
            return WorkflowResult.Failure("The selected table cell is no longer available.", row.Key);
        }

        var column = Columns[columnIndex];
        var cell = row.Cells[columnIndex];
        if (IsColumnReadOnly(column))
        {
            var message = !IsEditingAvailable
                ? "Create or open a writable module before editing table cells."
                : $"{column.Name} is managed by the editor and is read-only here.";
            cell.SetValidationError(message);
            return WorkflowResult.Failure(message, row.Key);
        }

        var result = _applyEdit(row.Key, column.Name, token);
        cell.SetValidationError(result.Succeeded ? null : result.Error ?? result.Message);
        if (result.Succeeded)
        {
            Invalidate(result.Impact);
        }
        return result;
    }

    private static TableRowViewModel ProjectRow(MergedTableRow row, IReadOnlyList<TableColumn> columns)
        => new(
            row.Key,
            columns.Select(column =>
            {
                var cell = row.Cells[column.Name];
                return new TableCellViewModel(
                    cell.DisplayValue,
                    cell.EffectiveModuleTag,
                    cell.EffectiveModuleColor,
                    cell.IsStaged,
                    cell.DiffersFromLowerInstance,
                    cell.OverrideChain.Count);
            }).ToArray());

    private void UpdateTabSelection()
    {
        foreach (var tab in Tabs)
        {
            tab.IsSelected = tab.Table == SelectedTable;
        }
    }
}

public sealed class TableTabViewModel : ObservableObject
{
    private bool _isSelected;

    public TableTabViewModel(GalaxyMapTable table, Action select)
    {
        Table = table;
        Label = table.ToString();
        SelectCommand = new RelayCommand(select);
    }

    public GalaxyMapTable Table { get; }
    public string Label { get; }
    public RelayCommand SelectCommand { get; }
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}

public sealed class TableRowViewModel
{
    public TableRowViewModel(GalaxyMapRowKey key, IReadOnlyList<TableCellViewModel> cells)
    {
        Key = key;
        Cells = cells;
    }

    public GalaxyMapRowKey Key { get; }
    public IReadOnlyList<TableCellViewModel> Cells { get; }
}

public sealed class TableCellViewModel : ObservableObject
{
    private string _editValue;
    private string? _validationError;

    public TableCellViewModel(
        string displayValue,
        string effectiveModuleTag,
        ModuleColor effectiveModuleColor,
        bool isStaged,
        bool differsFromLowerInstance,
        int overrideCount)
    {
        DisplayValue = displayValue;
        _editValue = displayValue;
        EffectiveModuleTag = effectiveModuleTag;
        EffectiveModuleColor = effectiveModuleColor;
        IsStaged = isStaged;
        DiffersFromLowerInstance = differsFromLowerInstance;
        OverrideCount = overrideCount;
    }

    public string DisplayValue { get; }
    public string EditValue
    {
        get => _editValue;
        set => SetProperty(ref _editValue, value);
    }

    public string EffectiveModuleTag { get; }
    public ModuleColor EffectiveModuleColor { get; }
    public bool IsStaged { get; }
    public bool DiffersFromLowerInstance { get; }
    public int OverrideCount { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(ValidationError);
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

    public string ToolTipText
    {
        get
        {
            if (ValidationError is { Length: > 0 })
            {
                return ValidationError;
            }

            var provenance = $"Effective value supplied by {EffectiveModuleTag}.";
            var overrides = OverrideCount > 1
                ? $" {OverrideCount} mounted instances exist for this row."
                : string.Empty;
            var comparison = DiffersFromLowerInstance
                ? " This value differs from the next lower instance."
                : string.Empty;
            var staged = IsStaged ? " This cell has an uncommitted change." : string.Empty;
            return provenance + overrides + comparison + staged;
        }
    }

    internal void SetValidationError(string? error) => ValidationError = error;
}
