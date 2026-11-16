using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>The "Sobre" tab: about me, interests as tags, featured groups and featured land. Two columns when there is room, one when there is not.</summary>
public partial class ProfileWindow
{
    private const int MaxTags = 20, MaxTagLength = 30, MaxInterestsText = 500, FeaturedGroups = 5;

    /// <summary>Opens the page of a group (the game supplies it).</summary>
    public Action<Guid>? OpenGroup { get; set; }
    /// <summary>Takes the person to a parcel (island, parcel): the island decides whether they may enter.</summary>
    public Action<Guid, Guid>? VisitParcel { get; set; }

    private readonly List<string> _tags = [];
    private HFlowContainer _tagBox = null!, _featuredGroups = null!;
    private LineEdit _tagInput = null!;
    private Control _tagAdd = null!, _parcelManage = null!;
    private VBoxContainer _featuredParcels = null!;
    private GridContainer _aboutGrid = null!;
    private ScrollContainer _scroll = null!;
    private Control _infoColumn = null!;
    private CheckBox _hideParcels = null!;

    // ---- test access -------------------------------------------------------------------------------------------------------
    public IReadOnlyList<string> TagsForTesting => _tags;
    public bool AddTagForTesting(string text) => AddTag(text);
    public void RemoveTagForTesting(int index) { if (index >= 0 && index < _tags.Count) { _tags.RemoveAt(index); RebuildTags(); } }
    public int FeaturedGroupsForTesting => _featuredGroups.GetChildren().OfType<VBoxContainer>().Count();
    public int FeaturedParcelsForTesting => _featuredParcels.GetChildren().OfType<PanelContainer>().Count();
    public int AboutColumnsForTesting => _aboutGrid.Columns;
    public bool TagInputVisibleForTesting => _tagAdd.Visible;
    public bool ManageParcelsVisibleForTesting => _parcelManage.Visible;
    public void PressFeaturedGroupForTesting(int index) => _featuredGroups.GetChildren().OfType<VBoxContainer>().ElementAt(index).GetChild<Button>(0).EmitSignal(BaseButton.SignalName.Pressed);
    public void PressVisitForTesting(int index) => _featuredParcels.GetChildren().OfType<PanelContainer>().ElementAt(index).FindChildren("*", "Button", true, false).OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed);
    public void SetHideParcelsForTesting(bool on) => _hideParcels.ButtonPressed = on;

    private string InterestsText => string.Join(", ", _tags);

    private Control BuildAbout()
    {
        _aboutGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _aboutGrid.AddThemeConstantOverride("h_separation", 10); _aboutGrid.AddThemeConstantOverride("v_separation", 8);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 8); _aboutGrid.AddChild(left);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 8); _aboutGrid.AddChild(right);

        // about me
        var about = new VBoxContainer(); about.AddThemeConstantOverride("separation", 6); about.AddChild(Text(Heading("Sobre mim"), 15, true, UiStyle.AccentText));
        _bio = new TextEdit { CustomMinimumSize = new Vector2(0, 110), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Conte quem você é no LifeVerse…" }; _bio.AddThemeFontSizeOverride("font_size", 16); about.AddChild(_bio);
        left.AddChild(Card(about));

        // interests as tags
        var interests = new VBoxContainer(); interests.AddThemeConstantOverride("separation", 8); interests.AddChild(Text(Heading("Interesses"), 15, true, UiStyle.AccentText));
        _tagBox = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _tagBox.AddThemeConstantOverride("h_separation", 6); _tagBox.AddThemeConstantOverride("v_separation", 6); interests.AddChild(_tagBox);
        var add = new HBoxContainer(); add.AddThemeConstantOverride("separation", 6); _tagAdd = add; interests.AddChild(add);
        _tagInput = new LineEdit { PlaceholderText = "Adicionar interesse (Enter)", MaxLength = MaxTagLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; add.AddChild(_tagInput);
        _tagInput.TextSubmitted += _ => CommitTagInput();
        var plus = new Button { Icon = UiStyle.Icon("plus", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, TooltipText = "Adicionar interesse", CustomMinimumSize = new Vector2(40, 36) }; UiStyle.StyleButton(plus, UiStyle.ButtonKind.Primary, 8, 4, 10); plus.Pressed += CommitTagInput; add.AddChild(plus);
        left.AddChild(Card(interests));

        // privacy (mine only)
        var settings = new VBoxContainer(); settings.AddThemeConstantOverride("separation", 4); settings.AddChild(Text(Heading("Privacidade do perfil"), 15, true, UiStyle.AccentText));
        _privateProfile = new CheckBox { Text = " Perfil privado (outros veem só o nome)" }; _hideGroups = new CheckBox { Text = " Esconder meus grupos de outras pessoas" }; _hideMarriage = new CheckBox { Text = " Esconder meu casamento de outras pessoas" };
        _hideParcels = new CheckBox { Text = " Esconder meus terrenos de outras pessoas" };
        _showFavorites = new CheckBox { Text = " Mostrar meus favoritos para outras pessoas" }; _hideAnniversary = new CheckBox { Text = " Esconder meu aniversário de outras pessoas" }; _hideStats = new CheckBox { Text = " Esconder minhas estatísticas de outras pessoas" }; _hideAchievements = new CheckBox { Text = " Esconder minhas conquistas de outras pessoas" };
        foreach (var box in new[] { _privateProfile, _hideGroups, _hideMarriage, _hideParcels, _hideAnniversary, _hideStats, _hideAchievements, _showFavorites }) { box.FocusMode = Control.FocusModeEnum.None; box.AddThemeFontSizeOverride("font_size", 16); box.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.ClipText = true; settings.AddChild(box); }
        _selfSettings = Card(settings); left.AddChild(_selfSettings);

        // featured groups
        var groups = new VBoxContainer(); groups.AddThemeConstantOverride("separation", 8);
        var groupHead = new HBoxContainer(); groups.AddChild(groupHead); groupHead.AddChild(Text(Heading("Grupos em destaque"), 15, true, UiStyle.AccentText));
        var all = new Button { Text = "Ver todos", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Abre a lista completa de grupos" }; all.AddThemeColorOverride("font_color", UiStyle.AccentText); all.AddThemeFontSizeOverride("font_size", 14); all.Pressed += () => _tabs.Select(TabGroups); groupHead.AddChild(all);
        _featuredGroups = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _featuredGroups.AddThemeConstantOverride("h_separation", 10); _featuredGroups.AddThemeConstantOverride("v_separation", 8); groups.AddChild(_featuredGroups);
        right.AddChild(Card(groups));

        // featured land
        var land = new VBoxContainer(); land.AddThemeConstantOverride("separation", 8);
        var landHead = new HBoxContainer(); land.AddChild(landHead); landHead.AddChild(Text(Heading("Terrenos"), 15, true, UiStyle.AccentText));
        var manage = new Button { Text = "Escolher terrenos…", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Escolhe quais dos seus terrenos aparecem no perfil" }; manage.AddThemeColorOverride("font_color", UiStyle.AccentText); manage.AddThemeFontSizeOverride("font_size", 14);
        manage.Pressed += OpenParcelChooser; _parcelManage = manage; landHead.AddChild(manage);
        _featuredParcels = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _featuredParcels.AddThemeConstantOverride("separation", 8); land.AddChild(_featuredParcels);
        right.AddChild(Card(land));
        return _aboutGrid;
    }

    // ---- interests ---------------------------------------------------------------------------------------------------------

    /// <summary>Reads the text the server keeps (separated by commas) into tags.</summary>
    private void SetTags(string text)
    {
        _tags.Clear();
        foreach (var part in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = part.Length > MaxTagLength ? part[..MaxTagLength] : part;
            if (_tags.Count < MaxTags && !_tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) _tags.Add(tag);
        }
        RebuildTags();
    }

    private bool AddTag(string text)
    {
        var tag = text.Replace(",", " ").Replace(";", " ").Trim();
        if (tag.Length == 0) return false;
        if (tag.Length > MaxTagLength) tag = tag[..MaxTagLength];
        if (_tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) { _status.Text = "Esse interesse já está na lista."; return false; }
        if (_tags.Count >= MaxTags) { _status.Text = $"Você pode ter até {MaxTags} interesses."; return false; }
        if (InterestsText.Length + tag.Length + 2 > MaxInterestsText) { _status.Text = "A lista de interesses ficou longa demais."; return false; }
        _tags.Add(tag); RebuildTags(); return true;
    }

    private void CommitTagInput() { if (AddTag(_tagInput.Text)) _tagInput.Text = string.Empty; _tagInput.GrabFocus(); }

    private void RebuildTags()
    {
        foreach (var child in _tagBox.GetChildren()) { _tagBox.RemoveChild(child); child.QueueFree(); }
        var self = IsSelf;
        if (_tags.Count == 0) { _tagBox.AddChild(Text(self ? "Adicione o que você gosta de fazer: música, construção, fotografia…" : "Nenhum interesse informado.", 15, false, UiStyle.TextMuted)); return; }
        for (var i = 0; i < _tags.Count; i++)
        {
            var index = i; var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .8f), 16, 1, 12, 5));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); chip.AddChild(row);
            var label = Text(_tags[i], 15); label.AutowrapMode = TextServer.AutowrapMode.Off; label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; row.AddChild(label);
            if (self)
            {
                var remove = new Button { Icon = UiStyle.Icon("close", UiStyle.TextMuted, 12), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Remover", CustomMinimumSize = new Vector2(20, 20) };
                remove.Pressed += () => { if (index < _tags.Count) { _tags.RemoveAt(index); RebuildTags(); } }; row.AddChild(remove);
            }
            _tagBox.AddChild(chip);
        }
    }

    // ---- featured groups and land --------------------------------------------------------------------------------------------------

    private void FillFeatured()
    {
        var self = IsSelf;
        foreach (var child in _featuredGroups.GetChildren()) { _featuredGroups.RemoveChild(child); child.QueueFree(); }
        var shown = _profile.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array ? groups.EnumerateArray().Where(g => !self || Flag(g, "shown")).Take(FeaturedGroups).ToList() : [];
        if (shown.Count == 0) _featuredGroups.AddChild(Text(self ? "Nenhum grupo em destaque. Escolha na aba Grupos quais mostrar." : "Nenhum grupo em destaque.", 15, false, UiStyle.TextMuted));
        foreach (var group in shown)
        {
            var id = Guid.TryParse(Str(group, "id"), out var parsed) ? parsed : Guid.Empty; var name = Str(group, "name"); var tag = Str(group, "tag");
            var color = Str(group, "color") is { Length: 7 } hex && Color.HtmlIsValid(hex) ? new Color(hex) : UiStyle.Accent;
            var tile = new VBoxContainer { CustomMinimumSize = new Vector2(84, 0) }; tile.AddThemeConstantOverride("separation", 4);
            var button = new Button { Text = tag.Length > 0 ? tag : (name.Length > 0 ? name[..1].ToUpperInvariant() : "?"), CustomMinimumSize = new Vector2(72, 72), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = $"{name}" + (RoleLabel(Str(group, "role")) is { Length: > 0 } role ? $" · {role}" : "") };
            button.AddThemeFontSizeOverride("font_size", 18); button.AddThemeFontOverride("font", UiStyle.Bold); button.AddThemeColorOverride("font_color", color);
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) button.AddThemeStyleboxOverride(state, UiStyle.Box(state == "normal" ? UiStyle.FieldFill : UiStyle.ButtonFillHover, new Color(color, .8f), 12, 2, 4, 4));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); button.Pressed += () => { if (id != Guid.Empty) OpenGroup?.Invoke(id); };
            tile.AddChild(button);
            var caption = Text(name, 12, false, UiStyle.TextMuted); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.AutowrapMode = TextServer.AutowrapMode.Off; caption.ClipText = true; caption.CustomMinimumSize = new Vector2(84, 0); tile.AddChild(caption);
            _featuredGroups.AddChild(tile);
        }

        foreach (var child in _featuredParcels.GetChildren()) { _featuredParcels.RemoveChild(child); child.QueueFree(); }
        var parcels = ParcelList().Where(p => !self || Flag(p, "shown")).ToList();
        _parcelManage.Visible = self && ParcelList().Any();
        if (parcels.Count == 0) _featuredParcels.AddChild(Text(self ? (ParcelList().Any() ? "Nenhum terreno em destaque. Use “Escolher terrenos…”." : "Você ainda não tem terrenos.") : "Nenhum terreno em destaque.", 15, false, UiStyle.TextMuted));
        foreach (var parcel in parcels) _featuredParcels.AddChild(ParcelCard(parcel, false));
    }

    private IEnumerable<JsonElement> ParcelList() => _profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("parcels", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    private static string CategoryLabel(string category) => category switch { "residencial" => "Residencial", "comercial" => "Comercial", "mista" => "Mista", "social" => "Social", "comum" => "Área comum", _ => category };

    private void OpenParcelChooser()
    {
        var dialog = MakeDialog("Terrenos no perfil", 460, 400);
        dialog.Content.AddChild(Text($"Marque até {MaxParcelsShown} terrenos para aparecer no seu perfil.", 15, false, UiStyle.TextMuted));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 200) }; dialog.Content.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 4); scroll.AddChild(list);
        var note = Text("", 14, false, UiStyle.Orange); dialog.Content.AddChild(note);
        foreach (var parcel in ParcelList())
        {
            if (!Guid.TryParse(Str(parcel, "id"), out var id)) continue;
            var box = new CheckBox { Text = $" {Str(parcel, "name")} — {Str(parcel, "islandName")}", ButtonPressed = Flag(parcel, "shown"), FocusMode = Control.FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 16);
            box.Toggled += on => _ = ToggleParcelAsync(id, on, box, note); list.AddChild(box);
        }
        DialogButtons(dialog, "Pronto", () => CloseDialog(dialog, _dialogs));
    }

    private const int MaxParcelsShown = 6;

    private async Task ToggleParcelAsync(Guid id, bool on, CheckBox box, Label note)
    {
        var reply = await _api.ParcelShown(id, on);
        if (reply.Ok) { note.Text = string.Empty; await RefreshOwnAsync(); return; }
        note.Text = Describe(reply); box.SetPressedNoSignal(!on);
    }
}
