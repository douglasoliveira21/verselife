using System.Globalization;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.RealEstate;

/// <summary>
/// "Minhas Propriedades": the islands, the land, the rentals (both ways), the offers, the payments and the contracts of the person, and the summary of the real estate account. Everything is what the server counted
/// from the real records; the buttons act on them (manage, visit, offer, withdraw, renew, end).
/// </summary>
public partial class MyPropertiesWindow : Node
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public static readonly (string Id, string Text)[] Tabs = [("ilhas", "Minhas Ilhas"), ("terrenos", "Meus Terrenos"), ("alugueis", "Meus Aluguéis"), ("alugadas", "Propriedades Alugadas"), ("anuncios", "Meus Anúncios"), ("pagamentos", "Pagamentos"), ("contratos", "Contratos")];

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Control _grip = null!, _body = null!;
    private Label _toast = null!, _note = null!;
    private Button _minimize = null!;
    private AcceptDialog _help = null!;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private VBoxContainer _content = null!;
    private readonly List<Label> _kpiValues = [];
    private PropertiesApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private PropertiesData _data = new();
    private string _tab = "ilhas", _paymentFilter = "all", _contractFilter = "all";
    private bool _dragging, _resizing, _moved, _minimized, _busy;
    private Vector2 _size = new(1360, 880);
    private Vector2? _viewportOverride;
    private double _toastLeft;
    private readonly Dictionary<string, int> _rows = [];
    private NoticesData _notices = new();
    private Control _noticeStrip = null!;
    private Label _noticeText = null!;
    private readonly HashSet<Guid> _told = [];
    private double _pollIn = 8, _pollEvery = 300;
    private bool _firstPoll = true;
    /// <summary>Said to the person when a new notice arrives (the main UI shows it as a toast).</summary>
    public Action<string>? NoticeArrived { get; set; }
    public int UnreadForTesting => _notices.Unread;
    public IReadOnlyList<PropNotice> NoticesForTesting => _notices.Items;
    public string NoticeStripForTesting => _noticeStrip.Visible ? _noticeText.Text : "";
    public async Task<int> PollNoticesForTesting() => await PollNoticesAsync();
    public async Task MarkNoticesReadForTesting(params Guid[] ids) => await MarkNoticesReadAsync(ids.Length == 0 ? null : ids);
    public void PressNoticesForTesting(string text) { if (_noticeStrip.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }

    public Action<Guid>? ManageIsland { get; set; }
    public Action<Guid>? ManageLand { get; set; }
    public Action<Guid>? Visit { get; set; }
    /// <summary>Goes to a parcel (island, parcel); without it "Visitar" only goes to the island.</summary>
    public Action<Guid, Guid>? VisitParcel { get; set; }
    private void GoTo(Guid island, Guid parcel) { if (parcel != Guid.Empty && VisitParcel is { } go) go(island, parcel); else Visit?.Invoke(island); }

    public bool IsOpen => _root is not null && _root.Visible;
    public PropertiesData Data => _data;
    public string TabForTesting => _tab;
    public string NoteForTesting => _note.Text;
    public string ToastForTesting => _toast.Text;
    public IReadOnlyList<string> TabTextsForTesting => [.. Tabs.Select(t => _tabButtons[t.Id].Text.Trim())];
    public IReadOnlyList<string> KpiValuesForTesting => [.. _kpiValues.Select(l => l.Text)];
    public IReadOnlyList<string> KpiTitlesForTesting => ["Imóveis Totais", "Em Uso", "Alugados", "Receita Mensal", "Próximos Vencimentos"];
    public int RowsForTesting(string tab) => _rows.GetValueOrDefault(tab);
    public string TextForTesting => string.Join("|", _content.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool ButtonEnabledForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false };
    public bool HasButtonForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text);
    public Vector2 PanelSizeForTesting => _panel.Size;
    public void SetViewportForTesting(Vector2 size) { _viewportOverride = size; Layout(); }
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new PropertiesApi(new IslandManagementApi(transport)); }
    public void SelectTabForTesting(string id) => SelectTab(id);
    public void SetPaymentFilterForTesting(string filter) { _paymentFilter = filter; Render(); }
    public void SetContractFilterForTesting(string filter) { _contractFilter = filter; Render(); }
    public void PressCloseForTesting() => RequestClose();
    public async Task<bool> OfferSaleForTesting(PropParcel p, long price) => await Do(_api!.OfferSale(p.IslandId, p.Id, price, true), "Terreno anunciado à venda.");
    public async Task<bool> OfferRentForTesting(PropParcel p, long price, int days) => await Do(_api!.OfferRent(p.IslandId, p.Id, price, days, 1, 6, 0), "Terreno oferecido para aluguel.");
    public async Task<bool> WithdrawForTesting(PropListing l) => await WithdrawAsync(l);
    public async Task<bool> OfferIslandSaleForTesting(PropIsland i, long price) => await Do(_api!.IslandOfferSale(i.Id, price, true), "Ilha anunciada à venda.");
    public async Task<bool> OfferIslandRentForTesting(PropIsland i, long price, int days) => await Do(_api!.IslandOfferRent(i.Id, price, days, 1, 6, 0), "Ilha oferecida para aluguel.");
    public async Task<bool> RenewForTesting(PropContract c, int periods) => await RenewAsync(c, periods);
    public async Task<bool> EndForTesting(PropContract c) => await EndAsync(c);
    public string ReceiptForTesting(PropPayment p) => ReceiptText(p);
    public void PressForTesting(string text) { if (_content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }

    public override void _Ready()
    {
        _api ??= new PropertiesApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));
        _root = new Control { Name = "MyPropertiesRoot", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ViewerTheme.Create() }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _panel = new PanelContainer { Name = "MyPropertiesPanel", ClipContents = true }; _panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .97f), IslandUi.BorderDefault, IslandUi.RadiusWindow, 1, 0, 0, 28)); _root.AddChild(_panel);
        var shell = new VBoxContainer(); shell.AddThemeConstantOverride("separation", 0); _panel.AddChild(shell);
        shell.AddChild(BuildTitleBar());
        var column = IslandUi.Column(12); column.SizeFlagsVertical = Control.SizeFlags.ExpandFill; var margin = IslandUi.Padded(column, 16, 12, 16, 14); margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _body = margin; shell.AddChild(margin);
        var tabs = IslandUi.Row(8); column.AddChild(tabs);
        foreach (var (id, text) in Tabs)
        {
            var button = IslandUi.Action(text, null, IslandUi.Kind.Secondary, 38); button.ToggleMode = true; button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; var tab = id; button.Pressed += () => SelectTab(tab); _tabButtons[id] = button; tabs.AddChild(button);
        }
        _noticeStrip = new PanelContainer { Visible = false }; ((PanelContainer)_noticeStrip).AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.AccentSecondary, .12f), IslandUi.AccentSecondary, IslandUi.RadiusCard, 1, 14, 8));
        var noticeRow = IslandUi.Row(10); _noticeStrip.AddChild(noticeRow); _noticeText = IslandUi.Text("", 14, IslandUi.TextPrimary, true, true); _noticeText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; noticeRow.AddChild(_noticeText);
        var seeNotices = IslandUi.Action("Ver Avisos", "bulb", IslandUi.Kind.Orange, 34); seeNotices.Pressed += ShowNotices; noticeRow.AddChild(seeNotices);
        var readNotices = IslandUi.Action("Marcar como Lidos", null, IslandUi.Kind.Secondary, 34); readNotices.Pressed += () => _ = MarkNoticesReadAsync(null); noticeRow.AddChild(readNotices);
        column.AddChild(_noticeStrip);
        _note = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); column.AddChild(_note);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddChild(scroll);
        _content = IslandUi.Column(12); _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(_content);
        column.AddChild(BuildSummary());

        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(20, 8 + i * 5), new Vector2(8 + i * 5, 20), new Color(IslandUi.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _resizing = button.Pressed; };
        _root.AddChild(_grip);
        _toast = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _toast.Visible = false; _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color("#0E3A47"), IslandUi.AccentPrimary, 10, 1, 16, 9)); _toast.MouseFilter = Control.MouseFilterEnum.Ignore; _root.AddChild(_toast);
        _help = new AcceptDialog { Title = "Ajuda", OkButtonText = "Entendi", Unresizable = true, DialogText = "Aqui está tudo o que é seu no mercado imobiliário do jogo.\n\n• Minhas Ilhas e Meus Terrenos: o que você tem. “Gerenciar” abre a janela de gerenciamento; “Anunciar” põe um terreno à venda ou para alugar.\n• Meus Aluguéis: terrenos seus alugados a outras pessoas. Propriedades Alugadas: os que você aluga (dá para renovar ou sair).\n• Meus Anúncios: o que está à venda ou para alugar agora.\n• Pagamentos e Contratos: o histórico, com recibo de cada pagamento.\n• O resumo embaixo é calculado com os seus registros reais." };
        _panel.AddChild(_help);
        GetViewport().SizeChanged += Layout;
        SelectTab("ilhas");
    }

    public override void _ExitTree() { if (IsInsideTree() && GetViewport() is { } viewport) viewport.SizeChanged -= Layout; }
    private readonly LiveTicker _live = new(30);
    public int LiveChecksForTesting { get; private set; }

    /// <summary>Reads the properties again, quietly: a sale, a new tenant or a payment shows up while the window is open.</summary>
    public async Task LiveRefreshAsync() { if (_api is null || _busy) return; LiveChecksForTesting++; await LoadAsync(true); }

    public override void _Process(double delta)
    {
        if (_toastLeft > 0) { _toastLeft -= delta; if (_toastLeft <= 0 && _toast is not null) _toast.Visible = false; }
        if (_live.Due(delta, IsOpen) && !LiveTicker.DialogOpen(_panel) && (_transport is not null || GetNodeOrNull<LifeVerse.Client.Autoloads.SessionManager>("/root/SessionManager") is { IsAuthenticated: true })) _ = LiveRefreshAsync();
        if (_api is null) return; _pollIn -= delta; if (_pollIn > 0) return; _pollIn = _pollEvery;
        if (_transport is null && GetNodeOrNull<LifeVerse.Client.Autoloads.SessionManager>("/root/SessionManager") is not { IsAuthenticated: true }) return; // nobody is signed in
        _ = PollNoticesAsync();
    }

    // ---- structure --------------------------------------------------------------------------------------------------------------

    private Control BuildTitleBar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 60), MouseDefaultCursorShape = Control.CursorShape.Move };
        var box = IslandUi.Box(IslandUi.TitleBar, null, IslandUi.RadiusWindow, 0, 22, 0); box.CornerRadiusBottomLeft = 0; box.CornerRadiusBottomRight = 0; box.BorderWidthBottom = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .7f); bar.AddThemeStyleboxOverride("panel", box);
        var row = IslandUi.Row(0); bar.AddChild(row);
        var logo = UiStyle.Brand(true, 34); logo.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; logo.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(logo);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new ColorRect { Color = new Color(IslandUi.BorderDefault, .9f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = IslandUi.Text("Minhas Propriedades", 21, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
        var help = IslandUi.IconButton("help", "Ajuda", 36); help.Pressed += () => _help.PopupCentered(new Vector2I(560, 0)); row.AddChild(help);
        _minimize = IslandUi.IconButton("minimize", "Minimizar", 36); _minimize.Pressed += () => { _minimized = !_minimized; _body.Visible = !_minimized; Layout(); }; row.AddChild(_minimize);
        var close = IslandUi.IconButton("close", "Fechar", 36); close.Pressed += RequestClose; row.AddChild(close);
        bar.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _dragging = button.Pressed;
            else if (input is InputEventMouseMotion motion && _dragging) { _panel.Position += motion.Relative; _moved = true; KeepOnScreen(); }
        };
        return bar;
    }

    private Control BuildSummary()
    {
        var (card, body) = IslandUi.Card("Resumo da Conta Imobiliária", "chart"); card.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; ((Control)body.GetParent()).SizeFlagsVertical = Control.SizeFlags.ShrinkBegin; body.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var row = IslandUi.Row(10); body.AddChild(row);
        foreach (var (title, icon) in new[] { ("Imóveis Totais", "home"), ("Em Uso", "people"), ("Alugados", "layers"), ("Receita Mensal", "coins"), ("Próximos Vencimentos", "calendar") })
        {
            var cell = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cell.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 12, 8));
            var column = IslandUi.Column(2); cell.AddChild(column);
            var icoRow = IslandUi.Row(8); icoRow.AddChild(UiStyle.IconRect(icon, IslandUi.AccentPrimary, 22)); icoRow.AddChild(IslandUi.Text(title, 13, IslandUi.TextSecondary, true)); column.AddChild(icoRow);
            var value = IslandUi.Text("—", 24, IslandUi.TextPrimary, true); column.AddChild(value); _kpiValues.Add(value); row.AddChild(cell);
        }
        return card;
    }

    private void Layout()
    {
        if (_panel is null) return;
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; var margin = 16f;
        var minWidth = Math.Min(1000f, view.X - 2 * margin); var minHeight = Math.Min(640f, view.Y - 2 * margin);
        var size = new Vector2(Math.Clamp(_size.X, minWidth, Math.Max(minWidth, view.X - 2 * margin)), Math.Clamp(_size.Y, minHeight, Math.Max(minHeight, view.Y - 2 * margin)));
        _panel.CustomMinimumSize = new Vector2(minWidth, _minimized ? 60 : minHeight); _panel.Size = _minimized ? new Vector2(size.X, 60) : size;
        if (!_moved) _panel.Position = ((view - _panel.Size) / 2).Floor(); else KeepOnScreen();
        _grip.Visible = !_minimized; _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
    }

    private void KeepOnScreen()
    {
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(Math.Clamp(_panel.Position.X, 16 - _panel.Size.X + 120, view.X - 120), Math.Clamp(_panel.Position.Y, 8, view.Y - 60));
        if (_grip is not null) _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
    }

    public override void _Input(InputEvent input)
    {
        if (!IsOpen) return;
        if (_resizing && input is InputEventMouseMotion motion) { _size += motion.Relative; Layout(); }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) { _resizing = false; _dragging = false; }
    }

    public void Toggle() { if (IsOpen) RequestClose(); else _ = OpenAsync(); }
    public void RequestClose() { _root.Visible = false; }
    public async Task OpenAsync() { _root.Visible = true; Layout(); await LoadAsync(); }

    private void Notify(string text) { _toast.Text = text; _toast.Visible = true; _toastLeft = 3.5; var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; _toast.Position = new Vector2((view.X - Math.Max(260, _toast.GetCombinedMinimumSize().X)) / 2, view.Y - 90); }

    // ---- the data ----------------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync(bool quiet = false)
    {
        if (_api is null || _busy) return;
        _busy = true; if (!quiet) _note.Text = "Carregando…"; var reply = await _api.Mine(); _busy = false;
        if (!reply.Ok && quiet) return; // a blip on the line must not blank the window
        if (!reply.Ok) { _note.Text = reply.Status == 404 ? "O servidor ainda não tem as suas propriedades. Elas chegam com a próxima publicação da API." : reply.Status == 0 ? "Sem conexão com o servidor." : LandManagementApi.Words(reply); return; }
        var data = PropertiesData.Parse(reply.Body); if (data is null) { _note.Text = "O servidor mandou uma resposta que não entendi."; return; }
        _data = data; _note.Text = ""; Render();
        await PollNoticesAsync();
    }

    private void SelectTab(string id) { _tab = id; foreach (var (key, button) in _tabButtons) button.ButtonPressed = key == id; Render(); }

    private async Task<bool> Do(Task<IslandReply> call, string done)
    {
        var reply = await call;
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; }
        Notify(done); await LoadAsync(); return true;
    }

    // ---- notices ----------------------------------------------------------------------------------------------------------------

    private void ShowNoticeStrip()
    {
        _noticeStrip.Visible = _notices.Unread > 0;
        if (_notices.Unread > 0) _noticeText.Text = _notices.Unread == 1 ? $"Você tem 1 aviso novo: {_notices.Items.FirstOrDefault(n => !n.Read)?.Title}" : $"Você tem {_notices.Unread} avisos novos. O mais recente: {_notices.Items.FirstOrDefault(n => !n.Read)?.Title}";
    }

    /// <summary>Asks for the notices; tells the person (once) about the ones not seen yet. Returns how many new ones were told.</summary>
    private async Task<int> PollNoticesAsync()
    {
        if (_api is null) return 0;
        var reply = await _api.Notices(); if (!reply.Ok || NoticesData.Parse(reply.Body) is not { } data) return 0;
        _notices = data; ShowNoticeStrip();
        var fresh = data.Items.Where(n => !n.Read && _told.Add(n.Id)).ToList();
        if (fresh.Count > 0) NoticeArrived?.Invoke(_firstPoll && fresh.Count > 1 ? $"Você tem {fresh.Count} avisos novos sobre os seus imóveis." : fresh.Count == 1 ? fresh[0].Title : $"{fresh.Count} avisos novos sobre os seus imóveis. O mais recente: {fresh[0].Title}");
        _firstPoll = false; return fresh.Count;
    }

    private async Task MarkNoticesReadAsync(IEnumerable<Guid>? ids)
    {
        if (_api is null) return;
        var reply = await _api.ReadNotices(ids?.ToList()); if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return; }
        await PollNoticesAsync();
    }

    private void ShowNotices()
    {
        var content = IslandUi.Column(10); content.CustomMinimumSize = new Vector2(560, 0);
        if (_notices.Items.Count == 0) content.AddChild(IslandUi.Text("Nenhum aviso.", 15, IslandUi.TextMuted));
        var list = IslandUi.Column(8);
        foreach (var n in _notices.Items.Take(25))
        {
            var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, n.Read ? IslandUi.BorderDefault : IslandUi.AccentSecondary, 8, 1, 10, 6)); var col = IslandUi.Column(2); card.AddChild(col);
            col.AddChild(IslandUi.Text(n.Title, 14, n.Read ? IslandUi.TextSecondary : IslandUi.TextPrimary, true, true)); if (n.Body.Length > 0) col.AddChild(IslandUi.Text(n.Body, 13, IslandUi.TextMuted, false, true)); col.AddChild(IslandUi.Text(Moment(n.At), 12, IslandUi.TextMuted)); list.AddChild(card);
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 340), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; scroll.AddChild(list); list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; content.AddChild(scroll);
        Ask("Avisos", content, async () => { await MarkNoticesReadAsync(null); return true; }, "Marcar como lidos");
    }

    // ---- drawing -----------------------------------------------------------------------------------------------------------------------

    private void Render()
    {
        var s = _data.Summary; string[] values = [s.Total.ToString(Pt), s.InUse.ToString(Pt), s.RentedOut.ToString(Pt), LandMath.Money(s.MonthlyRevenue), s.ExpiringSoon.ToString(Pt)];
        for (var i = 0; i < _kpiValues.Count; i++) _kpiValues[i].Text = values[i];
        foreach (var child in _content.GetChildren()) child.QueueFree();
        switch (_tab)
        {
            case "ilhas": ShowIslands(); break;
            case "terrenos": ShowParcels(); break;
            case "alugueis": ShowRentedOut(); break;
            case "alugadas": ShowRentedIn(); break;
            case "anuncios": ShowListings(); break;
            case "pagamentos": ShowPayments(); break;
            case "contratos": ShowContracts(); break;
        }
    }

    private static Label Cell(string text, Color? color = null, bool bold = false, float ratio = 1f)
    {
        var label = IslandUi.Text(text, 14, color ?? IslandUi.TextPrimary, bold); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.TooltipText = text; label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static Button Small(string text, IslandUi.Kind kind, Action act, bool enabled = true, string tip = "") { var b = IslandUi.Action(text, null, kind, 34); b.Pressed += act; b.Disabled = !enabled; b.TooltipText = tip; return b; }

    private static Control Actions(float width, params Button[] buttons)
    {
        var box = IslandUi.Row(6); box.CustomMinimumSize = new Vector2(width, 0); box.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; box.Alignment = BoxContainer.AlignmentMode.End; foreach (var b in buttons) box.AddChild(b); return box;
    }

    private Control Table(string key, string[] headers, float[] ratios, IReadOnlyList<Control[]> rows, string empty, float actionsWidth = 0)
    {
        _rows[key] = rows.Count;
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard));
        var column = IslandUi.Column(0); card.AddChild(column);
        var head = new PanelContainer { CustomMinimumSize = new Vector2(0, 40) }; var headBox = IslandUi.Box(IslandUi.CardHeader, null, IslandUi.RadiusCard, 0, 14, 0); headBox.CornerRadiusBottomLeft = 0; headBox.CornerRadiusBottomRight = 0; head.AddThemeStyleboxOverride("panel", headBox);
        var headRow = IslandUi.Row(10); head.AddChild(headRow);
        for (var i = 0; i < headers.Length; i++) { var l = IslandUi.Text(headers[i], 13, IslandUi.TextSecondary, true); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.SizeFlagsStretchRatio = ratios[i]; headRow.AddChild(l); }
        if (actionsWidth > 0) headRow.AddChild(new Control { CustomMinimumSize = new Vector2(actionsWidth, 0) });
        column.AddChild(head);
        if (rows.Count == 0) { column.AddChild(IslandUi.Padded(IslandUi.Text(empty, 15, IslandUi.TextMuted, false, true), 16, 16, 16, 16)); return card; }
        foreach (var cells in rows)
        {
            var row = new PanelContainer { CustomMinimumSize = new Vector2(0, 46) }; row.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 14, 4));
            var line = IslandUi.Row(10); row.AddChild(line); foreach (var c in cells) line.AddChild(c);
            column.AddChild(row); column.AddChild(IslandUi.Divider());
        }
        return card;
    }

    private static Control Heading(string text, string? note = null)
    {
        var column = IslandUi.Column(2); column.AddChild(IslandUi.Text(text, 18, IslandUi.TextPrimary, true)); if (note is not null) column.AddChild(IslandUi.Text(note, 13, IslandUi.TextMuted, false, true)); return column;
    }

    private static Color KindColor(string kind) => kind switch { "available" => LandTerritoryMap.Available, "sale" => LandTerritoryMap.OnSale, "rent" => LandTerritoryMap.Rented, "rented" => LandTerritoryMap.Rented, "common" => LandTerritoryMap.Common, _ => IslandUi.TextSecondary };
    private static string KindText(string kind) => kind switch { "available" => "Disponível", "sale" => "À venda", "rent" => "Para alugar", "rented" => "Alugado", "common" => "Área comum", _ => kind };
    private static string Day(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy", Pt);
    private static string Moment(DateTimeOffset when) => when == default ? "—" : when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt);
    private static string Left(DateTimeOffset when)
    {
        var span = when - DateTimeOffset.UtcNow; if (span <= TimeSpan.Zero) return "vencido"; return span.TotalDays >= 2 ? $"{(int)span.TotalDays} dias" : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h" : "menos de 1 h";
    }
    private static string Period(int days) => days == 30 ? "mensal" : days == 7 ? "semanal" : $"a cada {days} dias";

    private Control Picture(string cover, Guid island, Color tint, Vector2 size)
    {
        var frame = new PanelContainer { CustomMinimumSize = size, ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(tint, .18f), IslandUi.BorderDefault, 8, 1));
        var picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepCentered, Texture = UiStyle.Icon("map", new Color(tint, .85f), 40), MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(picture);
        if (cover.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(cover[6..], out var asset) && GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is { } cache)
            cache.Request(asset, texture => { if (texture is not null && IsInstanceValid(picture)) { picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; picture.Texture = texture; } }, island);
        return frame;
    }

    private Control PropertyCard(Control picture, string title, string subtitle, (string Text, Color Color) pill, string[] lines, Button[] buttons)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 12, 10));
        var row = IslandUi.Row(16); card.AddChild(row); row.AddChild(picture);
        var info = IslandUi.Column(3); info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(info);
        info.AddChild(IslandUi.Text(title, 19, IslandUi.TextPrimary, true)); var sub = IslandUi.Row(10); sub.AddChild(IslandUi.Text(subtitle, 14, IslandUi.TextSecondary)); sub.AddChild(IslandUi.Pill(pill.Text, pill.Color)); info.AddChild(sub);
        foreach (var line in lines) info.AddChild(IslandUi.Text(line, 14, IslandUi.TextSecondary));
        var side = IslandUi.Column(8); side.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; side.CustomMinimumSize = new Vector2(150, 0); foreach (var b in buttons) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; side.AddChild(b); } row.AddChild(side);
        return card;
    }

    // ---- the seven tabs ----------------------------------------------------------------------------------------------------------------

    private void ShowIslands()
    {
        _rows["ilhas"] = _data.Islands.Count; _content.AddChild(Heading($"Minhas Ilhas ({_data.Islands.Count})", "As ilhas que você criou. Pode vender, alugar ou transferir cada uma na aba Propriedade do gerenciamento."));
        if (_data.Islands.Count == 0) { _content.AddChild(IslandUi.Text("Você ainda não tem uma ilha.", 15, IslandUi.TextMuted)); return; }
        foreach (var i in _data.Islands)
        {
            var state = i.Tenant.Length > 0 ? ("Alugada", LandTerritoryMap.Rented) : i.SalePrice is not null ? ("À venda", IslandUi.AccentSecondary) : i.RentPrice is not null ? ("Para alugar", LandTerritoryMap.Rented) : (IslandUi.StateText(i.State), IslandUi.StateColor(i.State));
            var lines = new List<string> { $"Prims: {LandMath.Number(i.Objects)} / {LandMath.Number(i.MaxObjects)}", $"Visitantes: {i.Visitors} / {i.MaxPlayers}" };
            if (i.SalePrice is { } islandSale) lines.Add($"À venda por {LandMath.Money(islandSale)}"); if (i.RentPrice is { } islandRent && i.Tenant.Length == 0) lines.Add($"Para alugar: {LandMath.Money(islandRent)} ({Period(i.RentPeriodDays)})");
            if (i.Hosting is { } host) lines.Add(host.Status == "active" ? $"Hospedagem: {host.Plan}, até {Day(host.ExpiresAt)}" : $"Hospedagem vencida: renove até {Day(host.RecoveryUntil)} ({host.Plan})");
            if (i.Tenant.Length > 0) lines.Add($"Alugada para {i.Tenant}" + (i.LeaseExpiresAt is { } leaseEnd ? $" até {Day(leaseEnd)}" : ""));
            var offerButton = i.Tenant.Length > 0 ? Small("Anunciar", IslandUi.Kind.Orange, () => { }, false, "A ilha está alugada: espere o fim do contrato.") : i.SalePrice is not null || i.RentPrice is not null ? Small("Retirar anúncio", IslandUi.Kind.Danger, () => _ = Do(i.SalePrice is not null ? _api!.IslandCancelSale(i.Id) : _api!.IslandCancelRent(i.Id), "Anúncio retirado.")) : Small("Anunciar", IslandUi.Kind.Orange, () => OpenIslandOffer(i));
            _content.AddChild(PropertyCard(Picture(i.Cover, i.Id, IslandUi.StateColor(i.State), new Vector2(190, 110)), i.Name, $"{(i.Region == "homestead" ? "Homestead" : "Full Region")} · {(i.IsPublic ? "Pública" : "Privada")} · {i.Parcels} terreno(s)", state, [.. lines],
                [Small("Gerenciar", IslandUi.Kind.Secondary, () => ManageIsland?.Invoke(i.Id)), Small("Visitar", IslandUi.Kind.Secondary, () => { Visit?.Invoke(i.Id); RequestClose(); }), offerButton]));
        }
    }

    private void ShowParcels()
    {
        _rows["terrenos"] = _data.Parcels.Count; _content.AddChild(Heading($"Meus Terrenos ({_data.Parcels.Count})", "Os terrenos que são seus, em todas as ilhas."));
        if (_data.Parcels.Count == 0) { _content.AddChild(IslandUi.Text("Você ainda não tem terrenos.", 15, IslandUi.TextMuted)); return; }
        foreach (var p in _data.Parcels)
        {
            var listed = _data.Listings.FirstOrDefault(l => l.ParcelId == p.Id);
            var buttons = new List<Button> { Small("Visitar", IslandUi.Kind.Secondary, () => { GoTo(p.IslandId, p.Id); RequestClose(); }, p.CanVisit, p.CanVisit ? "" : "Este terreno não recebe visitas."), Small("Gerenciar", IslandUi.Kind.Secondary, () => ManageLand?.Invoke(p.IslandId)) };
            buttons.Add(listed is not null ? Small("Retirar anúncio", IslandUi.Kind.Danger, () => _ = WithdrawAsync(listed)) : Small("Anunciar", IslandUi.Kind.Orange, () => OpenOffer(p), p.Kind is not ("rented" or "common"), p.Kind == "rented" ? "O terreno está alugado." : p.Kind == "common" ? "Área comum não se anuncia." : ""));
            var lines = new List<string> { $"{LandMath.Area(p.Area)} · {LandMath.CategoryText(p.Category)}", $"Prims: {LandMath.Number(p.Objects)} / {LandMath.Number(p.MaxObjects)}" };
            if (p.SalePrice is { } sale) lines.Add($"À venda por {LandMath.Money(sale)}"); if (p.RentPrice is { } rent && p.Kind != "rented") lines.Add($"Para alugar: {LandMath.Money(rent)} ({Period(p.RentPeriodDays)})");
            if (p.Tenant.Length > 0) lines.Add($"Alugado para {p.Tenant}" + (p.RentExpiresAt is { } at ? $" até {Day(at)}" : ""));
            _content.AddChild(PropertyCard(Picture(p.Image, p.IslandId, KindColor(p.Kind), new Vector2(190, 100)), p.Title, p.Island, (KindText(p.Kind), KindColor(p.Kind)), [.. lines], [.. buttons]));
        }
    }

    private void ShowRentedOut()
    {
        var rows = _data.Contracts.Where(c => c.Role == "landlord" && c.Live).ToList(); _content.AddChild(Heading("Meus Aluguéis", "Terrenos seus que estão alugados para outras pessoas."));
        _content.AddChild(Table("alugueis", ["Imóvel", "Ilha", "Locatário", "Valor", "Vencimento", "Situação"], [1.5f, 1.1f, 1.1f, 1.1f, 1f, 1f],
            [.. rows.Select(c => new Control[] { Cell(c.Title, null, true, 1.5f), Cell(c.Island, null, false, 1.1f), Cell(c.Tenant, null, false, 1.1f), Cell($"{LandMath.Money(c.Price)} / {Period(c.PeriodDays)}", LandTerritoryMap.Rented, true, 1.1f), Cell(Day(c.ExpiresAt), null, false, 1f), Cell(c.Status == "grace" ? "Em tolerância" : "Em dia", c.Status == "grace" ? IslandUi.Warning : IslandUi.Success, true, 1f),
                Actions(230, Small("Ver Contrato", IslandUi.Kind.Secondary, () => SelectTab("contratos")), Small("Encerrar", IslandUi.Kind.Danger, () => ConfirmEnd(c), c.ExpiresAt <= DateTimeOffset.UtcNow, "O proprietário só encerra depois do vencimento."))
            })], "Nenhum terreno seu está alugado.", 236));
    }

    private void ShowRentedIn()
    {
        var rows = _data.Contracts.Where(c => c.Role == "tenant" && c.Live).ToList(); _content.AddChild(Heading("Propriedades Alugadas", "Terrenos que você aluga de outras pessoas."));
        _content.AddChild(Table("alugadas", ["Imóvel", "Ilha", "Proprietário", "Valor", "Vencimento", "Resta"], [1.5f, 1.1f, 1.1f, 1.1f, 1f, .9f],
            [.. rows.Select(c => new Control[] { Cell(c.Title, null, true, 1.5f), Cell(c.Island, null, false, 1.1f), Cell(c.Landlord, null, false, 1.1f), Cell($"{LandMath.Money(c.Price)} / {Period(c.PeriodDays)}", LandTerritoryMap.Rented, true, 1.1f), Cell(Day(c.ExpiresAt), null, false, 1f), Cell(Left(c.ExpiresAt), c.Status == "grace" ? IslandUi.Warning : IslandUi.TextPrimary, true, .9f),
                Actions(430, Small("Visitar", IslandUi.Kind.Secondary, () => { GoTo(c.IslandId, c.Target == "island" ? Guid.Empty : c.ParcelId); RequestClose(); }, c.CanVisit), Small("Renovar", IslandUi.Kind.Purple, () => OpenRenew(c)), Small("Ver Contrato", IslandUi.Kind.Secondary, () => SelectTab("contratos")), Small("Sair", IslandUi.Kind.Danger, () => ConfirmEnd(c)))
            })], "Você não aluga nenhum terreno.", 436));
    }

    private void ShowListings()
    {
        _content.AddChild(Heading("Meus Anúncios", "O que está à venda ou para alugar agora. Terrenos vendidos ou alugados saem daqui sozinhos."));
        _content.AddChild(Table("anuncios", ["Tipo", "Imóvel", "Ilha", "Área", "Preço", "Desde"], [.8f, 1.5f, 1.1f, .9f, 1.2f, 1f],
            [.. _data.Listings.Select(l => new Control[] { Cell(l.Kind == "sale" ? "Venda" : "Aluguel", l.Kind == "sale" ? IslandUi.AccentSecondary : IslandUi.RentPurple, true, .8f), Cell(l.Title, null, true, 1.5f), Cell(l.Island, null, false, 1.1f), Cell(LandMath.Area(l.Area), null, false, .9f),
                Cell(l.Kind == "sale" ? LandMath.Money(l.Price) : $"{LandMath.Money(l.Price)} / {Period(l.PeriodDays)}", null, true, 1.2f), Cell(Day(l.Since), null, false, 1f),
                Actions(260, Small("Editar", IslandUi.Kind.Secondary, () => { if (l.Target == "island") { if (_data.Islands.FirstOrDefault(i => i.Id == l.IslandId) is { } own) OpenIslandOffer(own, l); } else if (_data.Parcels.FirstOrDefault(p => p.Id == l.ParcelId) is { } p) OpenOffer(p, l); }), Small("Retirar", IslandUi.Kind.Danger, () => _ = WithdrawAsync(l)))
            })], "Você não tem anúncios no momento.", 266));
    }

    private void ShowPayments()
    {
        _content.AddChild(Heading("Pagamentos", "Compras, vendas e aluguéis pagos e recebidos. Não há taxas no jogo; o recibo mostra os dados do pagamento."));
        var head = IslandUi.Row(10); _content.AddChild(head);
        var drop = IslandUi.Dropdown([("Todos", "all"), ("Compras", "buy"), ("Vendas", "sell"), ("Aluguéis pagos", "rent_out"), ("Aluguéis recebidos", "rent_in"), ("Caução", "deposit")]); IslandUi.Select(drop, _paymentFilter); drop.CustomMinimumSize = new Vector2(220, 36); drop.ItemSelected += index => { _paymentFilter = IslandUi.IdOf(drop); Render(); }; head.AddChild(drop);
        bool Match(PropPayment p) => _paymentFilter switch { "buy" => p.Type == "sale" && !p.Incoming, "sell" => p.Type == "sale" && p.Incoming, "rent_out" => p.Type is "rent_start" or "rent_payment" && !p.Incoming, "rent_in" => p.Type is "rent_start" or "rent_payment" && p.Incoming, "deposit" => p.Type == "rent_end", _ => true };
        var rows = _data.Payments.Where(Match).ToList(); var received = rows.Where(p => p.Incoming).Sum(p => p.Amount); var paid = rows.Where(p => !p.Incoming).Sum(p => p.Amount);
        var totals = IslandUi.Text($"Recebido: {LandMath.Money(received)}   ·   Pago: {LandMath.Money(paid)}", 14, IslandUi.TextSecondary); totals.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; totals.HorizontalAlignment = HorizontalAlignment.Right; head.AddChild(totals);
        _content.AddChild(Table("pagamentos", ["Data", "Tipo", "Imóvel", "Pessoa", "Valor"], [1.2f, 1.5f, 1.5f, 1f, 1f],
            [.. rows.Select(p => new Control[] { Cell(Moment(p.At), null, false, 1.2f), Cell(PropertiesData.PaymentText(p.Type), null, true, 1.5f), Cell(p.Title, null, false, 1.5f), Cell(p.Other, null, false, 1f), Cell((p.Incoming ? "+ " : "− ") + LandMath.Money(p.Amount), p.Incoming ? IslandUi.Success : IslandUi.Error, true, 1f), Actions(110, Small("Recibo", IslandUi.Kind.Secondary, () => ShowReceipt(p))) })], "Nenhum pagamento com esse filtro.", 116));
    }

    private void ShowContracts()
    {
        _content.AddChild(Heading("Contratos", "Os aluguéis em que você é locador ou locatário."));
        var drop = IslandUi.Dropdown([("Todos", "all"), ("Ativos", "live"), ("Encerrados", "ended")]); IslandUi.Select(drop, _contractFilter); drop.CustomMinimumSize = new Vector2(200, 36); drop.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; drop.ItemSelected += index => { _contractFilter = IslandUi.IdOf(drop); Render(); }; _content.AddChild(drop);
        var rows = _data.Contracts.Where(c => _contractFilter switch { "live" => c.Live, "ended" => !c.Live, _ => true }).ToList();
        _content.AddChild(Table("contratos", ["Contrato", "Imóvel", "Meu papel", "Outra parte", "Valor", "Vencimento", "Estado"], [.8f, 1.5f, .9f, 1f, 1.2f, 1.1f, 1f],
            [.. rows.Select(c => new Control[] { Cell(c.Id.ToString("N")[..8].ToUpperInvariant(), IslandUi.AccentPrimary, true, .8f), Cell($"{c.Title} · {c.Island}", null, false, 1.5f), Cell(c.Role == "tenant" ? "Locatário" : "Locador", null, false, .9f), Cell(c.Role == "tenant" ? c.Landlord : c.Tenant, null, false, 1f),
                Cell($"{LandMath.Money(c.Price)} / {c.PeriodDays} d", null, false, 1.2f), Cell(c.EndedAt is { } e ? $"Encerrado {Day(e)}" : Day(c.ExpiresAt), null, false, 1.1f), Cell(PropertiesData.StatusText(c.Status), c.Status == "active" ? IslandUi.Success : c.Status == "grace" ? IslandUi.Warning : IslandUi.TextMuted, true, 1f) })], "Nenhum contrato com esse filtro."));
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------------------------

    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); _panel.AddChild(dialog);
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(480, 0));
    }

    private async Task<bool> WithdrawAsync(PropListing l) => await Do(l.Target == "island" ? (l.Kind == "sale" ? _api!.IslandCancelSale(l.IslandId) : _api!.IslandCancelRent(l.IslandId)) : l.Kind == "sale" ? _api!.CancelSale(l.IslandId, l.ParcelId) : _api!.CancelRent(l.IslandId, l.ParcelId), "Anúncio retirado.");

    private void OpenOffer(PropParcel p, PropListing? editing = null)
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"{p.Title} · {p.Island} · {LandMath.Area(p.Area)}", 15, IslandUi.TextPrimary, true, true));
        var kind = IslandUi.Dropdown([("Vender", "sale"), ("Alugar", "rent")]); if (editing is not null) IslandUi.Select(kind, editing.Kind); content.AddChild(kind);
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$"); price.Value = editing?.Price ?? (p.SalePrice ?? p.RentPrice ?? 1000); content.AddChild(price);
        var period = IslandUi.Dropdown([("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(period, editing is { PeriodDays: > 0 } ? editing.PeriodDays.ToString(Pt) : "30"); content.AddChild(period);
        var deposit = IslandUi.Number(0, 1_000_000_000, 1, " L$ de caução"); content.AddChild(deposit);
        var objects = new CheckBox { Text = "Incluir os meus objetos na venda", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; content.AddChild(objects);
        void Sync() { var rent = IslandUi.IdOf(kind) == "rent"; period.Visible = rent; deposit.Visible = rent; objects.Visible = !rent; price.Suffix = rent ? " L$ por período" : " L$"; }
        kind.ItemSelected += index => Sync(); Sync();
        Ask(editing is null ? "Anunciar terreno" : "Editar anúncio", content, async () =>
        {
            var rent = IslandUi.IdOf(kind) == "rent";
            if (editing is not null && editing.Kind != IslandUi.IdOf(kind)) await (editing.Kind == "sale" ? _api!.CancelSale(p.IslandId, p.Id) : _api!.CancelRent(p.IslandId, p.Id));
            else if (editing is not null) await (rent ? _api!.CancelRent(p.IslandId, p.Id) : _api!.CancelSale(p.IslandId, p.Id));
            return rent ? await Do(_api!.OfferRent(p.IslandId, p.Id, (long)price.Value, int.Parse(IslandUi.IdOf(period)), 1, 6, (long)deposit.Value), "Terreno oferecido para aluguel.") : await Do(_api!.OfferSale(p.IslandId, p.Id, (long)price.Value, objects.ButtonPressed), "Terreno anunciado à venda.");
        }, editing is null ? "Anunciar" : "Salvar");
    }

    private void OpenIslandOffer(PropIsland i, PropListing? editing = null)
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"{i.Name} · {(i.Region == "homestead" ? "Homestead" : "Full Region")}", 15, IslandUi.TextPrimary, true, true));
        var kind = IslandUi.Dropdown([("Vender", "sale"), ("Alugar", "rent")]); if (editing is not null) IslandUi.Select(kind, editing.Kind); content.AddChild(kind);
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$"); price.Value = editing?.Price ?? (i.SalePrice ?? i.RentPrice ?? 100000); content.AddChild(price);
        var period = IslandUi.Dropdown([("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(period, editing is { PeriodDays: > 0 } ? editing.PeriodDays.ToString(Pt) : "30"); content.AddChild(period);
        var deposit = IslandUi.Number(0, 1_000_000_000, 1, " L$ de caução"); content.AddChild(deposit);
        var objects = new CheckBox { Text = "Incluir os meus objetos na venda", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; content.AddChild(objects);
        var islandAuto = new CheckBox { Text = "Deixar quem aluga renovar sozinho a cada vencimento", FocusMode = Control.FocusModeEnum.None }; content.AddChild(islandAuto);
        var note = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); content.AddChild(note);
        void Sync() { var rent = IslandUi.IdOf(kind) == "rent"; period.Visible = rent; deposit.Visible = rent; islandAuto.Visible = rent; objects.Visible = !rent; price.Suffix = rent ? " L$ por período" : " L$"; note.Text = rent ? "Quem alugar cuida da ilha durante o contrato, mas ela continua sua." : "Quem comprar passa a ser o dono da ilha inteira, com os terrenos e objetos que ainda são seus."; }
        kind.ItemSelected += index => Sync(); Sync();
        Ask(editing is null ? "Anunciar ilha" : "Editar anúncio", content, async () =>
        {
            var rent = IslandUi.IdOf(kind) == "rent";
            if (editing is not null) await (editing.Kind == "sale" ? _api!.IslandCancelSale(i.Id) : _api!.IslandCancelRent(i.Id));
            return rent ? await Do(_api!.IslandOfferRent(i.Id, (long)price.Value, int.Parse(IslandUi.IdOf(period)), 1, 6, (long)deposit.Value, islandAuto.ButtonPressed), "Ilha oferecida para aluguel.") : await Do(_api!.IslandOfferSale(i.Id, (long)price.Value, objects.ButtonPressed), "Ilha anunciada à venda.");
        }, editing is null ? "Anunciar" : "Salvar");
    }

    private void OpenRenew(PropContract c)
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"Renovar {c.Title} ({c.Island}) por mais:", 15, IslandUi.TextPrimary, true, true));
        var periods = IslandUi.Number(1, 12, 1, $" período(s) de {c.PeriodDays} dias"); periods.Value = 1; content.AddChild(periods);
        var total = IslandUi.Text("", 16, IslandUi.RentPurple, true); content.AddChild(total); void Update() => total.Text = $"Total agora: {LandMath.Money(c.Price * (long)periods.Value)}"; periods.ValueChanged += v => Update(); Update();
        Ask("Renovar aluguel", content, async () => await RenewAsync(c, (int)periods.Value), "Renovar");
    }

    private async Task<bool> RenewAsync(PropContract c, int periods) => await Do(c.Target == "island" ? _api!.IslandRenew(c.IslandId, c.Id, periods) : _api!.Renew(c.IslandId, c.Id, periods), "Aluguel renovado.");

    private void ConfirmEnd(PropContract c)
    {
        var tenant = c.Role == "tenant";
        var text = tenant ? $"Sair de {c.Title}? Os seus objetos voltam para o seu inventário e a caução volta para você." : $"Encerrar o aluguel de {c.Title} com {c.Tenant}? Só é possível depois do vencimento; os objetos do locatário voltam para o inventário dele.";
        Ask(tenant ? "Sair do aluguel" : "Encerrar aluguel", IslandUi.Text(text, 14, IslandUi.TextSecondary, false, true), async () => await EndAsync(c), tenant ? "Sair" : "Encerrar");
    }

    private async Task<bool> EndAsync(PropContract c) => await Do(c.Target == "island" ? _api!.IslandEnd(c.IslandId, c.Id) : _api!.End(c.IslandId, c.Id), c.Role == "tenant" ? "Você saiu do aluguel." : "Aluguel encerrado.");

    private static string ReceiptText(PropPayment p) =>
        $"RECIBO {p.Id.ToString("N")[..10].ToUpperInvariant()}\nData: {Moment(p.At)}\nTipo: {PropertiesData.PaymentText(p.Type)}\nImóvel: {p.Title} ({p.Island})\n{(p.Incoming ? "Pago por" : "Pago a")}: {p.Other}\nValor: {LandMath.Money(p.Amount)}\nTaxas: nenhuma";

    private void ShowReceipt(PropPayment p)
    {
        var text = ReceiptText(p); var dialog = new AcceptDialog { Title = "Recibo", OkButtonText = "Fechar", DialogText = text, Theme = ViewerTheme.Create(), Unresizable = true };
        dialog.AddButton("Copiar", false, "copy"); dialog.CustomAction += action => { if (action == "copy") { DisplayServer.ClipboardSet(text); Notify("Recibo copiado."); } };
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); _panel.AddChild(dialog); dialog.PopupCentered(new Vector2I(460, 0));
    }
}
