namespace LE1GalaxyMapEditor.Models;

/// <summary>
/// Hard limits imposed by LE1's numbered galaxy-map labels and ActiveWorld encoding.
/// </summary>
public static class GalaxyMapIdentityLimits
{
    public const int MaxClusterLabel = 99;
    public const int MinAuthoredClusterLabel = 22;
    public const int MaxSystemLabel = 9;
    public const int MaxPlanetLabel = 99;
    public const int MaxActiveWorld = 990_999;

    public static int MaxLabel(string prefix) => prefix.ToUpperInvariant() switch
    {
        "CLUSTER" => MaxClusterLabel,
        "SYSTEM" => MaxSystemLabel,
        "PLANET" => MaxPlanetLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unknown galaxy-map label prefix.")
    };
}
