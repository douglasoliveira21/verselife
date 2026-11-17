using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The search window against a pretend server: the seven tabs, the start screen, grouped and per-tab results, filters, paging, hints and history, favorites, reports, interests, going to a result, and the loading, error, cancel and narrow states.</summary>
public partial class SearchSmoke : Node
{
    private readonly List<string> _calls = [];
    private string LastSearch => _calls.Last(c => c.StartsWith("GET /api/v1/search") && !c.Contains("/suggest") && !c.Contains("/popular") && !c.Contains("/recent") && !c.Contains("/recommended") && !c.Contains("/interests"));
    private readonly Guid _here = Guid.NewGuid(), _there = Guid.NewGuid(), _parcel = Guid.NewGuid(), _person = Guid.NewGuid(), _plot = Guid.NewGuid(), _group = Guid.NewGuid(), _studio = Guid.NewGuid();
    private readonly List<string> _teleports = [];
    private Guid _joined, _profileOpened, _groupOpened;
    private readonly List<(Guid, string)> _groupTabs = [];
    private bool _favoritesFull, _down, _hang;
    private List<string> _interests = ["musica"];

    private string Hit(string kind, Guid id, string title, Guid? island, float? x, float? z, bool favorite = false, string category = "social") =>
        JsonSerializer.Serialize(new { kind, kindLabel = kind, id, title, subtitle = "sub " + title, description = "desc " + title, category, thumbnailUrl = (string?)null, islandId = island, x, z, score = 1.0, popularity = 5.0, favorite });

