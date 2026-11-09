using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// The "Geral" properties of an object that only its owner changes: what the owner and the next owner may do (Modify, Copy, Transfer), the group it is shared
/// with, what anyone may do with it, and whether it is for sale. Sharing and sale live inside the object's permissions JSON.
/// </summary>
public static class BuildSharing
{
    public static readonly string[] SaleTypes = ["none", "original", "copy", "contents"];
    public const int MaxPrice = 10_000_000;

    private static JsonObject Parse(string? json) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject(); } catch (JsonException) { return new JsonObject(); } }
    public static bool Flag(string? permissionsJson, string key) => Parse(permissionsJson)[key] is JsonValue v && v.TryGetValue<bool>(out var on) && on;
    public static bool AnyoneMove(string? permissionsJson) => Flag(permissionsJson, "anyoneMove");
    public static bool GroupShare(string? permissionsJson) => Flag(permissionsJson, "groupShare");
    public static (string Type, int Price) Sale(string? permissionsJson)
    {
        var root = Parse(permissionsJson);
        var type = root["saleType"] is JsonValue t && t.TryGetValue<string>(out var s) && SaleTypes.Contains(s) ? s : "none";
        var price = root["salePrice"] is JsonValue p && p.TryGetValue<int>(out var n) ? Math.Clamp(n, 0, MaxPrice) : 0;
        return (type, price);
    }

    public static string With(string? permissionsJson, bool? groupShare, bool? anyoneMove, bool? anyoneCopy, string? saleType, int? salePrice)
    {
        var root = Parse(permissionsJson);
        if (groupShare is { } g) root["groupShare"] = g;
        if (anyoneMove is { } m) root["anyoneMove"] = m;
        if (anyoneCopy is { } c) root["anyoneCopy"] = c;
        if (saleType is { } st) { root["saleType"] = st; if (st == "none") root.Remove("salePrice"); }
        if (salePrice is { } sp && (saleType ?? Sale(permissionsJson).Type) != "none") root["salePrice"] = sp;
        return root.ToJsonString();
    }
}

public static partial class BuildOperations
{
    /// <summary>Whether somebody who is not the owner may make this change: anyone may move/turn an object marked that way, and members of the group it is shared with may edit it.</summary>
    private static async Task<bool> SharedAccessAsync(LifeVerseDbContext db, BuildObject item, Guid actor, bool changesWhatItIs, CancellationToken ct)
    {
        if (BuildSharing.AnyoneMove(item.PermissionsJson) && !changesWhatItIs) return true;
        return BuildSharing.GroupShare(item.PermissionsJson) && item.GroupId is { } group && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == group && m.UserId == actor, ct);
    }

    private static async Task<IResult> PropertiesAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var ids = (request.ObjectIds ?? (request.ObjectId is { } one ? [one] : [])).Distinct().ToArray();
        if (ids.Length == 0 || ids.Length > 500 || request.Properties is not { ValueKind: JsonValueKind.Object } p) return Refuse(400, "invalid_operation");
        int? Int(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        bool? Flag(string key) => p.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        string? Text(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var ownerRights = Int("ownerRights"); var nextRights = Int("nextOwnerRights"); var saleType = Text("saleType"); var price = Int("salePrice");
        var setGroup = p.TryGetProperty("groupId", out var groupElement); Guid? group = setGroup && groupElement.ValueKind == JsonValueKind.String && Guid.TryParse(groupElement.GetString(), out var parsedGroup) ? parsedGroup : null;
        if (saleType is not null && !BuildSharing.SaleTypes.Contains(saleType)) return Refuse(400, "invalid_sale_type");
        if (price is < 0 or > BuildSharing.MaxPrice) return Refuse(400, "invalid_price");
        if (setGroup && groupElement.ValueKind != JsonValueKind.Null && group is null) return Refuse(400, "invalid_group");
        if (group is { } wanted && !await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == wanted && m.UserId == actor, ct)) return Refuse(403, "not_group_member");

        var objects = await db.BuildObjects.Where(x => ids.Contains(x.Id) && x.IslandId == island.Id && x.DeletedAt == null).ToListAsync(ct);
        if (objects.Count != ids.Length) return Refuse(404, "not_found");
        foreach (var o in objects)
        {
            if (o.OwnerUserId != actor && !isAdmin) return Refuse(403, "not_owner", $"{o.Name}: not_owner");
            if (o.EditLocked) return Refuse(403, "object_protected");
            var newOwner = ownerRights is { } r ? ItemRights.Normalize(r) : o.OwnerRights;
            if ((newOwner & ~o.OwnerRights) != 0) return Refuse(403, "cannot_raise_rights");
            var newNext = ItemRights.Clamp(nextRights ?? o.NextOwnerRights, newOwner);
            var type = saleType ?? BuildSharing.Sale(o.PermissionsJson).Type;
            if (type == "copy" && !ItemRights.IsCopyable(newOwner)) return Refuse(403, "no_copy");
            if (type is "original" && !ItemRights.Has(newOwner, Right.Transfer)) return Refuse(403, "no_transfer");
        }
        var changed = new List<BuildObject>();
        foreach (var o in objects)
        {
            var before = (o.OwnerRights, o.NextOwnerRights, o.GroupId, o.PermissionsJson);
            if (ownerRights is { } r2) o.OwnerRights = ItemRights.Normalize(r2);
            o.NextOwnerRights = ItemRights.Clamp(nextRights ?? o.NextOwnerRights, o.OwnerRights);
            if (setGroup) o.GroupId = group;
            o.PermissionsJson = BuildSharing.With(o.PermissionsJson, Flag("groupShare"), Flag("anyoneMove"), Flag("anyoneCopy"), saleType, price);
            if (before == (o.OwnerRights, o.NextOwnerRights, o.GroupId, o.PermissionsJson)) continue;
            o.Revision++; o.UpdatedAt = now; changed.Add(o);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = o.Id, ActorUserId = actor, Operation = "properties", PayloadJson = "{}", Revision = o.Revision, CreatedAt = now });
        }
        if (changed.Count > 0 && await TrySaveAsync(db, changed[0].Id, ct) is { } conflict) return conflict;
        return Results.Ok(new { operation = "properties", objects = changed.Select(BuildEndpoints.ToResponse).ToArray() });
    }
}
