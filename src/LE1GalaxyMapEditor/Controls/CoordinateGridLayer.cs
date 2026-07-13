using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LE1GalaxyMapEditor.Controls;

public sealed class CoordinateGridLayer : FrameworkElement
{
    public const int DivisionCount = 40;
    public const int MajorDivisionInterval = 10;
    public const double MinorIncrement = 1d / DivisionCount;
    public const double MajorIncrement = MajorDivisionInterval * MinorIncrement;

    public static IReadOnlyList<string> AxisLabels { get; } = Enumerable.Range(0, (DivisionCount / MajorDivisionInterval) + 1)
        .Select(index => (index * MajorIncrement).ToString("0.00", CultureInfo.InvariantCulture))
        .ToArray();

    public static IReadOnlyList<string> BottomAxisLabels { get; } = AxisLabels.Skip(1).ToArray();

    public static IReadOnlyList<string> LeftAxisLabels { get; } = AxisLabels.Take(AxisLabels.Count - 1).ToArray();

    private Point? _cursorPosition;

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid),
        typeof(bool),
        typeof(CoordinateGridLayer),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public CoordinateGridLayer()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                HideCursor();
            }
        };
    }

    public Point? CursorPosition => _cursorPosition;

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public static Point NormalizePosition(Point position, Size surfaceSize)
    {
        var x = NormalizeAxis(position.X, surfaceSize.Width);
        var y = NormalizeAxis(position.Y, surfaceSize.Height);
        return new Point(x, y);
    }

    public static string FormatCoordinates(Point normalizedPosition)
    {
        var x = Math.Clamp(normalizedPosition.X, 0, 1);
        var y = Math.Clamp(normalizedPosition.Y, 0, 1);
        return FormattableString.Invariant($"X {x:0.00}   Y {y:0.00}");
    }

    public static Point RoundNormalizedPosition(Point normalizedPosition)
        => new(
            Math.Round(Math.Clamp(normalizedPosition.X, 0, 1), 2, MidpointRounding.AwayFromZero),
            Math.Round(Math.Clamp(normalizedPosition.Y, 0, 1), 2, MidpointRounding.AwayFromZero));

    public void ShowCursor(Point position)
    {
        _cursorPosition = position;
        InvalidateVisual();
    }

    public void HideCursor()
    {
        if (_cursorPosition is null)
        {
            return;
        }

        _cursorPosition = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minorGridBrush = new SolidColorBrush(Color.FromArgb(45, 144, 211, 232));
        var majorGridBrush = new SolidColorBrush(Color.FromArgb(112, 144, 211, 232));
        var borderBrush = new SolidColorBrush(Color.FromArgb(185, 170, 229, 246));
        var labelBrush = new SolidColorBrush(Color.FromRgb(218, 239, 247));
        var labelBackground = new SolidColorBrush(Color.FromArgb(185, 5, 13, 20));
        minorGridBrush.Freeze();
        majorGridBrush.Freeze();
        borderBrush.Freeze();
        labelBrush.Freeze();
        labelBackground.Freeze();

        var minorGridPen = new Pen(minorGridBrush, 0.75);
        var majorGridPen = new Pen(majorGridBrush, 1);
        var borderPen = new Pen(borderBrush, 1.5);
        minorGridPen.Freeze();
        majorGridPen.Freeze();
        borderPen.Freeze();

        if (ShowGrid)
        {
            for (var index = 0; index <= DivisionCount; index++)
            {
                var fraction = index / (double)DivisionCount;
                var x = PixelAligned(fraction * ActualWidth, ActualWidth);
                var y = PixelAligned(fraction * ActualHeight, ActualHeight);
                var pen = index is 0 or DivisionCount
                    ? borderPen
                    : index % MajorDivisionInterval == 0 ? majorGridPen : minorGridPen;
                drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
                drawingContext.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
            }

            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var labelIndex = 0; labelIndex < AxisLabels.Count; labelIndex++)
            {
                var text = AxisLabels[labelIndex];
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    labelBrush,
                    pixelsPerDip);

                var fraction = labelIndex * MajorIncrement;
                var x = Math.Clamp((fraction * ActualWidth) - (formatted.Width / 2), 3, ActualWidth - formatted.Width - 3);
                var y = Math.Clamp((fraction * ActualHeight) - (formatted.Height / 2), 3, ActualHeight - formatted.Height - 3);

                if (labelIndex > 0)
                {
                    var xLabelTop = ActualHeight - formatted.Height - 3;
                    DrawLabel(drawingContext, formatted, new Point(x, xLabelTop), labelBackground);
                }

                if (labelIndex < AxisLabels.Count - 1)
                {
                    DrawLabel(drawingContext, formatted, new Point(3, y), labelBackground);
                }
            }
        }

        if (_cursorPosition is Point cursorPosition)
        {
            DrawCursorCoordinates(
                drawingContext,
                cursorPosition,
                labelBrush,
                labelBackground,
                borderBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }

    private void DrawCursorCoordinates(
        DrawingContext drawingContext,
        Point cursorPosition,
        Brush foreground,
        Brush background,
        Brush border,
        double pixelsPerDip)
    {
        var normalizedPosition = NormalizePosition(cursorPosition, new Size(ActualWidth, ActualHeight));
        var formatted = new FormattedText(
            FormatCoordinates(normalizedPosition),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            11,
            foreground,
            pixelsPerDip);

        const double horizontalPadding = 6;
        const double verticalPadding = 3;
        const double cursorOffset = 14;
        const double edgePadding = 3;
        var width = formatted.Width + (horizontalPadding * 2);
        var height = formatted.Height + (verticalPadding * 2);

        var left = cursorPosition.X + cursorOffset;
        if (left + width > ActualWidth - edgePadding)
        {
            left = cursorPosition.X - cursorOffset - width;
        }

        var top = cursorPosition.Y + cursorOffset;
        if (top + height > ActualHeight - edgePadding)
        {
            top = cursorPosition.Y - cursorOffset - height;
        }

        left = ClampToSurface(left, width, ActualWidth, edgePadding);
        top = ClampToSurface(top, height, ActualHeight, edgePadding);

        var rect = new Rect(left, top, width, height);
        var borderPen = new Pen(border, 1);
        borderPen.Freeze();
        drawingContext.DrawRoundedRectangle(background, borderPen, rect, 3, 3);
        drawingContext.DrawText(
            formatted,
            new Point(left + horizontalPadding, top + verticalPadding));
    }

    private static void DrawLabel(
        DrawingContext drawingContext,
        FormattedText text,
        Point origin,
        Brush background)
    {
        var rect = new Rect(origin.X - 2, origin.Y - 1, text.Width + 4, text.Height + 2);
        drawingContext.DrawRoundedRectangle(background, null, rect, 2, 2);
        drawingContext.DrawText(text, origin);
    }

    private static double PixelAligned(double value, double maximum)
        => Math.Clamp(Math.Round(value) + 0.5, 0.5, Math.Max(0.5, maximum - 0.5));

    private static double NormalizeAxis(double value, double length)
        => length > 0 && double.IsFinite(length) && double.IsFinite(value)
            ? Math.Clamp(value / length, 0, 1)
            : 0;

    private static double ClampToSurface(double value, double extent, double surfaceExtent, double padding)
    {
        var maximum = Math.Max(padding, surfaceExtent - extent - padding);
        return Math.Clamp(value, padding, maximum);
    }
}
