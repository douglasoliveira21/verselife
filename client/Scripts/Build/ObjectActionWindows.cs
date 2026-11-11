using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>"Comprar objeto": shows what is for sale, who sells it and the price, and asks the island server to sell it. The server charges the price the object has at that moment and answers; this window only waits for it.</summary>
public partial class ObjectPurchaseWindow : Node
{
    private FloatingWindow _window = null!;
    private Label _title = null!, _seller = null!, _gets = null!, _price = null!, _status = null!;
    private Button _buy = null!;
    private NetworkManager? _network;
    private BuildObjectState? _object;
    private string _key = string.Empty;
    private long _priceShown;

    /// <summary>Tests capture the command here instead of sending it over the network.</summary>
    public Func<string, bool>? OperationSink { get; set; }
    public bool IsOpen => _window is not null && _window.Visible;
    public string StatusForTesting => _status.Text;
    public string PriceForTesting => _price.Text;
    public string GetsForTesting => _gets.Text;
    public string SellerForTesting => _seller.Text;
    public bool CanBuyForTesting => !_buy.Disabled;
    public void PressBuyForTesting() => _buy.EmitSignal(BaseButton.SignalName.Pressed);

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        _window = new FloatingWindow("Comprar objeto", icon: "coin") { Name = "PurchaseWindow", Visible = false, Theme = ViewerTheme.Create() }; _window.Place(.5f, .5f, -230, -170, 230, 170); AddChild(_window);
        _window.CloseRequested += () => _window.Visible = false;
        var c = _window.Content; c.AddThemeConstantOverride("separation", 10);
        _title = Line(c, 22, true, UiStyle.AccentText); _seller = Line(c, 18, false, UiStyle.TextMuted); _gets = Line(c, 18, false, UiStyle.Text); _price = Line(c, 24, true, UiStyle.Coin);
        _status = Line(c, 16, false, UiStyle.TextMuted); _status.CustomMinimumSize = new Vector2(0, 40);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 10); c.AddChild(buttons);
        _buy = new Button { Text = " Comprar", Icon = UiStyle.Icon("check", UiStyle.Text, 22), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        UiStyle.StyleButton(_buy, UiStyle.ButtonKind.Primary, 12, 6, 14); _buy.AddThemeFontSizeOverride("font_size", 20); _buy.Pressed += Buy; buttons.AddChild(_buy);
        var cancel = new Button { Text = "Cancelar", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 46) }; UiStyle.StyleButton(cancel, UiStyle.ButtonKind.Secondary, 12, 6, 14); cancel.AddThemeFontSizeOverride("font_size", 18); cancel.Pressed += () => _window.Visible = false; buttons.AddChild(cancel);
        if (_network is not null) { _network.BuildOperationReceived += OnOperation; _network.BuildOperationRejected += OnRejected; }
    }

    public override void _ExitTree() { if (_network is null) return; _network.BuildOperationReceived -= OnOperation; _network.BuildOperationRejected -= OnRejected; }

    private static Label Line(VBoxContainer parent, int size, bool bold, Color color)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        if (bold) label.AddThemeFontOverride("font", UiStyle.Bold); parent.AddChild(label); return label;
    }

    public void Open(BuildObjectState state, string sellerName)
    {
        _object = state; _key = "obj-" + Guid.NewGuid().ToString("N"); var (mode, price) = BuildObjectProperties.SaleOf(state); _priceShown = price;
        _title.Text = state.Name; _seller.Text = $"Vendedor: {sellerName}"; _price.Text = $"{price.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} moedas";
        _gets.Text = mode switch { "original" => "Você recebe: o objeto original. Ele passa a ser seu.", "copy" => "Você recebe: uma cópia, que vai para o seu inventário.", "contents" => "Você recebe: uma cópia do conteúdo do objeto, no seu inventário.", _ => "Este objeto não está à venda." };
        _buy.Disabled = mode == "none"; _status.Text = mode == "none" ? "O objeto saiu da venda." : string.Empty; _window.Visible = true; _window.MoveToFront();
    }

    private void Buy()
    {
        if (_object is null) return;
        var json = JsonSerializer.Serialize(new { operation = "buy", objectId = _object.Id, expectedRevision = 0, properties = new { idempotencyKey = _key, expectedPrice = _priceShown } }, BuildPersistenceManager.Web);
        if (OperationSink is not null) { OperationSink(json); _status.Text = "Comprando…"; _buy.Disabled = true; return; }
        if (_network is not { IsAuthenticated: true }) { _status.Text = "Entre em uma ilha para comprar."; return; }
        _network.SubmitBuildOperation(json); _status.Text = "Comprando…"; _buy.Disabled = true;
    }

    public void OnOperation(string json)
    {
        if (_object is null || !_window.Visible) return;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.GetProperty("operation").GetString() != "bought" || !root.TryGetProperty("objectId", out var id) || id.GetString() != _object.Id.ToString()) return;
            var mine = root.TryGetProperty("buyerUserId", out var who) && Guid.TryParse(who.GetString(), out var buyer) && buyer == (GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty);
            if (!mine) { _status.Text = "Este objeto foi comprado por outra pessoa."; _buy.Disabled = true; return; }
            _status.Text = "Compra concluída."; _buy.Disabled = true;
            GetTree().CreateTimer(1.2).Timeout += () => { if (IsInstanceValid(_window)) _window.Visible = false; };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    public void OnRejected(string code) { if (!_window.Visible) return; _status.Text = BuildPersistenceManager.RejectionText(code); _buy.Disabled = code is "not_for_sale" or "own_object"; if (code == "price_changed") _key = "obj-" + Guid.NewGuid().ToString("N"); }
}

