using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The frame the management windows share (as the Godot client draws them): a side menu of sections, a summary line on top, a status line and the page of the section. Pages are read from the API each time
    /// a section opens; an answer that arrives after the person moved to another section is dropped, and a change refused because someone else saved first (409 "stale") reads the page again and keeps the warning.
    /// Every button of a page is also kept by its text in <see cref="Actions"/>, so the checks can press it.
    /// </summary>
    public abstract class SectionWindow : FloatingWindow
    {
        protected static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        protected readonly ApiContext _c; protected readonly Label _summary, _status; protected readonly ScrollView _page = new ScrollView(); private readonly Dictionary<string, Button> _menu = new Dictionary<string, Button>();
        private int _view;
        public string Section { get; private set; } = "";
        public string Status => _status.text;
        public string SummaryText => _summary.text;
        public int Lines => _page.contentContainer.childCount;
        /// <summary>The fields of the page now on screen, by their label (for the tests and for filling forms).</summary>
        public readonly Dictionary<string, TextField> Fields = new Dictionary<string, TextField>();
        public readonly Dictionary<string, Toggle> Toggles = new Dictionary<string, Toggle>();
        public readonly Dictionary<string, DropdownField> Choices = new Dictionary<string, DropdownField>();
        /// <summary>The rows the page shows (ids of what each row is about), for the tests.</summary>
        public readonly List<Guid> RowIds = new List<Guid>();
        /// <summary>What each button of the page does, by its text (the last one wins when rows repeat a text), so the checks can press them.</summary>
        public readonly Dictionary<string, Action> Actions = new Dictionary<string, Action>();
        public int Loads { get; private set; }

        protected SectionWindow(ApiContext context, string id, string title, (string Id, string Text)[] sections, float width, float height, Vector2 position, float menuWidth = 190f) : base(context.Ui, id, title, width, height, position)
        {
            _c = context; _summary = UiKit.Text("Carregando…", "lv-small", "lv-summary"); _summary.style.whiteSpace = WhiteSpace.Normal; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            var menu = new ScrollView { name = "sections" }; menu.AddToClassList("lv-sidebar"); menu.style.width = menuWidth; menu.style.marginRight = 10;
            foreach (var (sid, text) in sections) { var section = sid; var b = UiKit.Button(text, () => Show(section), "lv-btn-flat"); b.AddToClassList("lv-tab"); UiKit.WithIcon(b, IconOfSection(sid), 15); b.style.whiteSpace = WhiteSpace.Normal; _menu[sid] = b; menu.Add(b); }
            FitScreen();
            // the summary of a window that has nothing to summarize takes no room
            void Fold() => _summary.style.display = string.IsNullOrEmpty(_summary.text) ? DisplayStyle.None : DisplayStyle.Flex;
            _summary.RegisterValueChangedCallback(e => Fold()); _summary.RegisterCallback<AttachToPanelEvent>(e => Fold());
            var right = new VisualElement(); right.style.flexGrow = 1; right.Add(_summary); right.Add(_status); _page.style.flexGrow = 1; right.Add(_page);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexGrow = 1; row.style.flexShrink = 1; row.style.minHeight = 0; row.style.overflow = Overflow.Hidden; menu.style.minHeight = 0; right.style.minHeight = 0; _page.style.minHeight = 0;
            row.Add(menu); row.Add(right); Body.style.minHeight = 0; Body.style.overflow = Overflow.Hidden; Body.Add(row);
        }

        private static readonly Dictionary<string, string> SectionIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["geral"] = "info", ["visao"] = "eye", ["sobre"] = "user", ["resumo"] = "coin", ["extrato"] = "list", ["comprar"] = "plus", ["taxas"] = "info", ["enviar"] = "send",
            ["visuais"] = "star", ["corpo"] = "avatar", ["roupas"] = "bag", ["animacoes"] = "play", ["editor"] = "user",
            ["paravoce"] = "bulb", ["tudo"] = "grid", ["pessoas"] = "people", ["lugares"] = "pin", ["eventos"] = "calendar", ["grupos"] = "people", ["itens"] = "bag", ["lojas"] = "home", ["favoritos"] = "star",
            ["meus"] = "people", ["descobrir"] = "search", ["convites"] = "mail", ["avisos"] = "bell", ["criar"] = "plus",
            ["membros"] = "people", ["cargos"] = "shield", ["permissoes"] = "lock", ["chat"] = "chat", ["economia"] = "coins", ["propostas"] = "doc", ["loja"] = "bag", ["configuracoes"] = "settings",
            ["arvore"] = "layers", ["pastas"] = "folder", ["sempasta"] = "folder-open", ["recentes"] = "history", ["atualizados"] = "download", ["devolvidos"] = "undo", ["lixeira"] = "trash", ["espaco"] = "chart", ["pacote"] = "download",
            ["acesso"] = "user-add", ["banimentos"] = "ban", ["administracao"] = "shield", ["objetos"] = "cube", ["fisica"] = "move", ["desempenho"] = "chart", ["ambiente"] = "sun", ["midia"] = "volume", ["terreno"] = "mountain",
            ["reinicio"] = "power", ["auditoria"] = "history", ["regras"] = "doc", ["terrenos"] = "grid", ["propriedade"] = "home", ["mapa"] = "map", ["demarcacao"] = "grid-dots", ["lista"] = "list", ["vendas"] = "coin", ["alugueis"] = "calendar",
            ["contratos"] = "doc", ["prims"] = "cube", ["historico"] = "history", ["opcoes"] = "settings", ["perto"] = "target", ["amigos"] = "people", ["feed"] = "chat-dots", ["galeria"] = "image", ["conquistas"] = "star",
            ["estatisticas"] = "chart", ["acoes"] = "dots", ["perfis"] = "copy",
            ["General"] = "settings", ["Graphics"] = "monitor", ["Audio"] = "volume", ["Voice"] = "mic", ["Chat"] = "chat", ["Interface"] = "layers", ["Controls"] = "pointer", ["Camera"] = "camera", ["World"] = "globe", ["Avatar"] = "avatar",
            ["Build"] = "hammer", ["Building"] = "hammer", ["Inventory"] = "bag", ["Scripts"] = "code", ["Network"] = "wifi", ["Privacy"] = "lock", ["Account"] = "user", ["Accessibility"] = "eye", ["Notifications"] = "bell", ["Metrics"] = "chart", ["Language"] = "globe",
        };

        /// <summary>The icon beside a section of the side menu.</summary>
        public static string IconOfSection(string id) => id != null && SectionIcons.TryGetValue(id, out var icon) ? icon : "caret-right";

        /// <summary>How many pictures of the page arrived and were drawn (for the checks).</summary>
        public int PicturesShown { get; private set; }

        /// <summary>A picture the platform hands out, drawn in a place of the page when it arrives; <paramref name="otherwise"/> is asked when the first address does not give it.</summary>
        protected void ShowPicture(VisualElement into, string path, float size, string otherwise = null)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, name = "picture" }; image.style.width = size; image.style.height = size; image.style.marginBottom = 4; image.style.flexShrink = 0; into.Add(image);
            Texture2D drawn = null; image.RegisterCallback<DetachFromPanelEvent>(_ => { if (drawn != null) UnityEngine.Object.Destroy(drawn); drawn = null; });
            void Ask(string from, string then) => this.Then(_c.Api.GetBytesAsync(from), r =>
            {
                if (image.parent == null) return;
                var texture = r.Status == 200 && r.Bytes != null ? new Texture2D(2, 2) : null;
                if (texture != null && LifeVerse.Unity.World.Pictures.Load(texture, r.Bytes)) { image.image = drawn = texture; PicturesShown++; return; }
                if (texture != null) UnityEngine.Object.Destroy(texture); if (then != null) Ask(then, null);
            });
            Ask(path, otherwise);
        }

        public bool Press(string text) { if (!Actions.TryGetValue(text, out var run)) return false; run(); return true; }

        public void Show(string section)
        {
            Section = section; _view++; foreach (var pair in _menu) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == section);
            Reset(); Say("", false); Fill(section);
        }

        /// <summary>Draws the page of a section (the page is empty when it is called).</summary>
        protected abstract void Fill(string section);

        protected void Reset() { _page.Clear(); Fields.Clear(); Toggles.Clear(); Choices.Clear(); RowIds.Clear(); Actions.Clear(); }
        protected void Say(string text, bool bad) { _status.text = text; _status.EnableInClassList("lv-bad", bad); }

        protected void Get(string path, Action<JsonElement> fill)
        {
            var view = _view; this.Then(_c.Api.ApiAsync("GET", path), r =>
            {
                if (view != _view) return; Loads++; if (r.Status != 200) { Say(r.Status == 404 ? "O servidor ainda não tem esta seção." : UiTasks.Explain(r.Status, r.Body), true); return; }
                try { using var d = JsonDocument.Parse(r.Body); fill(d.RootElement.Clone()); } catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is KeyNotFoundException || e is FormatException) { Say("Resposta inesperada do servidor.", true); }
            });
        }

        /// <summary>Sends a change; <paramref name="explain"/> may turn the API's refusal into words of its own (null to use the general ones).</summary>
        protected void Send(string method, string path, object body, string done, Action then = null, Func<int, string, string> explain = null)
        {
            var view = _view; this.Then(_c.Api.ApiAsync(method, path, body == null ? null : body as string ?? JsonSerializer.Serialize(body, Options)), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { if (done != null) _c.Notices.Show(done, NoticeKind.Good); if (view == _view) { then?.Invoke(); if (done != null) Say(done, false); } return; }   // the message stays after the page is read again
                var words = explain?.Invoke(r.Status, r.Body) ?? UiTasks.Explain(r.Status, r.Body);
                if (view != _view) { _c.Notices.Show(words, NoticeKind.Bad); return; }
                if (r.Status == 409 && r.Body.Contains("\"stale\"")) { Show(Section); Say("Outra pessoa mudou isto agora há pouco. A página foi lida de novo; confira e salve outra vez.", true); }   // read again, then keep the warning on screen
                else Say(words, true);
            });
        }

        protected static readonly JsonSerializerOptions Options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

        // ---- small readers ----
        public static string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : "" : "";
        public static long I(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (long)d : 0;
        public static double D(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;
        public static bool Has(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null;
        public static bool Flag(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        public static JsonElement Obj(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
        public static List<JsonElement> Arr(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement>();
        public static List<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToList() : Arr(e, "items");
        public static DateTimeOffset? Date(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var t) ? t : (DateTimeOffset?)null;
        public static Guid G(JsonElement e, string n) => Guid.TryParse(S(e, n), out var g) ? g : Guid.Empty;
        public static string When(DateTimeOffset? t) => t == null ? "—" : t.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt);
        public static string Coins(long value) => value.ToString("N0", Pt) + " VC";

        // ---- building a page ----
        protected Button Btn(string text, Action run, string kind = "lv-btn") { Actions[text] = run; return UiKit.Button(text, run, kind); }
        protected Button Sure(string text, Func<string> describe, Action run, string kind = "lv-btn") { Actions[text] = run; return Controls.Confirm(text, describe, run, kind); }
        protected TextField Field(string label, string value = "", int max = 200) { var f = UiKit.Field(label, false, max, value ?? ""); Fields[label] = f; _page.Add(f); return f; }
        protected Toggle Check(string label, bool value) { var t = UiKit.Check(label, value, _ => { }); Toggles[label] = t; _page.Add(t); return t; }
        protected DropdownField Choice(string label, List<(string Id, string Text)> options, string current)
        {
            if (options.Count == 0) options = new List<(string, string)> { (current, current) }; var index = Math.Max(0, options.FindIndex(o => o.Id == current));
            var d = new DropdownField(label, options.Select(o => o.Text).ToList(), index); d.userData = options; Choices[label] = d; _page.Add(d); return d;
        }
        protected static string IdOf(DropdownField d) => d.userData is List<(string Id, string Text)> options && d.index >= 0 && d.index < options.Count ? options[d.index].Id : d.value;
        protected void Title(string text) { var t = UiKit.Text(text, "lv-heading"); _page.Add(t); }
        protected void Line(string text, params string[] classes) { var t = UiKit.Text(text, classes.Length == 0 ? new[] { "lv-small" } : classes); t.style.whiteSpace = WhiteSpace.Normal; _page.Add(t); }
        protected VisualElement Row(Guid id, params VisualElement[] cells) { foreach (var c in cells) { c.style.marginRight = 8; if (c is Button) c.style.flexShrink = 0; else if (c is Label) { c.style.flexShrink = 1; c.style.minWidth = 40; c.style.whiteSpace = WhiteSpace.Normal; } } var r = UiKit.Row(cells); r.style.flexWrap = Wrap.Wrap; r.style.alignItems = Align.Center; r.AddToClassList("lv-row"); _page.Add(r); if (id != Guid.Empty) RowIds.Add(id); return r; }
        protected void Buttons(params VisualElement[] buttons) { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.flexWrap = Wrap.Wrap; foreach (var b in buttons) r.Add(b); _page.Add(r); }
        protected void Pager(JsonElement root, Action<int> go)
        {
            var total = I(root, "total"); var size = Math.Max(1, I(root, "pageSize")); var page = Math.Max(1, I(root, "page")); var pages = Math.Max(1, (total + size - 1) / size); if (pages <= 1) return;
            _page.Add(UiKit.Row(Btn("Anterior", () => go((int)Math.Max(1, page - 1))), UiKit.Text("página " + page + " de " + pages, "lv-small", "lv-muted"), Btn("Próxima", () => go((int)Math.Min(pages, page + 1)))));
        }

        // ---- the group access of a parcel (/group-access), shared by the land management and "Sobre este Terreno" ----
        public static readonly (string Id, string Text)[] GroupKinds = { ("allow", "Entrar"), ("build", "Entrar e construir") };

        protected void GroupAccess(VisualElement area, string parcelPath, Action reload) => Get(parcelPath + "/group-access", list =>
        {
            area.Add(UiKit.Text("Grupos com acesso", "lv-bold", "lv-accent")); var items = Items(list);
            foreach (var g in items)
            {
                var id = G(g, "id"); RowIds.Add(id); var name = S(g, "groupName");
                var r = UiKit.Row(UiKit.Text(name, "lv-bold"), UiKit.Text(S(g, "kind") == "build" ? "entrar e construir" : "entrar", "lv-small", "lv-muted"), UiKit.Spacer(), Sure("Tirar o grupo", () => "Tirar o acesso de " + name + "?", () => Send("DELETE", parcelPath + "/group-access/" + id, null, "Acesso do grupo retirado.", reload, LandManagementWindow.Words)));
                r.AddToClassList("lv-row"); area.Add(r);
            }
            if (items.Count == 0) area.Add(UiKit.Text("Nenhum grupo tem acesso.", "lv-small", "lv-muted"));
            Get("/api/v1/social/groups", groups =>
            {
                var mine = Items(groups).Select(g => (Id: S(g, "id"), Name: S(g, "name"))).Where(g => g.Id.Length > 0).ToList();
                if (mine.Count == 0) { area.Add(UiKit.Text("Você não está em nenhum grupo para dar acesso.", "lv-small", "lv-muted")); return; }
                var group = new DropdownField("Grupo", mine.Select(g => g.Name).ToList(), 0); var kind = new DropdownField("Pode", GroupKinds.Select(k => k.Text).ToList(), 0); area.Add(group); area.Add(kind); Choices["Grupo"] = group; Choices["Pode"] = kind;
                area.Add(UiKit.Row(Btn("Dar acesso ao grupo", () => Send("POST", parcelPath + "/group-access", new { groupId = Guid.Parse(mine[Math.Max(0, group.index)].Id), kind = GroupKinds[Math.Max(0, kind.index)].Id }, "Acesso dado ao grupo.", reload, LandManagementWindow.Words), "lv-btn-primary")));
            });
        });
    }
}
