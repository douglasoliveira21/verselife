using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Sales of objects as the scripts hear them: when a purchase the API accepted comes back through this island, the object that was bought tells its scripts (on_purchase).</summary>
public sealed partial class IslandScripting
{
    /// <summary>The API answered a purchase ("bought"): who paid how much for which kind of sale. A replay of the same purchase is not a new sale.</summary>
    private void NotePurchase(JsonElement root)
    {
        if (root.TryGetProperty("replay", out var replay) && replay.ValueKind == JsonValueKind.True) return;
        if (!root.TryGetProperty("objectId", out var o) || !Guid.TryParse(o.GetString(), out var objectId)) return;
        var price = root.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : 0;
        var saleType = root.TryGetProperty("saleType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
        var buyerUser = root.TryGetProperty("buyerUserId", out var b) && Guid.TryParse(b.GetString(), out var parsed) ? parsed : Guid.Empty;
        var buyer = _avatars.FirstOrDefault(a => a.UserId == buyerUser);
        var data = new Dictionary<string, object?> { ["price"] = price, ["sale_type"] = saleType, ["user_id"] = buyerUser, ["avatar_id"] = buyer?.AvatarId, ["avatar_name"] = buyer?.Name ?? "", ["avatar"] = new Dictionary<string, object?> { ["id"] = buyer?.AvatarId, ["name"] = buyer?.Name ?? "" } };
        _host.Enqueue(new ScriptEvent(objectId, "purchase", data));
    }
}