/// <summary>"Pagar objeto": the person chooses an amount and pays the owner of the object with their coins (a transfer on the economy ledger).</summary>
public partial class ObjectPaymentWindow : Node
{
    private FloatingWindow _window = null!;
    private Label _title = null!, _to = null!, _status = null!;
    private SpinBox _amount = null!;
    private Button _pay = null!;
    private HttpRequest? _http;
    private Guid _owner; private string _key = string.Empty;

    public bool IsOpen => _window is not null && _window.Visible;
    public string StatusForTesting => _status.Text;
    public double AmountForTesting { get => _amount.Value; set => _amount.Value = value; }
    /// <summary>The last payment asked for (recipient, amount, key), for tests.</summary>
    public (Guid To, long Amount, string Key)? LastRequestForTesting { get; private set; }
    /// <summary>Tests answer for the transfer instead of the network: (status, body).</summary>
    public Func<Guid, long, string, (long Status, string Body)>? TransferOverride { get; set; }
    public void PressPayForTesting() => _pay.EmitSignal(BaseButton.SignalName.Pressed);

    public override void _Ready()
    {
        _window = new FloatingWindow("Pagar objeto", icon: "coin") { Name = "PaymentWindow", Visible = false, Theme = ViewerTheme.Create() }; _window.Place(.5f, .5f, -220, -150, 220, 150); AddChild(_window);
        _window.CloseRequested += () => _window.Visible = false;
        var c = _window.Content; c.AddThemeConstantOverride("separation", 10);
        _title = new Label { Text = "Pagar" }; _title.AddThemeFontOverride("font", UiStyle.Bold); _title.AddThemeFontSizeOverride("font_size", 22); _title.AddThemeColorOverride("font_color", UiStyle.AccentText); c.AddChild(_title);
        _to = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _to.AddThemeFontSizeOverride("font_size", 18); c.AddChild(_to);
        _amount = new SpinBox { MinValue = 1, MaxValue = 10_000_000, Step = 1, Value = 1, Suffix = "moedas", CustomMinimumSize = new Vector2(0, 42), SelectAllOnFocus = true }; c.AddChild(_amount);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 40) }; _status.AddThemeFontSizeOverride("font_size", 16); _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); c.AddChild(_status);
        _pay = new Button { Text = " Pagar", Icon = UiStyle.Icon("check", UiStyle.Text, 22), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 46) }; UiStyle.StyleButton(_pay, UiStyle.ButtonKind.Primary, 12, 6, 14); _pay.AddThemeFontSizeOverride("font_size", 20); _pay.Pressed += Pay; c.AddChild(_pay);
        _http = new HttpRequest { Timeout = 15 }; AddChild(_http); _http.RequestCompleted += OnCompleted;
    }

    public void Open(BuildObjectState state, string ownerName)
    {
        _owner = state.OwnerUserId; _key = "pay-" + Guid.NewGuid().ToString("N"); var (mode, price) = BuildObjectProperties.SaleOf(state);
        _title.Text = $"Pagar: {state.Name}"; _to.Text = $"Quem recebe: {ownerName}"; _amount.Value = mode != "none" && price > 0 ? price : 1; _status.Text = string.Empty; _pay.Disabled = false; _window.Visible = true; _window.MoveToFront();
    }

    private void Pay()
    {
        var amount = (long)_amount.Value; if (amount <= 0) { _status.Text = "Escolha um valor maior que zero."; return; }
        LastRequestForTesting = (_owner, amount, _key); _pay.Disabled = true; _status.Text = "Pagando…";
        if (TransferOverride is not null) { var (status, body) = TransferOverride(_owner, amount, _key); Finish(status, body); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null || _http is null) { _status.Text = "Entre para pagar."; _pay.Disabled = false; return; }
        var body2 = JsonSerializer.Serialize(new { recipientUserId = _owner, amountCoins = amount, idempotencyKey = _key });
        if (_http.Request($"{app.ApiBaseUrl}/api/v1/economy/transfer", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body2) != Error.Ok) { _status.Text = "Não foi possível falar com o servidor."; _pay.Disabled = false; }
    }

    private void OnCompleted(long result, long code, string[] headers, byte[] body) => Finish(result == (long)HttpRequest.Result.Success ? code : 0, Encoding.UTF8.GetString(body));

    private void Finish(long status, string body)
    {
        if (status is >= 200 and < 300) { _status.Text = $"Pagamento de {((long)_amount.Value).ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} moedas enviado."; return; }
        _pay.Disabled = false; _key = "pay-" + Guid.NewGuid().ToString("N");
        string? error = null; try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString(); } catch (JsonException) { }
        _status.Text = error switch { "insufficient_balance" => "Saldo insuficiente.", "recipient_not_found" => "Quem receberia não foi encontrado.", "cannot_transfer_to_self" => "Este objeto é seu.", _ => status == 0 ? "Não foi possível falar com o servidor." : $"O servidor recusou o pagamento ({status})." };
    }
}

