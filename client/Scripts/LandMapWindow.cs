using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// One result of the property search.
public sealed record PropertyHit(Guid ParcelId, Guid IslandId, string IslandName, string Name, double Area, string Offer, long Price, int RentPeriodDays, string OwnerName);

/// "Mapa de terrenos": the island drawn from above with every parcel in the colour of what it means to you (yours, for sale, for rent,
/// public, restricted), a side panel with the details and the actions (teleport, see, buy, rent), and a search for land on all public islands.
public partial class LandMapWindow : Node
{
    private static readonly string[] FilterNames = ["Todos", "À venda", "Para alugar", "Meus", "Públicos", "Restritos"];
    private LandManager _land = null!;
    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private Control _mapPage = null!, _searchPage = null!;
    private MapCanvas _canvas = null!;
    private OptionButton _filter = null!;
    private Label _title = null!, _detail = null!;
    private Button _teleport = null!, _see = null!, _buy = null!, _rent = null!, _teleportPoint = null!;
    private LandParcelData? _selected;
    private (double X, double Z)? _point;

    // search
    private LineEdit _query = null!, _minPrice = null!, _maxPrice = null!, _minArea = null!;
    private OptionButton _offer = null!, _sort = null!;
    private ItemList _results = null!;
    private Label _resultNote = null!;
    private Button _goTo = null!;
    private readonly List<PropertyHit> _hits = [];

    /// Joins another island (set by the main UI). Teleporting inside the island goes through the land manager.
    public Action<Guid>? JoinIsland { get; set; }
    /// Tests: answers the search request without a network: (query string) -> (status, body).
    public Func<string, (int Status, string Body)>? SearchTransport { get; set; }

    public bool Visible => _window.Visible;
    public IReadOnlyList<PropertyHit> Hits => _hits;
    public LandParcelData? Selected => _selected;

    public override void _Ready()
    {
        _land = GetParent().GetNodeOrNull<LandManager>("LandManager") ?? throw new InvalidOperationException("LandMapWindow needs a LandManager next to it.");
        _land.Changed += OnLandChanged;
        Build();
    }

    public override void _ExitTree() { if (_land is not null) _land.Changed -= OnLandChanged; }

    public void Toggle() { if (_window.Visible) _window.Visible = false; else Open(); }

    public void Open()
    {
        _window.Visible = true; _window.MoveToFront(); _land.Load(); _canvas.QueueRedraw();
    }

    private void OnLandChanged()
    {
        if (_selected is not null) _selected = _land.Parcels.FirstOrDefault(p => p.Id == _selected.Id);
        if (_window is { Visible: true }) { ShowDetail(); _canvas.QueueRedraw(); }
    }

