using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Sales of objects as the scripts hear them: when a purchase the API accepted comes back through this island, the object that was bought tells its scripts (on_purchase).</summary>
public sealed partial class IslandScripting
{
    /// <summary>The script sets the sale price of its own object. The island sends the same "properties" operation a person's build tools send, as the owner of the script; the API checks that the object is for sale and is theirs.</summary>
    public bool SetObjectPrice(ScriptDefinition script, int price)
    {
        if (!_objects.ContainsKey(script.ObjectId)) return false;
        _ = SendPriceAsync(script, price);
        return true;
    }

    private async Task SendPriceAsync(ScriptDefinition script, int price)
    {
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/build-operations", new { userId = script.OwnerUserId, operation = "properties", objectIds = new[] { script.ObjectId }, expectedRevision = 0, properties = new { salePrice = price } }, cancel.Token);
            var body = await response.Content.ReadAsStringAsync(cancel.Token);
            if (!response.IsSuccessStatusCode) { _log("script_price_rejected", new { script = script.Name, status = (int)response.StatusCode, price }); return; }
            OnObjectApplied(body); _broadcastApplied(body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { _log("script_price_failed", new { script = script.Name, error = exception.Message }); }
    }

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
