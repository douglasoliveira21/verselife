using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// "Ofertas de itens": what other people want to give you (accept or refuse) and what you offered (take it back). An offer keeps the item reserved for 7 days.
/// Accepting is decided by the server in one step: the item changes hands or it does not, and it is never in two inventories.
/// </summary>
public partial class ItemOffersWindow : Node
{
    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private ItemList _list = null!;
    private Label _note = null!, _detail = null!;
    private Button _accept = null!, _reject = null!, _cancel = null!;
    private readonly List<OfferInfo> _offers = [];
    private Action? _changed; private string _sticky = string.Empty;

    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }
    public bool Visible => _window.Visible;
    public IReadOnlyList<OfferInfo> Offers => _offers;
    private bool Incoming => _tabs.Selected == 0;
    private bool History => _tabs.Selected == 2;
    private readonly List<TransferInfo> _transfers = [];
    private OptionButton _direction = null!, _outcome = null!; private LineEdit _search = null!; private Button _more = null!; private HBoxContainer _filters = null!;
    private int _historyPage = 1; private bool _hasMore;
    public IReadOnlyList<TransferInfo> Transfers => _transfers;
    private OfferInfo? Chosen => _list.GetSelectedItems() is { Length: > 0 } rows && rows[0] < _offers.Count ? _offers[rows[0]] : null;

    public override void _Ready() => Build();

    public void Toggle(Action? changed = null) { if (_window.Visible) _window.Visible = false; else Open(changed); }
    public void Open(Action? changed = null) { _changed = changed ?? _changed; _window.Visible = true; _window.MoveToFront(); Reload(); }

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private void Build()
    {
        _window = new FloatingWindow("Ofertas de itens", icon: "mail") { Name = "ItemOffers", Visible = false };
        _window.Place(.5f, .5f, -280, -230, 280, 230);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 16 }; _tabs.AddTab("Recebidas", "mail"); _tabs.AddTab("Enviadas", "send"); _tabs.AddTab("Histórico", "history"); content.AddChild(_tabs);
        _filters = new HBoxContainer { Visible = false }; _filters.AddThemeConstantOverride("separation", 6); content.AddChild(_filters);
        _direction = new OptionButton { FocusMode = Control.FocusModeEnum.None }; foreach (var label in new[] { "Todas", "Recebidas", "Enviadas" }) _direction.AddItem(label); _filters.AddChild(_direction);
        _outcome = new OptionButton { FocusMode = Control.FocusModeEnum.None }; foreach (var label in new[] { "Qualquer resultado", "Esperando", "Aceitas", "Recusadas", "Canceladas", "Expiradas" }) _outcome.AddItem(label); _filters.AddChild(_outcome);
        _search = new LineEdit { PlaceholderText = "Item ou pessoa…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 60 }; _filters.AddChild(_search);
        _direction.ItemSelected += _ => Reload(); _outcome.ItemSelected += _ => Reload(); _search.TextSubmitted += _ => Reload();
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 170), MaxTextLines = 2 }; _list.AddThemeFontSizeOverride("font_size", 15); _list.ItemSelected += _ => ShowDetail(); content.AddChild(_list);
        _detail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _detail.AddThemeFontSizeOverride("font_size", 14); _detail.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_detail);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _accept = Action("Aceitar", UiStyle.ButtonKind.Primary, () => Answer("accept")); _reject = Action("Recusar", UiStyle.ButtonKind.Secondary, () => Answer("reject")); _cancel = Action("Cancelar oferta", UiStyle.ButtonKind.Secondary, () => Answer("cancel"));
        row.AddChild(_accept); row.AddChild(_reject); row.AddChild(_cancel);
        _more = Action("Carregar mais", UiStyle.ButtonKind.Secondary, () => LoadHistory(_historyPage + 1)); _more.Visible = false; content.AddChild(_more);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeFontSizeOverride("font_size", 14); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_note);
        _tabs.TabChanged += _ => Reload();
        AddChild(_window); UpdateButtons();
    }

    private void Send(Godot.HttpClient.Method method, string path, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, null); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        if (http.Request($"{app.ApiBaseUrl}{path}", [$"Authorization: Bearer {session.AccessToken}"], method) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    private static readonly string[] DirectionKeys = ["all", "in", "out"], OutcomeKeys = ["", "pending", "accepted", "rejected", "cancelled", "expired"];

    /// <summary>The history of deliveries: every one, whatever came of it, a page at a time, with the filters above the list.</summary>
    private void LoadHistory(int page)
    {
        if (page == 1) { _transfers.Clear(); _list.Clear(); _note.Text = "Carregando…"; }
        Send(Godot.HttpClient.Method.Get, ItemRightsApi.TransfersPath(page, DirectionKeys[_direction.Selected], OutcomeKeys[_outcome.Selected], _search.Text.Trim()), (status, body) =>
        {
            if (!History) return; // another tab was opened while this was on its way
            if (status is < 200 or >= 300) { _note.Text = ItemRightsApi.Explain(ItemRightsApi.ErrorCode(body), status); return; }
            var (lines, more, total) = ItemRightsApi.ParseTransfers(body); _historyPage = page; _hasMore = more; _transfers.AddRange(lines);
            foreach (var line in lines) _list.AddItem(line.Text);
            _more.Visible = more; _note.Text = _transfers.Count == 0 ? "Nenhuma entrega com esses filtros." : $"{_transfers.Count} de {total} entrega(s).";
            ShowDetail();
        });
    }

    /// <summary>A live event from the API: an offer arrived, or somebody answered one. The list is read again when the window is open.</summary>
    public void OnLiveEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("type", out var type)) return;
            if (type.GetString() is "item_offer" or "item_offer_answer") { if (_window.Visible) Reload(); _changed?.Invoke(); }
        }
        catch (JsonException) { }
    }

    public void Reload()
    {
        _filters.Visible = History; _more.Visible = false;
        if (History) { LoadHistory(1); UpdateButtons(); return; }
        if (_sticky.Length == 0) _note.Text = "Carregando…";
        Send(Godot.HttpClient.Method.Get, ItemRightsApi.OffersPath(Incoming), (status, body) =>
        {
            _offers.Clear(); _list.Clear(); var message = _sticky; _sticky = string.Empty; // what the last answer said stays visible after the list is read again
            if (status is < 200 or >= 300) { _note.Text = ItemRightsApi.Explain(ItemRightsApi.ErrorCode(body), status); UpdateButtons(); return; }
            _offers.AddRange(ItemRightsApi.ParseOffers(body));
            foreach (var offer in _offers) _list.AddItem($"{offer.Text}\n{(Incoming ? "de " + offer.FromName : "para " + offer.ToName)}");
            _note.Text = message.Length > 0 ? message : _offers.Count == 0 ? (Incoming ? "Nenhuma oferta esperando resposta." : "Você não tem ofertas esperando resposta.") : string.Empty; ShowDetail();
        });
    }

    private void ShowDetail()
    {
        if (History) { _detail.Text = _list.GetSelectedItems() is { Length: > 0 } picked && picked[0] < _transfers.Count ? _transfers[picked[0]].Text.Replace("\n", " · ") : string.Empty; UpdateButtons(); return; }
        _detail.Text = Chosen is { } offer
            ? (offer.Message.Length > 0 ? $"“{offer.Message}”\n" : string.Empty) + $"Vale até {offer.ExpiresAt.LocalDateTime:dd/MM/yyyy HH:mm}." + (offer.Arrives != 0 ? $"\n{(Incoming ? "Você receberá com estes direitos" : "A pessoa receberá com estes direitos")}: {LifeVerse.Shared.Items.ItemRights.Describe(offer.Arrives)}." : "\nSem nenhum direito: só para guardar ou olhar.")
            : string.Empty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = Chosen is not null; _accept.Visible = _reject.Visible = Incoming; _cancel.Visible = !Incoming && !History; _accept.Disabled = _reject.Disabled = _cancel.Disabled = !has;
    }

    private void Answer(string answer)
    {
        if (Chosen is not { } offer) return;
        Send(Godot.HttpClient.Method.Post, ItemRightsApi.AnswerPath(offer.Id, answer), (status, body) =>
        {
            if (status is < 200 or >= 300) { _sticky = ItemRightsApi.Explain(ItemRightsApi.ErrorCode(body), status); Reload(); return; }
            _sticky = answer switch { "accept" => $"“{offer.ItemName}” agora está no seu inventário.", "reject" => "Oferta recusada.", _ => "Oferta cancelada: o item voltou a ser só seu." };
            _changed?.Invoke(); Reload();
        });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public void SelectForTesting(int row) { _list.Select(row); ShowDetail(); }
    public string DetailForTesting => _detail.Text;
    public void FilterForTesting(int direction, int outcome, string search) { _direction.Select(direction); _outcome.Select(outcome); _search.Text = search; }
    public void MoreForTesting() { if (_hasMore) LoadHistory(_historyPage + 1); }
    public bool MoreVisibleForTesting => _more.Visible;
    public bool FiltersVisibleForTesting => _filters.Visible;
    public int ListCountForTesting => _list.ItemCount;
    public void ShowTabForTesting(int tab) => _tabs.Select(tab);
    public void AnswerForTesting(string answer) => Answer(answer);
    public string NoteForTesting => _note.Text;
    public bool AcceptVisibleForTesting => _accept.Visible && !_accept.Disabled;
    public bool CancelVisibleForTesting => _cancel.Visible && !_cancel.Disabled;
}
