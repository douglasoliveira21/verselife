using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>A parcel (terreno) as the build rules see it.</summary>
public sealed record ParcelFacts(Guid Id, string Name, Guid OwnerUserId, bool PublicBuild, int MaxObjects, double MinX, double MinZ, double MaxX, double MaxZ)
{
    /// <summary>Per-parcel cost budget: every object slot of the parcel is worth this many cost points.</summary>
    public const int CostPerObjectSlot = 20;

    /// <summary>The 4 x 4 m cells of the parcel (see LandGrid); without them the parcel is the plain rectangle.</summary>
    public IReadOnlySet<long>? Cells { get; init; }

    /// <summary>The person renting the parcel right now (contract active), who may build in it.</summary>
    public Guid? TenantUserId { get; init; }

    public bool Contains(double x, double z) => x >= MinX && x < MaxX && z >= MinZ && z < MaxZ && (Cells is null || Cells.Contains(LandGrid.Key(LandGrid.CellOf(x), LandGrid.CellOf(z))));
    /// <summary>The cost budget the island's rules give this parcel (by its area); without such a rule every object slot is worth <see cref="CostPerObjectSlot"/> points.</summary>
    public long? CostBudget { get; init; }
    public int MaxCost => CostBudget is { } budget ? (int)Math.Min(int.MaxValue, budget) : (int)Math.Min(int.MaxValue, (long)MaxObjects * CostPerObjectSlot);

    /// <summary>owner, list, everyone or nobody (see <see cref="IslandParcel.BuildMode"/>).</summary>
    public string BuildMode { get; init; } = "owner";
    /// <summary>People on the builders list, who may build when the mode is "list".</summary>
    public IReadOnlySet<Guid>? Builders { get; init; }
    /// <summary>People currently banned from the parcel: they may not build in it either.</summary>
    public IReadOnlySet<Guid>? Banned { get; init; }
    /// <summary>Whether people who build here (and are not the owner or tenant) may put scripts in their objects.</summary>
    public bool AllowScripts { get; init; }
    /// <summary>Whether people who build here (and are not the owner or tenant) may put media (a picture or video on a surface) in their objects.</summary>
    public bool AllowMedia { get; init; } = true;
    /// <summary>For a parcel of a group: the members whose cargo lets them run it, who count as its owners.</summary>
    public IReadOnlySet<Guid>? Managers { get; init; }
    /// <summary>For a parcel of a group (or lent to one): the members who may build in it.</summary>
    public IReadOnlySet<Guid>? GroupBuilders { get; init; }
    /// <summary>The owner, or for a parcel of a group the members who manage its land.</summary>
    public bool IsOwner(Guid actor) => OwnerUserId == actor || Managers?.Contains(actor) == true;

    /// <summary>Minutes after which objects of people who may not build here are sent back; null: never.</summary>
    public int? AutoReturnMinutes { get; init; }

    public static ParcelFacts? From(IslandParcel parcel)
    {
        try
        {
            using var bounds = JsonDocument.Parse(parcel.BoundsJson);
            var b = bounds.RootElement;
            bool Build() { try { using var p = JsonDocument.Parse(parcel.PermissionsJson); return p.RootElement.TryGetProperty("build", out var v) && v.ValueKind == JsonValueKind.True; } catch (JsonException) { return false; } }
            bool Scripts() { try { using var p = JsonDocument.Parse(parcel.PermissionsJson); return p.RootElement.TryGetProperty("run_scripts", out var v) && v.ValueKind == JsonValueKind.True; } catch (JsonException) { return false; } }
            return new ParcelFacts(parcel.Id, parcel.Name, parcel.OwnerUserId, Build(), parcel.MaxObjects, b.GetProperty("minX").GetDouble(), b.GetProperty("minZ").GetDouble(), b.GetProperty("maxX").GetDouble(), b.GetProperty("maxZ").GetDouble()) { AllowMedia = parcel.AllowMedia, BuildMode = parcel.BuildMode, AllowScripts = Scripts(), AutoReturnMinutes = parcel.AutoReturnMinutes };
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
        if (parcel.Banned?.Contains(actor) == true) return "parcel_banned";
        if (parcel.BuildMode == "nobody") return "parcel_forbidden"; // closed to everyone but island administrators
        if (parcel.IsOwner(actor) || (parcel.TenantUserId is { } tenant && tenant == actor)) return null;
        if (parcel.GroupBuilders?.Contains(actor) == true) return null;
        if (parcel.PublicBuild || parcel.BuildMode == "everyone") return null;
        if (parcel.BuildMode == "list" && parcel.Builders?.Contains(actor) == true) return null;
        return "parcel_forbidden";
    }

    /// <summary>Null when this person may put scripts into an object on this spot: the owner, the tenant and administrators always may; others only where the parcel allows scripts.</summary>
    public static string? CanPutScripts(ParcelFacts? parcel, Guid actor, bool isIslandAdmin) =>
        isIslandAdmin || parcel is null || parcel.IsOwner(actor) || parcel.TenantUserId == actor || parcel.AllowScripts ? null : "scripts_not_allowed";


    /// <summary>Null when this person may put media (a picture or video on a surface) into an object on this spot: the owner, the tenant and administrators always may; others only where the parcel allows media.</summary>
    public static string? CanPutMedia(ParcelFacts? parcel, Guid actor, bool isIslandAdmin) =>
        isIslandAdmin || parcel is null || parcel.IsOwner(actor) || parcel.TenantUserId == actor || parcel.AllowMedia ? null : "media_not_allowed";

    /// <summary>Null when the actor may change or delete an object that belongs to <paramref name="objectOwner"/> and stands in <paramref name="parcel"/>.</summary>
    public static string? CanEditObject(Guid actor, Guid objectOwner, bool isIslandAdmin, ParcelFacts? parcel) =>
        actor == objectOwner || isIslandAdmin || parcel?.IsOwner(actor) == true ? null : "not_owner";

    /// <summary>Null when the actor may send this object back to its owner's inventory (an owner may always take their own).</summary>
    public static string? CanReturn(Guid actor, Guid objectOwner, bool isIslandAdmin, ParcelFacts? parcel) =>
        actor == objectOwner || isIslandAdmin || parcel?.IsOwner(actor) == true ? null : "not_allowed_to_return";
}
