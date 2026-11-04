using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Tests;

/// <summary>The item rights windows against a pretend server: reading and changing rights, the history, offers (giving, answering), resale, copies, and the words used when the server says no.</summary>
public partial class ItemRightsSmoke : Node
{
    private readonly List<string> _calls = [];
    private readonly Guid _item = Guid.NewGuid(), _asset = Guid.NewGuid(), _friend = Guid.NewGuid(), _offer = Guid.NewGuid(), _incomingItem = Guid.NewGuid();
    private bool _owner = true, _copyable = true, _raiseRefused;
    private int _ownerMask = 15, _nextMask = 7;
    private long _revision = 3;

    private string RightsJson() => JsonSerializer.Serialize(new
    {
        itemId = _item, assetId = _asset, name = "Cadeira", isOwner = _owner, content = "model",
        permissions = new { owner = _owner ? _ownerMask : 0, group = 0, nextOwner = _owner ? _nextMask : 0 }, yourRights = _ownerMask, copyable = _copyable, groupId = (Guid?)null, showOwnerHistory = false, status = "active", revision = _revision,
    });

    private (int, string) Server(string method, string path, string? body)
    {
        _calls.Add(method + " " + path + (body is null ? string.Empty : " " + body));
        if (method == "GET" && path == $"/api/v1/inventory/items/{_item}/rights") return _owner || _ownerMask != 0 ? (200, RightsJson()) : (404, "{}");
        if (method == "PUT" && path == $"/api/v1/inventory/items/{_item}/rights")
        {
            if (_raiseRefused) return (400, "{\"error\":\"cannot_raise_rights\"}");
            using var d = JsonDocument.Parse(body!); _ownerMask = d.RootElement.GetProperty("owner").GetInt32(); _nextMask = d.RootElement.GetProperty("nextOwner").GetInt32(); _revision++; return (204, string.Empty);
        }
        if (method == "PUT" && path.EndsWith("/history-visibility")) return (204, string.Empty);
        if (method == "GET" && path.EndsWith("/history")) return (200, "[{\"at\":\"2026-11-01T10:00:00Z\",\"reason\":\"created\",\"holder\":\"criador\",\"from\":null},{\"at\":\"2026-11-02T10:00:00Z\",\"reason\":\"purchase\",\"holder\":\"proprietário anterior\",\"from\":\"criador\"},{\"at\":\"2026-11-03T10:00:00Z\",\"reason\":\"gift\",\"holder\":\"eu\",\"from\":\"proprietário anterior\"}]");
        if (method == "GET" && path.StartsWith("/api/v1/search?")) return (200, $"{{\"total\":2,\"page\":1,\"pageSize\":8,\"items\":[{{\"kind\":\"person\",\"id\":\"{_friend}\",\"title\":\"Ana Souza\",\"subtitle\":\"@ana\",\"description\":\"\",\"category\":\"\"}},{{\"kind\":\"person\",\"id\":\"{Guid.NewGuid()}\",\"title\":\"Ana Maria\",\"subtitle\":\"@anamaria\",\"description\":\"\",\"category\":\"\"}}],\"counts\":{{}},\"categories\":{{}}}}");
        if (method == "POST" && path == $"/api/v1/inventory/items/{_item}/offers") return (201, "{\"id\":\"" + _offer + "\",\"status\":\"pending\"}");
        if (method == "POST" && path == "/api/v1/economy/products") return (201, "{}");
        if (method == "POST" && path == $"/api/v1/inventory/items/{_item}/copy") return (_copyable ? 200 : 403, _copyable ? "{}" : "{\"error\":\"not_allowed\"}");
        if (method == "GET" && path.StartsWith("/api/v1/inventory/offers?box=incoming")) return (200, $"[{{\"id\":\"{_offer}\",\"itemId\":\"{_incomingItem}\",\"itemName\":\"Vaso\",\"fromUserId\":\"{_friend}\",\"fromName\":\"ana\",\"toUserId\":\"{Guid.NewGuid()}\",\"toName\":\"eu\",\"kind\":\"gift\",\"message\":\"Para você!\",\"keepCopy\":false,\"status\":\"pending\",\"createdAt\":\"2026-11-04T10:00:00Z\",\"expiresAt\":\"2026-11-11T10:00:00Z\"}}]");
        if (method == "GET" && path.StartsWith("/api/v1/inventory/offers?box=outgoing")) return (200, $"[{{\"id\":\"{_offer}\",\"itemId\":\"{_item}\",\"itemName\":\"Cadeira\",\"fromUserId\":\"{Guid.NewGuid()}\",\"fromName\":\"eu\",\"toUserId\":\"{_friend}\",\"toName\":\"ana\",\"kind\":\"transfer\",\"message\":\"\",\"keepCopy\":true,\"status\":\"pending\",\"createdAt\":\"2026-11-04T10:00:00Z\",\"expiresAt\":\"2026-11-11T10:00:00Z\"}}]");
        if (method == "POST" && path.StartsWith("/api/v1/inventory/offers/")) return path.EndsWith("/accept") && _offerClosed ? (409, "{\"error\":\"offer_expired\"}") : (200, "{}");
        return (404, "{}");
    }

