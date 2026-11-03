namespace LifeVerse.Shared.Building;

/// <summary>
/// Limits for imported 3D models, shown to the person before they upload. The API enforces the same numbers
/// (<c>GltfLimits</c>, which cannot reference this project); a test keeps the two lists equal.
/// </summary>
public static class ModelLimits
{
    public const int MaxTriangles = 300_000;
    public const int MaxMaterials = 64;
    public const int MaxTextures = 32;
    public const int MaxNodes = 5_000;
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>Null when a model with these numbers is within the limits, otherwise what to fix (in Portuguese, for the import window).</summary>
    public static string? Check(int triangles, int materials, int textures, int nodes, long bytes)
    {
        if (bytes > MaxBytes) return $"O arquivo tem mais de {MaxBytes / (1024 * 1024)} MB.";
        if (triangles == 0) return "O modelo não tem triângulos para mostrar.";
        if (triangles > MaxTriangles) return $"O modelo tem {triangles:N0} triângulos; o limite é {MaxTriangles:N0}. Simplifique-o antes de importar.";
        if (materials > MaxMaterials) return $"O modelo tem {materials} materiais; o limite é {MaxMaterials}.";
        if (textures > MaxTextures) return $"O modelo usa {textures} texturas; o limite é {MaxTextures}.";
        if (nodes > MaxNodes) return $"O modelo tem {nodes:N0} partes; o limite é {MaxNodes:N0}.";
        return null;
    }
}
