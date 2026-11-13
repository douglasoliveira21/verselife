using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>Visão Geral, Mapa de Parcelas and Demarcação share one workspace: the indicators, the territory map and the panel at its side (the parcel's details, or the selection when marking land).</summary>
public partial class LandManagementWindow
{
    private LandTerritoryMap _map = null!;
    private Control _kpiRow = null!, _layersBox = null!, _toolsBox = null!, _legend = null!;
    private readonly List<Label> _kpiValues = [];
    private readonly List<Label> _kpiNotes = [];
    private PanelContainer _detailCard = null!, _selectionCard = null!;
    private Label _detailTitle = null!;
    private TextureRect _detailImage = null!;
    private readonly Dictionary<string, Label> _detail = [];
    private readonly Dictionary<string, Label> _selection = [];
    private Button _inspect = null!, _edit = null!, _split = null!, _merge = null!, _sell = null!, _rent = null!;
    private Button _create = null!, _cut = null!, _expand = null!, _shrink = null!, _join = null!, _clear = null!, _undoButton = null!, _redoButton = null!;
    private readonly Dictionary<LandTerritoryMap.Tool, Button> _toolButtons = [];
    private readonly Stack<HashSet<long>> _undo = new(), _redo = new();
    private HashSet<long> _lastCells = [];
    private bool _restoring;

    public IReadOnlyList<string> KpiTitlesForTesting => ["Área Total", "Área Parcelada", "Área Disponível", "Parcelas Vendidas", "Parcelas Alugadas", "Receita Mensal"];
    public IReadOnlyList<string> KpiValuesForTesting => [.. _kpiValues.Select(l => l.Text)];
    public bool KpiVisibleForTesting => _kpiRow.Visible;
    public string DetailForTesting(string key) => _detail.TryGetValue(key, out var label) ? label.Text : "";
    public string DetailTitleForTesting => _detailTitle.Text;
    public string SelectionForTesting(string key) => _selection.TryGetValue(key, out var label) ? label.Text : "";
    public bool DetailVisibleForTesting => _detailCard.Visible;
    public bool SelectionVisibleForTesting => _selectionCard.Visible;
    public bool ButtonEnabledForTesting(string name) => name switch { "inspecionar" => !_inspect.Disabled, "editar" => !_edit.Disabled, "dividir" => !_split.Disabled, "unir" => !_merge.Disabled, "vender" => !_sell.Disabled, "alugar" => !_rent.Disabled, "criar" => !_create.Disabled, "cortar" => !_cut.Disabled, "expandir" => !_expand.Disabled, "reduzir" => !_shrink.Disabled, "juntar" => !_join.Disabled, _ => false };
    public void SelectParcelForTesting(Guid? id) { _map.Select(id); RefreshWorkspace(); }
    public void MarkForTesting((int, int) a, (int, int) b, bool remove = false) { _map.MarkRectangleForTesting(a, b, remove ? LandTerritoryMap.Tool.Remove : LandTerritoryMap.Tool.Add); }
    public async Task CreateParcelForTesting(string name, string category) => await CreateParcelAsync(name, "", category);
    public async Task SplitForTesting(string name) => await SplitAsync(name);
    public async Task<bool> ExpandForTesting(int extra) => Selected is { } p && await ExpandAsync(p, extra);
    public async Task<bool> ShrinkForTesting() => ParcelsTouching(_map.SelectedCells).FirstOrDefault() is { } p && await ShrinkAsync(p);
    public async Task MergeForTesting(Guid primary) => await MergeAsync(primary);
    public async Task SellForTesting(long price) => await SellAsync(price, true, "");
    public async Task RentOfferForTesting(long price, int days) => await OfferRentAsync(price, days, 1, 6, 0, 3);
    public async Task SetCategoryForTesting(string category) { if (Selected is { } p) await Do(_api!.SetCategory(_island, p.Id, category), "Finalidade alterada."); }
    public void UndoForTesting() => UndoCells();
    public void RedoForTesting() => RedoCells();

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private Control BuildWorkspace()
    {
        var page = IslandUi.Column(12); page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _kpiRow = BuildKpis(); page.AddChild(_kpiRow);
        var row = IslandUi.Row(12); row.SizeFlagsVertical = Control.SizeFlags.ExpandFill; page.AddChild(row);

        var (mapCard, mapBody) = IslandUi.Card("Mapa de Parcelas", "map"); ((Control)mapBody.GetParent()).SizeFlagsVertical = Control.SizeFlags.ExpandFill; mapCard.SizeFlagsStretchRatio = 1.85f; mapCard.CustomMinimumSize = new Vector2(520, 0); row.AddChild(mapCard);
        var bar = IslandUi.Row(8); mapBody.AddChild(bar);
        _layersBox = IslandUi.Row(8); bar.AddChild(_layersBox);
        foreach (var (text, on, apply) in new (string, bool, Action<bool>)[] { ("Grade", false, v => _map.ShowGrid = v), ("Rótulos", true, v => _map.ShowLabels = v), ("Imagem da ilha", true, v => _map.ShowCover = v) })
        {
            var toggle = new CheckBox { Text = text, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None }; toggle.AddThemeFontSizeOverride("font_size", 13); toggle.Toggled += v => { apply(v); _map.QueueRedraw(); }; _layersBox.AddChild(toggle);
        }
        _toolsBox = IslandUi.Row(6); bar.AddChild(_toolsBox);
        AddTool("Selecionar", "pointer", LandTerritoryMap.Tool.Pick, "Clique num terreno para escolhê-lo (Ctrl soma mais)");
        AddTool("Demarcar", "plus", LandTerritoryMap.Tool.Add, "Arraste sobre o mapa para marcar quadrados de 4 × 4 m");
        AddTool("Reduzir", "minus", LandTerritoryMap.Tool.Remove, "Arraste para tirar quadrados da marcação");
        _toolsBox.AddChild(new VSeparator());
        _undoButton = IslandUi.Action("Desfazer", "undo", IslandUi.Kind.Quiet, 32, 16); _undoButton.Pressed += UndoCells; _toolsBox.AddChild(_undoButton);
        _redoButton = IslandUi.Action("Refazer", "redo", IslandUi.Kind.Quiet, 32, 16); _redoButton.Pressed += RedoCells; _toolsBox.AddChild(_redoButton);
        bar.AddChild(IslandUi.Spacer());
        foreach (var (icon, tip, act) in new (string, string, Action)[] { ("zoom-in", "Aproximar", () => _map.ZoomBy(1.4f)), ("zoom-out", "Afastar", () => _map.ZoomBy(1 / 1.4f)), ("focus", "Centralizar o mapa", () => _map.Fit()) })
        { var button = IslandUi.IconButton(icon, tip, 34, true); button.Pressed += act; bar.AddChild(button); }
        _map = new LandTerritoryMap { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; mapBody.AddChild(_map);
        _map.ParcelClicked += _ => RefreshWorkspace(); _map.CellSelectionChanged += OnCellsChanged;
        _legend = IslandUi.Row(18); mapBody.AddChild(_legend);
        foreach (var (text, color) in new[] { ("Disponível", LandTerritoryMap.Available), ("À Venda", LandTerritoryMap.OnSale), ("Alugado", LandTerritoryMap.Rented), ("Vendido", LandTerritoryMap.Sold), ("Área Comum", LandTerritoryMap.Common) })
        { var item = IslandUi.Row(6); item.AddChild(IslandUi.Dot(color, 12)); item.AddChild(IslandUi.Text(text, 13, IslandUi.TextSecondary)); _legend.AddChild(item); }

        var side = IslandUi.Column(0); side.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; side.SizeFlagsVertical = Control.SizeFlags.ExpandFill; side.CustomMinimumSize = new Vector2(330, 0); row.AddChild(side);
        _detailCard = BuildDetail(); side.AddChild(_detailCard); _selectionCard = BuildSelection(); side.AddChild(_selectionCard);
        return page;
    }

    private void AddTool(string text, string icon, LandTerritoryMap.Tool tool, string tip)
    {
        var button = IslandUi.Action(text, icon, IslandUi.Kind.Secondary, 32, 16); button.TooltipText = tip; button.ToggleMode = true; button.Pressed += () => { _map.CurrentTool = tool; foreach (var (t, b) in _toolButtons) b.ButtonPressed = t == tool; };
        _toolButtons[tool] = button; button.ButtonPressed = tool == LandTerritoryMap.Tool.Pick; _toolsBox.AddChild(button);
    }

    private Control BuildKpis()
    {
        var row = IslandUi.Row(10);
        var kpis = new (string Title, Color Tint)[] { ("Área Total", IslandUi.TextPrimary), ("Área Parcelada", IslandUi.TextPrimary), ("Área Disponível", LandTerritoryMap.Available), ("Parcelas Vendidas", IslandUi.TextPrimary), ("Parcelas Alugadas", IslandUi.TextPrimary), ("Receita Mensal", IslandUi.TextPrimary) };
        foreach (var (title, tint) in kpis)
        {
            var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 76) }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 14, 8));
            var column = IslandUi.Column(0); card.AddChild(column);
            var head = IslandUi.Row(6); head.AddChild(IslandUi.Text(title, 12, IslandUi.TextSecondary, true)); var note = IslandUi.Text("", 11, IslandUi.TextMuted); head.AddChild(note); column.AddChild(head); _kpiNotes.Add(note);
            var value = IslandUi.Text("—", 22, tint, true); column.AddChild(value); _kpiValues.Add(value); row.AddChild(card);
        }
        return row;
    }

    private PanelContainer BuildDetail()
    {
        var (card, body) = IslandUi.Card("Detalhes da Parcela", "layers"); card.SizeFlagsVertical = Control.SizeFlags.ExpandFill; ((Control)body.GetParent()).SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _detailImage = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(0, 120), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        var frame = new PanelContainer { ClipContents = true }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 8, 1)); frame.AddChild(_detailImage); body.AddChild(frame);
        _detailTitle = IslandUi.Text("Escolha um terreno no mapa", 17, IslandUi.TextPrimary, true, true); body.AddChild(_detailTitle);
        foreach (var (key, title) in new[] { ("area", "Área:"), ("owner", "Proprietário:"), ("state", "Estado:"), ("price", "Preço:"), ("rent", "Aluguel:"), ("tenant", "Locatário:"), ("prims", "Prims:") })
        {
            var line = IslandUi.Row(8); var k = IslandUi.Text(title, 14, IslandUi.TextSecondary); k.CustomMinimumSize = new Vector2(104, 0); line.AddChild(k);
            var v = IslandUi.Text("—", 14, IslandUi.TextPrimary, false, true); v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(v); body.AddChild(line); _detail[key] = v; _detailLines[key] = line;
        }
        body.AddChild(IslandUi.Spacer());
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 8); body.AddChild(grid);
        Button Cell(string text, string? icon, IslandUi.Kind kind, Action act) { var b = IslandUi.Action(text, icon, kind, 38, 16); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.Pressed += act; grid.AddChild(b); return b; }
        _inspect = Cell("Inspecionar", "search", IslandUi.Kind.Secondary, InspectSelected); _edit = Cell("Editar", "settings", IslandUi.Kind.Secondary, () => OpenEdit());
        _split = Cell("Dividir", "scissors", IslandUi.Kind.Secondary, () => { Select("demarcacao"); Notify("Marque com o mouse a parte que vira um terreno novo e use Dividir."); }); _merge = Cell("Unir", "link", IslandUi.Kind.Secondary, UniteSelected);
        _sell = Cell("Vender", "coins", IslandUi.Kind.Orange, () => OpenSell()); _rent = Cell("Alugar", "calendar", IslandUi.Kind.Purple, () => OpenRent());
        return card;
    }

    private readonly Dictionary<string, Control> _detailLines = [];

    private PanelContainer BuildSelection()
    {
        var (card, body) = IslandUi.Card("Seleção", "ruler"); card.SizeFlagsVertical = Control.SizeFlags.ExpandFill; card.Visible = false; ((Control)body.GetParent()).SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        foreach (var (key, title) in new[] { ("area", "Área selecionada:"), ("size", "Dimensões:"), ("cells", "Quadrados:"), ("origin", "Parcela original:"), ("conflict", "Conflitos:"), ("quota", "Cota de prims:") })
        {
            var line = IslandUi.Row(8); var k = IslandUi.Text(title, 14, IslandUi.TextSecondary); k.CustomMinimumSize = new Vector2(138, 0); line.AddChild(k);
            var v = IslandUi.Text("—", 14, IslandUi.TextPrimary, false, true); v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(v); body.AddChild(line); _selection[key] = v;
        }
        var hint = IslandUi.Text("Arraste no mapa com Demarcar para marcar quadrados de 4 × 4 m. Os quadrados precisam se encostar pelos lados.", 13, IslandUi.TextMuted, false, true); body.AddChild(hint);
        body.AddChild(IslandUi.Spacer());
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 8); body.AddChild(grid);
        Button Cell(string text, string icon, IslandUi.Kind kind, Action act) { var b = IslandUi.Action(text, icon, kind, 38, 16); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.Pressed += act; grid.AddChild(b); return b; }
        _create = Cell("Criar Parcela", "plus", IslandUi.Kind.Primary, OpenCreate); _clear = Cell("Limpar", "close", IslandUi.Kind.Secondary, () => _map.ClearCells());
        _cut = Cell("Dividir", "scissors", IslandUi.Kind.Secondary, OpenSplit); _join = Cell("Unir", "link", IslandUi.Kind.Secondary, UniteSelected);
        _expand = Cell("Expandir", "plus", IslandUi.Kind.Secondary, OpenExpand); _shrink = Cell("Reduzir", "minus", IslandUi.Kind.Secondary, OpenShrink);
        return card;
    }

    private void ApplyMode()
    {
        var demarcation = _current == "demarcacao";
        if (_kpiRow is null) return;
        _kpiRow.Visible = _current == "visao"; _layersBox.Visible = !demarcation; _toolsBox.Visible = demarcation; _detailCard.Visible = !demarcation; _selectionCard.Visible = demarcation;
        if (!demarcation) { _map.CurrentTool = LandTerritoryMap.Tool.Pick; foreach (var (t, b) in _toolButtons) b.ButtonPressed = t == LandTerritoryMap.Tool.Pick; }
        RefreshWorkspace();
    }

    private void RefreshWorkspace()
    {
        if (_data is null) return;
        var t = _data.Totals;
        string Pct(double value) => t.Area <= 0 ? "" : $"({value * 100 / t.Area:0.#}%)".Replace('.', ',');
        string[] values = [LandMath.Area(t.Area), LandMath.Area(t.AreaParcelled), LandMath.Area(t.AreaFree), t.Sold.ToString(Pt), t.Rented.ToString(Pt), LandMath.Money(t.MonthlyRevenue)];
        string[] notes = ["", Pct(t.AreaParcelled), Pct(t.AreaFree), "", t.ExpiringSoon > 0 ? $"({t.ExpiringSoon} vencem em 7 dias)" : "", ""];
        for (var i = 0; i < _kpiValues.Count; i++) { _kpiValues[i].Text = values[i]; _kpiNotes[i].Text = notes[i]; }
        RefreshDetail(); RefreshSelection();
    }

    private void RefreshDetail()
    {
        var p = Selected;
        _detailImage.Texture = _cover; _detailImage.GetParent<Control>().Visible = _cover is not null;
        foreach (var line in _detailLines.Values) line.Visible = true;
        if (p is null)
        {
            _detailTitle.Text = "Escolha um terreno no mapa"; foreach (var label in _detail.Values) label.Text = "—";
            foreach (var key in new[] { "price", "rent", "tenant" }) _detailLines[key].Visible = false;
            foreach (var b in new[] { _inspect, _edit, _split, _merge, _sell, _rent }) b.Disabled = true; return;
        }
        var mine = IsMine(p);
        _detailTitle.Text = $"{p.Code} – {p.Name}";
        _detail["area"].Text = $"{LandMath.Area(p.Area)} ({p.CellCount} quadrados)"; _detail["owner"].Text = p.Owner;
        _detail["state"].Text = p.KindText + (p.Kind == "common" ? "" : $" · {p.CategoryText}"); _detail["state"].AddThemeColorOverride("font_color", p.Kind switch { "available" => LandTerritoryMap.Available, "sale" => LandTerritoryMap.OnSale, "rented" => LandTerritoryMap.Rented, "common" => LandTerritoryMap.Common, _ => IslandUi.TextSecondary });
        _detail["price"].Text = p.SalePrice is { } s ? LandMath.Money(s) : "—"; _detailLines["price"].Visible = p.SalePrice is not null;
        _detail["rent"].Text = p.RentPrice is { } r ? $"{LandMath.Money(r)} / {p.RentPeriodDays} dias" : "—"; _detailLines["rent"].Visible = p.RentPrice is not null;
        _detail["tenant"].Text = p.Tenant.Length > 0 ? $"{p.Tenant}" + (p.RentExpiresAt is { } at ? $" (até {at.ToLocalTime():dd/MM/yyyy})" : "") : "—"; _detailLines["tenant"].Visible = p.Tenant.Length > 0;
        _detail["prims"].Text = $"{LandMath.Number(p.MaxObjects)} ({LandMath.Number(p.Objects)} usados)";
        _inspect.Disabled = false;
        _edit.Disabled = false; // the owner changes name, description and finalidade; the staff only the finalidade (the dialog says so)
        var rented = p.Kind == "rented";
        _split.Disabled = !mine || rented; _merge.Disabled = !mine || rented; _sell.Disabled = !mine || rented || p.SalePrice is not null || p.Kind == "common"; _rent.Disabled = !mine || rented || p.RentPrice is not null || p.SalePrice is not null || p.Kind == "common";
        _sell.TooltipText = mine ? "" : "Só quem é dono do terreno pode vender."; _rent.TooltipText = mine ? "" : "Só quem é dono do terreno pode alugar.";
        _sell.Text = p.SalePrice is not null ? "À venda" : "Vender"; _rent.Text = p.RentPrice is not null ? "Para alugar" : "Alugar";
    }

    // ---- marking land ---------------------------------------------------------------------------------------------------------

    private void OnCellsChanged()
    {
        if (!_restoring) { _undo.Push(new HashSet<long>(_lastCells)); _redo.Clear(); }
        _lastCells = new HashSet<long>(_map.SelectedCells); RefreshSelection();
    }

    private void UndoCells() { if (_undo.Count == 0) return; _redo.Push(new HashSet<long>(_map.SelectedCells)); _restoring = true; _map.SetCells(_undo.Pop()); _restoring = false; _lastCells = new HashSet<long>(_map.SelectedCells); RefreshSelection(); }
    private void RedoCells() { if (_redo.Count == 0) return; _undo.Push(new HashSet<long>(_map.SelectedCells)); _restoring = true; _map.SetCells(_redo.Pop()); _restoring = false; _lastCells = new HashSet<long>(_map.SelectedCells); RefreshSelection(); }

    private IEnumerable<LandParcelRow> ParcelsTouching(HashSet<long> cells) => _data is null ? [] : _data.Parcels.Where(p => p.Cells.Overlaps(cells));

    private void RefreshSelection()
    {
        if (_data is null || _create is null) return;
        var cells = _map.SelectedCells; var n = cells.Count;
        _selection["area"].Text = LandMath.Area(n * 16.0); _selection["cells"].Text = n.ToString(Pt);
        if (n == 0) _selection["size"].Text = "—";
        else { var xs = cells.Select(k => LandMath.Unkey(k).Cx).ToList(); var zs = cells.Select(k => LandMath.Unkey(k).Cz).ToList(); _selection["size"].Text = $"{(xs.Max() - xs.Min() + 1) * 4} m × {(zs.Max() - zs.Min() + 1) * 4} m"; }
        var touching = ParcelsTouching(cells).ToList(); var inside = touching.Sum(p => p.Cells.Count(cells.Contains));
        _selection["origin"].Text = n == 0 ? "—" : touching.Count == 0 ? "Nenhuma (terreno livre)" : touching.Count == 1 ? $"{touching[0].Code} – {touching[0].Name}" : $"{touching.Count} terrenos";
        _selection["conflict"].Text = n == 0 ? "—" : inside == 0 ? "Nenhum" : $"{inside} quadrado(s) já pertencem a terrenos"; _selection["conflict"].AddThemeColorOverride("font_color", inside > 0 ? IslandUi.Error : IslandUi.TextPrimary);
        var quota = _data.Totals.Area <= 0 || n == 0 ? 0 : (int)Math.Clamp(Math.Floor(_data.Totals.ObjectsLimit * (n * 16.0) / _data.Totals.Area), 10, 100000);
        _selection["quota"].Text = n == 0 ? "—" : $"cerca de {LandMath.Number(quota)} prims";
        _create.Disabled = n == 0 || inside > 0;
        var one = touching.Count == 1 ? touching[0] : null;
        _cut.Disabled = !(one is not null && IsMine(one) && one.Kind != "rented" && inside == n && n < one.Cells.Count);
        _shrink.Disabled = _cut.Disabled; _expand.Disabled = !(Selected is { } grow && IsMine(grow) && grow.Kind != "rented" && n > 0 && inside == 0);
        _join.Disabled = !(_map.Picked.Count >= 2 && _map.Picked.All(id => _data.Parcels.Any(p => p.Id == id && IsMine(p) && p.Kind != "rented")));
        _undoButton.Disabled = _undo.Count == 0; _redoButton.Disabled = _redo.Count == 0;
    }

    // ---- actions ---------------------------------------------------------------------------------------------------------------

    private void InspectSelected() { if (Selected is { } p) Inspect?.Invoke(p); }

    private void UniteSelected()
    {
        if (_data is null) return;
        var picked = _data.Parcels.Where(p => _map.Picked.Contains(p.Id)).ToList();
        if (picked.Count < 2) { Notify("Para unir, escolha dois ou mais terrenos seus que se encostam (Ctrl + clique)."); return; }
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("As configurações (nome, descrição, regras) valem para o terreno escolhido. Os outros somem e a área fica de uma peça só.", 14, IslandUi.TextSecondary, false, true));
        var drop = IslandUi.Dropdown(picked.Select(p => ($"{p.Code} – {p.Name}", p.Id.ToString()))); content.AddChild(drop);
        Ask($"Unir {picked.Count} terrenos", content, async () => await MergeAsync(Guid.Parse(IslandUi.IdOf(drop))), "Unir");
    }

    private async Task<bool> MergeAsync(Guid primary) { if (_data is null) return false; var ids = _map.Picked.ToList(); var ok = await Do(_api!.Merge(_island, ids, primary), "Terrenos unidos."); if (ok) { _map.Select(primary); RefreshWorkspace(); } return ok; }

    private void OpenSplit()
    {
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text("A parte marcada vira um terreno novo, do mesmo dono. As duas partes precisam ficar de uma peça só.", 14, IslandUi.TextSecondary, false, true));
        var name = IslandUi.Input("Nome do terreno novo", 120); content.AddChild(name);
        Ask("Dividir terreno", content, async () => await SplitAsync(name.Text.Trim()), "Dividir");
    }

    private void OpenExpand()
    {
        if (Selected is not { } parcel) return;
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"Os {_map.SelectedCells.Count} quadrados marcados passam a fazer parte de “{parcel.Name}”. Eles precisam encostar no terreno. Anúncios abertos deste terreno são retirados.", 14, IslandUi.TextSecondary, false, true));
        var extra = IslandUi.Number(0, 100000, 10, "", 140); extra.TooltipText = "Objetos a mais para o terreno. 0 mantém o limite atual.";
        var row = IslandUi.Row(8); row.AddChild(IslandUi.Text("Objetos a mais (0 = manter)", 14, IslandUi.TextSecondary)); row.AddChild(extra); content.AddChild(row);
        Ask($"Expandir {parcel.Code}", content, async () => await ExpandAsync(parcel, (int)extra.Value), "Expandir");
    }

    private async Task<bool> ExpandAsync(LandParcelRow parcel, int extraObjects)
    {
        var ok = await Do(_api!.Expand(_island, parcel.Id, _map.SelectedCells, extraObjects), "Terreno expandido."); if (ok) _map.ClearCells(); return ok;
    }

    private void OpenShrink()
    {
        var parcel = ParcelsTouching(_map.SelectedCells).FirstOrDefault(); if (parcel is null) return;
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"Os {_map.SelectedCells.Count} quadrados marcados saem de “{parcel.Name}” e voltam a ser terreno livre da ilha. O terreno precisa continuar de uma peça só e sem objetos nessa área. O limite de objetos não muda.", 14, IslandUi.TextSecondary, false, true));
        Ask($"Reduzir {parcel.Code}", content, async () => await ShrinkAsync(parcel), "Reduzir");
    }

    private async Task<bool> ShrinkAsync(LandParcelRow parcel)
    {
        var ok = await Do(_api!.Shrink(_island, parcel.Id, _map.SelectedCells), "Terreno reduzido."); if (ok) _map.ClearCells(); return ok;
    }

    private async Task<bool> SplitAsync(string name)
    {
        var parcel = ParcelsTouching(_map.SelectedCells).FirstOrDefault(); if (parcel is null || _data is null) return false;
        if (name.Length == 0) { Notify("Dê um nome ao terreno novo."); return false; }
        var ok = await Do(_api!.Split(_island, parcel.Id, _map.SelectedCells, name), "Terreno dividido."); if (ok) _map.ClearCells(); return ok;
    }

    private void OpenCreate()
    {
        var content = IslandUi.Column(8); var area = LandMath.Area(_map.SelectedCells.Count * 16.0);
        content.AddChild(IslandUi.Text($"Novo terreno de {area} ({_map.SelectedCells.Count} quadrados).", 14, IslandUi.TextSecondary));
        var name = IslandUi.Input("Nome do terreno", 120); content.AddChild(name);
        var description = IslandUi.TextArea("Descrição (opcional)", 70); content.AddChild(description);
        var category = IslandUi.Dropdown(LandOverviewData_Categories()); content.AddChild(category);
        var quota = IslandUi.Number(0, 100000, 10, "", 140); quota.TooltipText = "Quantos objetos cabem neste terreno. 0 = proporcional ao tamanho, do que a ilha ainda tem livre.";
        var quotaRow = IslandUi.Row(8); quotaRow.AddChild(IslandUi.Text("Limite de objetos (0 = automático)", 14, IslandUi.TextSecondary)); quotaRow.AddChild(quota); content.AddChild(quotaRow);
        Ask("Criar parcela", content, async () => await CreateParcelAsync(name.Text.Trim(), description.Text.Trim(), IslandUi.IdOf(category), (int)quota.Value), "Criar");
    }

    private IEnumerable<(string, string)> LandOverviewData_Categories() => (_data?.Categories is { Count: > 0 } c ? c : ["residencial", "comercial", "mista", "social", "comum"]).Select(id => (LandMath.CategoryText(id), id));

    private async Task<bool> CreateParcelAsync(string name, string description, string category, int maxObjects = 0)
    {
        if (name.Length == 0) { Notify("Dê um nome ao terreno."); return false; }
        var reply = await _api!.Claim(_island, name, description, _map.SelectedCells, maxObjects > 0 ? maxObjects : null);
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; }
        if (category.Length > 0 && category != "residencial" && reply.Body.ValueKind == System.Text.Json.JsonValueKind.Object && reply.Body.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parcel)) await _api.SetCategory(_island, parcel, category);
        _map.ClearCells(); Notify("Terreno criado."); await LoadAsync(); return true;
    }

    private void OpenEdit()
    {
        if (Selected is not { } p) return;
        var mine = IsMine(p);
        var content = IslandUi.Column(8); var name = IslandUi.Input("Nome", 120); name.Text = p.Name; name.Editable = mine; content.AddChild(name);
        var description = IslandUi.TextArea("Descrição", 80); description.Text = p.Description; description.Editable = mine; content.AddChild(description);
        var category = IslandUi.Dropdown(LandOverviewData_Categories()); IslandUi.Select(category, p.Category); content.AddChild(category);
        if (!mine) content.AddChild(IslandUi.Text("Este terreno é de outra pessoa: você só muda a finalidade dele.", 13, IslandUi.TextMuted, false, true));
        Ask($"Editar {p.Code}", content, async () =>
        {
            if (mine && (name.Text.Trim() != p.Name || description.Text.Trim() != p.Description))
            {
                var reply = await _api!.Rename(_island, p.Id, name.Text.Trim(), description.Text.Trim(), p.Revision);
                if (!reply.Ok) { Notify(reply.Status == 409 ? "O terreno mudou enquanto você editava. Atualizei; tente de novo." : LandManagementApi.Words(reply)); await LoadAsync(); return false; }
            }
            var chosen = IslandUi.IdOf(category);
            if (chosen != p.Category) { var reply = await _api!.SetCategory(_island, p.Id, chosen); if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; } }
            Notify("Terreno atualizado."); await LoadAsync(); return true;
        }, "Salvar");
    }

    private void OpenSell(LandParcelRow? target = null)
    {
        var p = target ?? Selected; if (p is null) return;
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"Colocar {p.Code} – {p.Name} à venda para todos.", 14, IslandUi.TextSecondary, false, true));
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$"); price.Value = p.SalePrice ?? 1000; content.AddChild(price);
        var objects = new CheckBox { Text = "Incluir os meus objetos no terreno", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; content.AddChild(objects);
        var description = IslandUi.Input("Descrição do anúncio (opcional)", 200); content.AddChild(description);
        Ask(p.SalePrice is null ? "Colocar à venda" : "Mudar o preço", content, async () => { _map.Select(p.Id); return await SellAsync((long)price.Value, objects.ButtonPressed, description.Text.Trim()); }, "Anunciar");
    }

    private async Task<bool> SellAsync(long price, bool includeObjects, string description)
    {
        if (Selected is not { } p) return false;
        if (p.SalePrice is not null) await _api!.CancelSale(_island, p.Id); // a new price replaces the offer
        return await Do(_api!.OfferSale(_island, p.Id, price, includeObjects, description), "Terreno anunciado à venda.");
    }

    private void OpenRent(LandParcelRow? target = null)
    {
        var p = target ?? Selected; if (p is null) return;
        var content = IslandUi.Column(8); content.AddChild(IslandUi.Text($"Oferecer {p.Code} – {p.Name} para aluguel.", 14, IslandUi.TextSecondary, false, true));
        var price = IslandUi.Number(1, 1_000_000_000, 1, " L$ por período"); price.Value = p.RentPrice ?? 500; content.AddChild(price);
        var period = IslandUi.Dropdown([("7 dias", "7"), ("14 dias", "14"), ("30 dias", "30")]); IslandUi.Select(period, "30"); content.AddChild(period);
        var min = IslandUi.Number(1, 52, 1, " período(s) no mínimo"); min.Value = 1; content.AddChild(min);
        var max = IslandUi.Number(1, 52, 1, " período(s) no máximo"); max.Value = 6; content.AddChild(max);
        var deposit = IslandUi.Number(0, 1_000_000_000, 1, " L$ de caução"); content.AddChild(deposit);
        var grace = IslandUi.Number(0, 30, 1, " dia(s) de tolerância"); grace.Value = 3; content.AddChild(grace);
        Ask("Oferecer para alugar", content, async () => { _map.Select(p.Id); return await OfferRentAsync((long)price.Value, int.Parse(IslandUi.IdOf(period)), (int)min.Value, (int)max.Value, (long)deposit.Value, (int)grace.Value); }, "Anunciar");
    }

    private async Task<bool> OfferRentAsync(long price, int days, int min, int max, long deposit, int grace)
    {
        if (Selected is not { } p) return false;
        return await Do(_api!.OfferRent(_island, p.Id, price, days, min, Math.Max(min, max), deposit, grace), "Terreno oferecido para aluguel.");
    }

    /// <summary>Shows a small dialog with the given content; the action runs when the person confirms and the dialog closes only if it worked.</summary>
    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText = "Confirmar")
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        var margin = IslandUi.Padded(content, 6, 6, 6, 6); dialog.AddChild(margin); _panel.AddChild(dialog);
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        IslandUi.Fit(dialog, 480);
    }
}
