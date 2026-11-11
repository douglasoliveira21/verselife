namespace LifeVerse.Shared.Building;

/// <summary>
/// The shapes an object can have. The island server checks every build command against this list before it reaches the API (which keeps
/// its own copy, <c>BuildCatalog</c>, because its container does not hold this project); a test keeps the two equal.
/// </summary>
public static class PrimitiveCatalog
{
    public static readonly string[] Types = ["cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid", "torus", "hemisphere", "tree", "grass", "mesh"];

    public static bool IsKnown(string type) => Types.Contains(type, StringComparer.OrdinalIgnoreCase);
}
