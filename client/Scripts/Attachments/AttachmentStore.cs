using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>One object worn on the avatar, as the server describes it. Rotation is in degrees (X, Y, Z); position and scale are relative to the attachment point.</summary>
public sealed record AttachmentData(Guid Id, Guid InventoryItemId, Guid AssetId, string Name, string Point, string PointName, float[] Position, float[] Rotation, float[] Scale, bool Equipped, int Order, long Revision, bool CanModify, string? OriginalPointName = null, float Sway = 0)
{
    /// <summary>One line telling name, point, placement and rights.</summary>
    public string Describe() => $"{(string.IsNullOrWhiteSpace(Name) ? "Objeto" : Name)} · {PointName}{(OriginalPointName is { } o ? $" (o corpo atual não tem {o})" : string.Empty)} · posição {Position[0]:0.###}, {Position[1]:0.###}, {Position[2]:0.###} m · rotação {Rotation[0]:0.##}°, {Rotation[1]:0.##}°, {Rotation[2]:0.##}° · escala {Scale[0]:0.###}, {Scale[1]:0.###}, {Scale[2]:0.###}{(CanModify ? string.Empty : " · sem direito de modificar")}";

    public static AttachmentData? Parse(JsonElement e)
    {
        try
        {
            float[] Three(string key, float fallback) => e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 3 ? [.. a.EnumerateArray().Select(v => v.GetSingle())] : [fallback, fallback, fallback];
            return new AttachmentData(e.GetProperty("id").GetGuid(), e.GetProperty("inventoryItemId").GetGuid(), e.GetProperty("assetId").GetGuid(), e.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                e.GetProperty("point").GetString() ?? string.Empty, e.TryGetProperty("pointName", out var pn) ? pn.GetString() ?? string.Empty : string.Empty, Three("position", 0), Three("rotation", 0), Three("scale", 1),
                e.TryGetProperty("equipped", out var q) && q.ValueKind == JsonValueKind.True, e.TryGetProperty("order", out var o) && o.TryGetInt32(out var order) ? order : 0,
                e.TryGetProperty("revision", out var r) && r.TryGetInt64(out var rev) ? rev : 0, !e.TryGetProperty("canModify", out var m) || m.ValueKind != JsonValueKind.False,
                e.TryGetProperty("originalPointName", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null, e.TryGetProperty("sway", out var sw) && sw.TryGetSingle(out var swayValue) ? swayValue : 0);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>A bare list of attachments (what the island sends inside an appearance).</summary>
    public static List<AttachmentData> ParseArray(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Array ? [.. d.RootElement.EnumerateArray().Select(Parse).OfType<AttachmentData>()] : []; }
        catch (JsonException) { return []; }
    }

    public static List<AttachmentData> ParseList(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Object || !d.RootElement.TryGetProperty("attachments", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            return [.. list.EnumerateArray().Select(Parse).OfType<AttachmentData>()];
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// What this person wears, kept in step with the server (which decides everything). Every change goes to the server first and the list is read back after it, so what the screen shows is what the server holds.
/// </summary>
public sealed class AttachmentStore(AvatarManager owner)
{
    private const string Path = "/attachments";
    private readonly AvatarManager _owner = owner;
    private List<AttachmentData> _items = [];
    public IReadOnlyList<AttachmentData> Items => _items;
    public IEnumerable<AttachmentData> Worn => _items.Where(a => a.Equipped);
    public bool Loaded { get; private set; }
    /// <summary>The skeleton profiles of the body worn now (a tail, wings or fingers unlock more points).</summary>
    public IReadOnlyList<string> Profiles { get; private set; } = [];
    public event Action? Changed;

    public AttachmentData? ForItem(Guid inventoryItemId) => _items.FirstOrDefault(a => a.InventoryItemId == inventoryItemId);
    public bool IsWorn(Guid inventoryItemId) => ForItem(inventoryItemId)?.Equipped == true;
    public int WornAt(string point) => Worn.Count(a => a.Point == point);

    public void Refresh(Action<bool, string>? done = null) => _owner.Call(Godot.HttpClient.Method.Get, Path, null, (status, body) =>
    {
        if (status is < 200 or >= 300) { done?.Invoke(false, Explain(status, body)); return; }
        _items = AttachmentData.ParseList(body); Profiles = ParseProfiles(body); Loaded = true; Changed?.Invoke(); done?.Invoke(true, string.Empty);
    });

    /// <summary>Wears an item at a point. <paramref name="replace"/> takes off what is at that point first.</summary>
    public void Attach(Guid inventoryItemId, string point, bool replace, Action<bool, string>? done = null) =>
        Send(Godot.HttpClient.Method.Post, Path, JsonSerializer.Serialize(new { inventoryItemId, point, mode = replace ? "replace" : "add" }), done);

    public void Move(Guid id, string point, bool replace, Action<bool, string>? done = null) =>
        Send(Godot.HttpClient.Method.Post, $"{Path}/{id}/move", JsonSerializer.Serialize(new { point, mode = replace ? "replace" : "add" }), done);

    public void Detach(Guid id, Action<bool, string>? done = null) => Send(Godot.HttpClient.Method.Delete, $"{Path}/{id}", null, done);

    /// <summary>Saves where the object sits (position in metres, rotation in degrees, scale per axis, all relative to the point).</summary>
    public void Transform(Guid id, Placement placement, Action<bool, string>? done = null, float? sway = null) => Send(Godot.HttpClient.Method.Patch, $"{Path}/{id}", JsonSerializer.Serialize(new
    {
        position = new[] { placement.Position.X, placement.Position.Y, placement.Position.Z }, rotation = new[] { placement.Rotation.X, placement.Rotation.Y, placement.Rotation.Z }, scale = new[] { placement.Scale.X, placement.Scale.Y, placement.Scale.Z }, sway
    }), done);

    private void Send(Godot.HttpClient.Method method, string path, string? body, Action<bool, string>? done) => _owner.Call(method, path, body, (status, text) =>
    {
        if (status is < 200 or >= 300) { done?.Invoke(false, Explain(status, text)); return; }
        Refresh((ok, error) => done?.Invoke(ok, error));
    });

    private static List<string> ParseProfiles(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("profiles", out var p) && p.ValueKind == JsonValueKind.Array ? [.. p.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : []; }
        catch (JsonException) { return []; }
    }

    /// <summary>What a refusal of the server means for a person.</summary>
    public static string Explain(int status, string body)
    {
        string? code = null;
        try { using var d = JsonDocument.Parse(body); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e)) code = e.GetString(); } catch (JsonException) { }
        return code switch
        {
            "item_not_found" => "Este item não está mais no seu inventário.",
            "item_unavailable" => "Este item está em uma oferta e não pode ser usado agora.",
            "not_attachable" => "Este tipo de item não pode ser anexado ao corpo.",
            "asset_unavailable" => "O arquivo deste item ainda não está pronto.",
            "unknown_point" or "point_unavailable" => "Este ponto do corpo não existe no seu avatar.",
            "already_attached" => "Este objeto já está no corpo. Use “Alterar ponto” para movê-lo.",
            "too_many_attachments" => "Você já está usando o máximo de objetos no corpo. Remova algum antes.",
            "too_many_at_point" => "Este ponto já está cheio. Use “Substituir” ou escolha outro ponto.",
            "too_many_huds" => "Você já está usando o máximo de HUDs.",
            "no_modify_right" => "Você não tem o direito de modificar este item.",
            "bad_position" => "A posição está longe demais do ponto do corpo.",
            "bad_rotation" => "A rotação não é válida.",
            "bad_scale" => "O tamanho está fora dos limites permitidos.",
            "bad_sway" => "O balanço precisa estar entre 0 e 1.",
            "nothing_to_change" => "Não há nada para mudar.",
            "not_attached" => "Este objeto não está no corpo.",
            "not_found" => "Este anexo não existe mais.",
            "avatar_not_found" => "Seu avatar não foi encontrado.",
            _ => status == 0 ? "Sem conexão com o servidor." : status == 401 ? "Sua sessão expirou. Entre de novo." : $"O servidor recusou a operação (HTTP {status})."
        };
    }
}
