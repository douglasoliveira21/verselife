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
        }

        protected override void Fill(string section)
        {
            Opened = Guid.Empty; _summary.text = "";
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
        public static string SearchPath(string q, string[] types, int page, string sort, string minPrice = "", string maxPrice = "", string rights = "", string creator = "", bool free = false)
        {
            var path = "/api/v1/search?page=" + Math.Max(1, page) + "&pageSize=" + PageSize;
            if (!string.IsNullOrWhiteSpace(q)) path += "&q=" + Uri.EscapeDataString(q.Trim()); if (types != null && types.Length > 0) path += "&types=" + string.Join(",", types);
            if (!string.IsNullOrEmpty(sort)) path += "&sort=" + sort; if (long.TryParse((minPrice ?? "").Trim(), out var min) && min >= 0) path += "&minPrice=" + min; if (long.TryParse((maxPrice ?? "").Trim(), out var max) && max >= 0) path += "&maxPrice=" + max;
            if (!string.IsNullOrEmpty(rights)) path += "&rights=" + rights; if (!string.IsNullOrWhiteSpace(creator)) path += "&creator=" + Uri.EscapeDataString(creator.Trim()); if (free) path += "&free=true";
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
                Btn("Limpar", () => { _query = ""; _sort = ""; _minPrice = _maxPrice = _creator = _rights = ""; _free = false; Results(tab); }));
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
            if (_query.Trim().Length == 0 && tab == "tudo") { Line("Digite o que procurar: pessoas, ilhas, terrenos, eventos, grupos, produtos ou lojas.", "lv-small", "lv-muted"); return; }
            if (page == 1 && _query.Trim().Length > 0 && _settings.KeepSearchHistory) { _settings.RecentSearches = Remember(_settings.RecentSearches, _query); _save?.Invoke(); }
            var sort = _sort; var path = SearchPath(_query, TypesOf(tab), page, sort, tab == "itens" ? _minPrice : "", tab == "itens" ? _maxPrice : "", tab == "itens" ? _rights : "", tab == "itens" ? _creator : "", tab == "eventos" && _free);
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
            foreach (var item in items)
            {
                var id = G(item, "id"); var it = item; _shown[id] = (item, back);
                Row(id, UiKit.Text((Flag(item, "favorite") ? "★ " : "") + S(item, "title"), "lv-bold"), UiKit.Text(Describe(item), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Detalhes", () => Detail(it, back)));
            }
        }

        // ---- the details of a result ----

        private void Detail(JsonElement item, Action back)
        {
            Reset(); var kind = S(item, "kind"); var id = G(item, "id"); var title = S(item, "title"); Opened = id;
            Signal("open");
            Buttons(Btn("Voltar aos resultados", back)); Title(title); Line(Describe(item), "lv-small", "lv-muted");
            if (S(item, "description").Length > 0) Line(S(item, "description"));
            if (Date(item, "endsAt") is { } end) Line("Termina " + When(end), "lv-small");
            var island = G(item, "islandId"); float? x = Has(item, "x") ? (float)D(item, "x") : (float?)null; float? z = Has(item, "z") ? (float)D(item, "z") : (float?)null;
            var actions = new List<VisualElement>();
            var place = kind == "island" ? id : island;
            if (place != Guid.Empty && kind != "person" && kind != "group") actions.Add(Btn(kind == "store" ? "Visitar a loja" : "Ir até lá", () => { Signal("teleport"); Say("Indo para " + title + "…", false); if (_go != null) _go(place, x, z); else _c.EnterIsland(place); }, "lv-btn-primary"));
            if (kind == "person") { actions.Add(Btn("Ver perfil", () => new ProfileHubWindow(_c, id).Open(), "lv-btn-primary")); actions.Add(Btn("Ações com a pessoa…", () => new PersonActionsWindow(_c, id, title).Open())); }
            if (kind == "group") actions.Add(Btn("Ver grupo", () => new GroupProfileWindow(_c, id).Open(), "lv-btn-primary"));
            if (kind == "event") actions.Add(Btn("Participar", () => Send("POST", "/api/v1/discovery/events/" + id + "/join", "{}", "Você vai participar de " + title + ".")));
            if (kind == "product") actions.Add(Btn("Abrir o mercado", () => new MarketWindow(_c).Open()));
            var favorite = Flag(item, "favorite");
            actions.Add(Btn(favorite ? "Tirar dos favoritos" : "Favoritar", () => Send(favorite ? "DELETE" : "PUT", "/api/v1/search/favorites/" + kind + "/" + id, favorite ? null : "{}", favorite ? "Tirado dos favoritos." : "Nos favoritos.", null, Words)));
            actions.Add(Btn("Copiar o link", () => { GUIUtility.systemCopyBuffer = LinkOf(kind, id, x, z); Say("Link copiado: " + LinkOf(kind, id, x, z), false); }));
            Buttons(actions.ToArray());
            Title("Denunciar este resultado"); var reason = Field("Motivo", "", 300);
            Buttons(Sure("Enviar denúncia", () => "Denunciar " + title + "?", () => { if (reason.value.Trim().Length < 3) { Say("Diga o motivo da denúncia.", true); return; } Send("POST", "/api/v1/search/report/" + kind + "/" + id, new { reason = reason.value.Trim() }, "Denúncia enviada. Obrigado.", null, Words); }));
        }

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
