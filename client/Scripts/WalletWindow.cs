using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The wallet of VerseCoin (VC$): a window with a side menu (buy VerseCoin, my balance, transfer, history, help) and the balance always in view. Everything shown comes from
/// the economy API (the balance, the packs, the history, the transfers): the client never decides a balance, it only asks for it again after each operation. Buying VerseCoin
/// creates an order that the server only completes when a payment gateway confirms the payment; without a gateway the server says so and nothing is charged.
/// </summary>
public sealed partial class WalletWindow : Control
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] PageNames = ["Comprar VerseCoin", "Meu Saldo", "Transferir VerseCoin", "Histórico de Transações", "Ajuda"];
    private static readonly (string Id, string Label, string Detail)[] Methods =
    [
        ("pix", "Pix", "Aprovação imediata"), ("card", "Cartão de Crédito", "Em até 12x (conforme operadora)"), ("mercadopago", "Mercado Pago", "Pague na sua conta"),
        ("asaas", "Asaas", "Boleto, Pix ou cartão"), ("pagbank", "PagBank", "Pague pelo PagBank")
    ];

    private readonly List<Button> _menu = [];
    private readonly List<Control> _pages = [];
    private Label _headerTitle = null!, _headerText = null!, _balance = null!;
    private PanelContainer _panel = null!;
    private int _page;
    public long Balance { get; private set; } = -1;
    public bool IsOpen => Visible;
    /// <summary>Tests: answers requests without a network, (method, path, body) → (status, body).</summary>
    public Func<Godot.HttpClient.Method, string, string?, (int Status, string Body)>? Transport { get; set; }
    /// <summary>The balance changed (the top bar shows it).</summary>
    public event Action<long>? BalanceChanged;

    // buy
    private record Pack(string Id, string Name, long Coins, int BonusPercent, long BonusCoins, long TotalCoins, long PriceCents);
    private readonly List<Pack> _packs = [];
    private int _pack = -1; private string _method = "pix"; private bool _paymentsEnabled;
    private VBoxContainer _packList = null!, _summary = null!, _methodList = null!;
    private Label _buyStatus = null!; private Button _confirmBuy = null!;
    // overview, history
    private VBoxContainer _overview = null!, _history = null!;
    // transfer
    private LineEdit _toName = null!, _amount = null!, _message = null!; private Label _transferStatus = null!, _transferSummary = null!; private Button _send = null!;
    private PersonActions _people = null!; private string _transferKey = Guid.NewGuid().ToString("N");

    public override void _Ready()
    {
        Visible = false; Name = string.IsNullOrEmpty(Name) || Name.ToString().StartsWith('@') ? "WalletWindow" : Name; SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = MouseFilterEnum.Stop }; dim.SetAnchorsPreset(LayoutPreset.FullRect); dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); }; AddChild(dim);
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(1180, 700) }; _panel.AddThemeStyleboxOverride("panel", UiStyle.Glass(.97f, UiStyle.RadiusLarge, 0)); var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center); center.AddChild(_panel);
        var root = new HBoxContainer(); root.AddThemeConstantOverride("separation", 0); _panel.AddChild(root);

        // side menu
        var side = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) }; side.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(UiStyle.TitleFill, .6f), BorderColor = UiStyle.Divider, BorderWidthRight = 1, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 18, ContentMarginBottom = 18, CornerRadiusTopLeft = UiStyle.RadiusLarge, CornerRadiusBottomLeft = UiStyle.RadiusLarge });
        root.AddChild(side); var sideBox = new VBoxContainer(); sideBox.AddThemeConstantOverride("separation", 8); side.AddChild(sideBox);
        var brand = new Label { Text = "∞ LifeVerse" }; brand.AddThemeFontOverride("font", UiStyle.Bold); brand.AddThemeFontSizeOverride("font_size", 26); brand.AddThemeColorOverride("font_color", UiStyle.Accent); sideBox.AddChild(brand); sideBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
        string[] icons = ["bag", "wallet", "coins", "history", "info"];
        for (var i = 0; i < PageNames.Length; i++)
        {
            var index = i; var button = new Button { Text = "  " + PageNames[i], Alignment = HorizontalAlignment.Left, ToggleMode = true, CustomMinimumSize = new Vector2(0, 46), FocusMode = FocusModeEnum.None };
            button.Pressed += () => Show(index); _menu.Add(button); sideBox.AddChild(button);
        }
        // the content
        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; main.AddThemeConstantOverride("separation", 0); root.AddChild(main);
        var header = new MarginContainer(); foreach (var m in new[] { "left", "right", "top", "bottom" }) header.AddThemeConstantOverride("margin_" + m, 22); main.AddChild(header);
        var headerRow = new HBoxContainer(); headerRow.AddThemeConstantOverride("separation", 16); header.AddChild(headerRow);
        var coin = new CoinIcon { CustomMinimumSize = new Vector2(60, 60) }; headerRow.AddChild(coin);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; headerRow.AddChild(titles);
        _headerTitle = new Label(); _headerTitle.AddThemeFontOverride("font", UiStyle.Bold); _headerTitle.AddThemeFontSizeOverride("font_size", 30); titles.AddChild(_headerTitle);
        _headerText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _headerText.AddThemeColorOverride("font_color", UiStyle.TextMuted); _headerText.AddThemeFontSizeOverride("font_size", 14); titles.AddChild(_headerText);
        var balanceCard = new PanelContainer { CustomMinimumSize = new Vector2(230, 0) }; balanceCard.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.AccentFill, .35f), UiStyle.Accent, 14, 1, 16, 10)); headerRow.AddChild(balanceCard);
        var bb = new VBoxContainer(); balanceCard.AddChild(bb); var bl = new Label { Text = "Seu saldo atual" }; bl.AddThemeFontSizeOverride("font_size", 13); bb.AddChild(bl);
        _balance = new Label { Text = "VC$ —" }; _balance.AddThemeFontOverride("font", UiStyle.Bold); _balance.AddThemeFontSizeOverride("font_size", 28); bb.AddChild(_balance);
        var close = new Button { Text = "✕", Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 36), SizeFlagsVertical = SizeFlags.ShrinkBegin }; close.AddThemeFontSizeOverride("font_size", 22); close.Pressed += Close; headerRow.AddChild(close);
        var body = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true }; main.AddChild(body);
        _pages.Add(BuildBuy(body)); _pages.Add(BuildOverview(body)); _pages.Add(BuildTransfer(body)); _pages.Add(BuildHistory(body)); _pages.Add(BuildHelp(body));
        foreach (var button in _menu) UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 14, 8, 10);
        BuildReceipt();
        _people = new PersonActions { Name = "WalletPeople" }; AddChild(_people); _people.Setup(this); _people.Said += text => { if (IsInstanceValid(_transferStatus)) _transferStatus.Text = text; };
        Show(0);
    }

    public override void _UnhandledKeyInput(InputEvent @event) { if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Close(); GetViewport().SetInputAsHandled(); } }

    public int PackCountForTesting => _packs.Count;
    public string BuyStatusForTesting => _buyStatus.Text;
    public int HistoryRowsForTesting => _history.GetChildCount();
    public void ConfirmBuyForTesting() => ConfirmBuy();
    public void SelectPackForTesting(int index) { _pack = index; RenderBuy(); }

    public void Open(int page = 0) { Visible = true; MoveToFront(); Show(page); }
    public void Close() => Visible = false;
    public void Toggle(int page = 1) { if (Visible) Close(); else Open(page); }

    private void Show(int page)
    {
        _page = page; for (var i = 0; i < _pages.Count; i++) { _pages[i].Visible = i == page; _menu[i].SetPressedNoSignal(i == page); }
        var (title, text) = page switch
        {
            0 => ("Comprar VerseCoin", "Adquira VerseCoin (VC$) e use em todo o universo LifeVerse: lojas, terrenos, objetos, roupas, serviços, aluguéis e muito mais."),
            1 => ("Meu Saldo", "Seu saldo e as últimas movimentações da sua carteira."),
            2 => ("Transferir VerseCoin", "Envie VerseCoin (VC$) para outros jogadores do LifeVerse de forma rápida e segura."),
            3 => ("Histórico de Transações", "Tudo o que entrou e saiu da sua carteira, com o estado de cada operação."),
            _ => ("Ajuda", "Como a carteira funciona.")
        };
        _headerTitle.Text = title; _headerText.Text = text;
        Refresh();
    }

    // ---- the API ------------------------------------------------------------------------------------------------------------------------

    private void Call(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method, path, body); done(status, text); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { done(401, "{}"); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, Encoding.UTF8.GetString(bytes)); };
        var headers = new List<string> { $"Authorization: Bearer {session.AccessToken}" }; if (body is not null) headers.Add("Content-Type: application/json");
        if (http.Request(app.ApiBaseUrl + path, [.. headers], method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, "{}"); }
    }

    public static string Coins(long value) => "VC$ " + value.ToString("N0", Pt);
    public static string Reais(long cents) => "R$ " + (cents / 100m).ToString("N2", Pt);

    /// <summary>Asks the server for what the open page shows.</summary>
    public void Refresh()
    {
        Call(Godot.HttpClient.Method.Get, "/api/v1/economy/wallet", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { if (IsInstanceValid(_balance)) _balance.Text = "VC$ —"; return; }
            try { using var d = JsonDocument.Parse(body); Balance = d.RootElement.GetProperty("availableCoins").GetInt64(); _balance.Text = Coins(Balance); BalanceChanged?.Invoke(Balance); } catch (Exception e) when (e is JsonException or KeyNotFoundException) { }
            if (_page == 2) UpdateTransferSummary();
        });
        if (_page == 0) LoadPackages();
        if (_page == 1) { LoadSummary(); LoadOverview(); }
        if (_page == 3) LoadHistory();
    }

    private static readonly Dictionary<string, string> TypeLabels = new() { ["transfer"] = "Transferência", ["user_transfer"] = "Transferência", ["admin_adjustment"] = "Ajuste da administração", ["coin_refund"] = "Estorno de compra", ["purchase"] = "Compra", ["sale"] = "Venda", ["rental"] = "Aluguel", ["land"] = "Terreno", ["fee"] = "Taxa", ["refund"] = "Reembolso", ["coin_purchase"] = "Compra de VerseCoin" };
    private static readonly Dictionary<string, string> StatusLabels = new() { ["completed"] = "Concluída", ["pending"] = "Pendente", ["failed"] = "Falhou", ["reversed"] = "Estornada" };

    private void FillRows(VBoxContainer box, string status, string body, int limit, string empty)
    {
        foreach (var child in box.GetChildren()) child.QueueFree();
        if (status is "0") { box.AddChild(Muted("Não foi possível carregar as transações agora.")); return; }
        try
        {
            using var d = JsonDocument.Parse(body); var rows = d.RootElement.EnumerateArray().ToList();
            if (rows.Count == 0) { box.AddChild(Muted(empty)); return; }
            box.AddChild(Row("Data", "Tipo", "Valor", "Situação", true, null, null));
            foreach (var r in rows.Take(limit))
            {
                var type = r.GetProperty("type").GetString() ?? ""; var st = r.GetProperty("status").GetString() ?? ""; var when = r.TryGetProperty("createdAt", out var c) && c.TryGetDateTimeOffset(out var t) ? t.LocalDateTime.ToString("dd/MM/yyyy HH:mm", Pt) : "";
                var amount = r.GetProperty("amount").GetInt64(); var dir = r.TryGetProperty("direction", out var dv) ? dv.GetString() : null; var id = r.TryGetProperty("id", out var iv) && Guid.TryParse(iv.GetString(), out var g) ? g : (Guid?)null;
                var row = Row(when, TypeLabels.GetValueOrDefault(type, type), dir == "in" ? "+ " + Coins(Math.Abs(amount)) : dir == "out" ? "− " + Coins(Math.Abs(amount)) : Coins(amount), StatusLabels.GetValueOrDefault(st, st), false, id, id is { } rid ? () => OpenReceipt(rid) : null);
                if (dir is "in" or "out") row.GetChild<Label>(2).AddThemeColorOverride("font_color", dir == "in" ? UiStyle.SuccessBright : UiStyle.DangerBright);
                box.AddChild(row);
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { box.AddChild(Muted("Resposta inesperada do servidor.")); }
    }

    private void LoadOverview() => Call(Godot.HttpClient.Method.Get, "/api/v1/economy/wallet/transactions", null, (status, body) => FillRows(_overview, status is >= 200 and < 300 ? "ok" : "0", body, 6, "Nenhuma movimentação ainda."));

    private static readonly (string Label, int? Days)[] Periods = [("Todo o período", null), ("Hoje", 1), ("Últimos 7 dias", 7), ("Últimos 30 dias", 30)];
    private static readonly (string Label, string? Id)[] Kinds = [("Todos os tipos", null), ("Transferências", "transfer"), ("Compras e vendas", "market"), ("Terrenos e aluguéis", "property"), ("VerseCoin", "coins"), ("Ajustes", "adjustment")];
    private static readonly (string Label, string? Id)[] Directions = [("Entradas e saídas", null), ("Só entradas", "in"), ("Só saídas", "out")];
    private OptionButton _period = null!, _kind = null!, _direction = null!;
    public string HistoryQueryForTesting => HistoryQuery();

    private string HistoryQuery()
    {
        var parts = new List<string>(); if (Periods[Math.Max(0, _period.Selected)].Days is { } days) parts.Add("days=" + days);
        if (Kinds[Math.Max(0, _kind.Selected)].Id is { } kind) parts.Add("kind=" + kind); if (Directions[Math.Max(0, _direction.Selected)].Id is { } dir) parts.Add("direction=" + dir);
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private void LoadHistory() => Call(Godot.HttpClient.Method.Get, "/api/v1/economy/wallet/transactions" + HistoryQuery(), null, (status, body) => FillRows(_history, status is >= 200 and < 300 ? "ok" : "0", body, 100, "Nada com esses filtros."));
    public void SelectHistoryFiltersForTesting(int period, int kind, int direction) { _period.Select(period); _kind.Select(kind); _direction.Select(direction); LoadHistory(); }

    private VBoxContainer _summaryBox = null!;
    private void LoadSummary() => Call(Godot.HttpClient.Method.Get, "/api/v1/economy/wallet/summary?days=30", null, (status, body) =>
    {
        foreach (var child in _summaryBox.GetChildren()) child.QueueFree(); _summaryBox.AddChild(Heading("Últimos 30 dias"));
        if (status is < 200 or >= 300) { _summaryBox.AddChild(Muted("Não foi possível carregar o resumo agora.")); return; }
        try
        {
            using var d = JsonDocument.Parse(body); var r = d.RootElement;
            void Line(string text, long value, Color? color = null) { var row = new HBoxContainer(); var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; var v = new Label { Text = Coins(value) }; v.AddThemeFontOverride("font", UiStyle.Bold); if (color is { } c) v.AddThemeColorOverride("font_color", c); row.AddChild(l); row.AddChild(v); _summaryBox.AddChild(row); }
            Line("Recebido", r.GetProperty("received").GetInt64(), UiStyle.SuccessBright); Line("Gasto", r.GetProperty("spent").GetInt64(), UiStyle.DangerBright);
            Line("Disponível", r.GetProperty("available").GetInt64()); Line("Reservado", r.GetProperty("reserved").GetInt64(), UiStyle.Orange); Line("Total", r.GetProperty("total").GetInt64(), UiStyle.AccentText);
            _summaryBox.AddChild(Muted($"{r.GetProperty("count").GetInt32()} movimentações concluídas."));
            if (r.TryGetProperty("isBlocked", out var blocked) && blocked.ValueKind == JsonValueKind.True) _summaryBox.AddChild(Muted("Sua carteira está bloqueada. Fale com o suporte."));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { _summaryBox.AddChild(Muted("Resposta inesperada do servidor.")); }
    });
    public int SummaryLinesForTesting => _summaryBox.GetChildCount();

    // ---- the receipt of one entry
    private Control _receiptLayer = null!; private Label _receiptText = null!; private string _receiptCode = "";
    public string ReceiptTextForTesting => _receiptText.Text;
    public bool ReceiptOpenForTesting => _receiptLayer.Visible;
    public void OpenReceipt(Guid id) => Call(Godot.HttpClient.Method.Get, $"/api/v1/economy/wallet/transactions/{id}", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { _receiptText.Text = "Não foi possível abrir o comprovante agora."; _receiptLayer.Visible = true; return; }
        try
        {
            using var d = JsonDocument.Parse(body); var r = d.RootElement; var dir = r.GetProperty("direction").GetString(); var type = r.GetProperty("type").GetString() ?? ""; var st = r.GetProperty("status").GetString() ?? "";
            string Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var when = r.TryGetProperty("createdAt", out var c) && c.TryGetDateTimeOffset(out var t) ? t.LocalDateTime.ToString("dd/MM/yyyy HH:mm", Pt) : "";
            _receiptCode = Str("code"); var sb = new StringBuilder();
            sb.AppendLine(TypeLabels.GetValueOrDefault(type, type)).AppendLine();
            sb.AppendLine($"Valor: {(dir == "in" ? "+ " : "− ")}{Coins(Math.Abs(r.GetProperty("amount").GetInt64()))}");
            if (Str("counterparty") is { Length: > 0 } other) sb.AppendLine($"{(dir == "in" ? "De" : "Para")}: {other}");
            if (r.TryGetProperty("fee", out var fee) && fee.ValueKind == JsonValueKind.Number) sb.AppendLine($"Taxa da plataforma: {Coins(fee.GetInt64())}");
            if (Str("message") is { Length: > 0 } msg) sb.AppendLine($"Mensagem: {msg}");
            sb.AppendLine($"Data: {when}").AppendLine($"Situação: {StatusLabels.GetValueOrDefault(st, st)}").AppendLine($"Código: {_receiptCode}");
            _receiptText.Text = sb.ToString().TrimEnd(); _receiptLayer.Visible = true;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { _receiptText.Text = "Resposta inesperada do servidor."; _receiptLayer.Visible = true; }
    });

    private void BuildReceipt()
    {
        _receiptLayer = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop }; _receiptLayer.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_receiptLayer);
        var dim = new ColorRect { Color = new Color(0, 0, 0, .5f) }; dim.SetAnchorsPreset(LayoutPreset.FullRect); _receiptLayer.AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsPreset(LayoutPreset.FullRect); _receiptLayer.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) }; panel.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.PanelFillSolid, UiStyle.Accent, 14, 1, 22, 18)); center.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        box.AddChild(Heading("Comprovante")); _receiptText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(_receiptText);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); box.AddChild(buttons);
        var copy = new Button { Text = "Copiar código", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None }; UiStyle.StyleButton(copy, UiStyle.ButtonKind.Tool, 12, 6, 8); copy.Pressed += () => DisplayServer.ClipboardSet(_receiptCode); buttons.AddChild(copy);
        var close = new Button { Text = "Fechar", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None }; UiStyle.StyleButton(close, UiStyle.ButtonKind.Primary, 12, 6, 8); close.Pressed += () => _receiptLayer.Visible = false; buttons.AddChild(close);
    }

    // ---- the pages ----------------------------------------------------------------------------------------------------------------------

    private static Label Muted(string text) { var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; l.AddThemeColorOverride("font_color", UiStyle.TextMuted); return l; }
    private static Label Heading(string text) { var l = new Label { Text = text }; l.AddThemeFontOverride("font", UiStyle.Bold); l.AddThemeFontSizeOverride("font_size", 20); return l; }

    private static HBoxContainer Row(string a, string b, string c, string d, bool head, Guid? id, Action? receipt)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        foreach (var (text, width) in new[] { (a, 170), (b, 190), (c, 150), (d, 120) })
        {
            var l = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) }; l.AddThemeFontSizeOverride("font_size", head ? 13 : 15); l.AddThemeColorOverride("font_color", head ? UiStyle.AccentText : UiStyle.Text); row.AddChild(l);
        }
        if (receipt is not null) { var see = new Button { Text = "Comprovante", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 30) }; see.AddThemeFontSizeOverride("font_size", 13); UiStyle.StyleButton(see, UiStyle.ButtonKind.Tool, 8, 3, 8); see.Pressed += receipt; row.AddChild(see); }
        return row;
    }

    private static PanelContainer Card(VBoxContainer inner) { var c = new PanelContainer(); c.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .55f), new Color(UiStyle.Divider, .8f), 12, 1, 18, 14)); c.AddChild(inner); return c; }

    private Control Page(Control parent, out VBoxContainer left, out VBoxContainer right)
    {
        var margin = new MarginContainer { Visible = false }; margin.SetAnchorsPreset(LayoutPreset.FullRect); foreach (var m in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + m, 22); margin.AddThemeConstantOverride("margin_top", 0);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 18); margin.AddChild(row);
        left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.45f }; left.AddThemeConstantOverride("separation", 12); row.AddChild(left);
        right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 12); row.AddChild(right);
        parent.AddChild(margin); return margin;
    }

    private Control BuildBuy(Control parent)
    {
        var page = Page(parent, out var left, out var right);
        left.AddChild(Heading("Escolha um pacote")); _packList = new VBoxContainer(); _packList.AddThemeConstantOverride("separation", 8); left.AddChild(_packList);
        _summary = new VBoxContainer(); _summary.AddThemeConstantOverride("separation", 6); right.AddChild(Card(_summary));
        right.AddChild(Heading("Forma de pagamento")); _methodList = new VBoxContainer(); _methodList.AddThemeConstantOverride("separation", 4); right.AddChild(_methodList);
        _buyStatus = Muted(""); _buyStatus.AddThemeColorOverride("font_color", UiStyle.Orange); right.AddChild(_buyStatus);
        _confirmBuy = new Button { Text = "Confirmar compra", CustomMinimumSize = new Vector2(0, 48), FocusMode = FocusModeEnum.None, Disabled = true }; UiStyle.StyleButton(_confirmBuy, UiStyle.ButtonKind.Primary, 14, 10, 10); _confirmBuy.Pressed += ConfirmBuy; right.AddChild(_confirmBuy);
        return page;
    }

    private void LoadPackages() => Call(Godot.HttpClient.Method.Get, "/api/v1/economy/packages", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { _buyStatus.Text = "Não foi possível carregar os pacotes agora."; return; }
        try
        {
            using var d = JsonDocument.Parse(body); _paymentsEnabled = d.RootElement.GetProperty("paymentsEnabled").GetBoolean(); _packs.Clear();
            foreach (var p in d.RootElement.GetProperty("packages").EnumerateArray())
                _packs.Add(new Pack(p.GetProperty("id").GetString()!, p.GetProperty("name").GetString()!, p.GetProperty("coins").GetInt64(), p.GetProperty("bonusPercent").GetInt32(), p.GetProperty("bonusCoins").GetInt64(), p.GetProperty("totalCoins").GetInt64(), p.GetProperty("priceCents").GetInt64()));
            if (_pack < 0 || _pack >= _packs.Count) _pack = _packs.Count > 2 ? 2 : 0;
            RenderBuy();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { _buyStatus.Text = "Resposta inesperada do servidor."; }
    });

    private void RenderBuy()
    {
        foreach (var child in _packList.GetChildren()) child.QueueFree(); foreach (var child in _methodList.GetChildren()) child.QueueFree(); foreach (var child in _summary.GetChildren()) child.QueueFree();
        for (var i = 0; i < _packs.Count; i++)
        {
            var index = i; var p = _packs[i]; var selected = i == _pack;
            var card = new Button { ToggleMode = true, ButtonPressed = selected, CustomMinimumSize = new Vector2(0, 62), FocusMode = FocusModeEnum.None, Text = "" }; card.SetPressedNoSignal(selected);
            UiStyle.StyleButton(card, selected ? UiStyle.ButtonKind.ToolSelected : UiStyle.ButtonKind.Tool, 16, 8, 12); card.Pressed += () => { _pack = index; RenderBuy(); };
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.SetAnchorsPreset(LayoutPreset.FullRect); row.OffsetLeft = 16; row.OffsetRight = -16; row.AddThemeConstantOverride("separation", 12); card.AddChild(row);
            var name = new Label { Text = $"{p.Name}\n{Coins(p.Coins)}", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center }; name.AddThemeFontOverride("font", UiStyle.Bold); row.AddChild(name);
            if (p.BonusPercent > 0) { var bonus = new Label { Text = $"+ {p.BonusPercent}% BÔNUS\nTotal: {Coins(p.TotalCoins)}", HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center }; bonus.AddThemeColorOverride("font_color", UiStyle.Orange); bonus.AddThemeFontSizeOverride("font_size", 14); row.AddChild(bonus); }
            var price = new Label { Text = Reais(p.PriceCents), CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center }; price.AddThemeFontOverride("font", UiStyle.Bold); price.AddThemeFontSizeOverride("font_size", 20); row.AddChild(price);
            _packList.AddChild(card);
        }
        if (_pack >= 0 && _pack < _packs.Count)
        {
            var p = _packs[_pack]; _summary.AddChild(Heading("Resumo da compra"));
            void Line(string a, string b, Color? color = null, int size = 15) { var r = new HBoxContainer(); var l = new Label { Text = a, SizeFlagsHorizontal = SizeFlags.ExpandFill }; l.AddThemeFontSizeOverride("font_size", size); var v = new Label { Text = b }; v.AddThemeFontSizeOverride("font_size", size); v.AddThemeFontOverride("font", UiStyle.Bold); if (color is { } c) v.AddThemeColorOverride("font_color", c); r.AddChild(l); r.AddChild(v); _summary.AddChild(r); }
            Line("Pacote selecionado:", p.Name); Line("VerseCoin:", Coins(p.Coins)); if (p.BonusPercent > 0) Line($"Bônus ({p.BonusPercent}%):", "+ " + Coins(p.BonusCoins), UiStyle.AccentText);
            Line("Total a receber:", Coins(p.TotalCoins), UiStyle.AccentText, 20); Line("Valor a pagar:", Reais(p.PriceCents), UiStyle.Orange, 20);
        }
        foreach (var (id, label, detail) in Methods)
        {
            var captured = id; var on = id == _method;
            var b = new Button { Text = $"{label}  ·  {detail}", Alignment = HorizontalAlignment.Left, ToggleMode = true, CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None }; b.SetPressedNoSignal(on);
            UiStyle.StyleButton(b, on ? UiStyle.ButtonKind.ToolSelected : UiStyle.ButtonKind.Tool, 12, 6, 10); b.Pressed += () => { _method = captured; RenderBuy(); }; _methodList.AddChild(b);
        }
        _confirmBuy.Disabled = _pack < 0; const string off = "Pagamentos ainda não habilitados neste servidor."; if (!_paymentsEnabled) _buyStatus.Text = off; else if (_buyStatus.Text == off) _buyStatus.Text = "";
    }

    private void ConfirmBuy()
    {
        if (_pack < 0 || _pack >= _packs.Count) return; _confirmBuy.Disabled = true; _buyStatus.Text = "Criando o pedido…";
        Call(Godot.HttpClient.Method.Post, "/api/v1/economy/coin-orders", JsonSerializer.Serialize(new { packageId = _packs[_pack].Id, method = _method, idempotencyKey = Guid.NewGuid().ToString("N") }), (status, body) =>
        {
            _confirmBuy.Disabled = false;
            _buyStatus.Text = status switch { 409 => "Sua carteira está bloqueada. Fale com o suporte.", 503 => "Os pagamentos ainda não foram habilitados neste servidor. Nenhuma cobrança foi feita.", >= 200 and < 300 => "Pedido criado. Aguardando a confirmação do pagamento…", 404 => "Esse pacote não está mais disponível.", 401 => "Entre na sua conta para comprar.", _ => "Não foi possível criar o pedido agora." };
            if (status is >= 200 and < 300) { try { using var d = JsonDocument.Parse(body); WatchOrder(d.RootElement.GetProperty("id").GetGuid()); } catch (Exception e) when (e is JsonException or KeyNotFoundException) { } }
        });
    }

    /// <summary>After an order is made the server is asked about it until the payment is confirmed (or the order closes); the coins appear only when the server says so.</summary>
    private void WatchOrder(Guid id, int attempt = 0)
    {
        if (attempt > 90 || !IsInstanceValid(this)) return;
        Call(Godot.HttpClient.Method.Get, $"/api/v1/economy/coin-orders/{id}", null, (status, body) =>
        {
            string? state = null;
            try { if (status is >= 200 and < 300) { using var d = JsonDocument.Parse(body); state = d.RootElement.GetProperty("status").GetString(); } } catch (Exception e) when (e is JsonException or KeyNotFoundException) { }
            if (state is null or "pending") { if (Transport is null && IsInstanceValid(this)) GetTree().CreateTimer(4).Timeout += () => WatchOrder(id, attempt + 1); return; }
            _buyStatus.Text = state == "paid" ? "Pagamento confirmado! O VerseCoin já está na sua carteira." : "O pedido foi encerrado sem pagamento (" + state + ").";
            Refresh();
        });
    }

    private Control BuildOverview(Control parent)
    {
        var page = Page(parent, out var left, out var right); left.AddChild(Heading("Últimas transações")); _overview = new VBoxContainer(); _overview.AddThemeConstantOverride("separation", 6); left.AddChild(Card(_overview));
        _summaryBox = new VBoxContainer(); _summaryBox.AddThemeConstantOverride("separation", 6); right.AddChild(Card(_summaryBox));
        var actions = new VBoxContainer(); actions.AddThemeConstantOverride("separation", 8); actions.AddChild(Heading("Ações"));
        var buy = new Button { Text = "Comprar VerseCoin", CustomMinimumSize = new Vector2(0, 44), FocusMode = FocusModeEnum.None }; UiStyle.StyleButton(buy, UiStyle.ButtonKind.Primary, 12, 8, 10); buy.Pressed += () => Show(0);
        var send = new Button { Text = "Enviar VerseCoin", CustomMinimumSize = new Vector2(0, 44), FocusMode = FocusModeEnum.None }; UiStyle.StyleButton(send, UiStyle.ButtonKind.Tool, 12, 8, 10); send.Pressed += () => Show(2);
        actions.AddChild(buy); actions.AddChild(send); right.AddChild(Card(actions)); return page;
    }

    private Control BuildHistory(Control parent)
    {
        var page = Page(parent, out var left, out var right); right.Visible = false; left.SizeFlagsStretchRatio = 1;
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 8); left.AddChild(filters);
        OptionButton Filter((string Label, string? Id)[] items) { var o = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None }; foreach (var i in items) o.AddItem(i.Label); o.ItemSelected += _ => LoadHistory(); filters.AddChild(o); return o; }
        _period = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None }; foreach (var p in Periods) _period.AddItem(p.Label); _period.ItemSelected += _ => LoadHistory(); filters.AddChild(_period);
        _kind = Filter(Kinds); _direction = Filter(Directions);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(scroll);
        _history = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _history.AddThemeConstantOverride("separation", 6); scroll.AddChild(_history); return page;
    }

    private Control BuildTransfer(Control parent)
    {
        var page = Page(parent, out var left, out var right);
        left.AddChild(Heading("Destinatário")); _toName = new LineEdit { PlaceholderText = "Nome exato do jogador", CustomMinimumSize = new Vector2(0, 42), MaxLength = 64 }; _toName.TextChanged += _ => UpdateTransferSummary(); left.AddChild(_toName);
        left.AddChild(Heading("Valor a transferir")); _amount = new LineEdit { PlaceholderText = "VC$ 0", CustomMinimumSize = new Vector2(0, 42), MaxLength = 12 }; _amount.TextChanged += _ => UpdateTransferSummary(); left.AddChild(_amount);
        var quick = new HBoxContainer(); quick.AddThemeConstantOverride("separation", 8); foreach (var v in new long[] { 100, 250, 500, 1000 }) { var value = v; var b = new Button { Text = Coins(v), CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Tool, 8, 6, 8); b.Pressed += () => _amount.Text = value.ToString(); quick.AddChild(b); } left.AddChild(quick);
        left.AddChild(Heading("Mensagem (opcional)")); _message = new LineEdit { PlaceholderText = "Ex.: Aqui está o combinado", CustomMinimumSize = new Vector2(0, 42), MaxLength = 200 }; _message.TextChanged += _ => UpdateTransferSummary(); left.AddChild(_message);
        _transferStatus = Muted(""); _transferStatus.AddThemeColorOverride("font_color", UiStyle.Orange); left.AddChild(_transferStatus);
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 8); inner.AddChild(Heading("Resumo da transferência")); _transferSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; inner.AddChild(_transferSummary); right.AddChild(Card(inner));
        right.AddChild(Muted("A transferência é instantânea, o saldo é conferido no servidor e a operação fica registrada no histórico da sua conta."));
        _send = new Button { Text = "Enviar VC$", CustomMinimumSize = new Vector2(0, 48), FocusMode = FocusModeEnum.None, Disabled = true }; UiStyle.StyleButton(_send, UiStyle.ButtonKind.Primary, 14, 10, 10); _send.Pressed += SendTransfer; right.AddChild(_send);
        UpdateTransferSummary(); return page;
    }

    private bool TryAmount(out long amount) => long.TryParse(_amount.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount > 0;

    private void UpdateTransferSummary()
    {
        if (!IsInstanceValid(_toName)) return; var ok = TryAmount(out var amount); var name = _toName.Text.Trim();
        _transferSummary.Text = $"Destinatário: {(name.Length > 0 ? name : "—")}\nValor: {(ok ? Coins(amount) : "—")}\nTaxa do sistema: VC$ 0\n{(_message.Text.Trim().Length > 0 ? "Mensagem: " + _message.Text.Trim() + "\n" : "")}Total a enviar:{(ok ? Coins(amount) : "—")}" + (ok && Balance >= 0 && amount > Balance ? "\n\nSaldo insuficiente." : "");
        _send.Text = ok ? $"Enviar {Coins(amount)}" : "Enviar VC$"; _send.Disabled = !ok || name.Length < 2 || (Balance >= 0 && amount > Balance);
    }

    private void SendTransfer()
    {
        if (!TryAmount(out var amount)) return; _send.Disabled = true; _transferStatus.Text = "Enviando…";
        _people.Pay(_toName.Text.Trim(), amount, _transferKey, ok => { if (ok) { _transferKey = Guid.NewGuid().ToString("N"); _amount.Text = ""; _message.Text = ""; Refresh(); } UpdateTransferSummary(); }, _message.Text);
    }

    private Control BuildHelp(Control parent)
    {
        var page = Page(parent, out var left, out var right); right.Visible = false; left.SizeFlagsStretchRatio = 1;
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 10);
        foreach (var line in new[]
        {
            "VerseCoin (VC$) é a moeda oficial do LifeVerse. Ela vale em todas as ilhas e fica na sua conta.",
            "O saldo é sempre o que o servidor informa: o jogo nunca altera o seu saldo sozinho.",
            "Para enviar VC$ a alguém: Transferir VerseCoin, o nome exato da pessoa e o valor. Também dá para usar Pagar no menu radial ou no perfil.",
            "Comprar VerseCoin com dinheiro real depende de um provedor de pagamento (Pix, cartão…). O VerseCoin só é creditado depois que o pagamento é confirmado.",
            "Em caso de dúvida sobre uma cobrança, consulte o Histórico de Transações."
        }) inner.AddChild(Muted(line));
        left.AddChild(Card(inner)); return page;
    }
}
