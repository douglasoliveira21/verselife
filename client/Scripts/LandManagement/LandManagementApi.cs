using System.Text.Json;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>The calls of the land management window; they go through the same transport as the island window, so a test answers for the server.</summary>
public sealed class LandManagementApi(IslandManagementApi inner)
{
    private static string Base(Guid island) => $"/api/v1/worlds/islands/{island}/land";

    /// <summary>Turns a refusal into words: the server answers with a code, the person reads Portuguese.</summary>
    public static string Words(IslandReply reply)
    {
        if (reply.Ok) return "";
        var code = reply.Body.ValueKind == JsonValueKind.Object && reply.Body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        return code switch
        {
            "overlap" => "Essa área já pertence a outro terreno.",
            "outside_island" => "A área passa dos limites da ilha.",
            "too_big" => "A área é grande demais para um terreno.",
            "empty" => "Marque algum quadrado no mapa.",
            "has_objects" => "Há objetos nessa área. Retire-os antes de devolver o terreno.",
            "budget_exceeded" => "A ilha não tem tantos objetos livres. Use um limite menor.",
            "bad_category" => "Finalidade inválida.",
            "forbidden" => "Você não tem permissão para isso nesta ilha.",
            "owner_only" => "Só o dono da ilha pode fazer isso.",
            "own_island" => "Esta ilha já é sua.",
            "welcome_island" => "A ilha de boas-vindas do jogo não pode ser vendida, alugada nem transferida.",
            "person_not_found" => "Não encontrei ninguém com esse nome.",
            "for_sale" => "Retire a ilha da venda antes.",
            "bad_buyer" => "Escolha outra pessoa que não seja você.",
            "" => reply.Message,
            _ => LandManager.AdminMessage((int)reply.Status, code),
        };
    }

    public Task<IslandReply> Overview(Guid island) => inner.Send("GET", $"{Base(island)}/overview");
    public Task<IslandReply> SetCategory(Guid island, Guid parcel, string category) => inner.Send("PUT", $"{Base(island)}/parcels/{parcel}/category", new { category });
    public Task<IslandReply> Rename(Guid island, Guid parcel, string name, string description, int expectedRevision) => inner.Send("PATCH", $"/api/v1/worlds/islands/{island}/parcels/{parcel}", new { name, description, expectedRevision });
    public Task<IslandReply> Claim(Guid island, string name, string description, IEnumerable<long> cells, int? maxObjects = null) =>
        inner.Send("POST", $"{Base(island)}/claim", new { name, description, cells = cells.Select(k => { var (cx, cz) = LandMath.Unkey(k); return new[] { cx, cz }; }), maxObjects });
    public Task<IslandReply> Split(Guid island, Guid parcel, IEnumerable<long> cells, string name) =>
        inner.Send("POST", $"{Base(island)}/parcels/{parcel}/split", new { cells = cells.Select(k => { var (cx, cz) = LandMath.Unkey(k); return new[] { cx, cz }; }), name });
    public Task<IslandReply> Version(Guid island) => inner.Send("GET", $"{Base(island)}/version");
    public Task<IslandReply> Expand(Guid island, Guid parcel, IEnumerable<long> cells, int extraObjects) =>
        inner.Send("POST", $"{Base(island)}/parcels/{parcel}/expand", new { cells = cells.Select(k => { var (cx, cz) = LandMath.Unkey(k); return new[] { cx, cz }; }), extraObjects });
    public Task<IslandReply> Shrink(Guid island, Guid parcel, IEnumerable<long> cells) =>
        inner.Send("POST", $"{Base(island)}/parcels/{parcel}/shrink", new { cells = cells.Select(k => { var (cx, cz) = LandMath.Unkey(k); return new[] { cx, cz }; }) });
    public Task<IslandReply> Merge(Guid island, IEnumerable<Guid> parcels, Guid primary) => inner.Send("POST", $"{Base(island)}/merge", new { parcelIds = parcels, primaryId = primary });
    public Task<IslandReply> OfferSale(Guid island, Guid parcel, long price, bool includeObjects, string description) => inner.Send("POST", $"{Base(island)}/parcels/{parcel}/sale", new { price, includeObjects, description });
    public Task<IslandReply> CancelSale(Guid island, Guid parcel) => inner.Send("DELETE", $"{Base(island)}/parcels/{parcel}/sale");
    public Task<IslandReply> OfferRent(Guid island, Guid parcel, long pricePerPeriod, int periodDays, int minPeriods, int maxPeriods, long depositCoins, int graceDays) =>
        inner.Send("POST", $"{Base(island)}/parcels/{parcel}/rental-offer", new { pricePerPeriod, periodDays, minPeriods, maxPeriods, depositCoins, graceDays, autoRenewAllowed = true });
    public Task<IslandReply> CancelRentOffer(Guid island, Guid parcel) => inner.Send("DELETE", $"{Base(island)}/parcels/{parcel}/rental-offer");
    public Task<IslandReply> EndRental(Guid island, Guid rental) => inner.Send("POST", $"{Base(island)}/rentals/{rental}/end", new { });
}
