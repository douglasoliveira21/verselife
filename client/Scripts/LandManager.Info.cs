using System.Globalization;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// The "Sobre o terreno" window: what is under the selection (a parcel, free land or a mix), its owner, size and how much is built,
/// and, for free land, the field to turn it into a parcel.
public partial class LandManager
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private FloatingWindow _info = null!;
    private UiTabs _infoTabs = null!;
    private Control _generalPage = null!, _objectsPage = null!, _claimBox = null!, _tradePage = null!;
    private Label _name = null!, _description = null!, _area = null!, _owner = null!, _status = null!, _region = null!, _location = null!, _limits = null!, _summary = null!;
    private Label _objects = null!, _cost = null!, _free = null!;
    private ProgressBar _objectsBar = null!, _costBar = null!;
    private LineEdit _claimName = null!;

    public bool InfoOpen => _info is not null && _info.Visible;

    private static Label Value(bool wrap = false)
    {
        var label = new Label { AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(wrap ? 300 : 0, 0) };
        if (!wrap) label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.AddThemeFontOverride("font", UiStyle.Medium); label.AddThemeFontSizeOverride("font_size", 18);
        return label;
    }

    private static Label Caption(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(120, 0), VerticalAlignment = VerticalAlignment.Top };
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private void BuildInfoWindow()
    {
        _info = new FloatingWindow("Sobre o terreno", icon: "map") { Name = "LandInfo", Visible = false };
        _info.Place(0, .5f, 24, -300, 684, 300);
        _info.CloseRequested += () => { if (!_active) ClearSelection(); };
        var content = _info.Content; content.AddThemeConstantOverride("separation", 10);

        _infoTabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 15 };
        _infoTabs.AddTab("Geral", "list"); _infoTabs.AddTab("Objetos", "cube"); _infoTabs.AddTab("Venda e aluguel", "coin"); _infoTabs.AddTab("Acesso", "lock"); _infoTabs.AddTab("Administração", "settings"); _infoTabs.AddTab("Ambiente", "sun");
        content.AddChild(_infoTabs);

        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 380) };
        content.AddChild(stack);

        var general = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; general.AddThemeConstantOverride("separation", 8); general.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 8); general.AddChild(grid);
        Label Row(string caption, bool wrap = false) { grid.AddChild(Caption(caption)); var value = Value(wrap); grid.AddChild(value); return value; }
        _name = Row("Nome"); _description = Row("Descrição", true); _area = Row("Área"); _owner = Row("Proprietário"); _status = Row("Estado"); _region = Row("Região"); _location = Row("Localização"); _limits = Row("Limites", true);

        _claimBox = new VBoxContainer { Visible = false }; ((VBoxContainer)_claimBox).AddThemeConstantOverride("separation", 8);
        var claimHint = new Label { Text = "Ninguém é dono desta área. Dê um nome para marcá-la como terreno (donos e administradores da ilha).", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        claimHint.AddThemeColorOverride("font_color", UiStyle.TextMuted); claimHint.AddThemeFontSizeOverride("font_size", 16); _claimBox.AddChild(claimHint);
        var claimRow = new HBoxContainer(); claimRow.AddThemeConstantOverride("separation", 8); _claimBox.AddChild(claimRow);
        _claimName = new LineEdit { PlaceholderText = "Nome do novo terreno", MaxLength = 120, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) }; _claimName.AddThemeFontSizeOverride("font_size", 18);
        _claimName.TextSubmitted += _ => ClaimSelection(_claimName.Text); claimRow.AddChild(_claimName);
        var claim = new Button { Text = " Criar terreno", Icon = UiStyle.Icon("check", UiStyle.Text, 22), CustomMinimumSize = new Vector2(190, 44), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(claim, UiStyle.ButtonKind.Primary, 10, 6); claim.AddThemeFontSizeOverride("font_size", 18); claim.Pressed += () => ClaimSelection(_claimName.Text); claimRow.AddChild(claim);
        general.AddChild(_claimBox);
        stack.AddChild(general); _generalPage = general;

        var objects = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; objects.AddThemeConstantOverride("separation", 10);
        ProgressBar Bar() => new() { MinValue = 0, MaxValue = 1, Step = .001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _objects = Value(); objects.AddChild(_objects); _objectsBar = Bar(); objects.AddChild(_objectsBar);
        _cost = Value(); objects.AddChild(_cost); _costBar = Bar(); objects.AddChild(_costBar);
        _free = Value(true); objects.AddChild(_free);
        BuildObjectsList(objects);
        var objectsScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; objectsScroll.SetAnchorsPreset(Control.LayoutPreset.FullRect); objectsScroll.AddChild(objects);
        stack.AddChild(objectsScroll); _objectsPage = objectsScroll;
        var trade = BuildTradePage(); stack.AddChild(trade); _tradePage = trade;
        _accessPage = BuildAccessPage(); stack.AddChild(_accessPage); _adminPage = BuildAdminPage(); stack.AddChild(_adminPage); _stylePage = BuildStylePage(); stack.AddChild(_stylePage);
        _people = new PersonActions { Name = "People" }; AddChild(_people);
        _infoTabs.TabChanged += ShowInfoPage;

        content.AddChild(new HSeparator());
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _summary.AddThemeColorOverride("font_color", UiStyle.AccentText); _summary.AddThemeFontSizeOverride("font_size", 17);
        content.AddChild(_summary);
        AddChild(_info);
    }

    private void ShowInfo()
    {
        _info.Visible = true; _info.MoveToFront(); _infoTabs.Select(0);
        UpdateInfo();
    }

    public void CloseInfo() { _info.Visible = false; if (!_active) ClearSelection(); }

    private static string Metres(double value) => value.ToString("0.#", Pt);

    private void UpdateInfo()
    {
        if (_info is null || !_info.Visible) return;
        var (parcels, free) = Covered();
        UpdateTrade(parcels, free);
        UpdateAdmin(parcels, free);
        UpdateStyle();
        var extent = LandMath.Extent(_selection);
        _region.Text = $"Ilha · {_islandSize} × {_islandSize} m";
        _claimBox.Visible = false;
        if (extent is { } e)
        {
            var s = (double)LandMath.CellSize;
            _location.Text = $"x {Metres((e.MinCx + e.MaxCx + 1) * s / 2)}, z {Metres((e.MinCz + e.MaxCz + 1) * s / 2)}";
            _limits.Text = $"x de {Metres(e.MinCx * s)} a {Metres((e.MaxCx + 1) * s)} · z de {Metres(e.MinCz * s)} a {Metres((e.MaxCz + 1) * s)}";
        }
        else { _location.Text = "—"; _limits.Text = "—"; }
        var selectedArea = LandMath.AreaOf(_selection.Count);
        _summary.Text = _selection.Count == 0 ? "Nada selecionado." : $"Selecionado: {_selection.Count:N0} quadrados · {Metres(selectedArea)} m²".Replace(",", ".");

        if (parcels.Count == 1 && free == 0)
        {
            var parcel = parcels[0];
            _name.Text = parcel.Name; _description.Text = string.IsNullOrWhiteSpace(parcel.Description) ? "—" : parcel.Description;
            _area.Text = _selection.Count == parcel.CellCount ? $"{Metres(parcel.Area)} m²" : $"{Metres(selectedArea)} m² selecionados de {Metres(parcel.Area)} m²";
            _owner.Text = parcel.Mine ? $"{parcel.OwnerName} (você)" : parcel.OwnerName; _status.Text = StateText(parcel);
            _objects.Text = $"Objetos: {parcel.Objects:N0} de {parcel.MaxObjects:N0}".Replace(",", "."); _objectsBar.Value = parcel.MaxObjects == 0 ? 0 : Mathf.Min(1.0, parcel.Objects / (double)parcel.MaxObjects);
            _cost.Text = $"Custo de construção: {parcel.Cost:N0} de {parcel.MaxCost:N0}".Replace(",", "."); _costBar.Value = parcel.MaxCost == 0 ? 0 : Mathf.Min(1.0, parcel.Cost / (double)parcel.MaxCost);
            _free.Text = $"Ainda cabem {Math.Max(0, parcel.MaxObjects - parcel.Objects):N0} objetos e {Math.Max(0, parcel.MaxCost - parcel.Cost):N0} de custo.\nDo dono: {parcel.Objects - parcel.OthersObjects:N0} objetos · De outras pessoas: {parcel.OthersObjects:N0} objetos (custo {parcel.OthersCost:N0}).".Replace(",", ".");
        }
        else if (parcels.Count == 0)
        {
            _name.Text = "Terreno livre"; _description.Text = "Sem parcela: vale a regra da ilha para construir."; _area.Text = $"{Metres(selectedArea)} m²"; _owner.Text = "—"; _status.Text = "Disponível";
            _claimBox.Visible = _selection.Count > 0;
            _objects.Text = "Objetos: —"; _objectsBar.Value = 0; _cost.Text = "Custo de construção: —"; _costBar.Value = 0; _free.Text = "A capacidade é calculada para cada terreno.";
        }
        else
        {
            _name.Text = $"{parcels.Count} terrenos" + (free > 0 ? " e área livre" : string.Empty); _description.Text = string.Join(", ", parcels.Take(4).Select(p => p.Name)) + (parcels.Count > 4 ? "…" : string.Empty);
            _area.Text = $"{Metres(selectedArea)} m²"; _owner.Text = string.Join(", ", parcels.Select(p => p.OwnerName).Distinct().Take(3)); _status.Text = "Seleção mista";
            _objects.Text = "Objetos: —"; _objectsBar.Value = 0; _cost.Text = "Custo de construção: —"; _costBar.Value = 0; _free.Text = "Selecione um só terreno para ver a capacidade.";
        }
    }

    private static string StateText(LandParcelData parcel) => parcel.State switch
    {
        "own" => "Seu terreno", "rented_by_me" => "Alugado por você", "public" => "Terreno público", "restricted" => "Entrada restrita", "sale" => "À venda", "rent" => "Para alugar", _ => "Terreno de outra pessoa"
    };

    public string InfoNameForTesting => _name.Text;
    public string InfoStatusForTesting => _status.Text;
    public string InfoAreaForTesting => _area.Text;
    public string InfoObjectsForTesting => _objects.Text;
    public bool ClaimBoxVisibleForTesting => _claimBox.Visible;
    public string SummaryForTesting => _summary.Text;
    public Control InfoWindowForTesting => _info;
}
