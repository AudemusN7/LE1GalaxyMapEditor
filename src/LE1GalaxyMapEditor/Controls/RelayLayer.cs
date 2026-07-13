using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Controls;

public sealed class RelayLayer : FrameworkElement
{
    public static readonly DependencyProperty ConnectionsProperty = DependencyProperty.Register(
        nameof(Connections), typeof(IEnumerable), typeof(RelayLayer),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender,
            ConnectionsOnChanged));

    public static readonly DependencyProperty RefreshTokenProperty = DependencyProperty.Register(
        nameof(RefreshToken), typeof(int), typeof(RelayLayer),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Connections
    {
        get => (IEnumerable?)GetValue(ConnectionsProperty);
        set => SetValue(ConnectionsProperty, value);
    }

    public int RefreshToken
    {
        get => (int)GetValue(RefreshTokenProperty);
        set => SetValue(RefreshTokenProperty, value);
    }

    private static void ConnectionsOnChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var layer = (RelayLayer)dependencyObject;
        if (eventArgs.OldValue is INotifyCollectionChanged oldCollection)
        {
            CollectionChangedEventManager.RemoveHandler(oldCollection, layer.ConnectionsOnCollectionChanged);
        }

        if (eventArgs.NewValue is INotifyCollectionChanged newCollection)
        {
            CollectionChangedEventManager.AddHandler(newCollection, layer.ConnectionsOnCollectionChanged);
        }

        layer.InvalidateVisual();
    }

    private void ConnectionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Connections is null)
        {
            return;
        }

        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 255, 46, 66)), 7);
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(235, 49, 69)), 2);
        foreach (var relay in Connections.OfType<RelayConnection>().Where(relay => relay.IsResolved))
        {
            var start = new Point(relay.StartCluster!.X * ActualWidth, relay.StartCluster.Y * ActualHeight);
            var end = new Point(relay.EndCluster!.X * ActualWidth, relay.EndCluster.Y * ActualHeight);
            drawingContext.DrawLine(glowPen, start, end);
            drawingContext.DrawLine(linePen, start, end);
        }
    }
}
