using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Busca", as in the Godot client, on the platform's own search service (<c>/api/v1/search</c>): the tabs Tudo, Pessoas, Lugares, Eventos, Grupos, Itens and Lojas, plus Para você (recommended, popular and recent
    /// places, the person's interests, their last searches, and opening a link of the game) and Favoritos. Suggestions come while typing; items can be filtered by price, creator and rights. A result opens its details:
    /// go there, see the profile or the group, join the event, favourite it, copy its link (<c>lifeverse://…</c>) or report it.
    /// </summary>
    public sealed class GlobalSearchWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("paravoce", "Para você"), ("tudo", "Tudo"), ("pessoas", "Pessoas"), ("lugares", "Lugares"), ("eventos", "Eventos"), ("grupos", "Grupos"), ("itens", "Itens"), ("lojas", "Lojas"), ("favoritos", "Favoritos"),
        };
        public static readonly (string Id, string Text)[] Sorts = { ("relevance", "Mais relevantes"), ("popularity", "Mais populares"), ("updated", "Mais recentes"), ("name", "Nome") };
        public static readonly (string Id, string Text)[] EventSorts = { ("start", "Começam antes"), ("relevance", "Mais relevantes"), ("popularity", "Mais populares") };
        public static readonly (string Id, string Text)[] ItemSorts = { ("relevance", "Mais relevantes"), ("price_asc", "Mais baratos"), ("price_desc", "Mais caros"), ("popularity", "Mais populares"), ("updated", "Mais recentes") };
        public static readonly (string Id, string Text)[] RightsFilters = { ("", "Quaisquer direitos"), ("copy", "Pode copiar"), ("modify", "Pode modificar"), ("transfer", "Pode transferir"), ("copy,modify,transfer", "Todos os direitos") };
        public const int MaxHistory = 10, PageSize = 20;
        private readonly UserSettings _settings; private readonly Action _save; private readonly Action<Guid, float?, float?> _go;
        private string _query = "", _sort = "", _rights = "", _minPrice = "", _maxPrice = "", _creator = ""; private bool _free;
        private Guid _ownerFilter; private string _ownerName = ""; private bool _keepOwner; private Action _back;
        /// <summary>The owner whose products the Itens tab is showing (empty: everybody's).</summary>
        public Guid OwnerFilter => _ownerFilter;
        private VisualElement _suggestions; private IVisualElementScheduledItem _typing;
        /// <summary>The suggestions on screen (for the tests).</summary>
        public readonly List<string> Suggested = new List<string>();
        private readonly Dictionary<Guid, (JsonElement Item, Action Back)> _shown = new Dictionary<Guid, (JsonElement, Action)>();
        /// <summary>Opens the details of a result on screen (as its Detalhes button does).</summary>
        public bool OpenResult(Guid id) { if (!RowIds.Contains(id) || !_shown.TryGetValue(id, out var shown)) return false; Detail(shown.Item, shown.Back); return true; }
        /// <summary>The result whose details are open, or empty.</summary>
        public Guid Opened { get; private set; }

        public GlobalSearchWindow(ApiContext context, UserSettings settings, Action save = null, Action<Guid, float?, float?> go = null) : base(context, "global-search", "Busca", Sections, 820f, 560f, new Vector2(220, 70), 150f)
        {
            _settings = settings ?? new UserSettings(); _save = save; _go = go; Show("paravoce");
            // the arrows walk the results, Enter opens the one marked
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (_rows.Count == 0 || Opened != Guid.Empty) return;
                if (e.keyCode == KeyCode.DownArrow) { Mark(Marked + 1); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.UpArrow) { Mark(Marked - 1); e.StopPropagation(); }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Marked >= 0 && !(e.target is TextField || (e.target as VisualElement)?.parent is TextField)) { var row = _rows[Marked]; Detail(row.Item, row.Back); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
        }

        private readonly List<(VisualElement Row, JsonElement Item, Action Back)> _rows = new List<(VisualElement, JsonElement, Action)>();
        /// <summary>The result marked by the arrows (-1 none).</summary>
        public int Marked { get; private set; } = -1;
        public int ResultRows => _rows.Count;

        /// <summary>Marks a result (kept inside the list) and brings it into view.</summary>
        public void Mark(int index)
        {
            if (_rows.Count == 0) { Marked = -1; return; } if (Marked >= 0 && Marked < _rows.Count) _rows[Marked].Row.RemoveFromClassList("lv-marked");
            Marked = Mathf.Clamp(index, 0, _rows.Count - 1); var row = _rows[Marked].Row; row.AddToClassList("lv-marked"); _page.ScrollTo(row);
        }

        // ---- the small pictures of the results: an image of the API or an asset; otherwise the icon of the kind ----
        private static readonly Dictionary<string, Texture2D> Thumbs = new Dictionary<string, Texture2D>();
        public int ThumbnailsShown { get; private set; }

        public static string IconOfKind(string kind) => kind switch { "person" => "user", "island" => "globe", "parcel" => "pin", "event" => "calendar", "group" => "people", "product" or "item" => "bag", "store" => "home", _ => "search" };

        /// <summary>Where the picture of a result is fetched from (null when it is not one this game fetches: only this API's images and assets, never another site).</summary>
        public static string ThumbPath(string reference, Guid island, Guid group = default)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            // a group keeps its picture as asset://…; the platform hands it out by the group, to who may see it
            if (reference.StartsWith("asset://", StringComparison.OrdinalIgnoreCase)) return group != Guid.Empty ? "/api/v1/groups/" + group + "/image" : null;
            if (reference.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(reference.Substring(6), out var asset)) return island != Guid.Empty ? "/api/v1/worlds/islands/" + island + "/assets/" + asset + "/file" : "/api/v1/assets/" + asset + "/download";
            return reference.StartsWith("/api/v1/", StringComparison.Ordinal) && !reference.Contains("..") ? reference : null;
        }

        /// <summary>The pictures already fetched are asked for again (after a photo, a group's picture or an island's changed).</summary>
        public static void ForgetPictures() => Thumbs.Clear();

        private VisualElement Thumb(JsonElement item)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore }; box.style.width = 40; box.style.height = 40; box.style.flexShrink = 0; box.style.marginRight = 8; box.AddToClassList("lv-thumb");
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 8; box.style.alignItems = Align.Center; box.style.justifyContent = Justify.Center;
            var icon = new Icon(IconOfKind(S(item, "kind")), 22); box.Add(icon);
            var island = G(item, "islandId"); var path = ThumbPath(S(item, "thumbnailUrl"), island != Guid.Empty ? island : S(item, "kind") == "island" ? G(item, "id") : Guid.Empty, S(item, "kind") == "group" ? G(item, "id") : Guid.Empty);
            if (path == null) return box;
            void Put(Texture2D t) { box.style.backgroundImage = new StyleBackground(t); box.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; icon.style.display = DisplayStyle.None; ThumbnailsShown++; }
            if (Thumbs.TryGetValue(path, out var have)) { if (have != null) Put(have); return box; }
            this.Then(_c.Api.GetBytesAsync(path), r =>
            {
                Texture2D texture = null; if (r.Status == 200 && r.Bytes != null && r.Bytes.Length > 8) { texture = new Texture2D(2, 2); if (!LifeVerse.Unity.World.Pictures.Load(texture, r.Bytes)) { UnityEngine.Object.Destroy(texture); texture = null; } }
                if (Thumbs.Count > 120) { foreach (var t in Thumbs.Values) if (t != null) UnityEngine.Object.Destroy(t); Thumbs.Clear(); }
                Thumbs[path] = texture; if (texture != null) Put(texture);
            });
            return box;
        }

        protected override void Fill(string section)
        {
            Opened = Guid.Empty; _summary.text = ""; if (!_keepOwner) _ownerFilter = Guid.Empty;
            if (section == "paravoce") ForYou(); else if (section == "favoritos") Favorites(); else Results(section);
        }

        // ---- the pieces that can be tested on their own ----

        /// <summary>The kinds each tab searches (none = everything), as in the Godot client.</summary>
        public static string[] TypesOf(string tab) => tab switch
        {
            "pessoas" => new[] { "person" }, "lugares" => new[] { "island", "parcel", "poi" }, "eventos" => new[] { "event" }, "grupos" => new[] { "group" }, "itens" => new[] { "product" }, "lojas" => new[] { "store" }, _ => new string[0],
        };

        public static string KindWords(string kind) => kind switch { "island" => "Ilha", "parcel" => "Terreno", "person" => "Pessoa", "group" => "Grupo", "store" => "Loja", "product" => "Produto", "event" => "Evento", "poi" => "Ponto de interesse", _ => kind };

        /// <summary>The address of a search: text, kinds, page, order and the filters of items and events.</summary>
        public static string SearchPath(string q, string[] types, int page, string sort, string minPrice = "", string maxPrice = "", string rights = "", string creator = "", bool free = false, Guid owner = default)
        {
            var path = "/api/v1/search?page=" + Math.Max(1, page) + "&pageSize=" + PageSize;
            if (!string.IsNullOrWhiteSpace(q)) path += "&q=" + Uri.EscapeDataString(q.Trim()); if (types != null && types.Length > 0) path += "&types=" + string.Join(",", types);
            if (!string.IsNullOrEmpty(sort)) path += "&sort=" + sort; if (long.TryParse((minPrice ?? "").Trim(), out var min) && min >= 0) path += "&minPrice=" + min; if (long.TryParse((maxPrice ?? "").Trim(), out var max) && max >= 0) path += "&maxPrice=" + max;
            if (!string.IsNullOrEmpty(rights)) path += "&rights=" + rights; if (!string.IsNullOrWhiteSpace(creator)) path += "&creator=" + Uri.EscapeDataString(creator.Trim()); if (free) path += "&free=true";
            if (owner != Guid.Empty) path += "&owner=" + owner;
            return path;
        }

        /// <summary>Puts a search at the top of the history (no repeats, no blanks, at most <see cref="MaxHistory"/>).</summary>
        public static List<string> Remember(List<string> history, string query)
        {
            var list = (history ?? new List<string>()).Where(h => !string.IsNullOrWhiteSpace(h)).ToList(); var q = (query ?? "").Trim(); if (q.Length == 0) return list;
            list.RemoveAll(h => string.Equals(h, q, StringComparison.OrdinalIgnoreCase)); list.Insert(0, q.Length > 120 ? q.Substring(0, 120) : q); return list.Take(MaxHistory).ToList();
        }

        public const string Scheme = "lifeverse://";
        private static readonly Regex LinkPattern = new Regex(@"^lifeverse://(person|group|event|item|store|island)/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:\?x=(-?\d{1,6}(?:\.\d{1,2})?)&z=(-?\d{1,6}(?:\.\d{1,2})?))?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        /// <summary>The link of the game for a result (the same shape as the Godot client): it carries the stable id, never a name.</summary>
        public static string LinkOf(string kind, Guid id, float? x = null, float? z = null) => kind switch
        {
            "person" => Scheme + "person/" + id, "group" => Scheme + "group/" + id, "event" => Scheme + "event/" + id, "product" => Scheme + "item/" + id, "store" => Scheme + "store/" + id,
            _ => Scheme + "island/" + id + (x is { } px && z is { } pz ? string.Format(CultureInfo.InvariantCulture, "?x={0:0.#}&z={1:0.#}", px, pz) : ""),
        };

        /// <summary>Reads a link of the game: only that exact shape, so a pasted link can only open one of our own panels.</summary>
        public static bool TryParseLink(string link, out string kind, out Guid id, out float? x, out float? z)
        {
            kind = ""; id = Guid.Empty; x = z = null; if (string.IsNullOrWhiteSpace(link) || link.Length > 200) return false;
            Match m; try { m = LinkPattern.Match(link.Trim()); } catch (RegexMatchTimeoutException) { return false; }
            if (!m.Success || !Guid.TryParse(m.Groups[2].Value, out id)) return false; kind = m.Groups[1].Value;
            if (m.Groups[3].Success && float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && float.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pz)) { x = px; z = pz; }
            return true;
        }

        /// <summary>The line under a result: its kind, what it is and when (events) or how much (items).</summary>
        public static string Describe(JsonElement item)
        {
            var parts = new List<string> { S(item, "kindLabel").Length > 0 ? S(item, "kindLabel") : KindWords(S(item, "kind")) };
            if (S(item, "subtitle").Length > 0) parts.Add(S(item, "subtitle")); if (S(item, "category").Length > 0) parts.Add(S(item, "category"));
            if (Date(item, "startsAt") is { } start) parts.Add("começa " + When(start)); if (Has(item, "priceCoins")) parts.Add(I(item, "priceCoins") == 0 ? "grátis" : Coins(I(item, "priceCoins")));
            if (S(item, "rating").Length > 0 && S(item, "rating") != "general") parts.Add(S(item, "rating") == "mature" ? "adulto" : S(item, "rating"));
            return string.Join(" · ", parts);
        }

        /// <summary>A refusal of the search service in words.</summary>
        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            if (status == 429) return "Denúncias demais seguidas; espere um pouco.";
            return code switch
            {
                "own_item" => "Você não pode denunciar o que é seu.", "reason_required" => "Diga o motivo da denúncia.", "favorites_full" => "Seus favoritos estão cheios: tire algum antes.", "unknown_kind" => "Esse tipo de resultado não pode ir para os favoritos.",
                "not_found" => "O resultado não existe mais.", _ => UiTasks.Explain(status, body),
            };
        }

        // ---- the search bar and the suggestions ----

        /// <summary>Searches everything for a text (what was written on the navigation bar of the game).</summary>
        public void Find(string text) { _query = (text ?? "").Trim(); Show("tudo"); }

        private void Bar(string tab)
        {
            var query = Field("Buscar", _query, 120); _suggestions = new VisualElement(); _page.Add(_suggestions);
            query.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { _query = query.value; Results(tab); } }, TrickleDown.TrickleDown);
            query.RegisterValueChangedCallback(e => { _typing?.Pause(); var text = e.newValue; _typing = schedule.Execute(() => Suggest(text)); _typing.ExecuteLater(300); });
            var sorts = tab == "eventos" ? EventSorts : tab == "itens" ? ItemSorts : Sorts; var sort = Choice("Ordem", sorts.ToList(), sorts.Any(s => s.Id == _sort) ? _sort : sorts[0].Id);
            TextField min = null, max = null, creator = null; DropdownField rights = null; Toggle free = null;
            if (tab == "itens") { min = Field("Preço mínimo (VC)", _minPrice, 9); max = Field("Preço máximo (VC)", _maxPrice, 9); creator = Field("Criador", _creator, 40); rights = Choice("Direitos", RightsFilters.ToList(), _rights); }
            if (tab == "eventos") free = Check("Só gratuitos", _free);
            Buttons(Btn("Buscar", () => { _query = query.value; _sort = IdOf(sort); if (min != null) { _minPrice = min.value; _maxPrice = max.value; _creator = creator.value; _rights = IdOf(rights); } if (free != null) _free = free.value; Results(tab); }, "lv-btn-primary"),
                Btn("Limpar", () => { _query = ""; _sort = ""; _minPrice = _maxPrice = _creator = _rights = ""; _free = false; _ownerFilter = Guid.Empty; Results(tab); }));
        }

        /// <summary>Asks for suggestions for what is being typed and shows them as buttons that search for them.</summary>
        public void Suggest(string text)
        {
            if (_suggestions == null) return; var q = (text ?? "").Trim(); if (q.Length < 2) { _suggestions.Clear(); Suggested.Clear(); return; }
            Get("/api/v1/search/suggest?take=6&q=" + Uri.EscapeDataString(q), list =>
            {
                _suggestions.Clear(); Suggested.Clear(); var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
                foreach (var s in Items(list)) { var t = S(s, "text"); if (t.Length == 0) continue; Suggested.Add(t); var tab = Section; row.Add(UiKit.Button(t + "  (" + KindWords(S(s, "kind")).ToLowerInvariant() + ")", () => { _query = t; Results(tab); }, "lv-btn-flat")); }
                _suggestions.Add(row);
            });
        }

        // ---- results ----

        private void Results(string tab, int page = 1)
        {
            Reset(); Opened = Guid.Empty; Bar(tab);
            if (tab == "itens" && _ownerFilter != Guid.Empty) { Line("Só os produtos de " + _ownerName + ".", "lv-small", "lv-accent"); Buttons(Btn("Ver todos os itens", () => { _ownerFilter = Guid.Empty; Results(tab); })); }
            if (_query.Trim().Length == 0 && tab == "tudo") { Line("Digite o que procurar: pessoas, ilhas, terrenos, eventos, grupos, produtos ou lojas.", "lv-small", "lv-muted"); return; }
            if (page == 1 && _query.Trim().Length > 0 && _settings.KeepSearchHistory) { _settings.RecentSearches = Remember(_settings.RecentSearches, _query); _save?.Invoke(); }
            var sort = _sort; var path = SearchPath(_query, TypesOf(tab), page, sort, tab == "itens" ? _minPrice : "", tab == "itens" ? _maxPrice : "", tab == "itens" ? _rights : "", tab == "itens" ? _creator : "", tab == "eventos" && _free, tab == "itens" ? _ownerFilter : Guid.Empty);
            Get(path, root =>
            {
                var counts = Obj(root, "counts"); var total = I(root, "total");
                _summary.text = total + (total == 1 ? " resultado" : " resultados") + (_query.Trim().Length > 0 ? " para “" + _query.Trim() + "”" : "") + (counts.ValueKind == JsonValueKind.Object ? "  ·  " + string.Join(", ", counts.EnumerateObject().Where(c => c.Value.ValueKind == JsonValueKind.Number && c.Value.GetInt32() > 0).Select(c => c.Value.GetInt32() + " " + KindWords(c.Name).ToLowerInvariant())) : "");
                Draw(Arr(root, "items"), () => Results(tab, page)); Pager(root, p => Results(tab, p));
                if (Arr(root, "items").Count == 0) Line("Nada encontrado. Tente outras palavras ou outra aba.", "lv-small", "lv-muted");
            });
        }

        private void Draw(List<JsonElement> items, Action back)
        {
            _rows.Clear(); Marked = -1; ThumbnailsShown = 0;
            foreach (var item in items)
            {
                var id = G(item, "id"); var it = item; _shown[id] = (item, back);
                var row = Row(id, Thumb(item), UiKit.Text((Flag(item, "favorite") ? "★ " : "") + S(item, "title"), "lv-bold"), UiKit.Text(Describe(item), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Detalhes", () => Detail(it, back)));
                _rows.Add((row, item, back));
            }
        }

        // ---- the details of a result ----

        private void Detail(JsonElement item, Action back)
        {
            Reset(); var kind = S(item, "kind"); var id = G(item, "id"); var title = S(item, "title"); Opened = id; FactsText = ""; Access = ""; Relation = ""; Bought = false; _back = back;
            Signal("open");
            Buttons(Btn("Voltar aos resultados", back)); Title(title); Line(Describe(item), "lv-small", "lv-muted");
            if (S(item, "description").Length > 0) Line(S(item, "description"));
            if (Date(item, "endsAt") is { } end) Line("Termina " + When(end), "lv-small");
            var island = G(item, "islandId"); float? x = Has(item, "x") ? (float)D(item, "x") : (float?)null; float? z = Has(item, "z") ? (float)D(item, "z") : (float?)null;
            var actions = new List<VisualElement>();
            var place = kind == "island" ? id : island;
            Button go = null;
            if (place != Guid.Empty && kind != "person" && kind != "group") actions.Add(go = Btn(kind == "store" ? "Visitar a loja" : "Ir até lá", () => { Signal("teleport"); Say("Indo para " + title + "…", false); if (_go != null) _go(place, x, z); else _c.EnterIsland(place); }, "lv-btn-primary"));
            var facts = new VisualElement(); _page.Add(facts);
            if (place != Guid.Empty && kind != "person" && kind != "group") PlaceInfo(place, go, facts);
            if (kind == "event") EventCalendar(id, facts, actions);
            if (kind == "product") ItemDetail(id, facts);
            if (kind == "person") { var relation = UiKit.Text("", "lv-small", "lv-accent"); _page.Add(relation); this.Then(_c.Api.ApiAsync("GET", "/api/v1/social/relations?ids=" + id), r => { if (r.Status == 200) try { using var d = JsonDocument.Parse(r.Body); if (d.RootElement.TryGetProperty(id.ToString(), out var rel) && rel.ValueKind == JsonValueKind.String) { Relation = rel.GetString(); relation.text = RelationWords(Relation); } } catch (JsonException) { } }); actions.Add(Btn("Ver perfil", () => new ProfileHubWindow(_c, id).Open(), "lv-btn-primary")); actions.Add(Btn("Ações com a pessoa…", () => new PersonActionsWindow(_c, id, title).Open())); }
            if (kind == "group") actions.Add(Btn("Ver grupo", () => new GroupProfileWindow(_c, id).Open(), "lv-btn-primary"));
            if (kind == "event") actions.Add(Btn("Participar", () => Send("POST", "/api/v1/discovery/events/" + id + "/join", "{}", "Você vai participar de " + title + ".")));
            if (kind == "store") StoreDetail(id, facts);
            var favorite = Flag(item, "favorite");
            actions.Add(Btn(favorite ? "Tirar dos favoritos" : "Favoritar", () => Send(favorite ? "DELETE" : "PUT", "/api/v1/search/favorites/" + kind + "/" + id, favorite ? null : "{}", favorite ? "Tirado dos favoritos." : "Nos favoritos.", null, Words)));
            actions.Add(Btn("Copiar o link", () => { GUIUtility.systemCopyBuffer = LinkOf(kind, id, x, z); Say("Link copiado: " + LinkOf(kind, id, x, z), false); }));
            Buttons(actions.ToArray());
            Title("Denunciar este resultado"); var reason = Field("Motivo", "", 300);
            Buttons(Sure("Enviar denúncia", () => "Denunciar " + title + "?", () => { if (reason.value.Trim().Length < 3) { Say("Diga o motivo da denúncia.", true); return; } Send("POST", "/api/v1/search/report/" + kind + "/" + id, new { reason = reason.value.Trim() }, "Denúncia enviada. Obrigado.", null, Words); }));
        }

        // ---- what the discovery knows of a place, an event and an item ----

        /// <summary>The access to the island now ("open", "full", "closed", "private", "banned", "gone"), from <c>/discovery/islands/{id}/info</c>.</summary>
        public string Access { get; private set; } = "";
        public bool GuestEvents { get; private set; }
        public bool InCalendar { get; private set; }
        public bool CanBuy { get; private set; }
        public string FactsText { get; private set; } = "";

        public static string AccessWords(string access) => access switch { "full" => "Ilha cheia no momento: tente de novo daqui a pouco.", "closed" => "Ilha temporariamente fechada.", "banned" => "Você foi banido desta ilha.", "private" => "Ilha privada: só convidados entram.", "gone" => "Este lugar não está mais disponível.", _ => "" };
        public static string AccessButton(string access) => access switch { "full" => "Ilha cheia", "closed" => "Ilha fechada", "banned" => "Acesso negado", "private" => "Ilha privada", _ => "Indisponível" };

        private void Facts(VisualElement area, IEnumerable<string> lines) { foreach (var l in lines.Where(t => !string.IsNullOrWhiteSpace(t))) { var label = UiKit.Text(l, "lv-small"); label.style.whiteSpace = WhiteSpace.Normal; area.Add(label); FactsText += l + "\n"; } }

        private void PlaceInfo(Guid island, Button go, VisualElement area) => this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/islands/" + island + "/info"), r =>
        {
            if (r.Status == 404) { Access = "gone"; Facts(area, new[] { AccessWords("gone") }); if (go != null) { go.text = AccessButton("gone"); go.SetEnabled(false); } return; }
            if (r.Status != 200) return;   // not known: the server decides when entering
            using var d = JsonDocument.Parse(r.Body); var p = d.RootElement; Access = S(p, "access");
            var owner = Obj(p, "owner"); var visitors = I(p, "visitors"); var max = I(p, "maxPlayers");
            Facts(area, new[]
            {
                S(owner, "displayName").Length > 0 ? "Proprietário(a): " + S(owner, "displayName") + "." : "",
                string.Join(" · ", new[] { S(p, "category"), S(p, "contentRating").Length > 0 ? "classificação " + S(p, "contentRating") : "", S(p, "language") }.Where(t => t.Length > 0)),
                Flag(p, "online") ? visitors + (visitors == 1 ? " visitante" : " visitantes") + " agora" + (max > 0 ? " (máximo " + max + ")" : "") + "." : "A ilha está desligada: ela liga quando alguém entra.",
                AccessWords(Access),
            });
            if (go != null && Access.Length > 0 && Access != "open") { go.text = AccessButton(Access); go.SetEnabled(false); }
            if (Flag(p, "isOwner"))
            {
                GuestEvents = Flag(p, "allowsGuestEvents"); Button toggle = null;
                toggle = Btn(GuestEvents ? "Eventos de convidados: permitidos" : "Eventos de convidados: bloqueados", () =>
                {
                    var wanted = !GuestEvents;
                    this.Then(_c.Api.ApiAsync("PUT", "/api/v1/discovery/islands/" + island + "/event-policy", JsonSerializer.Serialize(new { allowGuests = wanted })), w =>
                    {
                        if (w.Status < 200 || w.Status >= 300) { Say(w.Status == 404 ? "Só o dono da ilha decide isso." : UiTasks.Explain(w.Status, w.Body), true); return; }
                        GuestEvents = wanted; toggle.text = wanted ? "Eventos de convidados: permitidos" : "Eventos de convidados: bloqueados"; Say(wanted ? "Agora convidados podem anunciar eventos nesta ilha." : "Só você anuncia eventos nesta ilha.", false);
                    });
                });
                Actions["Eventos de convidados"] = Actions[toggle.text]; area.Add(toggle);
            }
        });

        private void EventCalendar(Guid id, VisualElement area, List<VisualElement> actions) => this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/events/" + id), r =>
        {
            if (r.Status != 200) return;
            using var d = JsonDocument.Parse(r.Body); var e = d.RootElement; InCalendar = Flag(e, "inCalendar"); var status = S(e, "status"); var closed = status is "ended" or "cancelled";
            Facts(area, new[] { Flag(e, "joined") ? "Você vai participar." : "", closed ? (status == "ended" ? "Este evento já terminou." : "Este evento foi cancelado.") : "" });
            Button calendar = null;
            calendar = Btn(InCalendar ? "Remover do calendário" : "Adicionar ao calendário", () =>
            {
                var on = !InCalendar;
                this.Then(_c.Api.ApiAsync("PUT", "/api/v1/discovery/events/" + id + "/calendar", JsonSerializer.Serialize(new { on })), w =>
                {
                    if (w.Status < 200 || w.Status >= 300) { Say(UiTasks.Explain(w.Status, w.Body), true); return; }
                    InCalendar = on; calendar.text = on ? "Remover do calendário" : "Adicionar ao calendário"; Say(on ? "Evento guardado no seu calendário." : "Evento tirado do seu calendário.", false);
                });
            });
            Actions["Calendário"] = Actions[calendar.text]; calendar.SetEnabled(!closed || InCalendar); area.Add(calendar);
        });

        public static string RightsWords(JsonElement flags)
        {
            var words = new List<string>(); if (Flag(flags, "copy")) words.Add("copiar"); if (Flag(flags, "modify")) words.Add("modificar"); if (Flag(flags, "transfer")) words.Add("transferir"); if (Flag(flags, "resell")) words.Add("revender");
            return words.Count == 0 ? "nada além de usar" : string.Join(", ", words);
        }

        public static string Size(long bytes) => bytes < 1024 ? bytes + " B" : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", Pt) + " KB" : (bytes / 1048576.0).ToString("0.#", Pt) + " MB";

        public static string BuyWords(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); code = S(d.RootElement, "error"); } catch (JsonException) { }
            return code switch { "insufficient_balance" => "Saldo insuficiente para esta compra.", "already_owned" => "Você já tem este item.", "cannot_buy_own_product" => "Você não pode comprar o seu próprio item.", "key_reused" => "Esta compra já foi enviada. Confira o seu inventário.", "product_unavailable" or "not_found" => "Este item não está mais à venda.", _ => UiTasks.Explain(status, body) };
        }

        /// <summary>The item as the market sells it (<c>/discovery/items/{id}</c>): price, creator, seller, what the buyer may do, gift, kind and size, whether it is already theirs; and buying it here.</summary>
        private void ItemDetail(Guid id, VisualElement area) => this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/items/" + id), r =>
        {
            if (r.Status == 404) { Facts(area, new[] { "Este item não está mais à venda." }); return; }
            if (r.Status != 200) { Facts(area, new[] { "Não foi possível carregar o item agora." }); return; }
            using var d = JsonDocument.Parse(r.Body); var p = d.RootElement; var price = I(p, "priceCoins"); var name = S(p, "name"); var creator = Obj(p, "creator"); var seller = Obj(p, "seller"); var asset = Obj(p, "asset"); var store = Obj(p, "store");
            CanBuy = Flag(p, "canBuy"); var own = Flag(p, "isOwn"); var owned = Flag(p, "owned");
            Facts(area, new[]
            {
                "Preço: " + (price > 0 ? Coins(price) : "gratuito"), S(creator, "displayName").Length > 0 ? "Criador(a): " + S(creator, "displayName") : "",
                S(seller, "name").Length > 0 ? "Vendedor(a): " + S(seller, "name") + (S(seller, "kind") == "group" ? " (grupo)" : "") : "",
                "Você poderá: " + RightsWords(Obj(p, "rights")) + ".", "E passar adiante: " + RightsWords(Obj(p, "nextRights")) + ".",
                Flag(p, "allowGift") ? "Pode ser comprado de presente." : "Não pode ser comprado de presente.", Flag(p, "resale") ? "Revenda de um item usado." : "",
                asset.ValueKind == JsonValueKind.Object ? "Tipo: " + S(asset, "category") + (Has(asset, "byteSize") ? " · " + Size(I(asset, "byteSize")) : "") + "." : "",
                own ? "Este item é seu." : owned ? "Você já tem este item." : "",
            });
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; area.Add(row);
            var taken = owned && price == 0; var label = own ? "Este item é seu" : taken ? "Você já pegou este item" : price > 0 ? "Comprar por " + Coins(price) : "Pegar grátis";
            Button buy = null; buy = Sure(label, () => price > 0 ? "Confirmar: " + Coins(price) + " saem agora da carteira" : "Confirmar: pegar grátis", () => Purchase(id, name, price, buy, label, area), "lv-btn-primary");
            buy.SetEnabled(CanBuy && !own && !taken); Actions["Comprar"] = Actions[label]; row.Add(buy);
            var creatorId = G(creator, "id"); if (creatorId != Guid.Empty) row.Add(Btn("Ver o criador", () => new ProfileHubWindow(_c, creatorId).Open()));
            var sellerId = G(seller, "id"); if (sellerId != Guid.Empty && sellerId != creatorId) row.Add(Btn("Ver o vendedor", () => { if (S(seller, "kind") == "group") new GroupProfileWindow(_c, sellerId).Open(); else new ProfileHubWindow(_c, sellerId).Open(); }));
            var storeId = G(store, "id"); if (storeId != Guid.Empty) row.Add(Btn("Ver a loja", () => OpenStore(storeId)));
        });

        public bool Bought { get; private set; }
        private bool _buying; private string _buyKey = ""; private Guid _buyKeyFor;

        /// <summary>The words when the wallet does not reach the price.</summary>
        public static string ShortOfCoins(long balance, long price) => "Saldo insuficiente: você tem " + Coins(balance) + " e o item custa " + Coins(price) + ".";

        /// <summary>
        /// Buys the item on screen: the balance is looked at first, and the purchase carries one key. If no answer comes the same key is sent next time (so the first attempt cannot be charged twice);
        /// after any answer the next attempt is a new one. The item is read again afterwards, so the page shows what is true now.
        /// </summary>
        private void Purchase(Guid id, string name, long price, Button buy, string label, VisualElement area)
        {
            if (_buying) return; _buying = true; buy.SetEnabled(false); buy.text = "Comprando…"; Bought = false;
            void Back(string words) { _buying = false; buy.text = label; buy.SetEnabled(true); Say(words, true); }
            void Pay()
            {
                if (_buyKeyFor != id || _buyKey.Length == 0) { _buyKey = Guid.NewGuid().ToString("N"); _buyKeyFor = id; }
                this.Then(_c.Api.ApiAsync("POST", "/api/v1/economy/products/" + id + "/purchase", JsonSerializer.Serialize(new { idempotencyKey = _buyKey }, Options)), b =>
                {
                    if (b.Status > 0) _buyKey = "";
                    if (b.Status < 200 || b.Status >= 300) { Back(b.Status == 0 ? "Sem conexão com o servidor. Confira o inventário antes de tentar de novo." : BuyWords(b.Status, b.Body)); return; }
                    _buying = false; Bought = true; buy.text = "Comprado"; _c.Notices.Show("Item comprado.", NoticeKind.Good);
                    var done = "Compra feita! “" + name + "” está no seu inventário.";
                    if (Opened != id) { Say(done, false); return; }
                    area.Clear(); FactsText = ""; ItemDetail(id, area); Say(done, false);
                });
            }
            if (price <= 0) { Pay(); return; }
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/economy/wallet"), w =>
            {
                long? balance = null; if (w.Status == 200) try { using var d = JsonDocument.Parse(w.Body); if (Has(d.RootElement, "availableCoins")) balance = I(d.RootElement, "availableCoins"); } catch (JsonException) { }
                if (balance is { } have && have < price) Back(ShortOfCoins(have, price)); else Pay();   // a wallet that cannot be read leaves the decision to the server
            });
        }

        // ---- a store ----

        /// <summary>The lines about a store, from <c>/discovery/stores/{id}</c>.</summary>
        public static List<string> StoreLines(JsonElement s)
        {
            var owner = Obj(s, "owner"); var island = Obj(s, "island"); var count = I(s, "productCount"); var featured = Arr(s, "featured");
            var lines = new List<string>
            {
                S(owner, "displayName").Length > 0 ? "Proprietário(a): " + S(owner, "displayName") + "." : "",
                S(s, "kind") == "physical" ? "Loja física" + (S(island, "name").Length > 0 ? " em " + S(island, "name") : "") + "." : "Loja virtual: sem endereço no mundo.",
                S(s, "categoryLabel"),
                count == 0 ? "Nenhum produto à venda agora." : count + (count == 1 ? " produto publicado." : " produtos publicados."),
            };
            if (featured.Count > 0) { lines.Add("Em destaque:"); foreach (var f in featured) lines.Add("• " + S(f, "name") + " — " + (I(f, "priceCoins") > 0 ? Coins(I(f, "priceCoins")) : "gratuito")); }
            return lines.Where(l => l.Length > 0).ToList();
        }

        private void StoreDetail(Guid id, VisualElement area) => this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/stores/" + id), r =>
        {
            if (r.Status != 200) { Facts(area, new[] { r.Status == 404 ? "Esta loja não está mais disponível." : "Não foi possível carregar a loja agora." }); return; }
            using var d = JsonDocument.Parse(r.Body); var s = d.RootElement; Facts(area, StoreLines(s));
            var owner = G(s, "ownerId"); var name = S(s, "name"); var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; area.Add(row);
            if (owner == Guid.Empty) return;
            var products = Btn("Ver produtos", () => ShowProductsOf(owner, name), "lv-btn-primary"); products.SetEnabled(I(s, "productCount") > 0); row.Add(products);
            row.Add(Btn("Ver o proprietário", () => new ProfileHubWindow(_c, owner).Open()));
        });

        /// <summary>The catalog of a store: the items its owner sells, on the Itens tab.</summary>
        public void ShowProductsOf(Guid owner, string storeName)
        {
            _ownerFilter = owner; _ownerName = storeName; _query = ""; _keepOwner = true; Show("itens"); _keepOwner = false;
        }

        /// <summary>Opens the details of a store by its id (from an item it sells); "Voltar" returns to where the item was opened from.</summary>
        public void OpenStore(Guid id) => this.Then(_c.Api.ApiAsync("GET", "/api/v1/discovery/stores/" + id), r =>
        {
            if (r.Status != 200) { Say(r.Status == 404 ? "Esta loja não está mais disponível." : UiTasks.Explain(r.Status, r.Body), true); return; }
            using var d = JsonDocument.Parse(r.Body); var s = d.RootElement; var island = Obj(s, "island");
            var hit = JsonSerializer.Serialize(new
            {
                kind = "store", id, title = S(s, "name"), subtitle = S(island, "name").Length > 0 ? "Loja em " + S(island, "name") : "Loja virtual", description = S(s, "description"),
                islandId = Guid.TryParse(S(island, "id"), out var at) ? at.ToString() : null, x = Has(s, "x") ? (double?)D(s, "x") : null, z = Has(s, "z") ? (double?)D(s, "z") : null,
            }, Options);
            using var item = JsonDocument.Parse(hit); Detail(item.RootElement.Clone(), _back ?? (() => Show("lojas")));
        });

        /// <summary>How the person shown relates to the one searching (friend, blocked, a request either way), from <c>/api/v1/social/relations</c>.</summary>
        public string Relation { get; private set; } = "";
        public static string RelationWords(string relation) => relation switch { "friend" => "Vocês são amigos.", "blocked" => "Você bloqueou esta pessoa.", "pending_out" => "Você pediu amizade: esperando a resposta.", "pending_in" => "Esta pessoa pediu sua amizade: responda em Social.", _ => "" };

        /// <summary>Tells the search service that a result was opened or used to travel (it ranks the popular places).</summary>
        private void Signal(string type) { if (_query.Trim().Length == 0) return; this.Then(_c.Api.ApiAsync("POST", "/api/v1/search/events", JsonSerializer.Serialize(new { type, query = _query.Trim() }, Options)), _ => { }); }

        // ---- Para você ----

        private void ForYou()
        {
            Title("Abrir um link do jogo"); var link = Field("Link (lifeverse://…)", "", 200); Buttons(Btn("Abrir o link", () => OpenLink(link.value)));
            if (_settings.RecentSearches.Count > 0)
            {
                Title("Suas últimas buscas"); var row = new List<VisualElement>(); foreach (var h in _settings.RecentSearches) { var q = h; row.Add(UiKit.Button(q, () => { _query = q; Show("tudo"); }, "lv-btn-flat")); }
                row.Add(Btn("Apagar o histórico", () => { _settings.RecentSearches = new List<string>(); _save?.Invoke(); Show("paravoce"); })); Buttons(row.ToArray());
            }
            var keep = Check("Guardar as minhas buscas neste computador", _settings.KeepSearchHistory); keep.RegisterValueChangedCallback(e => { _settings.KeepSearchHistory = e.newValue; if (!e.newValue) _settings.RecentSearches = new List<string>(); _save?.Invoke(); });
            Get("/api/v1/search/interests", interests =>
            {
                Title("Seus interesses"); var list = Items(interests).Select(i => i.GetString()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList(); var field = Field("Interesses (separados por vírgula)", string.Join(", ", list), 300);
                Buttons(Btn("Salvar os interesses", () => Send("PUT", "/api/v1/search/interests", new { interests = field.value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Take(20).ToArray() }, "Interesses salvos.", () => Show("paravoce"))));
                Get("/api/v1/search/recommended?take=8", rec => { Title("Recomendados para você"); Draw(Items(rec), () => Show("paravoce")); if (Items(rec).Count == 0) Line("Diga seus interesses para receber sugestões.", "lv-small", "lv-muted");
                    Get("/api/v1/search/popular?take=8", pop => { Title("Lugares populares"); Draw(Items(pop), () => Show("paravoce")); if (Items(pop).Count == 0) Line("Nada ainda.", "lv-small", "lv-muted");
                        Get("/api/v1/search/recent?take=8", rec2 => { Title("Lugares onde você esteve"); Draw(Items(rec2), () => Show("paravoce")); if (Items(rec2).Count == 0) Line("Nada ainda.", "lv-small", "lv-muted"); }); }); });
            });
        }

        /// <summary>Opens a link of the game: a person's profile, a group, a place (travelling there), or the search for an event, item or store.</summary>
        public bool OpenLink(string link)
        {
            if (!TryParseLink(link, out var kind, out var id, out var x, out var z)) { Say("Esse não é um link do jogo (lifeverse://…).", true); return false; }
            switch (kind)
            {
                case "person": new ProfileHubWindow(_c, id).Open(); Say("Abrindo o perfil.", false); break;
                case "group": new GroupProfileWindow(_c, id).Open(); Say("Abrindo o grupo.", false); break;
                case "island": Say("Indo até lá…", false); if (_go != null) _go(id, x, z); else _c.EnterIsland(id); break;
                case "event": Send("POST", "/api/v1/discovery/events/" + id + "/join", "{}", "Você vai participar do evento."); break;
                default: Say("Link de " + (kind == "item" ? "item" : "loja") + ": procure-o na aba " + (kind == "item" ? "Itens" : "Lojas") + ".", false); break;
            }
            return true;
        }

        // ---- Favoritos ----

        private void Favorites(int page = 1) => Get("/api/v1/search/favorites?page=" + page + "&pageSize=" + PageSize, root =>
        {
            Reset(); var items = Arr(root, "items"); _summary.text = I(root, "total") + " favorito(s)";
            Draw(items, () => Favorites(page)); if (items.Count == 0) Line("Nada nos favoritos: abra um resultado e toque em Favoritar.", "lv-small", "lv-muted"); Pager(root, Favorites);
        });
    }
}
