using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record ScriptGiveItemCall(Guid ScriptId, Guid RecipientUserId, Guid RecipientAvatarId, Guid AssetId);
public sealed record ScriptGiveItemResult(string Status, string? Error = null, Guid? ItemId = null, string? Name = null);

/// <summary>
/// A script gives an item of its object to an avatar who allowed it (the island checked that). What is given is always a COPY of an item the OWNER of the object holds and carries in the object's contents,
/// so it needs the right to copy and to transfer; the avatar gets it with the rights the owner set for the next owner. The script never names whose item it is: the owner is read here from the database.
/// </summary>
public static partial class ScriptingEndpoints
{
    public const int MaxGivenPerHour = 40;

    internal static async Task<IResult> ScriptGiveItemAsync(Guid instanceId, ScriptGiveItemCall call, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Platform.PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        var target = await (from script in db.ObjectScripts.AsNoTracking() join obj in db.BuildObjects.AsNoTracking() on script.ObjectId equals obj.Id
                            where script.Id == call.ScriptId && obj.IslandId == server.IslandId && obj.DeletedAt == null select new { obj.Id, obj.OwnerUserId, obj.ContentJson }).SingleOrDefaultAsync(ct);
        if (target is null) return Results.NotFound();
        if (!await db.Avatars.AsNoTracking().AnyAsync(a => a.Id == call.RecipientAvatarId && a.UserId == call.RecipientUserId, ct) || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == call.RecipientUserId && u.DeletedAt == null, ct)) return Results.Json(new ScriptGiveItemResult("rejected", "unknown_recipient"), statusCode: StatusCodes.Status404NotFound);
        if (!ContentAssets.AssetIds(target.ContentJson).Contains(call.AssetId)) return Results.Json(new ScriptGiveItemResult("rejected", "item_unavailable"), statusCode: StatusCodes.Status409Conflict);   // the object no longer carries it
        var now = clock.GetUtcNow();
        var source = await db.InventoryItems.Where(i => i.OwnerUserId == target.OwnerUserId && i.AssetId == call.AssetId && i.DeletedAt == null && i.Status == ItemRightsService.Active).OrderBy(i => i.CreatedAt).FirstOrDefaultAsync(ct);
        if (source is null) return Results.Json(new ScriptGiveItemResult("rejected", "item_unavailable"), statusCode: StatusCodes.Status409Conflict);
        if (!ItemRights.Has(source.OwnerRights, Right.Copy) || !ItemRights.Has(source.OwnerRights, Right.Transfer)) return Results.Json(new ScriptGiveItemResult("rejected", "not_copyable"), statusCode: StatusCodes.Status409Conflict);
        var since = now.AddHours(-1);
        if (await db.InventoryItems.CountAsync(i => i.CopiedFromItemId == source.Id && i.CreatedAt > since, ct) >= MaxGivenPerHour) return Results.Json(new ScriptGiveItemResult("rejected", "too_many"), statusCode: StatusCodes.Status429TooManyRequests);
        var arrives = new ItemPermissions(source.OwnerRights, ItemRights.None, source.NextOwnerRights).ForNextOwner();
        var copy = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = call.RecipientUserId, AssetId = source.AssetId, DisplayName = source.DisplayName, OwnerRights = arrives.Owner, GroupRights = ItemRights.None, NextOwnerRights = arrives.NextOwner, CopiedFromItemId = source.Id, CreatedAt = now, UpdatedAt = now };
        db.InventoryItems.Add(copy); ItemRightsService.RecordOwner(db, copy, call.RecipientUserId, target.OwnerUserId, "gift", now);
        db.ScriptLogs.Add(new ScriptLogLine { Id = Guid.NewGuid(), ScriptId = call.ScriptId, Level = "info", EventName = "item_given", Text = $"gave a copy of \"{source.DisplayName}\" to avatar {call.RecipientAvatarId}", CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ScriptGiveItemResult("completed", null, copy.Id, source.DisplayName));
    }
}
