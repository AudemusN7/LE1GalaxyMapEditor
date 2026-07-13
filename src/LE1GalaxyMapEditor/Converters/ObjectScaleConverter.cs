using System.Globalization;
using System.Windows.Data;

namespace LE1GalaxyMapEditor.Converters;

public sealed class ObjectScaleConverter : IValueConverter
{
    public const double MinimumSourceScale = 0.5d;
    public const double MaximumSourceScale = 8d;

    public static double Calculate(double sourceScale)
    {
        var clamped = Math.Clamp(
            double.IsFinite(sourceScale) ? sourceScale : 1d,
            MinimumSourceScale,
            MaximumSourceScale);
        var logarithmicScale = Math.Log2(clamped);
        return 1d + (0.3541666666666667d * logarithmicScale) +
               (0.10416666666666667d * logarithmicScale * logarithmicScale);
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double scale ? Calculate(scale) : 1d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