    // ---- building -----------------------------------------------------------------------------------------------------------

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action; return button;
    }

    private static LineEdit Entry(string placeholder) { var edit = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; edit.AddThemeFontSizeOverride("font_size", 16); return edit; }

    private static OptionButton Choice(params string[] items) { var option = new OptionButton { CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var item in items) option.AddItem(item); return option; }

    private static Label Text(string text = "", bool muted = false, int size = 16)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(220, 0) };
        label.AddThemeFontSizeOverride("font_size", size); if (muted) label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label;
    }

    private void Build()
    {
        _window = new FloatingWindow("Mapa de terrenos", icon: "map") { Name = "LandMap", Visible = false };
        _window.Place(.5f, .5f, -380, -290, 380, 290);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 16 }; _tabs.AddTab("Mapa", "map"); _tabs.AddTab("Buscar terrenos", "search"); content.AddChild(_tabs);
        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(740, 470) }; content.AddChild(stack);

        var map = new HBoxContainer { Visible = true }; map.SetAnchorsPreset(Control.LayoutPreset.FullRect); map.AddThemeConstantOverride("separation", 12); stack.AddChild(map); _mapPage = map;
        _canvas = new MapCanvas(this) { ClipContents = true, CustomMinimumSize = new Vector2(460, 460), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin }; map.AddChild(_canvas);
        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; side.AddThemeConstantOverride("separation", 8); map.AddChild(side);
        _filter = Choice(FilterNames); _filter.ItemSelected += _ => _canvas.QueueRedraw(); side.AddChild(_filter);
        _title = Text("Escolha um terreno no mapa", size: 19); _title.AddThemeFontOverride("font", UiStyle.Bold); side.AddChild(_title);
        _detail = Text(muted: true); side.AddChild(_detail);
        _teleport = Action("Teleportar", UiStyle.ButtonKind.Primary, TeleportSelected); side.AddChild(_teleport);
        _teleportPoint = Action("Teleportar para o ponto", UiStyle.ButtonKind.Secondary, TeleportToPoint); side.AddChild(_teleportPoint);
        _see = Action("Ver terreno", UiStyle.ButtonKind.Secondary, SeeSelected); side.AddChild(_see);
        _buy = Action("Comprar", UiStyle.ButtonKind.Secondary, () => OpenTrade()); side.AddChild(_buy);
        _rent = Action("Alugar", UiStyle.ButtonKind.Secondary, () => OpenTrade()); side.AddChild(_rent);
        var legend = Text("Azul-água: seu · Laranja: à venda ou aluguel · Cinza: público · Vermelho: restrito · Azul: de outras pessoas", muted: true, size: 14); side.AddChild(legend);

        var search = new VBoxContainer { Visible = false }; search.SetAnchorsPreset(Control.LayoutPreset.FullRect); search.AddThemeConstantOverride("separation", 8); stack.AddChild(search); _searchPage = search;
        _query = Entry("Nome do terreno ou da ilha"); _query.TextSubmitted += _ => Search(); search.AddChild(_query);
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 8); search.AddChild(filters);
        _offer = Choice("Venda e aluguel", "Só venda", "Só aluguel"); filters.AddChild(_offer);
        _minPrice = Entry("Preço mínimo"); _maxPrice = Entry("Preço máximo"); _minArea = Entry("Área mínima (m²)"); filters.AddChild(_minPrice); filters.AddChild(_maxPrice); filters.AddChild(_minArea);
        var sortRow = new HBoxContainer(); sortRow.AddThemeConstantOverride("separation", 8); search.AddChild(sortRow);
        _sort = Choice("Mais recentes", "Menor preço", "Maior preço", "Maior área"); sortRow.AddChild(_sort);
        sortRow.AddChild(Action("Buscar", UiStyle.ButtonKind.Primary, Search));
        _results = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 250) }; _results.AddThemeFontSizeOverride("font_size", 16); _results.ItemSelected += _ => UpdateGo(); _results.ItemActivated += _ => GoToResult(); search.AddChild(_results);
        _resultNote = Text(muted: true); search.AddChild(_resultNote);
        _goTo = Action("Ir até o terreno", UiStyle.ButtonKind.Primary, GoToResult); _goTo.Disabled = true; search.AddChild(_goTo);

        _tabs.TabChanged += index => { _mapPage.Visible = index == 0; _searchPage.Visible = index == 1; };
        AddChild(_window);
        ShowDetail();
    }

    // ---- the map ------------------------------------------------------------------------------------------------------------------

    private bool Passes(LandParcelData parcel) => _filter.Selected switch
    {
        1 => parcel.SalePrice is not null && parcel.State == "sale",
        2 => parcel.RentPrice is not null && parcel.State == "rent",
        3 => parcel.Mine || parcel.State == "rented_by_me",
        4 => parcel.State == "public",
        5 => parcel.State == "restricted",
        _ => true
    };

    private sealed partial class MapCanvas(LandMapWindow owner) : Control
    {
        /// The part of the island shown: it zooms in on the parcels (with a margin) so they are big enough to read, and shows the whole island when there are none.
        private (double MinX, double MinZ, double Span) View()
        {
            var land = owner._land; double size = land.IslandSize;
            if (land.Parcels.Count == 0) return (-size / 2, -size / 2, size);
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            foreach (var parcel in land.Parcels) foreach (var key in parcel.Cells) { var (cx, cz) = LandMath.Unkey(key); minX = Math.Min(minX, cx * (double)LandMath.CellSize); minZ = Math.Min(minZ, cz * (double)LandMath.CellSize); maxX = Math.Max(maxX, (cx + 1) * (double)LandMath.CellSize); maxZ = Math.Max(maxZ, (cz + 1) * (double)LandMath.CellSize); }
            var span = Math.Clamp(Math.Max(maxX - minX, maxZ - minZ) + 32, 96, size);
            return ((minX + maxX) / 2 - span / 2, (minZ + maxZ) / 2 - span / 2, span);
        }

        private Vector2 ToPixels(double x, double z)
        {
            var (minX, minZ, span) = View(); return new Vector2((float)((x - minX) / span * Size.X), (float)((z - minZ) / span * Size.Y));
        }

        private (double X, double Z) ToWorld(Vector2 pixel)
        {
            var (minX, minZ, span) = View(); return (minX + pixel.X / Size.X * span, minZ + pixel.Y / Size.Y * span);
        }

        public (double X, double Z) WorldAt(Vector2 pixel) => ToWorld(pixel);

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("#0B1A24"));
            var land = owner._land;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("#143447"), false, 2);
            var island = land.IslandSize / 2.0; DrawRect(new Rect2(ToPixels(-island, -island), ToPixels(island, island) - ToPixels(-island, -island)), new Color("#16323F"));
            var cell = (float)(LandMath.CellSize / View().Span * Size.X);
            foreach (var parcel in land.Parcels)
            {
                var pass = owner.Passes(parcel); var colour = LandManager.ColorOf(parcel.State);
                var fill = new Color(colour, pass ? .55f : .10f);
                foreach (var key in parcel.Cells)
                {
                    var (cx, cz) = LandMath.Unkey(key);
                    DrawRect(new Rect2(ToPixels(cx * (double)LandMath.CellSize, cz * (double)LandMath.CellSize), new Vector2(cell + .6f, cell + .6f)), fill);
                }
                var line = new Color(colour, pass ? 1f : .25f);
                foreach (var edge in LandMath.Boundary(parcel.Cells))
                    DrawLine(ToPixels(edge.X0 * (double)LandMath.CellSize, edge.Z0 * (double)LandMath.CellSize), ToPixels(edge.X1 * (double)LandMath.CellSize, edge.Z1 * (double)LandMath.CellSize), line, parcel == owner._selected ? 3f : 1.4f);
            }
            if (owner._selected is { } chosen && chosen.HasArrival) DrawCircle(ToPixels(chosen.ArrivalX!.Value, chosen.ArrivalZ!.Value), 4, UiStyle.Accent);
            if (owner._point is { } point) { var p = ToPixels(point.X, point.Z); DrawLine(p - new Vector2(6, 0), p + new Vector2(6, 0), Colors.White, 2); DrawLine(p - new Vector2(0, 6), p + new Vector2(0, 6), Colors.White, 2); }
            if (owner._land.GetParent()?.GetNodeOrNull<PlayerController>("Player") is { } player)
            {
                var p = ToPixels(player.GlobalPosition.X, player.GlobalPosition.Z); DrawCircle(p, 5, Colors.White); DrawCircle(p, 3, UiStyle.Accent);
            }
        }

        public override void _GuiInput(InputEvent input)
        {
            if (input is InputEventMouseMotion motion)
            {
                var (x, z) = ToWorld(motion.Position);
                var parcel = owner._land.ParcelAt(x, z);
                TooltipText = parcel is null ? string.Empty : $"{parcel.Name}\n{parcel.OwnerName} · {parcel.Area:0} m²" + (parcel.SalePrice is { } sale && parcel.State == "sale" ? $"\nÀ venda: L$ {sale}" : parcel.RentPrice is { } rent && parcel.State == "rent" ? $"\nAluguel: L$ {rent}" : string.Empty);
            }
            else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            {
                var (x, z) = ToWorld(click.Position); owner.Pick(x, z);
                if (click.DoubleClick) owner.TeleportSelected();
            }
        }
    }

    /// Chooses what is at this spot of the map (a parcel, or just the ground).
    public void Pick(double x, double z)
    {
        _point = (x, z); _selected = _land.ParcelAt(x, z);
        ShowDetail(); _canvas.QueueRedraw();
    }

    public void SelectParcel(LandParcelData parcel) { _selected = parcel; _point = null; ShowDetail(); _canvas.QueueRedraw(); }

    private void ShowDetail()
    {
        if (_title is null) return;
        var p = _selected;
        _teleport.Visible = _see.Visible = p is not null;
        _buy.Visible = p is { State: "sale" }; _rent.Visible = p is { State: "rent" };
        _teleportPoint.Visible = p is null && _point is not null;
        if (p is null)
        {
            _title.Text = _point is null ? "Escolha um terreno no mapa" : "Terreno livre";
            _detail.Text = _point is null ? "Clique em um terreno para ver quem é o dono, o tamanho e o preço. Duplo clique teleporta." : $"Ponto x {_point.Value.X:0}, z {_point.Value.Z:0}. Ninguém é dono daqui.";
            return;
        }
        _title.Text = p.Name;
        var state = p.State switch { "own" => "Seu terreno", "rented_by_me" => "Alugado por você", "sale" => $"À venda por L$ {p.SalePrice}", "rent" => $"Para alugar por L$ {p.RentPrice} a cada {p.RentPeriodDays} dias", "public" => "Terreno público", "restricted" => "Entrada restrita", _ => "Terreno de outra pessoa" };
        _detail.Text = $"{state}\nDono: {p.OwnerName}\nÁrea: {p.Area:0} m²\nTeleporte: {p.TeleportMode switch { "forced" => "no ponto de chegada", "disabled" => "desativado", _ => "livre" }}";
        _teleport.Disabled = p.TeleportMode == "disabled" || p.State == "restricted";
    }

    private void TeleportSelected() { if (_selected is { } p) _land.TeleportTo(p); }

    private void TeleportToPoint() { if (_point is { } point) _land.TeleportToPoint(point.X, point.Z); }

    private void SeeSelected() { if (_selected is { } p) _land.SelectCells([.. p.Cells]); }

    private void OpenTrade() { if (_selected is { } p) _land.ShowTrade(p); }

    // ---- searching ----------------------------------------------------------------------------------------------------------------

    /// The query string the form describes.
    public string QueryFromForm()
    {
        var parts = new List<string>();
        if (_query.Text.Trim().Length > 0) parts.Add("q=" + Uri.EscapeDataString(_query.Text.Trim()));
        parts.Add("offer=" + _offer.Selected switch { 1 => "sale", 2 => "rent", _ => "any" });
        if (long.TryParse(_minPrice.Text.Trim(), out var min) && min >= 0) parts.Add("minPrice=" + min);
        if (long.TryParse(_maxPrice.Text.Trim(), out var max) && max >= 0) parts.Add("maxPrice=" + max);
        if (double.TryParse(_minArea.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var area) && area > 0) parts.Add("minArea=" + area.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parts.Add("sort=" + _sort.Selected switch { 1 => "price", 2 => "price_desc", 3 => "area", _ => "new" });
        return string.Join("&", parts);
    }

    public void Search()
    {
        var query = QueryFromForm();
        _resultNote.Text = "Buscando…";
        if (SearchTransport is not null) { var (status, body) = SearchTransport(query); ReadResults(status, body); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { _resultNote.Text = "Entre na sua conta para buscar terrenos."; return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); ReadResults(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/discovery/properties?{query}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); _resultNote.Text = "Sem conexão com o servidor."; }
    }

    private void ReadResults(int status, string body)
    {
        _hits.Clear(); _results.Clear();
        if (status is < 200 or >= 300) { _resultNote.Text = status == 0 ? "Sem conexão com o servidor." : $"A busca falhou (HTTP {status})."; UpdateGo(); return; }
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string Str(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                long Num(string name) => item.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
                if (!Guid.TryParse(Str("parcelId"), out var parcel) || !Guid.TryParse(Str("islandId"), out var island)) continue;
                var hit = new PropertyHit(parcel, island, Str("islandName"), Str("name"), item.TryGetProperty("area", out var a) && a.TryGetDouble(out var area) ? area : 0, Str("offer"), Num("price"), (int)Num("rentPeriodDays"), Str("ownerName"));
                _hits.Add(hit);
                _results.AddItem($"{hit.Name} · {hit.IslandName} · {hit.Area:0} m² · " + (hit.Offer == "sale" ? $"venda L$ {hit.Price}" : $"aluguel L$ {hit.Price} / {hit.RentPeriodDays} dias"));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { _hits.Clear(); _results.Clear(); _resultNote.Text = "Não foi possível ler os resultados."; return; }
        _resultNote.Text = _hits.Count == 0 ? "Nenhum terreno encontrado com esses filtros." : $"{_hits.Count} terreno(s) encontrado(s). Duplo clique para ir até lá.";
        UpdateGo();
    }

    private PropertyHit? ChosenHit() { var picked = _results.GetSelectedItems(); return picked.Length == 0 || picked[0] >= _hits.Count ? null : _hits[picked[0]]; }

    private void UpdateGo()
    {
        if (_goTo is null) return;
        var hit = ChosenHit(); _goTo.Disabled = hit is null;
        _goTo.Text = hit is null ? "Ir até o terreno" : hit.IslandId == CurrentIsland ? "Teleportar para o terreno" : $"Ir para a ilha “{hit.IslandName}”";
    }

    private Guid CurrentIsland => GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.CurrentIslandId ?? Guid.Empty;

    /// Same island: teleport to the parcel. Another island: join it (the parcel can then be found on its map).
    public void GoToResult()
    {
        if (ChosenHit() is not { } hit) return;
        if (hit.IslandId == CurrentIsland && _land.Parcels.FirstOrDefault(p => p.Id == hit.ParcelId) is { } parcel) { _land.TeleportTo(parcel); return; }
        JoinIsland?.Invoke(hit.IslandId);
    }

    public void SelectResultForTesting(int index) { _results.Select(index); UpdateGo(); }
    public void GoToResultForTesting() => GoToResult();
    public void SetSearchFormForTesting(string query, int offer, string minPrice, string maxPrice, string minArea, int sort) { _query.Text = query; _offer.Select(offer); _minPrice.Text = minPrice; _maxPrice.Text = maxPrice; _minArea.Text = minArea; _sort.Select(sort); }
    public void SetFilterForTesting(int index) { _filter.Select(index); _canvas.QueueRedraw(); }
    public bool PassesForTesting(LandParcelData parcel) => Passes(parcel);
    public void ShowSearchForTesting() => _tabs.Select(1);
    public string TitleForTesting => _title.Text;
    public string DetailForTesting => _detail.Text;
    public bool BuyVisibleForTesting => _buy.Visible;
    public bool RentVisibleForTesting => _rent.Visible;
    public bool TeleportEnabledForTesting => _teleport.Visible && !_teleport.Disabled;
    public string GoTextForTesting => _goTo.Text;
    public string NoteForTesting => _resultNote.Text;
    public void TeleportSelectedForTesting() => TeleportSelected();
    public void TeleportPointForTesting() => TeleportToPoint();
    public Control WindowForTesting => _window;
}
