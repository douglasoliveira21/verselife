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
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 16 }; _tabs.AddTab("Recebidas", "mail"); _tabs.AddTab("Enviadas", "send"); content.AddChild(_tabs);
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 170), MaxTextLines = 2 }; _list.AddThemeFontSizeOverride("font_size", 15); _list.ItemSelected += _ => ShowDetail(); content.AddChild(_list);
        _detail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _detail.AddThemeFontSizeOverride("font_size", 14); _detail.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_detail);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _accept = Action("Aceitar", UiStyle.ButtonKind.Primary, () => Answer("accept")); _reject = Action("Recusar", UiStyle.ButtonKind.Secondary, () => Answer("reject")); _cancel = Action("Cancelar oferta", UiStyle.ButtonKind.Secondary, () => Answer("cancel"));
        row.AddChild(_accept); row.AddChild(_reject); row.AddChild(_cancel);
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

    public void Reload()
    {
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
        _detail.Text = Chosen is { } offer ? (offer.Message.Length > 0 ? $"“{offer.Message}”\n" : string.Empty) + $"Vale até {offer.ExpiresAt.LocalDateTime:dd/MM/yyyy HH:mm}." : string.Empty; UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = Chosen is not null; _accept.Visible = _reject.Visible = Incoming; _cancel.Visible = !Incoming; _accept.Disabled = _reject.Disabled = _cancel.Disabled = !has;
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
    public void ShowTabForTesting(int tab) => _tabs.Select(tab);
    public void AnswerForTesting(string answer) => Answer(answer);
    public string NoteForTesting => _note.Text;
    public bool AcceptVisibleForTesting => _accept.Visible && !_accept.Disabled;
    public bool CancelVisibleForTesting => _cancel.Visible && !_cancel.Disabled;
}
