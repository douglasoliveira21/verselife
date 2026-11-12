using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.ParcelInfo;

/// <summary>The calls of the "Sobre este Terreno" window.</summary>
public sealed class ParcelInfoApi(IslandManagementApi inner)
{
    private static string Land(Guid island) => $"/api/v1/worlds/islands/{island}/land";
    private static string Parcel(Guid island, Guid parcel) => $"{Land(island)}/parcels/{parcel}";
    public Task<IslandReply> About(Guid island, Guid parcel) => inner.Send("GET", $"{Parcel(island, parcel)}/about");
    public Task<IslandReply> Rename(Guid island, Guid parcel, string name, string description, int revision) => inner.Send("PATCH", $"/api/v1/worlds/islands/{island}/parcels/{parcel}", new { name, description, expectedRevision = revision });
    public Task<IslandReply> Category(Guid island, Guid parcel, string category) => inner.Send("PUT", $"{Parcel(island, parcel)}/category", new { category });
    public Task<IslandReply> Image(Guid island, Guid parcel, string image) => inner.Send("PUT", $"{Parcel(island, parcel)}/image", new { image });
    public Task<IslandReply> Arrival(Guid island, Guid parcel, double? x, double? z, double yaw, string mode) => inner.Send("PUT", $"{Parcel(island, parcel)}/arrival", new { x, z, yaw, mode });
    public Task<IslandReply> OfferSale(Guid island, Guid parcel, long price, bool includeObjects, string description) => inner.Send("POST", $"{Parcel(island, parcel)}/sale", new { price, includeObjects, description });
    public Task<IslandReply> CancelSale(Guid island, Guid parcel) => inner.Send("DELETE", $"{Parcel(island, parcel)}/sale");
    public Task<IslandReply> OfferRent(Guid island, Guid parcel, long price, int days, int min, int max, long deposit, int grace) => inner.Send("POST", $"{Parcel(island, parcel)}/rental-offer", new { pricePerPeriod = price, periodDays = days, minPeriods = min, maxPeriods = Math.Max(min, max), depositCoins = deposit, graceDays = grace, autoRenewAllowed = true });
    public Task<IslandReply> CancelRent(Guid island, Guid parcel) => inner.Send("DELETE", $"{Parcel(island, parcel)}/rental-offer");
    public Task<IslandReply> Buy(Guid island, Guid parcel, long price) => inner.Send("POST", $"{Parcel(island, parcel)}/buy", new { expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> Rent(Guid island, Guid parcel, int periods, long price) => inner.Send("POST", $"{Parcel(island, parcel)}/rent", new { periods, autoRenew = false, expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> Renew(Guid island, Guid rental, int periods) => inner.Send("POST", $"{Land(island)}/rentals/{rental}/renew", new { periods, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> End(Guid island, Guid rental) => inner.Send("POST", $"{Land(island)}/rentals/{rental}/end", new { });
    public Task<IslandReply> Access(Guid island, Guid parcel) => inner.Send("GET", $"{Parcel(island, parcel)}/access");
    public Task<IslandReply> AccessMode(Guid island, Guid parcel, string mode) => inner.Send("PUT", $"{Parcel(island, parcel)}/access-mode", new { mode });
    public Task<IslandReply> AddAccess(Guid island, Guid parcel, Guid user, string kind, int? minutes) => inner.Send("POST", $"{Parcel(island, parcel)}/access", new { userId = user, kind, minutes, reason = "" });
    public Task<IslandReply> RemoveAccess(Guid island, Guid parcel, Guid entry) => inner.Send("DELETE", $"{Parcel(island, parcel)}/access/{entry}");
    public Task<IslandReply> Options(Guid island, Guid parcel, string buildMode, bool allowScripts, int? autoReturnMinutes) => inner.Send("PUT", $"{Parcel(island, parcel)}/options", new { buildMode, allowScripts, autoReturnMinutes });
    public Task<IslandReply> Objects(Guid island, Guid parcel) => inner.Send("GET", $"{Parcel(island, parcel)}/objects");
    public Task<IslandReply> ReturnObjects(Guid island, Guid parcel, string scope, IEnumerable<Guid>? ids, string reason) => inner.Send("POST", $"{Parcel(island, parcel)}/objects/return", new { scope, objectIds = ids, reason });
    public Task<IslandReply> Media(Guid island, Guid parcel, string url, double volume, bool autoplay, string name) => inner.Send("PUT", $"{Parcel(island, parcel)}/media", new { musicUrl = url, volume, autoplay, name });
    public Task<IslandReply> FindPerson(string name) => inner.Send("GET", $"/api/v1/social/people?q={Uri.EscapeDataString(name)}");
}

/// <summary>
/// "Sobre este Terreno": one parcel, from the right click on the ground or from the lists of the management windows. A header with the picture, the name, the island and the area, and eight tabs: Geral, Propriedade,
/// Venda e Aluguel, Acesso, Permissões, Objetos, Mídia and Contrato. The server tells what the person may see and change (the owner changes; everybody else reads and may buy or rent what is offered).
/// </summary>
public partial class ParcelInfoWindow : Node
{
    internal static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public static readonly (string Id, string Text)[] Tabs = [("geral", "Geral"), ("propriedade", "Propriedade"), ("venda", "Venda e Aluguel"), ("acesso", "Acesso"), ("permissoes", "Permissões"), ("objetos", "Objetos"), ("midia", "Mídia"), ("contrato", "Contrato")];

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Control _grip = null!, _body = null!;
    private Label _toast = null!, _title = null!, _subtitle = null!, _note = null!;
    private Control _statePill = null!;
    private TextureRect _headerImage = null!;
    private Button _minimize = null!;
    private AcceptDialog _help = null!;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private VBoxContainer _content = null!;
    private BuildTexturePicker? _picker;
    private ParcelInfoApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private JsonElement _about;
    private bool _loaded, _dragging, _resizing, _moved, _minimized, _busy;
    private Guid _island, _parcel;
    private string _tab = "geral";
    private Vector2 _size = new(1040, 780);
    private Vector2? _viewportOverride;
    private double _toastLeft;
    private readonly Dictionary<string, string> _tabText = [];

    /// <summary>Where the person is standing (x, z, facing), to set the arrival point.</summary>
    public Func<(double X, double Z, double Yaw)?>? Position { get; set; }
    /// <summary>Opens the land selection window on this parcel (the older window that cuts and joins land).</summary>
    public Action<Guid>? OpenLandWindow { get; set; }
    public Action<Guid>? Visit { get; set; }

    public bool IsOpen => _root is not null && _root.Visible;
    public Guid ParcelId => _parcel;
    public bool Loaded => _loaded;
    public string TabForTesting => _tab;
    public string NoteForTesting => _note.Text;
    public string ToastForTesting => _toast.Text;
    public string TitleForTesting => _title.Text;
    public string SubtitleForTesting => _subtitle.Text;
    public IReadOnlyList<string> TabTextsForTesting => [.. Tabs.Select(t => _tabButtons[t.Id].Text.Trim())];
    public string TextForTesting => string.Join("|", _content.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool HasButtonForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text);
    public bool ButtonEnabledForTesting(string text) => _content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false };
    public Vector2 PanelSizeForTesting => _panel.Size;
    public JsonElement AboutForTesting => _about;
    public void SetViewportForTesting(Vector2 size) { _viewportOverride = size; Layout(); }
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new ParcelInfoApi(new IslandManagementApi(transport)); }
    public void SelectTabForTesting(string id) => SelectTab(id);
    public void PressCloseForTesting() => RequestClose();
    public void PressForTesting(string text) { if (_content.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false } b) b.EmitSignal(BaseButton.SignalName.Pressed); }

    internal ParcelInfoApi Api => _api!;
    internal Guid IslandId => _island;
    internal bool Can(string flag) => _loaded && _about.TryGetProperty("me", out var me) && me.TryGetProperty(flag, out var v) && v.ValueKind == JsonValueKind.True;
    internal JsonElement About => _about;

    public override void _Ready()
    {
        _api ??= new ParcelInfoApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));
        _root = new Control { Name = "ParcelInfoRoot", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ViewerTheme.Create() }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _panel = new PanelContainer { Name = "ParcelInfoPanel", ClipContents = true }; _panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .97f), IslandUi.BorderDefault, IslandUi.RadiusWindow, 1, 0, 0, 28)); _root.AddChild(_panel);
        var shell = new VBoxContainer(); shell.AddThemeConstantOverride("separation", 0); _panel.AddChild(shell);
        shell.AddChild(BuildTitleBar());
        var column = IslandUi.Column(12); column.SizeFlagsVertical = Control.SizeFlags.ExpandFill; var margin = IslandUi.Padded(column, 16, 12, 16, 14); margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _body = margin; shell.AddChild(margin);
        column.AddChild(BuildHeader());
        var tabs = IslandUi.Row(6); column.AddChild(tabs);
        foreach (var (id, text) in Tabs) { var button = IslandUi.Action(text, null, IslandUi.Kind.Secondary, 36); button.ToggleMode = true; button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; var tab = id; button.Pressed += () => SelectTab(tab); _tabButtons[id] = button; tabs.AddChild(button); }
        _note = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); column.AddChild(_note);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddChild(scroll);
        _content = IslandUi.Column(12); _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(_content);

        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(20, 8 + i * 5), new Vector2(8 + i * 5, 20), new Color(IslandUi.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _resizing = button.Pressed; };
        _root.AddChild(_grip);
        _toast = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _toast.Visible = false; _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color("#0E3A47"), IslandUi.AccentPrimary, 10, 1, 16, 9)); _toast.MouseFilter = Control.MouseFilterEnum.Ignore; _root.AddChild(_toast);
        _help = new AcceptDialog { Title = "Ajuda", OkButtonText = "Entendi", Unresizable = true, DialogText = "Tudo sobre este terreno.\n\n• Geral: nome, descrição, imagem, finalidade, prims e ponto de chegada (o dono muda; os outros leem).\n• Propriedade: quem é o dono, desde quando e como o terreno mudou de mãos.\n• Venda e Aluguel: o dono anuncia; quem visita compra ou aluga.\n• Acesso, Permissões, Objetos e Mídia: só o dono.\n• Contrato: as condições da oferta ou do aluguel em andamento." };
        _panel.AddChild(_help);
        GetViewport().SizeChanged += Layout;
        SelectTab("geral");
    }

    public override void _ExitTree() { if (IsInsideTree() && GetViewport() is { } viewport) viewport.SizeChanged -= Layout; }
    public override void _Process(double delta) { if (_toastLeft > 0) { _toastLeft -= delta; if (_toastLeft <= 0 && _toast is not null) _toast.Visible = false; } }

    // ---- structure --------------------------------------------------------------------------------------------------------

    private Control BuildTitleBar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 60), MouseDefaultCursorShape = Control.CursorShape.Move };
        var box = IslandUi.Box(IslandUi.TitleBar, null, IslandUi.RadiusWindow, 0, 22, 0); box.CornerRadiusBottomLeft = 0; box.CornerRadiusBottomRight = 0; box.BorderWidthBottom = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .7f); bar.AddThemeStyleboxOverride("panel", box);
        var row = IslandUi.Row(0); bar.AddChild(row);
        var logo = UiStyle.Brand(true, 34); logo.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; logo.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(logo);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new ColorRect { Color = new Color(IslandUi.BorderDefault, .9f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = IslandUi.Text("Sobre este Terreno", 21, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
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

    private Control BuildHeader()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 92) }; var row = IslandUi.Row(16); bar.AddChild(row);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(132, 84), ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        _headerImage = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepCentered, Texture = UiStyle.Icon("map", IslandUi.TextMuted, 40), MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(_headerImage); row.AddChild(frame);
        var identity = IslandUi.Column(4); identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(identity);
        _title = IslandUi.Text("Carregando…", 24, IslandUi.TextPrimary, true); identity.AddChild(_title);
        var sub = IslandUi.Row(12); _subtitle = IslandUi.Text("", 15, IslandUi.TextSecondary); sub.AddChild(_subtitle); _statePill = new Control(); sub.AddChild(_statePill); identity.AddChild(sub);
        return bar;
    }

    private void Layout()
    {
        if (_panel is null) return;
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; var margin = 16f;
        var minWidth = Math.Min(900f, view.X - 2 * margin); var minHeight = Math.Min(620f, view.Y - 2 * margin);
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

    public void RequestClose() { _root.Visible = false; }

    internal void Notify(string text) { _toast.Text = text; _toast.Visible = true; _toastLeft = 3.5; var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; _toast.Position = new Vector2((view.X - Math.Max(260, _toast.GetCombinedMinimumSize().X)) / 2, view.Y - 90); }

    // ---- loading ----------------------------------------------------------------------------------------------------------

    public async Task OpenAsync(Guid island, Guid parcel)
    {
        if (island != _island || parcel != _parcel) { _loaded = false; _tab = "geral"; foreach (var (key, button) in _tabButtons) button.ButtonPressed = key == _tab; }
        _island = island; _parcel = parcel; _root.Visible = true; Layout(); await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty || _parcel == Guid.Empty || _busy) return;
        _busy = true; _note.Text = "Carregando…"; var reply = await _api.About(_island, _parcel); _busy = false;
        if (!reply.Ok)
        {
            _loaded = false; _note.Text = reply.Status == 404 ? "O servidor ainda não tem os detalhes deste terreno. Eles chegam com a próxima publicação da API." : reply.Status == 0 ? "Sem conexão com o servidor." : LandManagementApi.Words(reply);
            foreach (var child in _content.GetChildren()) child.QueueFree(); return;
        }
        _about = reply.Body.Clone(); _loaded = true; _note.Text = ""; Fill(); Render();
    }

    private void Fill()
    {
        var p = _about.GetProperty("parcel"); var code = Str(p, "code"); var name = Str(p, "name");
        _title.Text = code.Length > 0 ? $"{code} — {name}" : name; _subtitle.Text = $"{Str(_about.GetProperty("island"), "name")} • {LandMath.Area(Dbl(p, "area"))}";
        foreach (var child in _statePill.GetChildren()) child.QueueFree();
        var (text, color) = StateOf(); var pill = IslandUi.Pill(text, color); _statePill.AddChild(pill); _statePill.CustomMinimumSize = pill.GetCombinedMinimumSize();
        _headerImage.StretchMode = TextureRect.StretchModeEnum.KeepCentered; _headerImage.Texture = UiStyle.Icon("map", IslandUi.TextMuted, 40);
        ShowPicture(_headerImage, Str(p, "imageUrl"), Str(_about.GetProperty("island"), "coverImageUrl"));
    }

    private (string, Color) StateOf()
    {
        if (_about.TryGetProperty("rental", out var r) && r.ValueKind == JsonValueKind.Object) return ("Alugado", LandTerritoryMap.Rented);
        if (_about.TryGetProperty("sale", out var s) && s.ValueKind == JsonValueKind.Object) return ("À Venda", IslandUi.AccentSecondary);
        if (_about.TryGetProperty("rentOffer", out var o) && o.ValueKind == JsonValueKind.Object) return ("Para Alugar", LandTerritoryMap.Rented);
        return Str(_about.GetProperty("parcel"), "category") == "comum" ? ("Área Comum", LandTerritoryMap.Common) : ("Disponível", LandTerritoryMap.Available);
    }

    /// <summary>Puts the picture of the parcel (or, without one, the cover of the island) in the frame.</summary>
    internal void ShowPicture(TextureRect target, string image, string fallback)
    {
        var reference = image.Length > 0 ? image : fallback;
        if (!reference.StartsWith("asset:", StringComparison.Ordinal) || !Guid.TryParse(reference[6..], out var asset) || GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is not { } cache) return;
        cache.Request(asset, texture => { if (texture is not null && IsInstanceValid(target)) { target.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; target.Texture = texture; } }, _island);
    }

    internal void PickImage(Action<Guid> chosen)
    {
        if (_picker is null || !IsInstanceValid(_picker)) { _picker = new BuildTexturePicker { Name = "ParcelImagePicker" }; (GetParent() ?? this).AddChild(_picker); }
        _picker.Open(false, entry => { if (Guid.TryParse(entry.Asset, out var asset)) chosen(asset); });
    }

    private void SelectTab(string id) { _tab = id; foreach (var (key, button) in _tabButtons) button.ButtonPressed = key == id; if (_loaded) Render(); }

    // ---- json helpers -----------------------------------------------------------------------------------------------------

    internal static string Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    internal static long Long(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    internal static int Int(JsonElement e, string n) => (int)Long(e, n);
    internal static double Dbl(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : 0;
    internal static bool Flag(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    internal static JsonElement? Obj(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    internal static DateTimeOffset Date(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : default;

    internal async Task<bool> Do(Task<IslandReply> call, string done)
    {
        var reply = await call;
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; }
        Notify(done); await LoadAsync(); return true;
    }

    internal void Ask(string title, Control content, Func<Task<bool>> accept, string okText)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); _panel.AddChild(dialog);
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(500, 0));
    }
}
