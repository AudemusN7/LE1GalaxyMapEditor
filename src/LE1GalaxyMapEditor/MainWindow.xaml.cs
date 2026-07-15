using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;
using LE1GalaxyMapEditor.Infrastructure;

namespace LE1GalaxyMapEditor;

public partial class MainWindow : Window
{
    private HierarchyNodeViewModel? _coordinateDragNode;
    private bool _endingCoordinateDrag;
    private Point _coordinateDragStart;
    private Point _coordinateDragOffset;
    private bool _coordinateDragMoved;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        MapSquare.PreviewMouseMove += OnMapSquarePreviewMouseMove;
        MapSquare.PreviewMouseLeftButtonDown += OnMapSquarePreviewMouseLeftButtonDown;
        MapSquare.PreviewMouseLeftButtonUp += OnMapSquarePreviewMouseLeftButtonUp;
        MapSquare.LostMouseCapture += OnMapSquareLostMouseCapture;
        MapSquare.MouseLeave += OnMapSquareMouseLeave;
        CoordinateGrid.IsVisibleChanged += OnCoordinateGridIsVisibleChanged;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        PreviewKeyUp += MainWindow_OnPreviewKeyUp;
        Deactivated += MainWindow_OnDeactivated;
        Closed += MainWindow_OnClosed;
        Closing += MainWindow_OnClosing;
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        EndCoordinateDrag(cancel: true);
        if (DataContext is not MainViewModel { HasPendingChanges: true } viewModel)
        {
            return;
        }

