using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>The house rules of an island: how new land starts (who may enter, who may build, scripts, voice, media, how soon strangers' objects are returned).</summary>
public sealed record IslandRuleSet(string AccessMode, string BuildMode, bool AllowScripts, bool AllowVoice, bool AllowMedia, int? AutoReturnMinutes)
{
    /// <summary>What a parcel has when the island says nothing.</summary>
    public static IslandRuleSet Plain { get; } = new("public", "owner", false, true, true, null);
}

/// <summary>
/// Reading, checking and saving the island's rules. They are the starting point of new parcels (claimed, bought from the island is not touched) and, when the person says so,
/// are also pushed onto the parcels that are still the island owner's and not rented. Parcels that belong to somebody else keep their own rules.
/// </summary>
public static class IslandRules
{
    public static IslandRuleSet Read(Island island)
    {
        try
        {
            if (JsonNode.Parse(island.RulesJson) is not JsonObject o) return IslandRuleSet.Plain;
            var plain = IslandRuleSet.Plain;
            var access = o["accessMode"]?.GetValue<string>(); var build = o["buildMode"]?.GetValue<string>();
            return new IslandRuleSet(access is not null && LandAdmin.Modes.Contains(access) ? access : plain.AccessMode, build is not null && LandAdmin.BuildModes.Contains(build) ? build : plain.BuildMode,
                o["allowScripts"]?.GetValue<bool>() ?? plain.AllowScripts, o["allowVoice"]?.GetValue<bool>() ?? plain.AllowVoice, o["allowMedia"]?.GetValue<bool>() ?? plain.AllowMedia,
                o["autoReturnMinutes"] is JsonValue v && v.TryGetValue<int>(out var minutes) ? minutes : null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { return IslandRuleSet.Plain; }
    }

    public static object View(IslandRuleSet r) => new { accessMode = r.AccessMode, buildMode = r.BuildMode, allowScripts = r.AllowScripts, allowVoice = r.AllowVoice, allowMedia = r.AllowMedia, autoReturnMinutes = r.AutoReturnMinutes };

    /// <summary>Puts the rules on a parcel (what the owner would have chosen by hand in "Sobre este Terreno").</summary>
    public static void Apply(IslandParcel parcel, IslandRuleSet r)
    {
        parcel.AccessMode = r.AccessMode; parcel.BuildMode = r.BuildMode; parcel.AllowVoice = r.AllowVoice; parcel.AllowMedia = r.AllowMedia; parcel.AutoReturnMinutes = r.AutoReturnMinutes;
        var permissions = JsonNode.Parse(parcel.PermissionsJson) as JsonObject ?? [];
        permissions["build"] = r.BuildMode == "everyone"; permissions["run_scripts"] = r.AllowScripts; // the old keys keep meaning the same
        parcel.PermissionsJson = permissions.ToJsonString();
    }

    /// <summary>Saves the rules (the owner and the administrators of the island may). With <paramref name="applyToExisting"/> they are also put on every parcel that is the island owner's and not rented.</summary>
    public static async Task<CommerceResult> SetAsync(LifeVerseDbContext db, Guid islandId, Guid actor, IslandRuleSet rules, bool applyToExisting, TimeProvider clock, CancellationToken ct)
    {
        var island = await db.Islands.SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow();
        if (!await LandOverview.IsStaffAsync(db, island, actor, now, ct)) return CommerceResult.Fail("forbidden");
        if (!LandAdmin.Modes.Contains(rules.AccessMode) || !LandAdmin.BuildModes.Contains(rules.BuildMode)) return CommerceResult.Fail("bad_mode");
        if (rules.AutoReturnMinutes is { } m && (m < 1 || m > 10_080)) return CommerceResult.Fail("bad_duration");

        island.RulesJson = JsonSerializer.Serialize(View(rules)); island.UpdatedAt = now;
        var changed = 0;
        if (applyToExisting)
        {
            var parcels = await db.IslandParcels.Where(p => p.IslandId == islandId && p.DeletedAt == null && p.OwnerUserId == island.OwnerUserId && p.GroupId == null).ToListAsync(ct);
            var ids = parcels.Select(p => p.Id).ToList();
            var rented = await db.ParcelRentals.AsNoTracking().Where(r => ids.Contains(r.ParcelId) && (r.Status == "active" || r.Status == "grace")).Select(r => r.ParcelId).ToListAsync(ct);
            foreach (var parcel in parcels.Where(p => !rented.Contains(p.Id)))
            {
                Apply(parcel, rules); parcel.Revision++; parcel.UpdatedAt = now; changed++;
                db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = "island_rules", PayloadJson = JsonSerializer.Serialize(View(rules)), Revision = parcel.Revision, CreatedAt = now });
            }
        }
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
        return CommerceResult.Done(new { rules = View(rules), applied = changed });
    }
}
