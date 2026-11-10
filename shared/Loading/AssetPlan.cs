using System.Security.Cryptography;

namespace LifeVerse.Shared.Loading;

public enum AssetKind { Model, Texture }

/// <summary>One thing an island needs to show its objects: a 3D model or an image, with the hash that proves the bytes are the right ones and how far from the arrival point it is used.</summary>
public sealed record AssetEntry(Guid AssetId, AssetKind Kind, string Hash, long Size, string Mime, double Distance);

public sealed record AssetPlanResult(IReadOnlyList<AssetEntry> Essential, IReadOnlyList<AssetEntry> Deferred)
{
    public long EssentialBytes => Essential.Sum(a => a.Size);
    public long DeferredBytes => Deferred.Sum(a => a.Size);
}

/// <summary>
/// Which assets must be there before the person enters (the ones around the arrival point, up to a size budget) and which can keep loading afterwards.
/// The nearest come first; what is far, or would not fit in the budget, is deferred. Whatever the plan says, the same asset is never listed twice.
/// </summary>
public static class AssetPlanner
{
    public const double DefaultRadius = 80;
    public const long DefaultBudgetBytes = 24L * 1024 * 1024;

    public static AssetPlanResult Plan(IEnumerable<AssetEntry> entries, double radius = DefaultRadius, long budgetBytes = DefaultBudgetBytes)
    {
        var ordered = entries.GroupBy(e => e.AssetId).Select(g => g.OrderBy(e => e.Distance).First()).OrderBy(e => e.Distance).ThenBy(e => e.Size).ToList();
        var essential = new List<AssetEntry>(); var deferred = new List<AssetEntry>(); long used = 0;
        foreach (var entry in ordered)
        {
            if (entry.Distance <= radius && used + entry.Size <= budgetBytes) { essential.Add(entry); used += entry.Size; }
            else deferred.Add(entry);
        }
        return new AssetPlanResult(essential, deferred);
    }

    /// <summary>True when the bytes hash (SHA-256) to the hexadecimal value the server announced.</summary>
    public static bool HashMatches(string expectedHex, ReadOnlySpan<byte> bytes) =>
        !string.IsNullOrWhiteSpace(expectedHex) && string.Equals(expectedHex.Trim(), Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase);
}