        var dialog = new ConfirmationWindow(
            "Uncommitted galaxy-map changes",
            "There are uncommitted module changes. Commit them before closing?",
            "Commit",
            "Discard",
            "Cancel")
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Choice == ConfirmationChoice.Cancel)
        {
            eventArgs.Cancel = true;
        }
        else if (dialog.Choice == ConfirmationChoice.Primary && !viewModel.CommitPendingChanges())
        {
            eventArgs.Cancel = true;
        }
        else if (dialog.Choice == ConfirmationChoice.Secondary)
        {
            viewModel.DiscardPendingChanges();
        }
    }

    private void OnMapSquarePreviewMouseMove(object sender, MouseEventArgs eventArgs)
    {
        var shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        UpdateShiftDragMode(shiftHeld);

        var position = eventArgs.GetPosition(MapSquare);
        if (_coordinateDragNode is { Model: { } row } && DataContext is MainViewModel viewModel)
        {
            if (eventArgs.LeftButton == MouseButtonState.Pressed)
            {
                if (!_coordinateDragMoved &&
                    (Math.Abs(position.X - _coordinateDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(position.Y - _coordinateDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance))
                {
                    _coordinateDragMoved = true;
                }

                if (_coordinateDragMoved)
                {
                    var normalized = CoordinateGridLayer.NormalizePosition(
                        position,
                        new Size(MapSquare.ActualWidth, MapSquare.ActualHeight));
                    viewModel.PreviewCoordinateDrag(
                        row,
                        new Point(normalized.X + _coordinateDragOffset.X, normalized.Y + _coordinateDragOffset.Y));
                }
                eventArgs.Handled = true;
            }
            else
            {
                EndCoordinateDrag(cancel: false);
            }
        }

        if (CoordinateGrid.IsVisible)
        {
            CoordinateGrid.ShowCursor(position);
        }
        else
        {
            CoordinateGrid.HideCursor();
        }
    }

    private void OnMapSquarePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ||
            FindHierarchyNode(eventArgs.OriginalSource as DependencyObject) is not { Model: { } row } node ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        UpdateShiftDragMode(true);
        viewModel.SelectMapNode(node);
        if (!viewModel.BeginCoordinateDrag(row))
        {
            return;
        }

        _coordinateDragNode = node;
        Mouse.Capture(MapSquare, CaptureMode.SubTree);
        var position = eventArgs.GetPosition(MapSquare);
        var normalized = CoordinateGridLayer.NormalizePosition(
            position,
            new Size(MapSquare.ActualWidth, MapSquare.ActualHeight));
        var rowCoordinates = RowCoordinates(row);
        _coordinateDragStart = position;
        _coordinateDragOffset = new Point(
            rowCoordinates.X - normalized.X,
            rowCoordinates.Y - normalized.Y);
        _coordinateDragMoved = false;
        CoordinateGrid.ShowCursor(position);
        eventArgs.Handled = true;
    }

    private void OnMapSquarePreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_coordinateDragNode is null)
        {
            return;
        }

        EndCoordinateDrag(cancel: false);
        eventArgs.Handled = true;
    }

    private void OnMapSquareLostMouseCapture(object sender, MouseEventArgs eventArgs)
    {
        if (!_endingCoordinateDrag && _coordinateDragNode is not null)
        {
            EndCoordinateDrag(cancel: false);
        }
    }

    private void OnMapSquareMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _coordinateDragNode is null)
        {
            CoordinateGrid.HideCursor();
        }
    }

    private void OnCoordinateGridIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (!CoordinateGrid.IsVisible)
        {
            CoordinateGrid.HideCursor();
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Key.LeftShift or Key.RightShift)
        {
            UpdateShiftDragMode(true);
        }
        else if (eventArgs.Key == Key.Escape && _coordinateDragNode is not null)
        {
            EndCoordinateDrag(cancel: true);
            eventArgs.Handled = true;
        }
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.LeftShift or Key.RightShift))
        {
            return;
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            EndCoordinateDrag(cancel: false);
            UpdateShiftDragMode(false);
        }
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs eventArgs)
    {
        EndCoordinateDrag(cancel: true);
        UpdateShiftDragMode(false);
        CoordinateGrid.HideCursor();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs eventArgs)
    {
        MapSquare.ForceCursor = false;
        MapSquare.ClearValue(CursorProperty);
    }

    private void UpdateShiftDragMode(bool enabled)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetShiftDragMode(enabled);
        }
        MapSquare.ForceCursor = enabled;
        if (enabled)
        {
            MapSquare.Cursor = Cursors.SizeAll;
        }
        else
        {
            MapSquare.ClearValue(CursorProperty);
        }
        if (enabled && MapSquare.IsMouseOver)
        {
            CoordinateGrid.ShowCursor(Mouse.GetPosition(MapSquare));
        }
    }

    private void EndCoordinateDrag(bool cancel)
    {
        if (_coordinateDragNode is null && DataContext is not MainViewModel { HasActiveCoordinateDrag: true })
        {
            return;
        }

        try
        {
            _endingCoordinateDrag = true;
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.CompleteCoordinateDrag(cancel);
            }
            _coordinateDragNode = null;
            _coordinateDragMoved = false;
            _coordinateDragOffset = default;
            if (ReferenceEquals(Mouse.Captured, MapSquare) || MapSquare.IsMouseCaptureWithin)
            {
                Mouse.Capture(null);
            }
        }
        finally
        {
            _endingCoordinateDrag = false;
        }
    }

    private static HierarchyNodeViewModel? FindHierarchyNode(DependencyObject? source)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement { DataContext: HierarchyNodeViewModel node })
            {
                return node;
            }
        }

        return null;
    }

    private static Point RowCoordinates(GalaxyMapRow row) => row switch
    {
        Cluster cluster => new Point(cluster.X, cluster.Y),
        GalaxySystem system => new Point(system.X, system.Y),
        Planet planet => new Point(planet.X, planet.Y),
        _ => default
    };

    private void InspectorField_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || sender is not TextBox { IsReadOnly: false } textBox)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (textBox.DataContext is InspectorFieldViewModel { HasError: false })
        {
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        eventArgs.Handled = true;
    }

    private void ColorField_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: InspectorFieldViewModel field } || field.IsReadOnly) return;
        var dialog = new ColorPickerWindow(field.Value) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } value) field.Value = value;
    }

    private void DiagnosticsHeader_OnDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        DiagnosticsTransform.X += eventArgs.HorizontalChange;
        DiagnosticsTransform.Y += eventArgs.VerticalChange;
    }

    private void DiagnosticsResize_OnDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        var oldWidth = DiagnosticsPanel.ActualWidth;
        var oldHeight = DiagnosticsPanel.ActualHeight;
        var newWidth = Math.Max(DiagnosticsPanel.MinWidth, oldWidth + eventArgs.HorizontalChange);
        var newHeight = Math.Max(DiagnosticsPanel.MinHeight, oldHeight + eventArgs.VerticalChange);
        DiagnosticsPanel.Width = newWidth;
        DiagnosticsPanel.Height = newHeight;
        // The panel is right/bottom anchored. Offset it by the applied growth so
        // its top-left corner stays still while the resize grip follows the mouse.
        DiagnosticsTransform.X += newWidth - oldWidth;
        DiagnosticsTransform.Y += newHeight - oldHeight;
    }

    private void HierarchyItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left ||
            sender is not TreeViewItem treeViewItem ||
            treeViewItem.DataContext is not HierarchyNodeViewModel node ||
            !ReferenceEquals(FindNearestTreeViewItem(eventArgs.OriginalSource as DependencyObject), treeViewItem) ||
            !node.IsSelected ||
            IsInsideExpanderToggle(eventArgs.OriginalSource as DependencyObject, treeViewItem) ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // Clicking an already-selected row does not change IsSelected. Activate
        // it explicitly so a map-selected item still navigates from the sidebar.
        viewModel.ActivateHierarchyNode(node);
    }

    private static TreeViewItem? FindNearestTreeViewItem(DependencyObject? source)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is TreeViewItem item)
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsInsideExpanderToggle(DependencyObject? source, DependencyObject boundary)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, boundary); current = GetParent(current))
        {
            if (current is ToggleButton)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement) ??
                   (contentElement as FrameworkContentElement)?.Parent;
        }

        return child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }
}
