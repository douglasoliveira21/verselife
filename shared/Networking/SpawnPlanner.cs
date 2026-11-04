namespace LifeVerse.Shared.Networking;

/// <summary>Where a player arriving on an island stands. Shared by the API (which judges a saved spot) and the island server (which places the player).</summary>
public static class SpawnPlanner
{
    /// <summary>The distance to the edge of the island a spot keeps (metres).</summary>
    public const float EdgeMargin = 1f;
    /// <summary>The most a saved spot may be above the ground when the player comes back (a flight is not resumed in the air).</summary>
    public const float MaxHeightAboveGround = 3f;

    /// <summary>Whether a spot lies on the island (which goes from -size/2 to +size/2 on both axes) with a little room to the edge.</summary>
    public static bool Inside(float x, float z, int size)
    {
        var half = size / 2f - EdgeMargin;
        return float.IsFinite(x) && float.IsFinite(z) && Math.Abs(x) <= half && Math.Abs(z) <= half;
    }

    /// <summary>The place of a player who was not here before: around the spawn point of the island, eight to a row so nobody lands inside somebody else.</summary>
    public static (float X, float Z) Around(float spawnX, float spawnZ, int slot) => (spawnX + slot % 8 * 2f, spawnZ + slot / 8 * 2f);

    /// <summary>The place of a player coming back: the spot they were on (when it is on the island), or else around the spawn point.</summary>
    public static (float X, float Z) Choose(float spawnX, float spawnZ, int slot, (float X, float Z)? resume, int size) =>
        resume is { } spot && Inside(spot.X, spot.Z, size) ? spot : Around(spawnX, spawnZ, slot);

    /// <summary>The height to stand at: where they were, but never under the ground and never high in the air.</summary>
    public static float Height(float groundY, float? resumeY) => resumeY is { } y && float.IsFinite(y) ? Math.Clamp(y, groundY, groundY + MaxHeightAboveGround) : groundY;
}
