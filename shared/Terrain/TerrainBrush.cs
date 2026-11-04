namespace LifeVerse.Shared.Terrain;

/// <summary>
/// How a brush looks and behaves beyond its tool, radius and strength: a round or square footprint, how hard its edge is (0 is the soft
/// default falloff, 1 applies the full effect right up to the edge) and, for "Nivelar", the height to level to.
/// </summary>
public readonly record struct BrushOptions(bool Square = false, float Hardness = 0f, float? TargetHeight = null)
{
    public static BrushOptions Default => new();
    public bool IsDefault => !Square && Hardness <= 0f && TargetHeight is null;
}

/// <summary>
/// The layer number and the brush options travel together in the one integer the terrain edit message already has, so old and new
/// messages keep the same shape: a plain 0 to 15 is just a layer (a round, soft brush), exactly as before.
/// Bits 0-3 layer, bit 4 square, bits 5-8 hardness in sixteenths, bit 9 "has a target height", bits 10-25 the height in millimetres above the lowest ground.
/// </summary>
public static class BrushCodec
{
    public static int Pack(int layer, BrushOptions brush)
    {
        var packed = layer & 0xF;
        if (brush.Square) packed |= 1 << 4;
        packed |= Math.Clamp((int)MathF.Round(Math.Clamp(brush.Hardness, 0f, 1f) * 15f), 0, 15) << 5;
        if (brush.TargetHeight is { } height && float.IsFinite(height)) packed |= (1 << 9) | (Math.Clamp((int)MathF.Round((height - TerrainMap.MinHeight) * 1000f), 0, 65535) << 10);
        return packed;
    }

    /// <summary>False when the number is not a valid message (negative, bits beyond the fields, a layer that does not exist).</summary>
    public static bool TryUnpack(int packed, out int layer, out BrushOptions brush)
    {
        layer = packed & 0xF; brush = BrushOptions.Default;
        if (packed < 0 || packed >> 26 != 0 || layer >= TerrainMap.LayerCount) return false;
        var hasTarget = (packed >> 9 & 1) == 1;
        brush = new BrushOptions((packed >> 4 & 1) == 1, (packed >> 5 & 0xF) / 15f, hasTarget ? TerrainMap.MinHeight + (packed >> 10 & 0xFFFF) / 1000f : null);
        return true;
    }
}