    private bool _offerClosed;

    public override void _Ready()
    {
        try
        {
            // the pure side: masks, explanations, reading what the server says
            Require(ItemRightsApi.MaskOf(true, false, true, true) == 13 && ItemRightsApi.MaskOf(true, true, false, true) == 3, "the resell box means nothing without transfer");
            Require(ItemRightsApi.Explain("item_in_use").Contains("devolva o objeto") && ItemRightsApi.Explain("", 0) == "Sem conexão com o servidor." && ItemRightsApi.Explain("algo_novo").Contains("algo_novo"), "refusals are explained in words");
            Require(ItemRightsApi.ErrorCode("{\"error\":\"stale\"}") == "stale" && ItemRightsApi.ErrorCode("{\"code\":\"no_modify\"}") == "no_modify" && ItemRightsApi.ErrorCode("lixo") == string.Empty, "the error code is read from either shape");
            Require(ItemRightsApi.ParseRights("not json") is null && ItemRightsApi.ParseHistory("{}").Count == 0 && ItemRightsApi.ParseOffers("[1]").Count == 0, "bad answers are empty");
            Require(ItemRightsApi.OfferBody(_friend, "gift", "oi", true, "k").Contains("\"keepCopy\":true") && ItemRightsApi.SetRightsBody(255, 0, 8, 4).Contains("\"owner\":15") && ItemRightsApi.SetRightsBody(255, 0, 8, 4).Contains("\"nextOwner\":0"), "bodies carry clean masks");

            var rights = new ItemRightsWindow { Name = "R", Transport = Server }; AddChild(rights);
            var changed = 0; rights.Open(_item, "Cadeira", false, () => changed++);
            Require(rights.Visible && rights.Info is { IsOwner: true, Owner: 15, NextOwner: 7, Revision: 3 }, "the rights of an owned item are shown");
            Require(rights.YoursForTesting.Contains("Copiar · Modificar · Transferir · Revender") && rights.RowForTesting(0) == 15 && rights.RowForTesting(2) == 7, $"the boxes show the sets: {rights.YoursForTesting}");
            Require(rights.BoxEnabledForTesting(0, 0) && !rights.BoxEnabledForTesting(1, 0), "the holder changes their own and the next owner's boxes; the group's needs a group");
            Require(rights.OfferVisibleForTesting && rights.SellVisibleForTesting && rights.CopyVisibleForTesting && rights.KeepCopyEnabledForTesting, "every way to pass it on is offered when every right is held");

            rights.SetRowForTesting(0, ItemRights.Modify | ItemRights.Transfer); rights.SetRowForTesting(2, ItemRights.Transfer); rights.SaveForTesting();
            Require(_calls.Any(c => c.StartsWith("PUT ") && c.Contains("\"owner\":6") && c.Contains("\"nextOwner\":4") && c.Contains("\"expectedRevision\":3")), $"the change is sent with the version it was read at: {_calls.Last(c => c.StartsWith("PUT "))}");
            Require(rights.Info is { Owner: 6, Revision: 4 } && changed == 1 && rights.NoteForTesting == "Direitos salvos.", $"saved and read again: {rights.Info} / {rights.NoteForTesting}");
            _copyable = false; rights.Open(_item, "Cadeira", false, null);
            Require(!rights.CopyVisibleForTesting && !rights.KeepCopyEnabledForTesting && !rights.SellVisibleForTesting, "an item that cannot be copied or resold hides those buttons");
            _raiseRefused = true; rights.SetRowForTesting(0, 15); rights.SaveForTesting(); Require(rights.NoteForTesting.Contains("só dá para diminuir"), $"raising is explained: {rights.NoteForTesting}"); _raiseRefused = false;

            // history
            rights.HistoryForTesting(); Require(rights.HistoryCountForTesting == 3, "the history is listed"); rights.ToggleHistoryForTesting(true); Require(_calls.Any(c => c.StartsWith("PUT ") && c.Contains("history-visibility") && c.Contains("true")), "the history can be shown to others");

            // offers: the person is found by exact user name, then the offer is sent with a key of its own
            _ownerMask = 15; _copyable = true; rights.Open(_item, "Cadeira", true, null); _calls.Clear();
            rights.OfferForTesting("ana", transfer: true, keep: true);
            var offerCall = _calls.FirstOrDefault(c => c.StartsWith("POST ") && c.Contains("/offers")); Require(offerCall is not null && offerCall.Contains(_friend.ToString()) && offerCall.Contains("\"kind\":\"transfer\"") && offerCall.Contains("\"keepCopy\":true") && offerCall.Contains("oferta-"), $"the offer: {offerCall}");
            Require(rights.NoteForTesting.Contains("Oferta enviada para Ana Souza"), rights.NoteForTesting);
            _calls.Clear(); rights.OfferForTesting("an", false, false); Require(!_calls.Any(c => c.Contains("/offers")) && rights.NoteForTesting.Contains("Não encontrei"), "a partial name is not enough to give an item away");
            rights.OfferForTesting("x", false, false); Require(rights.NoteForTesting.Contains("nome de usuário"), "too short");

            // resale and copies
            _calls.Clear(); rights.SellForTesting("-5"); Require(_calls.Count == 0, "a price below zero is not sent"); rights.SellForTesting("250");
            Require(_calls.Any(c => c.StartsWith("POST /api/v1/economy/products") && c.Contains("\"priceCoins\":250") && c.Contains(_item.ToString()) && c.Contains(_asset.ToString())), "the resale names the item and its asset");
            rights.CopyForTesting(); Require(_calls.Any(c => c == $"POST /api/v1/inventory/items/{_item}/copy"), "copy asks the server");

            // not the holder: a group member or a stranger only sees their own set
            _owner = false; _ownerMask = 3; rights.Open(_item, "Cadeira", false, null);
            Require(rights.Info is { IsOwner: false } && !rights.BoxEnabledForTesting(0, 0) && !rights.OfferVisibleForTesting && !rights.SellVisibleForTesting, "someone who only shares the item cannot change or hand it over");

            // incoming and outgoing offers
            var offers = new ItemOffersWindow { Name = "O", Transport = Server }; AddChild(offers); var accepted = 0; offers.Open(() => accepted++);
            Require(offers.Offers.Count == 1 && offers.Offers[0].ItemName == "Vaso" && offers.Offers[0].FromName == "ana" && !offers.AcceptVisibleForTesting, "incoming offers are listed; nothing is chosen yet");
            offers.SelectForTesting(0); Require(offers.AcceptVisibleForTesting, "an offer can be accepted once chosen");
            offers.AnswerForTesting("accept"); Require(_calls.Any(c => c == $"POST /api/v1/inventory/offers/{_offer}/accept") && accepted == 1 && offers.NoteForTesting.Contains("“Vaso” agora está"), $"accepted: {offers.NoteForTesting}");
            _offerClosed = true; offers.SelectForTesting(0); offers.AnswerForTesting("accept"); Require(offers.NoteForTesting.Contains("expirou") && accepted == 1, $"an expired offer is explained and nothing changes: {offers.NoteForTesting}");
            offers.ShowTabForTesting(1); Require(offers.Offers.Count == 1 && offers.Offers[0].KeepCopy && !offers.AcceptVisibleForTesting, "outgoing offers"); offers.SelectForTesting(0); Require(offers.CancelVisibleForTesting, "an offer you sent can be taken back");
            offers.AnswerForTesting("cancel"); Require(_calls.Any(c => c == $"POST /api/v1/inventory/offers/{_offer}/cancel"), "cancel asks the server");

            GD.Print("event=item_rights_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=item_rights_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
