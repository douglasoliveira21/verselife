using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// "Direitos e propriedades" of an inventory item: what you may do with it (copy, modify, transfer, resell), what the group and the next owner will be able to do,
/// the history of who held it, and the ways to pass it on (offer it as a gift or transfer, put it up for resale, make a copy). The window only shows and asks:
/// every rule is the server's, and what the server refuses is explained here in words.
/// </summary>
public partial class ItemRightsWindow : Node
{
    private static readonly string[] RightNames = ["Copiar", "Modificar", "Transferir", "Revender"];
    private static readonly string[] RowNames = ["Você", "Grupo", "Próximo dono"];

    private FloatingWindow _window = null!;
    private Label _title = null!, _yours = null!, _note = null!, _sets = null!;
    private readonly CheckBox[,] _boxes = new CheckBox[3, 4];
    private Button _save = null!, _copy = null!, _showHistoryButton = null!, _send = null!, _sell = null!;
    private CheckBox _showHistory = null!, _keepCopy = null!;
    private ItemList _history = null!;
    private Control _offerBox = null!, _sellBox = null!, _grid = null!;
    private LineEdit _recipient = null!, _message = null!, _price = null!;
    private OptionButton _kind = null!;
    private RightsInfo? _info; private Action? _changed; private string _itemName = string.Empty, _sticky = string.Empty; private bool _loading;

    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }
    public bool Visible => _window.Visible;
    public RightsInfo? Info => _info;

    public override void _Ready() => Build();

    public void Open(Guid itemId, string name, bool toOffer, Action? changed)
    {
        _changed = changed; _itemName = name; _title.Text = name; _info = null; _note.Text = "Abrindo…"; _history.Clear();
        _window.Visible = true; _window.MoveToFront(); Load(itemId); if (toOffer) _recipient.GrabFocus();
    }

    // ---- building -----------------------------------------------------------------------------------------------------------------

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action, int height = 36)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private static Label Text(string text = "", bool muted = false, int size = 15)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        label.AddThemeFontSizeOverride("font_size", size); if (muted) label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label;
    }

    private static LineEdit Entry(string placeholder) { var edit = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; edit.AddThemeFontSizeOverride("font_size", 15); return edit; }

    private void Build()
    {
        _window = new FloatingWindow("Direitos do item", icon: "lock") { Name = "ItemRights", Visible = false };
        _window.Place(.5f, .5f, -300, -330, 300, 330);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _title = Text("", size: 19); _title.AddThemeFontOverride("font", UiStyle.Bold); content.AddChild(_title);
        _yours = Text(); content.AddChild(_yours);

        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 4); content.AddChild(grid); _grid = grid;
        grid.AddChild(Text("", true)); foreach (var name in RightNames) { var head = Text(name, true, 13); head.CustomMinimumSize = new Vector2(70, 0); head.HorizontalAlignment = HorizontalAlignment.Center; grid.AddChild(head); }
        for (var row = 0; row < 3; row++)
        {
            var label = Text(RowNames[row], false, 15); label.CustomMinimumSize = new Vector2(110, 0); grid.AddChild(label);
            for (var col = 0; col < 4; col++) { var box = new CheckBox { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter }; _boxes[row, col] = box; grid.AddChild(box); }
        }
        _sets = Text("O grupo só tem direitos se o item for compartilhado com um grupo (na janela do grupo). O próximo dono é quem comprar ou receber.", true, 13); content.AddChild(_sets);
        _save = Action("Salvar direitos", UiStyle.ButtonKind.Primary, SaveRights); content.AddChild(_save);

        _showHistory = new CheckBox { Text = "Deixar quem tem o item ver quem já o teve", FocusMode = Control.FocusModeEnum.None }; _showHistory.AddThemeFontSizeOverride("font_size", 14); _showHistory.Toggled += OnHistoryToggled; content.AddChild(_showHistory);
        _showHistoryButton = Action("Ver o histórico de donos", UiStyle.ButtonKind.Secondary, LoadHistory, 32); content.AddChild(_showHistoryButton);
        _history = new ItemList { CustomMinimumSize = new Vector2(0, 80), MaxTextLines = 2 }; _history.AddThemeFontSizeOverride("font_size", 13); content.AddChild(_history);

        _offerBox = new VBoxContainer(); ((VBoxContainer)_offerBox).AddThemeConstantOverride("separation", 6); content.AddChild(_offerBox);
        _offerBox.AddChild(Text("Dar a alguém", false, 16));
        _recipient = Entry("Nome de usuário da pessoa (ex.: ana)"); _offerBox.AddChild(_recipient);
        _kind = new OptionButton { CustomMinimumSize = new Vector2(0, 34), FocusMode = Control.FocusModeEnum.None }; _kind.AddItem("Presente"); _kind.AddItem("Transferência"); _offerBox.AddChild(_kind);
        _message = Entry("Mensagem (opcional)"); _offerBox.AddChild(_message);
        _keepCopy = new CheckBox { Text = "Ficar com o meu e dar uma cópia", FocusMode = Control.FocusModeEnum.None }; _keepCopy.AddThemeFontSizeOverride("font_size", 14); _offerBox.AddChild(_keepCopy);
        _send = Action("Enviar oferta", UiStyle.ButtonKind.Primary, SendOffer); _offerBox.AddChild(_send);

        _sellBox = new VBoxContainer(); ((VBoxContainer)_sellBox).AddThemeConstantOverride("separation", 6); content.AddChild(_sellBox);
        _sellBox.AddChild(Text("Revender no mercado", false, 16)); _price = Entry("Preço em moedas (0 = grátis)"); _sellBox.AddChild(_price);
        _sell = Action("Colocar à venda", UiStyle.ButtonKind.Secondary, Sell); _sellBox.AddChild(_sell);

        _copy = Action("Fazer uma cópia", UiStyle.ButtonKind.Secondary, MakeCopy); content.AddChild(_copy);
        _note = Text("", true, 14); content.AddChild(_note);
        AddChild(_window);
    }

    // ---- network ------------------------------------------------------------------------------------------------------------------

    private void Send(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, body); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        string[] headers = body is null ? [$"Authorization: Bearer {session.AccessToken}"] : [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"];
        if (http.Request($"{app.ApiBaseUrl}{path}", headers, method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    private bool Fail(int status, string body) { if (status is >= 200 and < 300) return false; _note.Text = ItemRightsApi.Explain(ItemRightsApi.ErrorCode(body), status); return true; }

    private void Load(Guid itemId)
    {
        Send(Godot.HttpClient.Method.Get, ItemRightsApi.RightsPath(itemId), null, (status, body) =>
        {
            if (Fail(status, body)) { _info = null; Fill(); return; }
            var message = _sticky; _sticky = string.Empty; // what the last action said stays visible after the item is read again
            _info = ItemRightsApi.ParseRights(body); _note.Text = _info is null ? "Não foi possível ler os direitos." : message; Fill();
        });
    }

    // ---- showing ------------------------------------------------------------------------------------------------------------------

    private void Fill()
    {
        _loading = true;
        var info = _info; var owner = info?.IsOwner == true; var active = info?.Status == "active";
        _yours.Text = info is null ? string.Empty : $"Você pode: {ItemRights.Describe(info.YourRights)}" + (info.Status == "active" ? string.Empty : $" · item {(info.Status == "offered" ? "em oferta" : info.Status == "listed" ? "à venda" : info.Status)}: aguardando");
        var masks = info is null ? new[] { 0, 0, 0 } : new[] { owner ? info.Owner : info.YourRights, info.Group, info.NextOwner };
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 4; col++)
            {
                var box = _boxes[row, col]; box.ButtonPressed = (masks[row] & (1 << col)) != 0;
                box.Disabled = !(owner && active) || (row == 1 && info?.GroupId is null); // only the holder changes them; a group set means nothing without a group
                box.Visible = owner || row == 0;
            }
        _grid.Visible = info is not null; _sets.Visible = owner; _save.Visible = owner;
        _showHistory.Visible = owner; _showHistory.ButtonPressed = info?.ShowHistory == true; _showHistoryButton.Visible = info is not null && (owner || info.ShowHistory);
        _offerBox.Visible = owner && active && info!.CanTransfer; _keepCopy.Disabled = info?.Copyable != true; if (info?.Copyable != true) _keepCopy.ButtonPressed = false;
        _sellBox.Visible = owner && active && info!.CanResell && info.CanTransfer; _copy.Visible = info is not null && info.Copyable && active;
        _loading = false;
    }

    private int RowMask(int row) => ItemRightsApi.MaskOf(_boxes[row, 0].ButtonPressed, _boxes[row, 1].ButtonPressed, _boxes[row, 2].ButtonPressed, _boxes[row, 3].ButtonPressed);

    // ---- actions ------------------------------------------------------------------------------------------------------------------

    private void SaveRights()
    {
        if (_info is not { IsOwner: true } info) return;
        Send(Godot.HttpClient.Method.Put, ItemRightsApi.RightsPath(info.ItemId), ItemRightsApi.SetRightsBody(RowMask(0), info.GroupId is null ? 0 : RowMask(1), RowMask(2), info.Revision), (status, body) =>
        {
            if (Fail(status, body)) return;
            _sticky = "Direitos salvos."; Load(info.ItemId); _changed?.Invoke();
        });
    }

    private void OnHistoryToggled(bool visible)
    {
        if (_loading || _info is not { IsOwner: true } info) return;
        Send(Godot.HttpClient.Method.Put, ItemRightsApi.HistoryVisibilityPath(info.ItemId), ItemRightsApi.HistoryVisibilityBody(visible), (status, body) =>
        {
            if (Fail(status, body)) return;
            _note.Text = visible ? "Quem tem o item agora pode ver o histórico (os nomes de quem passou no meio ficam ocultos)." : "O histórico ficou só para você.";
        });
    }

    private void LoadHistory()
    {
        if (_info is not { } info) return;
        Send(Godot.HttpClient.Method.Get, ItemRightsApi.HistoryPath(info.ItemId), null, (status, body) =>
        {
            if (Fail(status, body)) return;
            _history.Clear(); foreach (var line in ItemRightsApi.ParseHistory(body)) _history.AddItem(line.Text);
            if (_history.ItemCount == 0) _note.Text = "Sem histórico para mostrar.";
        });
    }

    /// <summary>Finds the person by their user name ("ana" or "@ana"), then sends the offer.</summary>
    private void SendOffer()
    {
        if (_info is not { IsOwner: true } info) return;
        var typed = _recipient.Text.Trim().TrimStart('@'); if (typed.Length < 2) { _note.Text = "Escreva o nome de usuário da pessoa."; return; }
        Send(Godot.HttpClient.Method.Get, SearchApi.SearchPath(typed, "person", null, "relevance", 1, 8), null, (status, body) =>
        {
            if (Fail(status, body)) return;
            var matches = SearchApi.ParsePage(body).Items.Where(h => string.Equals(h.Subtitle.TrimStart('@'), typed, StringComparison.OrdinalIgnoreCase) || string.Equals(h.Title, typed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) { _note.Text = matches.Count == 0 ? "Não encontrei essa pessoa. Use o nome de usuário exato." : "Mais de uma pessoa com esse nome; use o nome de usuário (@…)."; return; }
            Send(Godot.HttpClient.Method.Post, ItemRightsApi.OfferPath(info.ItemId), ItemRightsApi.OfferBody(matches[0].Id, _kind.Selected == 1 ? "transfer" : "gift", _message.Text, _keepCopy.ButtonPressed, "oferta-" + Guid.NewGuid().ToString("N")), (s2, b2) =>
            {
                if (Fail(s2, b2)) return;
                _sticky = $"Oferta enviada para {matches[0].Title}. Ela vale por 7 dias; enquanto espera, o item fica reservado."; _recipient.Text = string.Empty; _message.Text = string.Empty; Load(info.ItemId); _changed?.Invoke();
            });
        });
    }

    private void Sell()
    {
        if (_info is not { IsOwner: true } info) return;
        if (!long.TryParse(_price.Text.Trim(), out var price) || price < 0) { _note.Text = "Escreva o preço em moedas (0 para grátis)."; return; }
        Send(Godot.HttpClient.Method.Post, ItemRightsApi.ProductsPath, ItemRightsApi.ResaleBody(info.AssetId, info.ItemId, _itemName, price), (status, body) =>
        {
            if (Fail(status, body)) return;
            _sticky = $"À venda por {price} moedas. Enquanto estiver à venda você não pode usar o item (se ele não puder ser copiado)."; Load(info.ItemId); _changed?.Invoke();
        });
    }

    private void MakeCopy()
    {
        if (_info is not { } info) return;
        Send(Godot.HttpClient.Method.Post, ItemRightsApi.CopyPath(info.ItemId), null, (status, body) => { if (Fail(status, body)) return; _note.Text = "Cópia criada no seu inventário."; _changed?.Invoke(); });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public void SetRowForTesting(int row, int mask) { for (var col = 0; col < 4; col++) _boxes[row, col].ButtonPressed = (mask & (1 << col)) != 0; }
    public int RowForTesting(int row) => RowMask(row);
    public bool BoxEnabledForTesting(int row, int col) => !_boxes[row, col].Disabled && _boxes[row, col].Visible;
    public string NoteForTesting => _note.Text;
    public string YoursForTesting => _yours.Text;
    public bool OfferVisibleForTesting => _offerBox.Visible;
    public bool SellVisibleForTesting => _sellBox.Visible;
    public bool CopyVisibleForTesting => _copy.Visible;
    public bool KeepCopyEnabledForTesting => !_keepCopy.Disabled;
    public void SaveForTesting() => SaveRights();
    public void OfferForTesting(string recipient, bool transfer, bool keep) { _recipient.Text = recipient; _kind.Select(transfer ? 1 : 0); _keepCopy.ButtonPressed = keep; SendOffer(); }
    public void SellForTesting(string price) { _price.Text = price; Sell(); }
    public void CopyForTesting() => MakeCopy();
    public void HistoryForTesting() => LoadHistory();
    public void ToggleHistoryForTesting(bool visible) { _showHistory.ButtonPressed = visible; }
    public int HistoryCountForTesting => _history.ItemCount;
}
