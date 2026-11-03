using System.Text.Json;
using LifeVerse.Api.Data;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>A parcel (terreno) as the build rules see it.</summary>
public sealed record ParcelFacts(Guid Id, string Name, Guid OwnerUserId, bool PublicBuild, int MaxObjects, double MinX, double MinZ, double MaxX, double MaxZ)
{
    /// <summary>Per-parcel cost budget: every object slot of the parcel is worth this many cost points.</summary>
    public const int CostPerObjectSlot = 20;

    public bool Contains(double x, double z) => x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;
    public int MaxCost => (int)Math.Min(int.MaxValue, (long)MaxObjects * CostPerObjectSlot);

    public static ParcelFacts? From(IslandParcel parcel)
    {
        try
        {
            using var bounds = JsonDocument.Parse(parcel.BoundsJson);
            var b = bounds.RootElement;
            bool Build() { try { using var p = JsonDocument.Parse(parcel.PermissionsJson); return p.RootElement.TryGetProperty("build", out var v) && v.ValueKind == JsonValueKind.True; } catch (JsonException) { return false; } }
            return new ParcelFacts(parcel.Id, parcel.Name, parcel.OwnerUserId, Build(), parcel.MaxObjects, b.GetProperty("minX").GetDouble(), b.GetProperty("minZ").GetDouble(), b.GetProperty("maxX").GetDouble(), b.GetProperty("maxZ").GetDouble());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

/// <summary>
/// Who may build and edit where. The island server calls the API for every operation, so these rules are the last word:
/// whatever buttons the client shows or hides, a request that breaks them is refused here.
/// <list type="bullet">
/// <item>The island owner and anyone with the island's "admin" permission may do everything on the island (audited).</item>
/// <item>A parcel's owner may build in it, and edit or return any object standing in it.</item>
/// <item>Anyone else may build in a parcel only when the parcel's "build" permission is on; with no parcel, island "build"/"edit" permission is needed.</item>
/// <item>Editing or deleting an object needs being its owner, an island admin, or the owner of the parcel it stands in.</item>
/// </list>
/// </summary>
public static class BuildRules
{
    public static ParcelFacts? ParcelAt(IReadOnlyList<ParcelFacts> parcels, double x, double z) => parcels.FirstOrDefault(p => p.Contains(x, z));

    /// <summary>Null when the actor may create (or move something to) this spot, otherwise the refusal code.</summary>
    public static string? CanBuildAt(ParcelFacts? parcel, Guid actor, bool isIslandAdmin, bool hasIslandBuild)
    {
        if (isIslandAdmin) return null;
        if (parcel is null) return hasIslandBuild ? null : "forbidden";
        if (parcel.OwnerUserId == actor || parcel.PublicBuild) return null;
        return "parcel_forbidden";
    }

    /// <summary>Null when the actor may change or delete an object that belongs to <paramref name="objectOwner"/> and stands in <paramref name="parcel"/>.</summary>
    public static string? CanEditObject(Guid actor, Guid objectOwner, bool isIslandAdmin, ParcelFacts? parcel) =>
        actor == objectOwner || isIslandAdmin || parcel?.OwnerUserId == actor ? null : "not_owner";

    /// <summary>Null when the actor may send this object back to its owner's inventory (an owner may always take their own).</summary>
    public static string? CanReturn(Guid actor, Guid objectOwner, bool isIslandAdmin, ParcelFacts? parcel) =>
        actor == objectOwner || isIslandAdmin || parcel?.OwnerUserId == actor ? null : "not_allowed_to_return";
}
