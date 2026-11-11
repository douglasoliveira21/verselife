using System.Globalization;
using Godot;
using LifeVerse.Client.Build;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Geral tab: who the island is (name, description, cover), how it is listed (category, rating, language, visibility), how full it is (prims used) and where people arrive.
/// It shows what the server sent and hands back a draft; the window sends it. Nothing here is a made-up number.
/// </summary>
public partial class IslandGeneralPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly Dictionary<string, string> CategoryNames = new() { ["social"] = "Social", ["nature"] = "Natureza", ["sandbox"] = "Construção livre", ["games"] = "Jogos", ["shopping"] = "Compras", ["music"] = "Música", ["art"] = "Arte", ["education"] = "Educação", ["roleplay"] = "Roleplay", ["other"] = "Outra" };
    private static readonly Dictionary<string, string> RatingNames = new() { ["livre"] = "Livre", ["moderado"] = "Moderado", ["adulto"] = "Adulto" };
    private static readonly Dictionary<string, string> LanguageNames = new() { ["pt-BR"] = "Português (BR)", ["en-US"] = "Inglês (EUA)", ["es-ES"] = "Espanhol", ["fr-FR"] = "Francês", ["de-DE"] = "Alemão", ["it-IT"] = "Italiano", ["ja-JP"] = "Japonês", ["other"] = "Outro" };

    private LineEdit _name = null!;
    private TextEdit _description = null!;
    private Label _nameCount = null!, _descriptionCount = null!, _owner = null!, _admins = null!, _created = null!, _mode = null!, _coverNote = null!;
    private Label _total = null!, _used = null!, _free = null!, _percent = null!;
    private OptionButton _category = null!, _rating = null!, _language = null!;
    private SpinBox _capacity = null!, _x = null!, _z = null!;
    private IslandToggle _inSearch = null!, _onMap = null!;
    private TextureRect _cover = null!;
    private ProgressBar _usage = null!;
    private VBoxContainer _accessRules = null!;
    private Button _pickCover = null!, _removeCover = null!, _usePosition = null!, _openAccess = null!;
    private BuildTexturePicker? _picker;
    private IslandInfo? _loaded;
    private string _coverRef = "";
    private bool _filling;
    private int _nameLimit = 48, _descriptionLimit = 500;
    private readonly Dictionary<string, Control> _fieldControls = [];

    public event Action? Changed;
    public event Action? ConfigureAccessRequested;
    /// <summary>Where the avatar stands now (x and z), or null when there is no avatar to read.</summary>
    public Func<(double X, double Z)?>? CurrentPosition { get; set; }
    /// <summary>Loads the picture of an asset (the window knows where the texture cache lives).</summary>
    public Action<Guid, Action<Texture2D?>>? RequestTexture { get; set; }

    public IslandInfo? Loaded => _loaded;
    public string NameForTesting { get => _name.Text; set { _name.Text = value; _name.EmitSignal(LineEdit.SignalName.TextChanged, value); } }
    public string DescriptionForTesting { get => _description.Text; set { _description.Text = value; OnDescriptionChanged(); } }
    public string NameCountForTesting => _nameCount.Text;
    public string DescriptionCountForTesting => _descriptionCount.Text;
    public double CapacityForTesting { get => _capacity.Value; set => _capacity.Value = value; }
    public bool InSearchForTesting { get => _inSearch.ButtonPressed; set => _inSearch.ButtonPressed = value; }
    public bool OnMapForTesting { get => _onMap.ButtonPressed; set => _onMap.ButtonPressed = value; }
    public string UsageTextForTesting => $"{_total.Text}|{_used.Text}|{_free.Text}|{_percent.Text}";
    public double UsageBarForTesting => _usage.Value;
    public string OwnerForTesting => _owner.Text;
    public string AdminsForTesting => _admins.Text;
    public string CreatedForTesting => _created.Text;
    public string ModeForTesting => _mode.Text;
    public string CategoryForTesting => IslandUi.IdOf(_category);
    public bool EditableForTesting => _name.Editable;
    public bool CoverShownForTesting => _cover.Texture is not null;
    public string CoverRefForTesting { get => _coverRef; set { _coverRef = value; Changed?.Invoke(); } }
    public double SpawnXForTesting => _x.Value;
    public void UseMyPositionForTesting() => UseMyPosition();

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; column.SizeFlagsVertical = SizeFlags.ExpandFill; AddChild(column);

        var top = IslandUi.Row(14); top.SizeFlagsVertical = SizeFlags.ExpandFill; top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 410); column.AddChild(top);
        top.AddChild(BuildIdentity()); top.AddChild(BuildInfo());
        var bottom = IslandUi.Row(14); bottom.SizeFlagsHorizontal = SizeFlags.ExpandFill; bottom.CustomMinimumSize = new Vector2(0, 200); column.AddChild(bottom);
        var usage = BuildUsage(); usage.SizeFlagsStretchRatio = 32; var access = BuildAccess(); access.SizeFlagsStretchRatio = 32; var arrival = BuildArrival(); arrival.SizeFlagsStretchRatio = 36;
        bottom.AddChild(usage); bottom.AddChild(access); bottom.AddChild(arrival);
        SetEditable(false);
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildIdentity()
    {
        var (card, body) = IslandUi.Card("Identidade da Ilha", "doc");
        body.AddChild(IslandUi.Text("Nome da Ilha", 14, IslandUi.TextSecondary));
        _name = IslandUi.Input("Nome da ilha"); _name.TextChanged += _ => { UpdateCounts(); NotifyChanged(); }; body.AddChild(_name); _fieldControls["name"] = _name;
        _nameCount = IslandUi.Text("0 / 48", 13, IslandUi.TextMuted); _nameCount.HorizontalAlignment = HorizontalAlignment.Right; _nameCount.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_nameCount);
        body.AddChild(IslandUi.Text("Descrição da Ilha", 14, IslandUi.TextSecondary));
        _description = IslandUi.TextArea("Conte o que as pessoas encontram na sua ilha…", 94); _description.TextChanged += OnDescriptionChanged; body.AddChild(_description); _fieldControls["description"] = _description;
        _descriptionCount = IslandUi.Text("0 / 500", 13, IslandUi.TextMuted); _descriptionCount.HorizontalAlignment = HorizontalAlignment.Right; _descriptionCount.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_descriptionCount);
        body.AddChild(IslandUi.Text("Imagem de Divulgação", 14, IslandUi.TextPrimary, true));

        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 125), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        _cover = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddChild(_cover);
        _coverNote = IslandUi.Text("Sem imagem de divulgação", 14, IslandUi.TextMuted); _coverNote.HorizontalAlignment = HorizontalAlignment.Center; _coverNote.SizeFlagsHorizontal = SizeFlags.ExpandFill; frame.AddChild(_coverNote);
        var tools = IslandUi.Row(8); tools.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; tools.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _pickCover = IslandUi.IconButton("image", "Escolher imagem", 36, true); _pickCover.Pressed += OpenPicker; tools.AddChild(_pickCover);
        _removeCover = IslandUi.IconButton("trash", "Remover imagem", 36, true); _removeCover.Pressed += () => { _coverRef = ""; ShowCover(""); NotifyChanged(); }; tools.AddChild(_removeCover);
        frame.AddChild(IslandUi.Padded(tools, 8, 8, 8, 8));
        body.AddChild(frame);
        return card;
    }

    private Control BuildInfo()
    {
        var (card, body) = IslandUi.Card("Informações da Ilha", "info");
        body.AddThemeConstantOverride("separation", 9);
        _category = IslandUi.Dropdown(CategoryNames.Select(p => (p.Value, p.Key))); _category.ItemSelected += _ => NotifyChanged(); body.AddChild(Line("Categoria:", _category)); _fieldControls["category"] = _category;
        _rating = IslandUi.Dropdown(RatingNames.Select(p => (p.Value, p.Key))); _rating.ItemSelected += _ => NotifyChanged(); body.AddChild(Line("Classificação de Conteúdo:", _rating)); _fieldControls["contentRating"] = _rating;
        _language = IslandUi.Dropdown(LanguageNames.Select(p => (p.Value, p.Key))); _language.ItemSelected += _ => NotifyChanged(); body.AddChild(Line("Idioma Principal:", _language)); _fieldControls["language"] = _language;
        _owner = IslandUi.Text("", 15); body.AddChild(Line("Proprietário:", WithIcon("user", _owner)));
        _admins = IslandUi.Text("", 15); var adminRow = WithIcon("people", _admins);
        var manage = IslandUi.Action("Gerenciar", null, IslandUi.Kind.Secondary, 32); manage.Disabled = true; manage.TooltipText = "A gestão de administradores fica na aba Permissões, que ainda não está disponível."; adminRow.AddChild(manage);
        body.AddChild(Line("Administradores:", adminRow));
        _created = IslandUi.Text("", 15); body.AddChild(Line("Data de criação:", WithIcon("calendar", _created)));
        _capacity = IslandUi.Number(1, 256, 1, " visitantes"); _capacity.ValueChanged += _ => NotifyChanged(); _capacity.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _capacity.CustomMinimumSize = new Vector2(190, 38);
        body.AddChild(Line("Capacidade máxima:", WithIcon("people", _capacity))); _fieldControls["maxPlayers"] = _capacity;
        _inSearch = new IslandToggle(); _inSearch.Toggled += _ => NotifyChanged(); _onMap = new IslandToggle(); _onMap.Toggled += _ => NotifyChanged();
        body.AddChild(Line("Visibilidade:", ToggleRow("eye", "Aparecer no buscador", _inSearch)));
        body.AddChild(Line("", ToggleRow("map", "Aparecer no mapa", _onMap)));
        return card;
    }

    private Control BuildUsage()
    {
        var (card, body) = IslandUi.Card("Estatísticas de Uso", "chart");
        _total = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _used = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _free = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _percent = IslandUi.Text("", 15, IslandUi.TextPrimary, true);
        foreach (var (caption, value) in new[] { ("Total de Prims:", _total), ("Prims Utilizadas:", _used), ("Prims Livres:", _free), ("Uso:", _percent) })
        {
            var row = IslandUi.Row(8); var label = IslandUi.Text(caption, 15, IslandUi.TextSecondary); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(label); value.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(value);
            body.AddChild(row); body.AddChild(IslandUi.Divider());
        }
        _usage = IslandUi.Progress(0, 14); body.AddChild(_usage);
        return card;
    }

    private Control BuildAccess()
    {
        var (card, body) = IslandUi.Card("Acesso Atual", "people");
        var modeRow = IslandUi.Row(8); modeRow.AddChild(IslandUi.Text("Modo de Acesso:", 15, IslandUi.TextSecondary)); _mode = IslandUi.Text("", 15, IslandUi.Success, true); modeRow.AddChild(_mode); body.AddChild(modeRow);
        _accessRules = IslandUi.Column(5); body.AddChild(_accessRules);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _openAccess = IslandUi.Action("Configurar Acesso", null, IslandUi.Kind.Secondary, 38); _openAccess.Pressed += () => ConfigureAccessRequested?.Invoke(); body.AddChild(_openAccess);
        return card;
    }

    private Control BuildArrival()
    {
        var (card, body) = IslandUi.Card("Ponto de Chegada", "target");
        _x = IslandUi.Number(-4096, 4096, .01); _z = IslandUi.Number(-4096, 4096, .01);
        foreach (var spin in new[] { _x, _z }) spin.ValueChanged += _ => NotifyChanged();
        _fieldControls["spawn"] = _x;
        var position = IslandUi.Row(8); position.AddChild(Caption("Posição (X, Z):", 140)); position.AddChild(_x); position.AddChild(_z); body.AddChild(position);
        body.AddChild(IslandUi.Text("A altura (Y) é a do terreno naquele ponto, calculada quando a pessoa chega.", 13, IslandUi.TextMuted, false, true));
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var buttons = IslandUi.Row(10);
        var world = IslandUi.Action("Definir no Mundo", "pin", IslandUi.Kind.Secondary, 38); world.Disabled = true; world.TooltipText = "Escolher o ponto clicando no cenário ainda não está disponível. Use “Usar Minha Posição”."; buttons.AddChild(world);
        _usePosition = IslandUi.Action("Usar Minha Posição", "target", IslandUi.Kind.Orange, 38); _usePosition.Pressed += UseMyPosition; buttons.AddChild(_usePosition);
        foreach (var button in new[] { world, _usePosition }) button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(buttons);
        return card;
    }

    // ---- small builders ---------------------------------------------------------------------------------------------------

    private static Label Caption(string text, float width) { var label = IslandUi.Text(text, 14, IslandUi.TextPrimary); label.CustomMinimumSize = new Vector2(width, 0); return label; }

    private static HBoxContainer Line(string caption, Control value)
    {
        var row = IslandUi.Row(12); var label = IslandUi.Text(caption, 14, IslandUi.TextPrimary); label.CustomMinimumSize = new Vector2(205, 0); row.AddChild(label);
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(value); return row;
    }

    private static HBoxContainer WithIcon(string icon, Control value)
    {
        var row = IslandUi.Row(9); row.AddChild(UiStyle.IconRect(icon, IslandUi.TextSecondary, 20)); value.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(value); return row;
    }

    private static HBoxContainer ToggleRow(string icon, string text, IslandToggle toggle)
    {
        var row = IslandUi.Row(9); row.AddChild(UiStyle.IconRect(icon, IslandUi.TextSecondary, 20)); var label = IslandUi.Text(text, 15, IslandUi.TextPrimary); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(label); row.AddChild(toggle); return row;
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private void NotifyChanged() { if (!_filling) { ClearErrors(); Changed?.Invoke(); } }

    private void UpdateCounts()
    {
        _nameCount.Text = $"{_name.Text.Length} / {_nameLimit}"; _descriptionCount.Text = $"{_description.Text.Length} / {_descriptionLimit}";
        _descriptionCount.AddThemeColorOverride("font_color", _description.Text.Length >= _descriptionLimit ? IslandUi.Warning : IslandUi.TextMuted);
        _nameCount.AddThemeColorOverride("font_color", _name.Text.Length >= _nameLimit ? IslandUi.Warning : IslandUi.TextMuted);
    }

    private void OnDescriptionChanged()
    {
        if (_description.Text.Length > _descriptionLimit) { var line = _description.GetCaretLine(); var column = _description.GetCaretColumn(); _description.Text = _description.Text[.._descriptionLimit]; _description.SetCaretLine(line); _description.SetCaretColumn(Math.Min(column, _description.GetLine(line).Length)); }
        UpdateCounts(); NotifyChanged();
    }

    private static string Thousands(long value) => value.ToString("N0", Pt);

    /// <summary>Shows what the server sent and forgets any draft.</summary>
    public void Fill(IslandInfo info)
    {
        _filling = true;
        _loaded = info; _nameLimit = info.NameLimit; _descriptionLimit = info.DescriptionLimit; _name.MaxLength = info.NameLimit;
        _name.Text = info.Name; _description.Text = info.Description;
        IslandUi.Select(_category, info.Category); IslandUi.Select(_rating, info.ContentRating); IslandUi.Select(_language, info.Language);
        _capacity.MaxValue = info.MaxPlayersLimit; _capacity.Value = info.MaxPlayers;
        _inSearch.SetPressedNoSignal(info.ShowInSearch); _onMap.SetPressedNoSignal(info.ShowOnMap);
        var half = Math.Max(32, info.Size); _x.MinValue = _z.MinValue = -half; _x.MaxValue = _z.MaxValue = half;
        _x.Value = Math.Round(info.SpawnX, 2); _z.Value = Math.Round(info.SpawnZ, 2);
        _owner.Text = info.OwnerName; _admins.Text = info.AdminCount == 1 ? "1 administrador" : $"{info.AdminCount} administradores";
        _created.Text = info.CreatedAt == default ? "—" : info.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy", Pt);
        _total.Text = Thousands(info.MaxCost); _used.Text = Thousands(info.Cost); _free.Text = Thousands(Math.Max(0, info.MaxCost - info.Cost));
        _percent.Text = info.UsagePercent.ToString("0.0", Pt) + "%"; _usage.Value = info.UsagePercent; IslandUi.SetProgressColor(_usage, info.UsagePercent);
        _percent.AddThemeColorOverride("font_color", info.UsagePercent >= 90 ? IslandUi.Error : info.UsagePercent >= 75 ? IslandUi.AccentSecondary : IslandUi.TextPrimary);
        ShowAccess(info);
        _coverRef = info.CoverImageUrl; ShowCover(_coverRef);
        UpdateCounts(); SetEditable(info.CanEdit); ClearErrors();
        _filling = false;
    }

    private void ShowAccess(IslandInfo info)
    {
        _mode.Text = info.IsPublic ? "Pública" : "Privada"; _mode.AddThemeColorOverride("font_color", info.IsPublic ? IslandUi.Success : IslandUi.AccentSecondary);
        foreach (var child in _accessRules.GetChildren()) child.QueueFree();
        // The same rules the server applies: a public island can be reached from the map (when it shows there) and by teleport; invitations ("enter" permission) always work.
        foreach (var (text, on) in new[] { ("Entrada pelo mapa", info.IsPublic && info.ShowOnMap), ("Teleporte direto", info.IsPublic), ("Entrada por convites", true) })
        {
            var row = IslandUi.Row(9); row.AddChild(UiStyle.IconRect(on ? "check" : "close", on ? IslandUi.Success : IslandUi.Disabled, 18)); row.AddChild(IslandUi.Text(text, 14, on ? IslandUi.TextPrimary : IslandUi.TextMuted)); _accessRules.AddChild(row);
        }
    }

    private void SetEditable(bool editable)
    {
        _name.Editable = editable; _description.Editable = editable; _category.Disabled = _rating.Disabled = _language.Disabled = !editable; _capacity.Editable = editable;
        _inSearch.Disabled = _onMap.Disabled = !editable; foreach (var spin in new[] { _x, _z }) spin.Editable = editable;
        _pickCover.Disabled = _removeCover.Disabled = _usePosition.Disabled = !editable; _pickCover.Visible = _removeCover.Visible = editable;
    }

    /// <summary>The values as they are on screen now.</summary>
    public IslandInfo Draft()
    {
        var info = _loaded ?? new IslandInfo();
        return new IslandInfo
        {
            Id = info.Id, Name = _name.Text.Trim(), Description = _description.Text.Trim(), CoverImageUrl = _coverRef, Category = IslandUi.IdOf(_category), ContentRating = IslandUi.IdOf(_rating), Language = IslandUi.IdOf(_language),
            MaxPlayers = (int)_capacity.Value, ShowInSearch = _inSearch.ButtonPressed, ShowOnMap = _onMap.ButtonPressed, IsPublic = info.IsPublic, Size = info.Size,
            SpawnX = _x.Value, SpawnZ = _z.Value, UpdatedAt = info.UpdatedAt,
        };
    }

    public bool IsDirty
    {
        get
        {
            if (_loaded is not { } was) return false; var now = Draft();
            return now.Name != was.Name.Trim() || now.Description != was.Description.Trim() || now.CoverImageUrl != was.CoverImageUrl || now.Category != was.Category || now.ContentRating != was.ContentRating || now.Language != was.Language
                || now.MaxPlayers != was.MaxPlayers || now.ShowInSearch != was.ShowInSearch || now.ShowOnMap != was.ShowOnMap
                || Math.Abs(now.SpawnX - Math.Round(was.SpawnX, 2)) > .004 || Math.Abs(now.SpawnZ - Math.Round(was.SpawnZ, 2)) > .004;
        }
    }

    /// <summary>The default for what has a default (not the name, the description or the picture, which belong to the owner).</summary>
    public void RestoreDefaults()
    {
        _filling = true;
        IslandUi.Select(_category, "social"); IslandUi.Select(_rating, "livre"); IslandUi.Select(_language, "pt-BR"); _capacity.Value = Math.Min(64, _capacity.MaxValue); _inSearch.SetPressedNoSignal(true); _onMap.SetPressedNoSignal(true);
        _filling = false; Changed?.Invoke();
    }

    /// <summary>Drops the draft and shows the saved values again.</summary>
    public void Revert() { if (_loaded is { } info) Fill(info); Changed?.Invoke(); }

    public void ShowErrors(IReadOnlyDictionary<string, string> fields)
    {
        ClearErrors();
        foreach (var (field, message) in fields)
        {
            if (!_fieldControls.TryGetValue(field, out var control)) continue;
            control.TooltipText = message;
            if (control is LineEdit or TextEdit) { var box = IslandUi.Box(IslandUi.InputBackground, IslandUi.Error, IslandUi.RadiusInput, 1, 11, 7); control.AddThemeStyleboxOverride("normal", box); control.AddThemeStyleboxOverride("focus", box); }
        }
    }

    private void ClearErrors()
    {
        foreach (var control in _fieldControls.Values) { control.TooltipText = ""; if (control is LineEdit or TextEdit) IslandUi.StyleField(control); }
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    private void UseMyPosition()
    {
        if (CurrentPosition?.Invoke() is not { } p) return;
        _x.Value = Math.Round(p.X, 2); _z.Value = Math.Round(p.Z, 2);
    }

    private void OpenPicker()
    {
        if (_picker is null) { _picker = new BuildTexturePicker { Name = "IslandCoverPicker" }; AddChild(_picker); }
        _picker.Open(false, entry => { if (Guid.TryParse(entry.Asset, out var asset)) { _coverRef = $"asset:{asset:D}"; ShowCover(_coverRef); NotifyChanged(); } });
    }

    private void ShowCover(string reference)
    {
        _cover.Texture = null; _coverNote.Visible = true;
        if (reference.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(reference[6..], out var asset) && RequestTexture is { } request)
            request(asset, texture => { if (_coverRef == reference && texture is not null) { _cover.Texture = texture; _coverNote.Visible = false; } else if (_coverRef == reference) _coverNote.Text = "Não foi possível carregar a imagem"; });
        else _coverNote.Text = reference.Length == 0 ? "Sem imagem de divulgação" : "Imagem definida fora do jogo (endereço da web)";
    }
}
