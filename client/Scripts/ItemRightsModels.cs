using System.Text.Json;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Scripts;

/// <summary>An item's rights as the server describes them to its holder.</summary>
public sealed record RightsInfo(Guid ItemId, Guid AssetId, string Name, bool IsOwner, int Owner, int Group, int NextOwner, int YourRights, bool Copyable, Guid? GroupId, bool ShowHistory, string Status, long Revision, string Content)
{
    public bool CanResell => ItemRights.Has(YourRights, Right.Resell);
    public bool CanTransfer => ItemRights.Has(YourRights, Right.Transfer);
}

public sealed record HistoryLine(DateTimeOffset At, string Reason, string? Holder, string? From)
{
    public string Text => $"{At.LocalDateTime:dd/MM/yyyy HH:mm} · {ItemRightsApi.ReasonText(Reason)}{(Holder is null ? string.Empty : " · " + Holder)}{(From is null ? string.Empty : " (de " + From + ")")}";
}

public sealed record OfferInfo(Guid Id, Guid ItemId, string ItemName, string FromName, string ToName, string Kind, string Message, bool KeepCopy, string Status, DateTimeOffset ExpiresAt)
{
    public string Text => $"{ItemName} · {(Kind == "gift" ? "presente" : "transferência")}{(KeepCopy ? " (uma cópia)" : string.Empty)}";
}

/// <summary>What the rights windows send to the API and how they read it back. No nodes in here, so it can be tested alone.</summary>
public static class ItemRightsApi
{
    public static string RightsPath(Guid item) => $"/api/v1/inventory/items/{item}/rights";
    public static string HistoryPath(Guid item) => $"/api/v1/inventory/items/{item}/history";
    public static string HistoryVisibilityPath(Guid item) => $"/api/v1/inventory/items/{item}/history-visibility";
    public static string OfferPath(Guid item) => $"/api/v1/inventory/items/{item}/offers";
    public static string CopyPath(Guid item) => $"/api/v1/inventory/items/{item}/copy";
    public static string OffersPath(bool incoming) => $"/api/v1/inventory/offers?box={(incoming ? "incoming" : "outgoing")}&pending=true";
    public static string AnswerPath(Guid offer, string answer) => $"/api/v1/inventory/offers/{offer}/{answer}";
    public const string ProductsPath = "/api/v1/economy/products";

    public static string SetRightsBody(int owner, int group, int next, long revision) => JsonSerializer.Serialize(new { owner = ItemRights.Normalize(owner), group = ItemRights.Normalize(group), nextOwner = ItemRights.Normalize(next), expectedRevision = revision });
    public static string HistoryVisibilityBody(bool visible) => JsonSerializer.Serialize(new { visible });
    public static string OfferBody(Guid to, string kind, string? message, bool keepCopy, string key) => JsonSerializer.Serialize(new { toUserId = to, kind, message, keepCopy, idempotencyKey = key });
    public static string ResaleBody(Guid assetId, Guid itemId, string name, long price) => JsonSerializer.Serialize(new { assetId, name, description = "Revenda", category = "revenda", priceCoins = price, itemId });

    /// <summary>The permissions grid has one box per right and per set; the number is what the server stores.</summary>
    public static int MaskOf(bool copy, bool modify, bool transfer, bool resell) => ItemRights.Normalize((copy ? ItemRights.Copy : 0) | (modify ? ItemRights.Modify : 0) | (transfer ? ItemRights.Transfer : 0) | (resell ? ItemRights.Resell : 0));