    private (int, string) Server(string method, string path, string? body)
    {
        _calls.Add(method + " " + path);
        if (Extra(method, path, body) is { } special) return special;
        if (_hang) return (-1, string.Empty);
        if (_down) return (0, string.Empty);
        if (method == "GET" && path.StartsWith("/api/v1/search?") && path.Contains("types=group")) return (200, $"{{\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{Hit("group", _group, "Amigos da Praia", null, null, null)}],\"counts\":{{\"group\":1}},\"categories\":{{}},\"sort\":\"relevance\"}}");
        var items = new[] { Hit("island", _here, "Praia Grande", _here, 10, 20), Hit("island", _there, "Praia Distante", _there, 30, 40, category: "negocios"), Hit("person", _person, "Praia Pessoa", null, null, null), Hit("parcel", _parcel, "Lote da Praia", _here, 5, 6) };
        if (method == "GET" && path.StartsWith("/api/v1/search?")) return (200, $"{{\"total\":45,\"page\":{(path.Contains("page=2") ? 2 : 1)},\"pageSize\":20,\"items\":[{string.Join(",", items)}],\"counts\":{{\"island\":6,\"person\":1,\"parcel\":1}},\"categories\":{{\"social\":3,\"negocios\":1}},\"sort\":\"relevance\"}}");
        if (method == "GET" && path.StartsWith("/api/v1/search/suggest")) return (200, "[{\"text\":\"Praia Grande\",\"kind\":\"island\"},{\"text\":\"Praia Distante\",\"kind\":\"island\"}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/popular")) return (200, $"[{items[0]},{items[1]}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/recent")) return (200, $"[{items[1]},{Hit("poi", _plot, "Farol Novo", _here, 1, 2)}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/favorites?")) return (200, $"{{\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{Hit("island", _here, "Praia Grande", _here, 10, 20, true)}],\"counts\":{{}},\"categories\":{{}}}}");
        if (path.StartsWith("/api/v1/search/favorites/")) return method == "PUT" && _favoritesFull ? (409, "{}") : (204, string.Empty);
        if (method == "GET" && path == "/api/v1/search/interests") return (200, JsonSerializer.Serialize(_interests));
        if (method == "PUT" && path == "/api/v1/search/interests") { var list = JsonDocument.Parse(body!).RootElement.GetProperty("interests").EnumerateArray().Select(e => e.GetString()!.ToLowerInvariant()); _interests = [.. list]; return (200, JsonSerializer.Serialize(_interests)); }
        if (method == "GET" && path.StartsWith("/api/v1/search/recommended")) return (200, $"[{Hit("poi", _studio, "Estudio de Musica", _here, 1, 2)}]");
        if (method == "POST" && path.StartsWith("/api/v1/search/report/")) return (202, string.Empty);
        if (method == "POST" && path == "/api/v1/search/events") return (204, string.Empty);
        return (404, "{}");
    }

    // ---- people and groups (stage 2) -------------------------------------------------------------------------------------------------
    private readonly List<string> _social = [];
    private string _access = "open", _evStatus = "scheduled"; private bool _isOwner, _guests, _evJoined, _evCal, _evFull, _evManage, _agenda; private string? _createError; private readonly List<string> _created = []; private readonly Guid _event = Guid.NewGuid(); private readonly DateTimeOffset _evStart = DateTimeOffset.UtcNow.AddHours(5);
    private string EventHit() => JsonSerializer.Serialize(new { kind = "event", kindLabel = "Evento", id = _event, title = "Show na praia", subtitle = "Evento em Praia Grande", description = "Música ao vivo.", category = "show", thumbnailUrl = (string?)null, islandId = _here, x = 12f, z = 34f, score = 1.0, popularity = 5.0, favorite = false, startsAt = _evStart, endsAt = _evStart.AddHours(2), priceCoins = 0, rating = "livre" });
    private readonly Guid _product = Guid.NewGuid(), _shop = Guid.NewGuid(), _worldShop = Guid.NewGuid(), _creatorId = Guid.NewGuid(); private long _price = 300; private bool _itemOwn, _itemOwned, _itemGone, _shopMine; private int _buyStatus = 200; private string _sellerKind = "user", _buyKeyLast = ""; private string? _storeError; private readonly List<string> _buys = [], _storeCalls = [];
    private string ProductHit() => JsonSerializer.Serialize(new { kind = "product", kindLabel = "Produto", id = _product, title = "Vestido longo", subtitle = "300 moedas · roupas", description = "Vestido elegante.", category = "roupas", thumbnailUrl = (string?)null, islandId = (Guid?)null, x = (float?)null, z = (float?)null, score = 1.0, popularity = 5.0, favorite = false, priceCoins = 300, rights = LifeVerse.Shared.Items.ItemRights.Copy | LifeVerse.Shared.Items.ItemRights.Modify });
    private string ShopHit() => JsonSerializer.Serialize(new { kind = "store", kindLabel = "Loja", id = _shop, title = "Moda da Luna", subtitle = "Loja virtual · Luna", description = "Roupas e acessórios.", category = "moda", thumbnailUrl = (string?)null, islandId = (Guid?)null, x = (float?)null, z = (float?)null, score = 1.0, popularity = 3.0, favorite = false });
    private string WorldShopHit() => JsonSerializer.Serialize(new { kind = "store", kindLabel = "Loja", id = _worldShop, title = "Armas do Porto", subtitle = "Loja em Praia Grande", description = "Espadas.", category = "armas", thumbnailUrl = (string?)null, islandId = _here, x = 7f, z = 9f, score = 1.0, popularity = 1.0, favorite = false });
    private string _relation = "none", _policy = "open"; private long _fee; private bool _member, _pending, _banned, _full;
    private Control _ui = null!;

    private (int, string) People(Godot.HttpClient.Method method, string path, string? body)
    {
        _social.Add($"{method} {path} {body}");
        if (method == Godot.HttpClient.Method.Post && path == "/api/v1/social/friends/requests") { _relation = "pending_out"; return (200, "{}"); }
        if (method == Godot.HttpClient.Method.Post && path.StartsWith("/api/v1/social/blocks/")) { _relation = "blocked"; return (200, "{}"); }
        if (method == Godot.HttpClient.Method.Post && path is "/api/v1/social/messages" or "/api/v1/social/teleports" or "/api/v1/social/reports") return (200, "{}");
        if (method == Godot.HttpClient.Method.Get && path == "/api/v1/economy/wallet") return (200, "{\"availableCoins\":500}");
        if (method == Godot.HttpClient.Method.Post && path == "/api/v1/economy/transfer") return (200, "{}");
        return (404, "{}");
    }

    private (int, string)? Extra(string method, string path, string? body)
    {
        if (method == "GET" && path.StartsWith("/api/v1/search?") && path.Contains("types=product")) return (200, $"{{\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{ProductHit()}],\"counts\":{{\"product\":1}},\"categories\":{{\"roupas\":1}},\"sort\":\"relevance\"}}");
        if (method == "GET" && path.StartsWith("/api/v1/search?") && path.Contains("types=store")) return (200, $"{{\"total\":2,\"page\":1,\"pageSize\":20,\"items\":[{ShopHit()},{WorldShopHit()}],\"counts\":{{\"store\":2}},\"categories\":{{\"moda\":1}},\"sort\":\"relevance\"}}");
        if (method == "GET" && path == $"/api/v1/discovery/items/{_product}") return _itemGone ? (404, "{}") : (200, JsonSerializer.Serialize(new { id = _product, name = "Vestido longo", description = "Vestido elegante.", category = "roupas", priceCoins = _price, status = "active", creator = new { id = _creatorId, username = "criador", displayName = "Criador" }, seller = new { kind = _sellerKind, id = _sellerKind == "group" ? _group : _person, name = "Luna" }, rights = new { copy = true, modify = true, transfer = false, resell = false }, nextRights = new { copy = false, modify = false, transfer = true, resell = false }, allowGift = true, resale = false, asset = new { category = "models", mimeType = "model/gltf-binary", byteSize = 2048, version = 1 }, owned = _itemOwned, isOwn = _itemOwn, available = true, canBuy = !_itemOwn && !(_price == 0 && _itemOwned), store = new { id = _shop, name = "Moda da Luna", kind = "virtual" } }));
        if (method == "POST" && path == $"/api/v1/economy/products/{_product}/purchase") { _buys.Add(body!); if (_buyStatus == 0) return (0, string.Empty); if (_buyStatus == 409) return (409, "{\"error\":\"insufficient_balance\"}"); return (200, "{\"id\":\"" + Guid.NewGuid() + "\"}"); }
        if (method == "GET" && path == $"/api/v1/discovery/stores/{_shop}") return (200, JsonSerializer.Serialize(new { id = _shop, kind = "virtual", name = "Moda da Luna", description = "Roupas e acessórios.", category = "moda", categoryLabel = "Moda", logoUrl = (string?)null, bannerUrl = (string?)null, owner = new { id = _person, username = "luna", displayName = "Luna" }, island = (object?)null, productCount = 3, featured = new[] { new { id = _product, name = "Vestido longo", priceCoins = 300, category = "roupas", imageUrl = (string?)null } }, isOwner = _shopMine, open = true, ownerId = _person }));
        if (method == "GET" && path == $"/api/v1/discovery/stores/{_worldShop}") return (200, JsonSerializer.Serialize(new { id = _worldShop, kind = "physical", name = "Armas do Porto", description = "Espadas.", category = "armas", categoryLabel = "armas", owner = new { id = _person, username = "luna", displayName = "Luna" }, island = new { id = _here, name = "Praia Grande" }, x = 7f, z = 9f, productCount = 1, featured = Array.Empty<object>(), isOwner = false, open = true, ownerId = _person }));
        if (path == "/api/v1/discovery/stores" && method == "POST") { _storeCalls.Add("POST " + body); return _storeError is { } e ? (409, "{\"error\":\"" + e + "\"}") : (201, "{\"id\":\"" + Guid.NewGuid() + "\"}"); }
        if (path == $"/api/v1/discovery/stores/{_shop}" && method == "PUT") { _storeCalls.Add("PUT " + body); return (204, string.Empty); }
        if (path == $"/api/v1/discovery/stores/{_shop}" && method == "DELETE") { _storeCalls.Add("DELETE"); return (204, string.Empty); }
        if (method == "GET" && path.StartsWith("/api/v1/discovery/islands/") && path.EndsWith("/info")) return (200, JsonSerializer.Serialize(new { id = Guid.NewGuid(), name = "Praia Grande", category = "social", contentRating = "livre", language = "pt-BR", owner = new { id = _person, username = "luna", displayName = "Luna" }, online = true, visitors = 3, maxPlayers = 64, access = _access, canEnter = _access == "open", isOwner = _isOwner, allowsGuestEvents = _guests }));
        if (method == "PUT" && path.StartsWith("/api/v1/discovery/islands/") && path.EndsWith("/event-policy")) { _guests = JsonDocument.Parse(body!).RootElement.GetProperty("allowGuests").GetBoolean(); return (204, string.Empty); }
        if (method == "GET" && path == $"/api/v1/discovery/events/{_event}") return (200, JsonSerializer.Serialize(new { id = _event, name = "Show na praia", description = "Música ao vivo.", category = "show", categoryLabel = "Show", startsAt = _evStart, endsAt = _evStart.AddHours(2), status = _evStatus, capacity = 10, going = _evFull ? 10 : 3, full = _evFull, priceCoins = 0, contentRating = "livre", island = new { id = _here, name = "Praia Grande" }, organizer = new { id = _person, username = "luna", displayName = "Luna" }, joined = _evJoined, inCalendar = _evCal, canManage = _evManage, canJoin = _evStatus is "scheduled" or "live" && !_evJoined && !_evFull }));
        if (method == "POST" && path == $"/api/v1/discovery/events/{_event}/join") { _evJoined = true; return (204, string.Empty); }
        if (method == "POST" && path == $"/api/v1/discovery/events/{_event}/leave") { _evJoined = false; return (204, string.Empty); }
        if (method == "PUT" && path == $"/api/v1/discovery/events/{_event}/calendar") { _evCal = JsonDocument.Parse(body!).RootElement.GetProperty("on").GetBoolean(); return (204, string.Empty); }
        if (method == "POST" && path == $"/api/v1/discovery/events/{_event}/cancel") { _evStatus = "cancelled"; return (204, string.Empty); }
        if (method == "GET" && path == "/api/v1/discovery/events/mine") return (200, _agenda ? JsonSerializer.Serialize(new[] { new { id = _event, name = "Show na praia", startsAt = _evStart, endsAt = _evStart.AddHours(2), island = new { id = _here, name = "Praia Grande" }, going = true, inCalendar = false, status = "scheduled" } }) : "[]");
        if (method == "POST" && path == "/api/v1/discovery/events") { _created.Add(body!); return _createError is { } error ? (403, "{\"error\":\"" + error + "\"}") : (201, "{\"id\":\"" + Guid.NewGuid() + "\"}"); }
        if (method == "GET" && path.StartsWith("/api/v1/search?") && path.Contains("types=event")) return (200, $"{{\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{EventHit()}],\"counts\":{{\"event\":1}},\"categories\":{{\"show\":1}},\"sort\":\"relevance\"}}");
        if (method == "GET" && path == $"/api/v1/users/{_person}/profile/full") return (200, JsonSerializer.Serialize(new { id = _person, username = "praiasilva", displayName = "Praia Silva", privateProfile = false, joinedAt = "2024-03-15T10:00:00Z", presence = "online", bio = "Exploradora e fotógrafa.", hasPhoto = false, groups = new[] { new { name = "Clube do Mar" } } }));
        if (method == "GET" && path.StartsWith("/api/v1/social/relations?ids=")) return (200, "{" + string.Join(",", path["/api/v1/social/relations?ids=".Length..].Split(',').Select(id => $"\"{id}\":\"{_relation}\"")) + "}");
        if (method == "GET" && path == $"/api/v1/groups/{_group}") return (200, JsonSerializer.Serialize(new { card = new { id = _group, name = "Amigos da Praia", description = "Quem ama praia.", members = 128, memberLimit = 500, joinPolicy = _policy, joinFee = _fee, isMember = _member, requestPending = _pending, invited = false, banned = _banned, category = "social", language = "pt-BR", contentRating = "livre" }, leaderName = "luna", createdAt = "2025-01-02T00:00:00Z" }));
        if (method == "POST" && path == $"/api/v1/groups/{_group}/join")
        {
            _calls.Add("BODY " + body); var confirm = JsonDocument.Parse(body!).RootElement.TryGetProperty("confirmFee", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
            if (_full) return (409, "{\"error\":\"group_full\"}");
            if (_policy == "request") { _pending = true; return (200, "{\"requested\":true}"); }
            if (_fee > 0 && confirm != _fee) return (409, "{\"error\":\"fee_required\",\"detail\":{\"fee\":" + _fee + "}}");
            _member = true; return (200, "{\"joined\":true}");
        }
        return null;
    }

    private void PressDialog(string prefix, string button)
    {
        var window = _ui.GetChildren().OfType<FloatingWindow>().Last(w => w.Title.StartsWith(prefix) && !w.IsQueuedForDeletion());
        window.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void Type(string prefix, string field, string text)
    {
        var window = _ui.GetChildren().OfType<FloatingWindow>().LastOrDefault(w => w.Title.StartsWith(prefix) && !w.IsQueuedForDeletion()) ?? throw new InvalidOperationException($"no window {prefix}: " + string.Join(",", _ui.GetChildren().Select(c => c.Name.ToString())));
        var found = window.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().ToList(); if (found.Count == 0) throw new InvalidOperationException($"no field in {window.Name} (content: {string.Join(",", window.Content.GetChildren().Select(c => c.GetType().Name))}) for {prefix}");
        var edit = found[0]; edit.Text = text; edit.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
    }

    private async Task PeopleAndGroups(SearchWindow window)
    {
        _ui = new Control(); AddChild(_ui); var people = new PersonActions { Name = "PersonActions", Transport = People }; AddChild(people); people.Setup(_ui); window.People = people;
        window.SetQueryForTesting("praia"); window.SelectTabForTesting(1); window.SelectForTesting(2);
        Require(window.PersonBoxVisibleForTesting && window.PreviewTitleForTesting == "Praia Silva", "a person shows their profile actions and the name from the profile: " + window.PreviewTitleForTesting);
        Require(window.ExtraForTesting.Contains("No LifeVerse desde 15/03/2024") && window.ExtraForTesting.Contains("Grupos: Clube do Mar") && window.ExtraForTesting.Contains("Online agora"), "public facts of the person: " + window.ExtraForTesting);
        Require(window.FriendTextForTesting == "Adicionar amigo" && window.FriendEnabledForTesting && window.MessageEnabledForTesting, "a stranger can be added");
        window.PressFriendForTesting();
        Require(_social.Any(s => s.StartsWith("Post /api/v1/social/friends/requests") && s.Contains(_person.ToString())), "the request goes to the exact account found: " + string.Join(" | ", _social));
        Require(window.FriendTextForTesting == "Pedido de amizade enviado" && !window.FriendEnabledForTesting, "the state becomes pending and cannot be asked twice");
        var before = _social.Count; window.PressFriendForTesting(); Require(_social.Count == before, "a pending request is not sent again");
        window.PressMessageForTesting(); Type("Mensagem para", "", "Oi, tudo bem?"); Require(_social.Any(s => s.StartsWith("Post /api/v1/social/messages") && s.Contains(_person.ToString()) && s.Contains("Oi, tudo bem?")), "the message goes to the person");
        window.PressInviteForTesting(); Require(_social.Any(s => s.StartsWith("Post /api/v1/social/teleports") && s.Contains("\"kind\":\"offer\"") && s.Contains(_here.ToString())), "a teleport offer names the person, the island and the kind");
        window.PressPayForTesting(); Type("Pagar a", "", "50"); Require(_social.Any(s => s.StartsWith("Post /api/v1/economy/transfer") && s.Contains(_person.ToString()) && s.Contains("\"amountCoins\":50")), "paying asks for the value and sends it to the person: " + string.Join(" | ", _social.TakeLast(3)));
        window.ChooseMoreForTesting(3); Type("Denunciar", "", "spam"); Require(_social.Any(s => s.StartsWith("Post /api/v1/social/reports") && s.Contains(_person.ToString())), "the report names the person");
        window.ChooseMoreForTesting(1); Require(window.NoteForTesting == "Identificador copiado.", "the id can be copied");
        window.PressBlockForTesting(); Require(!_social.Any(s => s.Contains("/social/blocks/")), "blocking asks first"); PressDialog("Bloquear pessoa", "Bloquear");
        Require(_social.Any(s => s.StartsWith("Post /api/v1/social/blocks/" + _person)) && window.FriendTextForTesting == "Pessoa bloqueada" && !window.MessageEnabledForTesting, "after blocking nothing else can be done to the person");
        _relation = "friend"; window.SelectForTesting(-1); window.SelectForTesting(2); Require(window.FriendTextForTesting == "Vocês são amigos ✓" && !window.FriendEnabledForTesting && window.MetaForTesting(2).Contains("Amigo"), "a friend is shown as one, in the card too: " + window.MetaForTesting(2));
        _relation = "pending_in"; window.SelectForTesting(-1); window.SelectForTesting(2); Require(window.FriendTextForTesting == "Aceitar pedido de amizade" && window.FriendEnabledForTesting, "an incoming request can be accepted");
        _relation = "none";

        // groups
        window.SetQueryForTesting("amigos"); window.SelectTabForTesting(4);
        Require(window.Hits.Count == 1 && window.MetaForTesting(0).Contains("5 membros"), "a group card tells the members: " + window.MetaForTesting(0));
        window.SelectForTesting(0);
        Require(window.GroupBoxVisibleForTesting && !window.PersonBoxVisibleForTesting && window.ExtraForTesting.Contains("128 membros") && window.ExtraForTesting.Contains("Entrada: livre") && window.ExtraForTesting.Contains("Líder: luna"), "group facts: " + window.ExtraForTesting);
        Require(window.JoinTextForTesting == "Participar" && window.JoinEnabledForTesting && !window.GroupChatEnabledForTesting, "an open group can be joined, its chat is for members");
        window.PressGroupMembersForTesting(); Require(_groupTabs.Last() == (_group, "membros"), "Ver membros opens the group on its members tab");
        _fee = 100; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.JoinTextForTesting == "Entrar (VC$ 100)", "a fee is shown on the button: " + window.JoinTextForTesting);
        window.PressJoinForTesting(); Require(!_calls.Contains("POST /api/v1/groups/" + _group + "/join"), "paying to enter asks first"); PressDialog("Entrar em", "Pagar e entrar");
        Require(_calls.Any(c => c.StartsWith("BODY") && c.Contains("\"confirmFee\":100")) && window.NoteForTesting.StartsWith("Você entrou") && window.JoinTextForTesting == "Você já é membro" && window.GroupChatEnabledForTesting, "joining with the confirmed fee makes a member: " + window.NoteForTesting);
        window.PressGroupChatForTesting(); Require(_groupTabs.Last() == (_group, "chat"), "members write in the chat of the group");
        _member = false; _fee = 0; _policy = "request"; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.JoinTextForTesting == "Pedir para entrar", "a group that approves is asked");
        window.PressJoinForTesting(); PressDialog("Pedir para entrar", "Enviar pedido"); Require(window.NoteForTesting.StartsWith("Pedido enviado") && window.JoinTextForTesting == "Pedido enviado" && !window.JoinEnabledForTesting, "a request is sent once: " + window.NoteForTesting);
        _pending = false; _policy = "invite"; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.JoinTextForTesting == "Somente por convite" && !window.JoinEnabledForTesting, "an invite-only group cannot be joined");
        _policy = "open"; _banned = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.JoinTextForTesting == "Entrada indisponível" && !window.JoinEnabledForTesting, "a banned person cannot try");
        _banned = false; _full = true; window.SelectForTesting(-1); window.SelectForTesting(0); window.PressJoinForTesting(); Require(window.NoteForTesting == "O grupo está cheio.", "a full group is explained: " + window.NoteForTesting); _full = false;
        window.PressGroupForTesting(); Require(_groupOpened == _group, "Ver grupo opens the group window");
        window.SetQueryForTesting("praia"); await Frames(1);
    }
    // ---- places and events (stage 3) --------------------------------------------------------------------------------------------------
    private readonly List<(Guid, float, float)> _mapShown = [];

    private async Task PlacesAndEvents(SearchWindow window)
    {
        window.ShowOnMap = (island, x, z) => _mapShown.Add((island, x, z));
        // which filters and orders each tab has
        window.SetQueryForTesting("praia"); window.SelectTabForTesting(2);
        Require(window.SortLabelsForTesting.SequenceEqual(["Relevância", "Mais populares", "Mais recentes", "Nome A–Z"]) && window.FilterRowShowsForTesting("rating") && !window.FilterRowShowsForTesting("when") && !window.FilterRowShowsForTesting("new"), "places: ordered by name too, rated, no event filters");
        window.SetSortForTesting(3); window.Search(); Require(LastSearch.Contains("sort=name") && LastSearch.Contains("types=island%2Cparcel%2Cpoi"), "places can be sorted by name: " + LastSearch); window.SetSortForTesting(0);
        window.SetRatingForTesting(2); window.Search(); Require(LastSearch.Contains("ratings=moderado"), "the content rating is sent: " + LastSearch); window.SetRatingForTesting(0);

        // a place: facts and whether it can be entered
        window.Search(); window.SelectForTesting(0);
        Require(window.PlaceBoxVisibleForTesting && window.PlaceExtraForTesting.Contains("Proprietário(a): Luna.") && window.PlaceExtraForTesting.Contains("3 visitantes agora (limite 64)") && window.PlaceExtraForTesting.Contains("social"), "a place tells its owner, category and visitors: " + window.PlaceExtraForTesting);
        Require(window.GoEnabledForTesting && window.GoTextForTesting == "Teleportar até lá", "an open place can be entered");
        foreach (var (state, label, words) in new[] { ("full", "Ilha cheia", "Ilha cheia no momento"), ("closed", "Ilha fechada", "temporariamente fechada"), ("banned", "Acesso negado", "banido desta ilha"), ("private", "Ilha privada", "só convidados") })
        {
            _access = state; window.SelectForTesting(-1); window.SelectForTesting(0);
            Require(!window.GoEnabledForTesting && window.GoTextForTesting == label && window.PlaceExtraForTesting.Contains(words), $"{state} is said and teleport is off: {window.GoTextForTesting} / {window.PlaceExtraForTesting}");
        }
        _access = "open"; window.SelectForTesting(-1); window.SelectForTesting(0);
        window.PressMapForTesting(); Require(_mapShown.Count == 1 && _mapShown[0].Item1 == _here && _mapShown[0].Item2 == 10 && _mapShown[0].Item3 == 20, "Mostrar no mapa asks for that spot on that island");
        window.PressCopyLinkForTesting(); Require(window.NoteForTesting == "Link da localização copiado.", "the location link can be copied");
        Require(window.GuestEventsTextForTesting == "", "only the owner sees the guest-events switch");
        _isOwner = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.GuestEventsTextForTesting == "Eventos de convidados: bloqueados", "the owner sees it");
        window.PressGuestEventsForTesting(); Require(_guests && window.GuestEventsTextForTesting == "Eventos de convidados: permitidos" && window.NoteForTesting.Contains("convidados podem anunciar"), "the owner lets guests announce"); _isOwner = false;

        // the events tab: filters, orders and cards
        window.SetQueryForTesting("show"); window.SelectTabForTesting(3);
        Require(window.FilterRowShowsForTesting("when") && window.FilterRowShowsForTesting("price") && window.FilterRowShowsForTesting("calendar") && window.FilterRowShowsForTesting("new") && window.SortLabelsForTesting.SequenceEqual(["Relevância", "Mais próximos", "Mais populares"]), "the events tab has its own filters");
        Require(window.Hits.Count == 1 && window.EventMetaForTesting(0).StartsWith("Evento · ") && window.EventMetaForTesting(0).Contains("Praia Grande") && window.EventMetaForTesting(0).EndsWith("Gratuito"), "an event card tells when, where and the price: " + window.EventMetaForTesting(0));
        window.SetWhenForTesting(1); window.Search(); Require(LastSearch.Contains("from=") && LastSearch.Contains("to="), "happening now sends a time window: " + LastSearch);
        window.SetWhenForTesting(2); window.SetPriceForTesting(1); window.SetSortForTesting(1); window.Search(); Require(LastSearch.Contains("from=") && LastSearch.Contains("to=") && LastSearch.Contains("free=true") && LastSearch.Contains("sort=start"), "today, free and soonest: " + LastSearch);
        window.SetPriceForTesting(2); window.SetWhenForTesting(4); window.Search(); Require(LastSearch.Contains("free=false") && LastSearch.Contains("from=") && !LastSearch.Contains("to="), "paid and coming: " + LastSearch);
        window.SetPriceForTesting(0); window.SetWhenForTesting(0); window.SetSortForTesting(0); window.SetCategoryForTesting("Show"); window.Search(); Require(LastSearch.Contains("category=show"), "the category is shown by its name and sent by its key: " + LastSearch);
        window.SetCategoryForTesting("Todas as categorias"); window.Search();

        // one event
        window.SelectForTesting(0);
        Require(window.EventBoxVisibleForTesting && window.PlaceBoxVisibleForTesting == false && window.ExtraForTesting.Contains("Agendado") && window.ExtraForTesting.Contains("Quando: ") && window.ExtraForTesting.Contains("UTC") && window.ExtraForTesting.Contains("Onde: Praia Grande") && window.ExtraForTesting.Contains("Organização: Luna") && window.ExtraForTesting.Contains("Valor: gratuito") && window.ExtraForTesting.Contains("3 de 10"), "the event tells everything: " + window.ExtraForTesting);
        Require(window.EventJoinTextForTesting == "Participar" && window.EventJoinEnabledForTesting && window.EventCalendarTextForTesting == "Adicionar ao calendário" && window.GoEnabledForTesting && !window.EventCancelVisibleForTesting, "an open event can be joined and saved");
        window.PressEventJoinForTesting(); Require(_calls.Any(c => c == $"POST /api/v1/discovery/events/{_event}/join") && window.EventJoinTextForTesting == "Sair do evento" && window.NoteForTesting.StartsWith("Inscrição feita"), "joining signs up and the button turns into leaving: " + window.NoteForTesting);
        window.PressEventJoinForTesting(); Require(_calls.Any(c => c == $"POST /api/v1/discovery/events/{_event}/leave") && window.EventJoinTextForTesting == "Participar", "leaving frees the place");
        window.PressEventCalendarForTesting(); Require(_calls.Any(c => c == $"PUT /api/v1/discovery/events/{_event}/calendar") && window.EventCalendarTextForTesting == "Remover do calendário", "the calendar keeps the event");
        window.PressEventCalendarForTesting(); Require(window.EventCalendarTextForTesting == "Adicionar ao calendário", "and lets it go");
        window.PressEventOrganizerForTesting(); Require(_profileOpened == _person, "the organizer opens their profile");
        window.PressEventShareForTesting(); Require(window.NoteForTesting == "Link do evento copiado.", "the event link can be copied");
        _evFull = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.EventJoinTextForTesting == "Evento lotado" && !window.EventJoinEnabledForTesting, "a full event says so"); _evFull = false;
        _evStatus = "ended"; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.EventJoinTextForTesting == "Evento encerrado" && !window.EventJoinEnabledForTesting && !window.GoEnabledForTesting && !window.EventCalendarEnabledForTesting, "an event that ended cannot be joined, saved or visited"); _evStatus = "cancelled";
        window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.EventJoinTextForTesting == "Evento cancelado" && window.GoTextForTesting == "Evento cancelado" && window.ExtraForTesting.Contains("Cancelado"), "a cancelled event is shown as such"); _evStatus = "scheduled";
        _evManage = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.EventCancelVisibleForTesting, "the organizer sees the button to cancel");
        window.PressEventCancelForTesting(); Require(!_calls.Any(c => c == $"POST /api/v1/discovery/events/{_event}/cancel"), "cancelling asks first"); PressDialog("Cancelar evento", "Cancelar evento");
        Require(_calls.Any(c => c == $"POST /api/v1/discovery/events/{_event}/cancel") && window.NoteForTesting == "Evento cancelado.", "after confirming the event is called off"); _evManage = false; _evStatus = "scheduled";

        // my calendar
        _agenda = true; window.SetOnlyCalendarForTesting(true); window.Search();
        Require(_calls.Last() == "GET /api/v1/discovery/events/mine" && window.Hits.Count == 1 && window.CountForTesting == "1 evento no calendário", "the calendar lists what I signed up for");
        _agenda = false; window.Search(); Require(window.Hits.Count == 0 && window.StateForTesting.Contains("calendário está vazio"), "an empty calendar says so");
        window.SetOnlyCalendarForTesting(false);

        // announcing an event
        var tomorrow = DateTimeOffset.Now.AddDays(1).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        window.OpenEventFormForTesting(); Require(window.EventFormOpenForTesting, "the form opens");
        window.FillEventFormForTesting("Show de teste", "31-12", "20:00", 3, 10, 0); window.SubmitEventFormForTesting(); Require(window.EventFormNoteForTesting.Contains("dd/mm/aaaa") && _created.Count == 0, "a wrong date is told before sending");
        window.FillEventFormForTesting("Show de teste", tomorrow, "20:00", 3, 10, 0); _createError = "not_allowed"; window.SubmitEventFormForTesting();
        Require(_created.Count == 1 && window.EventFormNoteForTesting.Contains("Só o dono da ilha") && window.EventFormOpenForTesting, "the server refusing is explained and the form stays: " + window.EventFormNoteForTesting);
        _createError = null; window.SubmitEventFormForTesting(); await Frames(2);
        Require(_created.Count == 2 && _created[1].Contains("\"name\":\"Show de teste\"") && _created[1].Contains(_here.ToString()) && _created[1].Contains("\"capacity\":10") && _created[1].Contains("\"category\":\"show\"") && _created[1].Contains("\"contentRating\":\"livre\"") && !window.EventFormOpenForTesting && window.NoteForTesting.StartsWith("Evento anunciado"), "announcing sends the event for the island I am on: " + _created[1]);
        window.SetQueryForTesting("praia"); await Frames(1);
    }
    // ---- items and stores (stage 4) ---------------------------------------------------------------------------------------------------

    private string KeyOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("idempotencyKey").GetString()!;

    private async Task ItemsAndStores(SearchWindow window)
    {
        // the items tab
        window.SetQueryForTesting("vestido"); window.SelectTabForTesting(5);
        Require(window.MarketFilterShowsForTesting("price") && window.MarketFilterShowsForTesting("creator") && window.MarketFilterShowsForTesting("rights") && !window.MarketFilterShowsForTesting("kind") && window.FilterRowShowsForTesting("price"), "the items tab has price, creator and rights filters");
        Require(window.SortLabelsForTesting.SequenceEqual(["Relevância", "Menor preço", "Maior preço", "Mais recentes", "Mais populares"]), "items can be sorted by price");
        window.SetPriceRangeForTesting("10", "500"); window.SetRightsForTesting(true, true, false); window.SetCreatorForTesting("criador"); window.SetPriceForTesting(1); window.SetSortForTesting(1); window.Search();
        Require(LastSearch.Contains("types=product") && LastSearch.Contains("minPrice=10") && LastSearch.Contains("maxPrice=500") && LastSearch.Contains("rights=copy%2Cmodify") && LastSearch.Contains("creator=criador") && LastSearch.Contains("free=true") && LastSearch.Contains("sort=price_asc"), "every item filter is sent: " + LastSearch);
        window.SetPriceRangeForTesting("", ""); window.SetRightsForTesting(false, false, false); window.SetCreatorForTesting(""); window.SetPriceForTesting(0); window.SetSortForTesting(0); window.Search();
        Require(window.Hits.Count == 1 && window.MarketMetaForTesting(0) == "Item · roupas · VC$ 300 · Permissões: " + LifeVerse.Shared.Items.ItemRights.Letters(LifeVerse.Shared.Items.ItemRights.Copy | LifeVerse.Shared.Items.ItemRights.Modify), "an item card tells the price and the rights: " + window.MarketMetaForTesting(0));

        // one item
        window.SelectForTesting(0);
        Require(window.ItemBoxVisibleForTesting && !window.PersonBoxVisibleForTesting && window.ExtraForTesting.Contains("Preço: VC$ 300") && window.ExtraForTesting.Contains("Criador(a): Criador") && window.ExtraForTesting.Contains("Vendedor(a): Luna") && window.ExtraForTesting.Contains("Você poderá: copiar, modificar.") && window.ExtraForTesting.Contains("E passar adiante: transferir.") && window.ExtraForTesting.Contains("Tipo: models"), "the item tells everything: " + window.ExtraForTesting);
        Require(window.BuyTextForTesting == "Comprar por VC$ 300" && window.BuyEnabledForTesting && window.CreatorButtonVisibleForTesting && window.VisitShopVisibleForTesting, "an item for sale can be bought");
        window.PressCreatorForTesting(); Require(_profileOpened == _creatorId, "Ver criador opens the creator"); window.PressSellerForTesting(); Require(_profileOpened == _person, "Ver vendedor opens the seller");
        _sellerKind = "group"; window.SelectForTesting(-1); window.SelectForTesting(0); window.PressSellerForTesting(); Require(_groupOpened == _group, "a group that sells opens as a group"); _sellerKind = "user";

        // buying: the price and rights are shown first, nothing is charged before the yes
        window.PressBuyForTesting(); Require(_buys.Count == 0, "buying asks first"); PressDialog("Confirmar compra", "Comprar");
        Require(_buys.Count == 1 && KeyOf(_buys[0]).Length == 32 && window.NoteForTesting.StartsWith("Compra feita!"), "after the yes the purchase is sent with a key: " + window.NoteForTesting + " / " + string.Join("|", _buys));
        var firstKey = KeyOf(_buys[0]);
        // a connection that fails keeps the key, so trying again cannot charge twice
        _buyStatus = 0; window.PressBuyForTesting(); PressDialog("Confirmar compra", "Comprar"); Require(window.NoteForTesting.StartsWith("Sem conexão") && _buys.Count == 2, "no answer is told plainly: " + window.NoteForTesting);
        window.PressBuyForTesting(); PressDialog("Confirmar compra", "Comprar"); Require(_buys.Count == 3 && KeyOf(_buys[2]) == KeyOf(_buys[1]), "trying again after no answer sends the same key");
        _buyStatus = 200; window.PressBuyForTesting(); PressDialog("Confirmar compra", "Comprar"); Require(_buys.Count == 4 && KeyOf(_buys[3]) == KeyOf(_buys[1]), "and it goes through with that key"); window.PressBuyForTesting(); PressDialog("Confirmar compra", "Comprar"); Require(KeyOf(_buys[4]) != KeyOf(_buys[3]) && KeyOf(_buys[4]) != firstKey, "after an answer the next purchase is a new one");
        _buyStatus = 409; window.PressBuyForTesting(); PressDialog("Confirmar compra", "Comprar"); Require(window.NoteForTesting == "Saldo insuficiente para esta compra.", "the server refusing is explained: " + window.NoteForTesting); _buyStatus = 200;
        _price = 900; window.SelectForTesting(-1); window.SelectForTesting(0); var buys = _buys.Count; window.PressBuyForTesting(); Require(_buys.Count == buys && window.NoteForTesting.Contains("Saldo insuficiente: você tem VC$ 500") && window.NoteForTesting.Contains("900"), "a balance that cannot pay is told before asking: " + window.NoteForTesting);
        _price = 0; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.BuyTextForTesting == "Pegar grátis" && window.BuyEnabledForTesting, "a free item is taken, not bought");
        _itemOwned = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.BuyTextForTesting == "Você já pegou este item" && !window.BuyEnabledForTesting, "a free item is taken once"); _itemOwned = false; _price = 300;
        _itemOwn = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.BuyTextForTesting == "Este item é seu" && !window.BuyEnabledForTesting, "one's own item cannot be bought"); _itemOwn = false;
        _itemGone = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.BuyTextForTesting == "Item indisponível" && !window.BuyEnabledForTesting && window.ExtraForTesting.Contains("não está mais à venda"), "a removed item says so"); _itemGone = false;
        window.SelectForTesting(-1); window.SelectForTesting(0);

        // from the item to its shop
        window.PressVisitShopForTesting(); Require(window.TabForTesting == 6 && window.Hits.Count == 1 && window.Hits[0].Id == _shop && window.StoreBoxVisibleForTesting, "Visitar loja opens the shop of the item, chosen: tab " + window.TabForTesting);
        Require(window.ExtraForTesting.Contains("Proprietário(a): Luna.") && window.ExtraForTesting.Contains("Loja virtual: sem endereço no mundo.") && window.ExtraForTesting.Contains("3 produtos publicados.") && window.ExtraForTesting.Contains("Em destaque:") && window.ExtraForTesting.Contains("• Vestido longo — VC$ 300"), "the virtual shop: " + window.ExtraForTesting);

        // the stores tab
        window.SetQueryForTesting("moda"); window.Search();
        Require(window.MarketFilterShowsForTesting("kind") && window.MarketFilterShowsForTesting("new") && !window.MarketFilterShowsForTesting("price"), "the stores tab tells virtual from physical and lets me open a shop");
        Require(window.Hits.Count == 2 && window.MarketMetaForTesting(0) == "Loja virtual · de Luna · Moda" && window.MarketMetaForTesting(1) == "Loja física em Praia Grande", "cards tell virtual from physical: " + window.MarketMetaForTesting(0) + " / " + window.MarketMetaForTesting(1));
        window.SetStoreKindForTesting(1); window.Search(); Require(LastSearch.Contains("located=false"), "only virtual: " + LastSearch); window.SetStoreKindForTesting(2); window.Search(); Require(LastSearch.Contains("located=true"), "only physical: " + LastSearch); window.SetStoreKindForTesting(0); window.Search();
        window.SelectForTesting(1); Require(window.GoEnabledForTesting && window.GoTextForTesting.StartsWith("Visitar loja") && window.ExtraForTesting.Contains("Loja física em Praia Grande"), "a shop in the world can be visited: " + window.GoTextForTesting); window.GoToResult(); Require(_teleports.Last() == "7,9", "the visit goes to the place of the shop: " + _teleports.Last());
        window.SelectForTesting(0); Require(!window.GoEnabledForTesting && window.GoTextForTesting == "Loja virtual (sem endereço)", "a virtual shop has no place to visit: " + window.GoTextForTesting);
        window.PressStoreOwnerForTesting(); Require(_profileOpened == _person, "the owner of the shop opens");
        window.PressViewProductsForTesting(); Require(window.TabForTesting == 5 && window.OwnerChipVisibleForTesting && window.OwnerChipTextForTesting == "Produtos de “Moda da Luna”" && LastSearch.Contains("owner=" + _person) && LastSearch.Contains("types=product"), "Ver produtos lists the items of that owner: " + LastSearch);
        window.PressClearOwnerForTesting(); Require(!window.OwnerChipVisibleForTesting && !LastSearch.Contains("owner="), "and everything can be shown again: " + LastSearch);

        // my own virtual store
        window.SelectTabForTesting(6); window.SelectForTesting(0); Require(!window.EditStoreVisibleForTesting, "only the owner edits a shop");
        _shopMine = true; window.SelectForTesting(-1); window.SelectForTesting(0); Require(window.EditStoreVisibleForTesting, "the owner can edit it");
        window.OpenStoreFormForTesting(true); Require(window.StoreFormOpenForTesting, "the form opens"); window.FillStoreFormForTesting("Moda da Luna 2", "Nova descrição", 3, false); window.SubmitStoreFormForTesting();
        Require(_storeCalls.Any(c => c.StartsWith("PUT ") && c.Contains("\"name\":\"Moda da Luna 2\"") && c.Contains("\"category\":\"decoracao\"") && c.Contains("\"isOpen\":false")) && !window.StoreFormOpenForTesting, "editing sends the changes: " + string.Join("|", _storeCalls));
        window.SelectForTesting(-1); window.SelectForTesting(0); window.OpenStoreFormForTesting(true); window.DeleteStoreForTesting(); Require(!_storeCalls.Contains("DELETE"), "deleting asks first"); PressDialog("Excluir loja", "Excluir"); Require(_storeCalls.Contains("DELETE") && window.NoteForTesting == "Loja excluída.", "after confirming the shop is gone: " + window.NoteForTesting); _shopMine = false;
        window.OpenStoreFormForTesting(false); window.FillStoreFormForTesting("Minha loja nova", "Vendo de tudo", 0, true); _storeError = "too_many_stores"; window.SubmitStoreFormForTesting();
        Require(window.StoreFormNoteForTesting.Contains("máximo de 3 lojas") && window.StoreFormOpenForTesting, "a limit is explained and the form stays: " + window.StoreFormNoteForTesting);
        _storeError = null; window.SubmitStoreFormForTesting(); Require(_storeCalls.Any(c => c.StartsWith("POST ") && c.Contains("\"name\":\"Minha loja nova\"") && c.Contains("\"category\":\"moda\"")) && !window.StoreFormOpenForTesting && window.NoteForTesting.StartsWith("Loja aberta"), "opening a shop sends it: " + window.NoteForTesting);
        window.SetQueryForTesting("praia"); await Frames(1);
    }
    // ---- links, history and keyboard (stage 5) ----------------------------------------------------------------------------------------

    private async Task LinksHistoryKeys(SearchWindow window)
    {
        // the shape of a link
        var id = Guid.NewGuid();
        Require(SearchLinks.For("event", id) == $"lifeverse://event/{id}" && SearchLinks.For("product", id) == $"lifeverse://item/{id}" && SearchLinks.For("parcel", id, 1, 2).StartsWith("lifeverse://island/"), "links carry the stable id");
        Require(SearchLinks.For("island", id, 12.5f, -3f) == $"lifeverse://island/{id}?x=12.5&z=-3", "a place link carries the spot, always with a dot: " + SearchLinks.For("island", id, 12.5f, -3f));
        Require(SearchLinks.TryParse($"lifeverse://island/{id}?x=12.5&z=-3", out var kind, out var parsed, out var px, out var pz) && kind == "island" && parsed == id && px == 12.5f && pz == -3f, "a link is read back");
        foreach (var bad in new[] { "http://x.com", "lifeverse://person/abc", $"lifeverse://secret/{id}", $"lifeverse://event/{id}[b]", $"javascript:lifeverse://event/{id}", $"lifeverse://event/{id}?x=1", "", new string('a', 300) })
            Require(!SearchLinks.TryParse(bad, out _, out _, out _, out _), "not a link: " + bad);
        var line = $"vem ver lifeverse://event/{id} e lifeverse://secret/{id} também";
        var linked = SearchLinks.Linkify(line);
        Require(linked.Contains($"[url=lifeverse://event/{id}]") && linked.Contains("↗ Evento") && linked.Contains($"lifeverse://secret/{id}") && !linked.Contains("[url=lifeverse://secret"), "only links of the exact shape become clickable: " + linked);
        Require(SearchLinks.Linkify("sem links aqui") == "sem links aqui", "text without links is left alone");

        // opening a link
        window.OpenLink("lixo"); Require(window.NoteForTesting == "Este link não é do LifeVerse.", "a wrong link is told");
        window.OpenLink($"lifeverse://person/{_person}"); Require(_profileOpened == _person, "a person link opens the profile"); window.OpenLink($"lifeverse://group/{_group}"); Require(_groupOpened == _group, "a group link opens the group");
        window.OpenLink($"lifeverse://event/{_event}"); Require(window.TabForTesting == 3 && window.Hits.Count == 1 && window.Hits[0].Id == _event && window.EventBoxVisibleForTesting && window.ExtraForTesting.Contains("Quando: "), "an event link shows the event, chosen, on its tab");
        window.OpenLink($"lifeverse://item/{_product}"); Require(window.TabForTesting == 5 && window.Hits.Count == 1 && window.Hits[0].Id == _product && window.ItemBoxVisibleForTesting && window.BuyEnabledForTesting, "an item link shows the item, chosen");
        window.OpenLink($"lifeverse://store/{_shop}"); Require(window.TabForTesting == 6 && window.Hits.Count == 1 && window.Hits[0].Id == _shop && window.StoreBoxVisibleForTesting, "a store link shows the store, chosen");
        window.OpenLink($"lifeverse://island/{_here}?x=5&z=6"); Require(window.TabForTesting == 2 && window.Hits.Count == 1 && window.Hits[0].Id == _here && window.Hits[0].X == 5 && window.Hits[0].Z == 6 && window.PlaceBoxVisibleForTesting, "a place link shows the island at that spot");
        window.OpenLink($"lifeverse://event/{Guid.NewGuid()}"); Require(window.NoteForTesting.Contains("não leva a nada"), "a link to something gone says so: " + window.NoteForTesting);

        // the history preference
        SearchWindow.KeepHistory = false; window.ClearHistory(); window.SetQueryForTesting("segredo"); window.SelectTabForTesting(0); window.Search();
        Require(window.RecentForTesting.Count == 0, "with the history off nothing is remembered"); window.SetQueryForTesting("segre"); window.ShowHints(["Segredo grande"]); Require(window.HintsForTesting.SequenceEqual(["Segredo grande"]), "and only the server suggests: " + string.Join("|", window.HintsForTesting));
        SearchWindow.KeepHistory = true; window.SetQueryForTesting("aberto"); window.Search(); Require(window.RecentForTesting.Contains("aberto") && !window.RecentForTesting.Contains("segredo"), "with it on again, searches are kept");

        // the keyboard
        window.SelectTabForTesting(2); window.SetQueryForTesting("praia"); window.Search(); window.SelectForTesting(-1); window.FocusQueryForTesting();
        Require(window.Hits.Count == 4, "four results to walk through");
        Require(window.PressKeyForTesting(Key.Down) && window.SelectedIndexForTesting == 0, "Down from the search box chooses the first result"); window.PressKeyForTesting(Key.Down); Require(window.SelectedIndexForTesting == 1, "and the next");
        window.PressKeyForTesting(Key.Up); window.PressKeyForTesting(Key.Up); window.PressKeyForTesting(Key.Up); Require(window.SelectedIndexForTesting == 0, "Up stops at the first");
        for (var i = 0; i < 6; i++) window.PressKeyForTesting(Key.Down); Require(window.SelectedIndexForTesting == 3, "Down stops at the last");
        var tab = window.TabForTesting; Require(window.PressKeyForTesting(Key.Tab, ctrl: true) && window.TabForTesting == tab + 1, "Ctrl+Tab goes to the next tab"); window.PressKeyForTesting(Key.Tab, ctrl: true, shift: true); Require(window.TabForTesting == tab, "Ctrl+Shift+Tab goes back");
        window.SelectTabForTesting(0); window.PressKeyForTesting(Key.Tab, ctrl: true, shift: true); Require(window.TabForTesting == 6, "the tabs wrap around");
        window.SelectTabForTesting(2); window.SetQueryForTesting("praia"); await Frames(1);
    }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try
        {
            SearchWindow.HistoryPath = "user://test_search_history.json"; DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SearchWindow.HistoryPath));
            var window = new SearchWindow { Name = "SearchWindow", Transport = Server, CurrentIsland = () => _here, JoinIsland = id => _joined = id, OpenProfile = id => _profileOpened = id, OpenGroup = id => _groupOpened = id, OpenGroupTab = (id, tab) => _groupTabs.Add((id, tab)), TeleportToPoint = (x, z) => _teleports.Add($"{x:0},{z:0}"), TeleportToParcel = id => { if (id == _parcel) { _teleports.Add("parcel"); return true; } return false; } };
            AddChild(window); await Frames(2);

            Require(window.TabsForTesting.SequenceEqual(["TUDO", "PESSOAS", "LUGARES", "EVENTOS", "GRUPOS", "ITENS", "LOJAS"]), "the seven tabs: " + string.Join("|", window.TabsForTesting));
            window.Open(); await Frames(2);
            Require(window.Visible && window.TabForTesting == 0, "opens on Tudo");
            Require(window.Hits.Select(h => h.Title).SequenceEqual(["Praia Grande", "Praia Distante", "Farol Novo", "Estudio de Musica"]), $"the start screen: popular, news (no repeats) and for you: {string.Join(",", window.Hits.Select(h => h.Title))}");
            Require(window.HeadersForTesting.Contains("Lugares em destaque") && window.HeadersForTesting.Contains("Novidades") && window.HeadersForTesting.Contains("Para você"), "the start screen sections: " + string.Join("|", window.HeadersForTesting));
            Require(window.InterestsForTesting == "musica" && window.DetailsEmptyVisibleForTesting, "interests are shown and nothing is selected yet");

            window.SetQueryForTesting("x"); window.Search(); Require(window.NoteForTesting.Contains("pelo menos 2"), "one letter is not a search"); Require(!_calls.Any(c => c.Contains("q=x")), "nothing was sent for it");

            // Tudo: results split by kind
            window.SetQueryForTesting("praia"); window.Search();
            Require(_calls.Contains("GET /api/v1/search?q=praia&page=1&pageSize=50&sort=relevance"), "the global search path");
            Require(window.Hits.Select(h => h.Title).SequenceEqual(["Praia Pessoa", "Praia Grande", "Praia Distante", "Lote da Praia"]) && window.HeadersForTesting.SequenceEqual(["Pessoas", "Lugares"]), "grouped by kind: " + string.Join(",", window.Hits.Select(h => h.Title)));
            Require(window.CountForTesting == "45 resultados" && window.NoteForTesting.StartsWith("45 resultado"), "the total is told");
            Require(window.CategoriesForTesting.SequenceEqual(["Todas as categorias", "social", "negocios"]), "the categories found become the filter");
            window.PressSeeAllForTesting("Lugares"); Require(window.TabForTesting == 2 && LastSearch.Contains("types=island%2Cparcel%2Cpoi") && LastSearch.Contains("q=praia"), "Ver todos opens the tab keeping the words: " + LastSearch);

            // one tab: its own kinds, paging, order and category
            Require(window.Hits.Count == 4 && window.PagerVisibleForTesting, "45 results need paging"); window.NextPageForTesting(); Require(LastSearch.Contains("page=2") && LastSearch.Contains("q=praia"), "the next page is the same search: " + LastSearch);
            window.SetSortForTesting(1); window.Search(); Require(LastSearch.Contains("sort=popularity"), "the order is sent: " + LastSearch);
            window.SetCategoryForTesting("negocios"); window.Search(); Require(LastSearch.Contains("category=negocios"), "category is sent: " + LastSearch);
            window.SetSortForTesting(0); window.SetCategoryForTesting("Todas as categorias"); window.Search();

            // hints: the search made before comes back first, then what the server suggests
            window.SetQueryForTesting("pra"); window.ShowHints(["Praia Grande", "Praia Distante"]);
            Require(window.HintsForTesting.SequenceEqual(["praia", "Praia Grande", "Praia Distante"]), "hints mix history and suggestions: " + string.Join("|", window.HintsForTesting));
            window.ClearHistory(); window.ShowHints(); Require(window.HintsForTesting.Count == 0, "the history can be cleared");

            // preview and going there
            window.SetQueryForTesting("praia"); window.Search();
            Require(window.DetailsEmptyVisibleForTesting, "with nothing chosen the details ask for a choice");
            window.SelectForTesting(0); Require(!window.DetailsEmptyVisibleForTesting && window.PreviewTitleForTesting == "Praia Grande" && window.PreviewWhereForTesting.StartsWith("Nesta ilha") && window.GoTextForTesting == "Teleportar até lá", "a result on this island: " + window.PreviewWhereForTesting);
            window.GoToResult(); Require(_teleports.SequenceEqual(["10,20"]) && _joined == Guid.Empty, "teleports to the spot without leaving the island");
            window.SelectForTesting(3); window.GoToResult(); Require(_teleports.Last() == "parcel", "a plot on this island goes through the plot teleport");
            window.SelectForTesting(1); Require(window.PreviewWhereForTesting.StartsWith("Em outra ilha") && window.GoTextForTesting == "Ir para a ilha", "a result on another island: " + window.PreviewWhereForTesting);
            window.GoToResult(); Require(_joined == _there && SearchTravel.HasPending, "joins the island and keeps the spot for when it arrives");
            Require(SearchTravel.TakeFor(Guid.NewGuid()) is null && !SearchTravel.HasPending, "arriving somewhere else forgets the spot");
            window.GoToResult(); Require(SearchTravel.TakeFor(_there) is { X: 30, Z: 40 }, "arriving at the island gives the spot back");
            var parcelTarget = Guid.NewGuid(); SearchTravel.SetParcel(_there, parcelTarget);
            Require(SearchTravel.HasPending && SearchTravel.TakeParcelFor(Guid.NewGuid()) is null && !SearchTravel.HasPending, "arriving somewhere else forgets the parcel");
            SearchTravel.SetParcel(_there, parcelTarget); Require(SearchTravel.TakeParcelFor(_there) == parcelTarget && SearchTravel.TakeParcelFor(_there) is null, "arriving at the island gives the parcel back, once");
            SearchTravel.SetParcel(_there, parcelTarget); SearchTravel.Set(_there, 1, 2); Require(SearchTravel.TakeParcelFor(_there) is null && SearchTravel.TakeFor(_there) is { X: 1, Z: 2 }, "a newer spot replaces the parcel");
            window.SelectForTesting(2); Require(!window.GoEnabledForTesting && window.ProfileVisibleForTesting, "a person has no place to go, only a profile");
            window.PressProfileForTesting(); Require(_profileOpened == _person, "Ver perfil opens that profile");
            window.PressCardActionForTesting(2); Require(_profileOpened == _person, "the button on the card of a person opens the profile too");
            Require(_calls.Any(c => c == "POST /api/v1/search/events"), "opening and going are counted");

            // favorites
            window.SelectForTesting(0); window.FavoriteForTesting();
            Require(_calls.Last() == $"PUT /api/v1/search/favorites/island/{_here}" && window.FavoriteTextForTesting == "Remover dos favoritos" && window.RowTextForTesting(0).StartsWith("★ "), "favoriting marks the row");
            window.FavoriteForTesting(); Require(_calls.Last().StartsWith("DELETE ") && window.FavoriteTextForTesting == "Favoritar", "and unmarks it");
            _favoritesFull = true; window.FavoriteForTesting(); Require(window.NoteForTesting.Contains("cheia"), "a full list is explained"); _favoritesFull = false;

            // report
            window.SelectForTesting(1); window.ReportForTesting("ab"); Require(window.NoteForTesting.Contains("motivo"), "a report needs a reason");
            window.ReportForTesting("conteudo ofensivo"); Require(_calls.Last() == $"POST /api/v1/search/report/island/{_there}" && window.NoteForTesting.StartsWith("Denúncia enviada"), "the report is sent: " + window.NoteForTesting);

            await PeopleAndGroups(window); await PlacesAndEvents(window); await ItemsAndStores(window); await LinksHistoryKeys(window); window.SelectTabForTesting(2); window.SetQueryForTesting("praia"); window.Search(); window.SetOnlyFavoritesForTesting(false);

            // only favorites
            window.SetOnlyFavoritesForTesting(true); window.Search(); Require(LastSearch.StartsWith("GET /api/v1/search/favorites?") && window.Hits.Count == 1 && window.NoteForTesting == "1 favorito(s).", "only favorites: " + LastSearch);
            window.SelectForTesting(0); window.FavoriteForTesting(); Require(window.Hits.Count == 1 && _calls.Any(c => c.StartsWith("DELETE ")), "removing a favorite reloads the list");
            window.SetOnlyFavoritesForTesting(false);

            // the other tabs: browsing with nothing typed, and people only by name
            window.SetQueryForTesting(""); window.SelectTabForTesting(3); Require(LastSearch.Contains("types=event") && LastSearch.Contains("q=&"), "Eventos with nothing typed lists the most popular: " + LastSearch);
            var before = _calls.Count; window.SelectTabForTesting(1); Require(_calls.Count == before && window.StateForTesting.Contains("Digite um nome") && window.Hits.Count == 0, "Pessoas asks for a name and sends nothing");
            window.SetQueryForTesting("pra"); window.Search(); Require(LastSearch.Contains("types=person") && LastSearch.Contains("q=pra"), "people are searched by name: " + LastSearch);

            // start screen: interests
            window.SetQueryForTesting(""); window.SelectTabForTesting(0);
            window.SetInterestsForTesting("Arte, Pesca"); Require(window.InterestsForTesting == "arte, pesca" && _calls.Any(c => c == "PUT /api/v1/search/interests"), "interests are saved and cleaned by the server");

            // a group opens through its own window
            window.SetQueryForTesting("grupo"); window.SelectTabForTesting(4); window.Search();

            // loading, cancelling and failing
            window.SelectTabForTesting(0); window.SetQueryForTesting("lago"); _hang = true; window.Search();
            Require(window.GoButtonLabelForTesting == "Cancelar" && window.NoteForTesting == "Buscando…" && window.StateForTesting == "Buscando…", "while waiting the button says Cancelar and the list says so");
            var sent = _calls.Count; window.Search(); Require(_calls.Count == sent, "the same search is not sent twice while it waits");
            window.PressGoForTesting(); Require(window.GoButtonLabelForTesting == "Buscar" && window.NoteForTesting == "Busca cancelada.", "cancelling stops the search"); _hang = false;
            _down = true; window.Search(); Require(window.NoteForTesting == "Sem conexão com o servidor." && window.Hits.Count == 0 && window.StateForTesting == "Sem conexão com o servidor.", "no connection is said plainly");
            _down = false; window.PressRetryForTesting(); Require(window.Hits.Count == 4 && window.StateForTesting != "Sem conexão com o servidor.", "Tentar novamente runs the search again");
            window.SetQueryForTesting("nada-aqui"); window.SelectTabForTesting(0);

            // narrow window: one column, results or details
            window.SetQueryForTesting("praia"); window.Search(); window.SelectTabForTesting(2); window.WindowForTesting.Size = new Vector2(1100, 700); await Frames(3);
            Require(!window.NarrowForTesting && window.ResultsShownForTesting && window.DetailsShownForTesting, "a wide window shows both columns");
            window.WindowForTesting.Size = new Vector2(560, 700); await Frames(4);
            Require(window.NarrowForTesting && window.ResultsShownForTesting && !window.DetailsShownForTesting, "a narrow window shows the results first");
            window.SelectForTesting(0); await Frames(2); Require(!window.ResultsShownForTesting && window.DetailsShownForTesting, "choosing one shows its details");
            window.WindowForTesting.Size = new Vector2(1100, 700); await Frames(4); Require(window.ResultsShownForTesting && window.DetailsShownForTesting, "growing the window brings both columns back");

            // history file survives a reload
            var again = new SearchWindow { Name = "SearchWindow2", Transport = Server }; AddChild(again); again.SetQueryForTesting("pr"); again.ShowHints();
            Require(again.HintsForTesting.Contains("praia"), "the history on disk has what was searched: " + string.Join("|", again.HintsForTesting));

            // the answers are read defensively
            Require(SearchApi.ParsePage("not json").Total == 0 && SearchApi.ParseList("{}").Count == 0 && SearchApi.ParseSuggestions("[1,2]").Count == 0, "bad answers are empty");
            Require(!SearchApi.IsLoadableImageUrl("file:///c:/x.png") && !SearchApi.IsLoadableImageUrl("https://u:p@host/x.png") && SearchApi.IsLoadableImageUrl("https://cdn.exemplo.com/x.png"), "only plain http(s) thumbnails");

            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SearchWindow.HistoryPath));
            GD.Print("event=search_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            var where = exception.StackTrace?.Split((char)10).Where(l => l.Contains("Search")).Take(3).Aggregate("", (a, l) => a + l.Trim() + " ; ");
            GD.PushError($"event=search_smoke result=failed error={exception.Message} at {where}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
