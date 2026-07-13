using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Converters;

public sealed class PlanetGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PlanetVisualKind kind
            ? kind switch
            {
                PlanetVisualKind.Planet => "●",
                PlanetVisualKind.AsteroidBelt => "▲",
                PlanetVisualKind.Anomaly => "◆",
                PlanetVisualKind.RingedPlanet => "◉",
                PlanetVisualKind.Relay => "⇄",
                PlanetVisualKind.FuelDepot => "▣",
                PlanetVisualKind.Sun => "☀",
                _ => "◇"
            }
            : "●";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PlanetBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PlanetVisualKind kind
            ? kind switch
            {
                PlanetVisualKind.Planet => new SolidColorBrush(Color.FromRgb(103, 196, 225)),
                PlanetVisualKind.AsteroidBelt => new SolidColorBrush(Color.FromRgb(204, 183, 145)),
                PlanetVisualKind.Anomaly => new SolidColorBrush(Color.FromRgb(230, 172, 83)),
                PlanetVisualKind.RingedPlanet => new SolidColorBrush(Color.FromRgb(177, 151, 231)),
                PlanetVisualKind.Relay => new SolidColorBrush(Color.FromRgb(238, 70, 83)),
                PlanetVisualKind.FuelDepot => new SolidColorBrush(Color.FromRgb(104, 210, 151)),
                PlanetVisualKind.Sun => new SolidColorBrush(Color.FromRgb(255, 215, 91)),
                _ => new SolidColorBrush(Color.FromRgb(180, 190, 201))
            }
            : Brushes.LightBlue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