    public static RightsInfo? ParseRights(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); var r = d.RootElement; if (r.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(r, "itemId"), out var id)) return null;
            var permissions = r.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            return new RightsInfo(id, Guid.TryParse(Str(r, "assetId"), out var asset) ? asset : Guid.Empty, Str(r, "name"), Bool(r, "isOwner"), Int(permissions, "owner"), Int(permissions, "group"), Int(permissions, "nextOwner"), Int(r, "yourRights"), Bool(r, "copyable"),
                Guid.TryParse(Str(r, "groupId"), out var g) ? g : null, Bool(r, "showOwnerHistory"), Str(r, "status") is { Length: > 0 } s ? s : "active", r.TryGetProperty("revision", out var rev) && rev.TryGetInt64(out var n) ? n : 0, Str(r, "content"));
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<HistoryLine> ParseHistory(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); if (d.RootElement.ValueKind != JsonValueKind.Array) return [];
            return [.. d.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && DateTimeOffset.TryParse(Str(e, "at"), out _)).Select(e => new HistoryLine(DateTimeOffset.Parse(Str(e, "at")), Str(e, "reason"), NullIfEmpty(Str(e, "holder")), NullIfEmpty(Str(e, "from"))))];
        }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<OfferInfo> ParseOffers(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); if (d.RootElement.ValueKind != JsonValueKind.Array) return [];
            return [.. d.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && Guid.TryParse(Str(e, "id"), out _) && Guid.TryParse(Str(e, "itemId"), out _))
                .Select(e => new OfferInfo(Guid.Parse(Str(e, "id")), Guid.Parse(Str(e, "itemId")), Str(e, "itemName"), Str(e, "fromName"), Str(e, "toName"), Str(e, "kind"), Str(e, "message"), Bool(e, "keepCopy"), Str(e, "status"), DateTimeOffset.TryParse(Str(e, "expiresAt"), out var at) ? at : DateTimeOffset.MinValue))];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>The code in an error body ({"error":"…"} or {"code":"…"}), or empty.</summary>
    public static string ErrorCode(string body)
    {
        try { using var d = JsonDocument.Parse(body); return d.RootElement.ValueKind == JsonValueKind.Object ? (Str(d.RootElement, "error") is { Length: > 0 } e ? e : Str(d.RootElement, "code")) : string.Empty; }
        catch (JsonException) { return string.Empty; }
    }

    /// <summary>What to tell the person when the server says no. Unknown codes are shown as they are.</summary>
    public static string Explain(string code, int status = 0) => code switch
    {
        "not_found" => "Esse item não existe mais ou não é seu.",
        "not_allowed" => "Você não tem esse direito sobre o item.",
        "item_busy" => "O item está esperando resposta de uma oferta, ou à venda.",
        "stale" => "Outra mudança aconteceu ao mesmo tempo; abra de novo e tente outra vez.",
        "cannot_raise_rights" => "Direitos que você não tem não podem ser dados; só dá para diminuir.",
        "exceeds_owner" => "O grupo e o próximo dono não podem ter mais direitos do que você.",
        "not_in_group" => "Você não faz parte desse grupo.",
        "keep_copy_needs_copy" => "Só dá para ficar com uma cópia de um item que pode ser copiado.",
        "item_in_use" => "O item faz parte de um objeto no mundo: devolva o objeto primeiro.",
        "recipient_not_found" => "Não encontramos essa pessoa (ou ela não pode receber de você).",
        "to_self" => "Escolha outra pessoa: o item já é seu.",
        "too_many_offers" => "Você já tem ofertas demais esperando resposta.",
        "recipient_full" => "Essa pessoa já tem ofertas demais esperando resposta.",
        "offer_closed" => "Essa oferta já foi respondida.",
        "offer_expired" => "Essa oferta expirou.",
        "item_unavailable" => "O item mudou e a oferta foi cancelada.",
        "inventory_full" => "O inventário está cheio.",
        "key_reused" => "Esse pedido já foi usado para outra coisa; tente de novo.",
        "bad_kind" or "bad_key" => "Pedido inválido.",
        "already_listed" => "Esse item já está à venda.",
        "bad_rights" or "next_exceeds_buyer" => "Direitos inválidos para o comprador.",
        "already_owned" => "Você já pegou este produto gratuito.",
        "gift_not_allowed" => "Este produto não pode ser comprado de presente.",
        "listing_unavailable" => "A venda não está mais disponível.",
        "insufficient_balance" => "Saldo insuficiente.",
        "use_rez" => "Esse item não pode ser copiado: ele é colocado pelo servidor.",
        "" => status switch { 0 => "Sem conexão com o servidor.", 401 => "Entre na sua conta.", 404 => "Não encontrado.", 429 => "Muitas tentativas; aguarde um pouco.", _ => $"Algo deu errado (HTTP {status})." },
        _ => $"O servidor recusou: {code}.",
    };

    public static string ReasonText(string reason) => reason switch { "created" => "criado", "purchase" => "comprado", "transfer" => "transferido", "gift" => "presente", "copy" => "cópia", "resale" => "revenda", "legacy" => "no inventário desde", _ => reason };

    /// <summary>"Copiar · Modificar" and so on, for labels.</summary>
    public static string Describe(int mask) => ItemRights.Describe(mask);

    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static int Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