/// <summary>"Abrir objeto": the things the object carries, by name and kind. The owner can ask for a copy to the inventory.</summary>
public partial class ObjectContentsWindow : Node
{
    private FloatingWindow _window = null!;
    private Label _title = null!, _note = null!;
    private ItemList _list = null!;
    private Button _copy = null!;
    private readonly List<Guid> _assets = [];
    private bool _mine;

    public bool IsOpen => _window is not null && _window.Visible;
    public IReadOnlyList<string> RowsForTesting => [.. Enumerable.Range(0, _list.ItemCount).Select(_list.GetItemText)];
    public string NoteForTesting => _note.Text;
    public bool CopyVisibleForTesting => _copy.Visible;

    public override void _Ready()
    {
        _window = new FloatingWindow("Conteúdo do objeto", icon: "folder") { Name = "ContentsWindow", Visible = false, Theme = ViewerTheme.Create() }; _window.Place(.5f, .5f, -250, -200, 250, 200); AddChild(_window);
        _window.CloseRequested += () => _window.Visible = false;
        var c = _window.Content; c.AddThemeConstantOverride("separation", 8);
        _title = new Label(); _title.AddThemeFontOverride("font", UiStyle.Bold); _title.AddThemeFontSizeOverride("font_size", 22); _title.AddThemeColorOverride("font_color", UiStyle.AccentText); c.AddChild(_title);
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200), SelectMode = ItemList.SelectModeEnum.Single }; _list.AddThemeFontSizeOverride("font_size", 18); c.AddChild(_list);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _note.AddThemeFontSizeOverride("font_size", 16); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); c.AddChild(_note);
        _copy = new Button { Text = " Copiar para o inventário", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), Visible = false }; UiStyle.StyleButton(_copy, UiStyle.ButtonKind.Secondary, 12, 6, 14); _copy.AddThemeFontSizeOverride("font_size", 18); _copy.Pressed += CopySelected; c.AddChild(_copy);
    }

    private static string Kind(string category) => category switch { "scripts" => "Script", "sounds" => "Som", "textures" => "Textura", "animations" => "Animação", "models" => "Modelo 3D", "objects" => "Objeto", "notecards" => "Nota", "packages" => "Construção", _ => "Item" };

    public void Open(BuildObjectState state, bool mine)
    {
        _mine = mine; _assets.Clear(); _list.Clear(); _title.Text = state.Name;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(state.ContentJson) ? "[]" : state.ContentJson);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? "Item" : "Item"; var kind = Kind(entry.TryGetProperty("category", out var k) ? k.GetString() ?? "" : "");
                _list.AddItem($"{name}  ·  {kind}"); _assets.Add(entry.TryGetProperty("assetId", out var a) && Guid.TryParse(a.GetString(), out var id) ? id : Guid.Empty);
            }
        }
        catch (JsonException) { }
        _note.Text = _list.ItemCount == 0 ? "Este objeto não guarda nada." : mine ? "Escolha um item para copiá-lo ao seu inventário." : "Você pode ver o que o objeto guarda; só o dono tira cópias daqui.";
        _copy.Visible = mine && _list.ItemCount > 0; _window.Visible = true; _window.MoveToFront();
    }

    private void CopySelected()
    {
        var picked = _list.GetSelectedItems(); if (picked.Length != 1 || _assets[picked[0]] == Guid.Empty) { _note.Text = "Escolha um item da lista."; return; }
        var inventory = GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");
        _note.Text = inventory is null ? "O inventário não está disponível." : inventory.RunOnAsset(_assets[picked[0]], "copy") ?? "Cópia pedida ao inventário.";
    }
}
