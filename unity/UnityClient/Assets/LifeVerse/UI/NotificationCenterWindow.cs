using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>One line of the notification center as the server sent it.</summary>
    public sealed class CenterEntry
    {
        public string Source = "", Category = "", Kind = "", Title = "", Body = "", GroupName = ""; public Guid Id; public Guid? GroupId; public DateTimeOffset At; public bool Read; public List<string> Actions = new List<string>();
        public bool IsInvite => Source == "invite";
        public bool CanAnswer => IsInvite && Actions.Contains("accept") && Actions.Contains("decline");
    }

    /// <summary>A page of the center with the counters that sit on the bell.</summary>
    public sealed class CenterPage
    {
        public int Unread, Grupos, Convites, Sistema, Skip; public bool HasMore; public List<CenterEntry> Items = new List<CenterEntry>();
        public int UnreadOf(string category) => category == "grupos" ? Grupos : category == "convites" ? Convites : category == "sistema" ? Sistema : Unread;

        public static CenterPage Parse(string json)
        {
            try
            {
                using var d = JsonDocument.Parse(json); var root = d.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
                string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; int I(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : 0;
                var page = new CenterPage();
                foreach (var i in items.EnumerateArray())
                {
                    if (i.ValueKind != JsonValueKind.Object || !Guid.TryParse(S(i, "id"), out var id)) continue;
                    var entry = new CenterEntry { Source = S(i, "source"), Id = id, Category = S(i, "category"), Kind = S(i, "kind"), Title = S(i, "title"), Body = S(i, "body"), GroupName = S(i, "groupName"), Read = i.TryGetProperty("read", out var read) && read.ValueKind == JsonValueKind.True, GroupId = Guid.TryParse(S(i, "groupId"), out var g) ? g : (Guid?)null };
                    if (i.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTimeOffset(out var when)) entry.At = when;
                    if (i.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array) foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } act) entry.Actions.Add(act);
                    page.Items.Add(entry);
                }
                var by = root.TryGetProperty("unreadByCategory", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
                page.Unread = I(root, "unread"); page.Grupos = I(by, "grupos"); page.Convites = I(by, "convites"); page.Sistema = I(by, "sistema"); page.HasMore = root.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True; page.Skip = I(root, "skip");
                return page;
            }
            catch (JsonException) { return null; }
        }

        public static string ListPath(string category, bool unreadOnly, string search, int skip, int take) =>
            "/api/v1/notifications/center?category=" + Uri.EscapeDataString(category) + "&unread=" + (unreadOnly ? "true" : "false") + "&skip=" + skip + "&take=" + take + (string.IsNullOrWhiteSpace(search) ? "" : "&q=" + Uri.EscapeDataString(search.Trim()));

        public static string ReadBody(IEnumerable<CenterEntry> items, bool all, string category) =>
            JsonSerializer.Serialize(new { items = items?.Select(i => new { source = i.Source, id = i.Id }).ToList(), all, category }, new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() });
    }

    /// <summary>How a moment is told in the lists: "agora", "há 2 minutos", "há 3 horas", "há 2 dias", then the date.</summary>
    public static class RelativeTime
    {
        public static string Of(DateTimeOffset when, DateTimeOffset now)
        {
            if (when == default) return ""; var span = now - when; if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalSeconds < 45) return "agora";
            if (span.TotalMinutes < 60) { var m = Math.Max(1, (int)Math.Round(span.TotalMinutes)); return m == 1 ? "há 1 minuto" : "há " + m + " minutos"; }
            if (span.TotalHours < 24) { var h = (int)span.TotalHours; return h == 1 ? "há 1 hora" : "há " + h + " horas"; }
            if (span.TotalDays < 7) { var d = (int)span.TotalDays; return d == 1 ? "há 1 dia" : "há " + d + " dias"; }
            return when.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        }
    }

    /// <summary>
    /// The notification center: everything the platform told the person, in four tabs (Todas, Grupos, Convites, Sistema) with the unread count of each, a search, "só não lidas", pages of 20, marking one or all as read,
    /// and answering group invites right there. It reads from the server every time it opens and when a live event arrives, so it is the full history the toasts only hint at.
    /// </summary>
    public sealed class NotificationCenterWindow : FloatingWindow
    {
        public static readonly (string Id, string Text)[] Tabs = { ("todas", "Todas"), ("grupos", "Grupos"), ("convites", "Convites"), ("sistema", "Sistema") };
        public const int PageSize = 20;
        private readonly ApiContext _c; private readonly Action<int> _unreadChanged; private readonly ScrollView _list = new ScrollView(); private readonly Label _status; private readonly TextField _search; private readonly Toggle _unreadOnly; private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>(); private readonly Button _more;
        public string Tab { get; private set; } = "todas";
        public CenterPage Page { get; private set; } = new CenterPage();
        public List<CenterEntry> Shown { get; } = new List<CenterEntry>();
        public int Count => _list.contentContainer.childCount;
        public string Notice => _status.text;
        public TextField SearchField => _search;
        public Toggle UnreadOnly => _unreadOnly;
        public string TabText(string id) => _tabButtons[id].text;

        public NotificationCenterWindow(ApiContext context, Action<int> unreadChanged = null) : base(context.Ui, "notification-center", "Notificações", 520f, 560f, new Vector2(400, 80))
        {
            _c = context; _unreadChanged = unreadChanged; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.flexWrap = Wrap.Wrap; foreach (var (id, text) in Tabs) { var tab = id; var b = UiKit.Button(text, () => Show(tab), "lv-btn-flat"); _tabButtons[id] = b; bar.Add(b); } Body.Add(bar);
            _search = UiKit.Field("Buscar nas notificações", false, 60); _search.style.flexGrow = 1; _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Reload(); }, TrickleDown.TrickleDown);
            _unreadOnly = new Toggle("Só não lidas"); _unreadOnly.RegisterValueChangedCallback(_ => Reload());
            Body.Add(UiKit.Row(_search, UiKit.Button("Buscar", Reload))); Body.Add(UiKit.Row(_unreadOnly, UiKit.Spacer(), UiKit.Button("Marcar tudo como lido", MarkAllRead, "lv-btn-primary"))); Body.Add(_status);
            _list.style.flexGrow = 1; _list.style.minHeight = 260; Body.Add(_list); _more = UiKit.Button("Mais antigas", () => Load(Shown.Count, true)); _more.style.display = DisplayStyle.None; Body.Add(_more);
            Show("todas");
        }

        public void Show(string tab) { Tab = tab; foreach (var pair in _tabButtons) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == tab); Reload(); }

        public void Reload() => Load(0, false);

        private void Load(int skip, bool append) => this.Then(_c.Api.ApiAsync("GET", CenterPage.ListPath(Tab, _unreadOnly.value, _search.value, skip, PageSize)), r =>
        {
            if (r.Status != 200) { _status.text = r.Status == 404 ? "O servidor ainda não tem a central de notificações." : UiTasks.Explain(r.Status, r.Body); return; }
            var page = CenterPage.Parse(r.Body); if (page == null) { _status.text = "Resposta inesperada do servidor."; return; }
            Page = page; _status.text = ""; if (!append) { Shown.Clear(); _list.Clear(); }
            foreach (var entry in page.Items) { Shown.Add(entry); _list.Add(RowOf(entry)); }
            if (Shown.Count == 0) _list.Add(UiKit.Text(_unreadOnly.value ? "Nada por ler aqui." : "Nenhuma notificação.", "lv-muted"));
            _more.style.display = page.HasMore ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (id, text) in Tabs) { var n = page.UnreadOf(id); _tabButtons[id].text = n > 0 ? text + " (" + n + ")" : text; }
            _unreadChanged?.Invoke(page.Unread);
        });

        private VisualElement RowOf(CenterEntry entry)
        {
            var box = new VisualElement(); box.AddToClassList("lv-row"); box.style.flexDirection = FlexDirection.Column; box.style.marginBottom = 4; box.style.opacity = entry.Read ? .7f : 1f;
            var head = UiKit.Row(UiKit.Text((entry.Read ? "" : "● ") + (entry.Title.Length > 0 ? entry.Title : entry.Kind), entry.Read ? "lv-small" : "lv-bold"), UiKit.Spacer(), UiKit.Text(RelativeTime.Of(entry.At, DateTimeOffset.UtcNow), "lv-dim", "lv-small")); box.Add(head);
            if (entry.Body.Length > 0 || entry.GroupName.Length > 0) { var text = UiKit.Text((entry.GroupName.Length > 0 ? entry.GroupName + " — " : "") + entry.Body, "lv-small", "lv-muted"); text.style.whiteSpace = WhiteSpace.Normal; box.Add(text); }
            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            if (entry.CanAnswer) { buttons.Add(UiKit.Button("Aceitar", () => Answer(entry, true), "lv-btn-primary")); buttons.Add(UiKit.Button("Recusar", () => Answer(entry, false))); }
            if (!entry.Read) buttons.Add(UiKit.Button("Marcar como lida", () => MarkRead(entry)));
            if (buttons.childCount > 0) box.Add(buttons); return box;
        }

        public void MarkRead(CenterEntry entry) => Post("/api/v1/notifications/center/read", CenterPage.ReadBody(new[] { entry }, false, Tab), null);
        public void MarkAllRead() => Post("/api/v1/notifications/center/read", CenterPage.ReadBody(null, true, Tab), "Tudo marcado como lido.");

        /// <summary>Accepts or declines a group invite from the list; the invite then leaves it.</summary>
        public void Answer(CenterEntry entry, bool accept) => Post("/api/v1/social/groups/invites/" + entry.Id + "/" + (accept ? "accept" : "decline"), "{}", accept ? "Convite aceito: você entrou no grupo." : "Convite recusado.");

        private void Post(string path, string json, string done) => this.Then(_c.Api.ApiAsync("POST", path, json), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { if (done != null) _c.Notices.Show(done, NoticeKind.Good); Reload(); } else { _status.text = UiTasks.Explain(r.Status, r.Body); _status.AddToClassList("lv-bad"); }
        });
    }
}
