using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The profile of a person, as in Second Life: picture, name, type of account (padrão or doador), whether they are married in the game, "sobre mim", "vida real", interests, birthday and the
/// groups they chose to show. Mine can be edited (and what is hidden from others is chosen here); another person's is read-only, with a button to propose marriage. The server decides
/// what each viewer may see; this only draws what it sent.
/// Layout (stage 1 of the redesign): a navigation column on the left (only my own profile; every entry opens a module that already exists), the large picture in the middle, and on the
/// right the identity card, the information cards and the tabs. It is a floating window: it can be dragged, resized, minimised and expanded, and it adapts to its own width.
/// </summary>
public partial class ProfileWindow : Node
{
    private const float SidebarWide = 184, SidebarNarrow = 52, WideWindow = 1000, MediumWindow = 860;

    private FloatingWindow _window = null!;
    private TextureRect _photo = null!, _avatarSmall = null!;
    private Control _photoColumn = null!, _photoEmpty = null!, _sidebar = null!, _bar = null!, _presenceDot = null!;
    private Label _name = null!, _user = null!, _account = null!, _marriage = null!, _joined = null!, _status = null!, _presenceText = null!;
    private Label _joinedValue = null!, _joinedSub = null!, _partnerValue = null!, _visibilityValue = null!;
    private TextEdit _bio = null!, _firstLife = null!;
    private LineEdit _proposeMessage = null!;
    private Label _birthdayText = null!;
    private Control _selfSettings = null!, _proposeBox = null!, _save = null!, _discard = null!, _copyUser = null!;
    private CheckBox _hideGroups = null!, _hideMarriage = null!, _privateProfile = null!;
    private VBoxContainer _groupRows = null!, _marriageRows = null!, _navBox = null!;
    private readonly List<Control> _pages = [];
    private readonly List<Button> _navButtons = [];
    private UiTabs _tabs = null!;


    private ProfileApi _api = null!;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private JsonElement _profile;
    private Guid _viewing;

    private bool _filling, _navUserChoice, _navCollapsed;
    private Rect2? _beforeExpand;

    public bool IsOpen => _window is not null && _window.Visible;
    public Guid Viewing => _viewing;
    public Guid MyId => GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty;
    public Func<Guid>? MyIdOverride { get; set; }
    private Guid Me => MyIdOverride?.Invoke() ?? MyId;
    private bool IsSelf => _profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("isSelf", out var s) && s.ValueKind == JsonValueKind.True;

    /// <summary>What the navigation column opens: "appearance", "inventory", "groups", "land", "settings". An entry only exists when the game supplied what it opens.</summary>
    public Dictionary<string, Action> Shortcuts { get; } = [];

    // ---- test access -------------------------------------------------------------------------------------------------------

    public string NameForTesting => _name.Text;
    public string AccountForTesting => _account.Text;
    public string MarriageForTesting => _marriage.Text;
    public string BioForTesting { get => _bio.Text; set => _bio.Text = value; }
    public string FirstLifeForTesting { get => _firstLife.Text; set => _firstLife.Text = value; }
    public string InterestsForTesting { get => InterestsText; set => SetTags(value); }
    public string BirthdayForTesting => _birthdayText.Text;
    public string JoinedForTesting => _joinedValue.Text + " " + _joinedSub.Text;
    public string PartnerForTesting => _partnerValue.Text;
    public string VisibilityForTesting => _visibilityValue.Text;
    public string PresenceForTesting => _presenceText.Text;
    public string TitleForTesting => _window.Title;
    public string StatusForTesting => _status.Text;
    public bool EditableForTesting => _bio.Editable;
    public bool SaveVisibleForTesting => _save.Visible;
    public bool ProposeVisibleForTesting => _proposeBox.Visible;
    public bool SidebarVisibleForTesting => _sidebar.Visible;
    public bool SidebarCollapsedForTesting => _navCollapsed;
    public bool PhotoColumnVisibleForTesting => _photoColumn.Visible;
    public IReadOnlyList<string> NavForTesting => [.. _navButtons.Select(b => b.TooltipText)];
    public int GroupRowsForTesting => _groupRows.GetChildCount();
    public int MarriageRowsForTesting => _marriageRows.GetChildCount();
    public IReadOnlyList<string> TabsForTesting => [.. Enumerable.Range(0, _tabs.Count).Select(i => _tabs.TabAt(i).Text.Trim())];
    public bool PhotoShownForTesting => _photo.Texture is not null;
    public Vector2 WindowSizeForTesting => _window.Size;
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new ProfileApi(transport); }
    public void SetHideGroupsForTesting(bool on) => _hideGroups.ButtonPressed = on;
    public void SetHideMarriageForTesting(bool on) => _hideMarriage.ButtonPressed = on;
    public void SetPrivateForTesting(bool on) => _privateProfile.ButtonPressed = on;
    public void SelectTabForTesting(int index) => _tabs.Select(index);
    public void SetProposeMessageForTesting(string text) => _proposeMessage.Text = text;
    public Task ToggleGroupForTesting(int row, bool shown) => SetGroupShown(row, shown);
    public void ResizeForTesting(Vector2 size) { _window.Size = size; Adapt(); }
    public void ToggleSidebarForTesting() => ToggleSidebar();
    public void PressNavForTesting(int index) { if (index >= 0 && index < _navButtons.Count) _navButtons[index].EmitSignal(BaseButton.SignalName.Pressed); }
    public void PressNavByNameForTesting(string label) => _navButtons.First(b => b.TooltipText == label).EmitSignal(BaseButton.SignalName.Pressed);
    public Task DiscardForTesting() => DiscardAsync();
    public void ToggleExpandForTesting() => ToggleExpanded();

    public override void _Ready()
    {
        _api = new ProfileApi(_transport ?? ScriptApi.HttpTransport(this));
        _window = new FloatingWindow("Perfil", icon: "user", minimizable: true, expandable: true) { Name = "ProfileFloating", Visible = false, Theme = ViewerTheme.Create() };
        _window.MinimumDragSize = new Vector2(560, 380); _window.ExpandToggled += _ => ToggleExpanded();
        PlaceDefault(); AddChild(_window);
        _window.Resized += Adapt;
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);

        // body: navigation | picture | information
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 10); content.AddChild(body);
        body.AddChild(BuildSidebar());
        body.AddChild(BuildPhotoColumn());
        body.AddChild(BuildInfoColumn());

        // bottom bar
        var bar = new VBoxContainer { Name = "ProfileBar" }; bar.AddThemeConstantOverride("separation", 6); _bar = bar; content.AddChild(bar);
        _status = Text("", 15, false, UiStyle.TextMuted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; bar.AddChild(_status);
        _actions = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _actions.AddThemeConstantOverride("h_separation", 8); _actions.AddThemeConstantOverride("v_separation", 8); bar.AddChild(_actions);
        var discard = new Button { Text = " Descartar alterações", Icon = UiStyle.Icon("undo", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), TooltipText = "Volta ao que está salvo no servidor" };
        UiStyle.StyleButton(discard, UiStyle.ButtonKind.Tool, 12, 6, 10); discard.Pressed += () => _ = DiscardAsync(); _discard = discard; _actions.AddChild(discard);
        var save = new Button { Text = " Salvar perfil", Icon = UiStyle.Icon("check", UiStyle.Text, 20), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42) }; UiStyle.StyleButton(save, UiStyle.ButtonKind.Primary, 14, 6, 10);
        save.Pressed += () => _ = SaveAsync(); _save = save; _actions.AddChild(save);
        Adapt();
    }

    // ---- building ----------------------------------------------------------------------------------------------------------

    private static Label Text(string text, int size, bool bold = false, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", size); if (bold) label.AddThemeFontOverride("font", UiStyle.Bold); if (color is { } c) label.AddThemeColorOverride("font_color", c); return label;
    }

    private static HBoxContainer Row(string caption, Control control)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(110, 0) }; label.AddThemeFontSizeOverride("font_size", 15); label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(label); control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    private static Button SmallButton(string text, string tip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 8, 5, 10);
        button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private static PanelContainer Card(Control child, float margin = 12, bool solid = false)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(solid ? UiStyle.PanelFillSolid : UiStyle.RowFill, new Color(UiStyle.BorderSoft, .55f), 12, 1, margin, margin));
        card.AddChild(child); return card;
    }

    private static string Heading(string text) => text.ToUpperInvariant();

    private Control BuildSidebar()
    {
        var panel = new PanelContainer { Name = "ProfileNav", SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(SidebarWide, 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .55f), new Color(UiStyle.BorderSoft, .55f), 12, 1, 8, 8));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 4); panel.AddChild(column);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; column.AddChild(scroll);
        _navBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _navBox.AddThemeConstantOverride("separation", 4); scroll.AddChild(_navBox);
        var collapse = new Button { Name = "NavCollapse", Icon = UiStyle.Icon("chevron-left", UiStyle.TextMuted, 18), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Recolher o menu", CustomMinimumSize = new Vector2(0, 32) };
        collapse.Pressed += () => { _navUserChoice = true; ToggleSidebar(); }; column.AddChild(collapse);
        _sidebar = panel; return panel;
    }

    /// <summary>Rebuilds the navigation: "Perfil" always, the others only when the game gave something to open.</summary>
    private void BuildNav()
    {
        foreach (var child in _navBox.GetChildren()) { _navBox.RemoveChild(child); child.QueueFree(); }
        _navButtons.Clear();
        AddNav("Perfil", "user", null, true);
        void Module(string label, string icon, string key) { if (Shortcuts.TryGetValue(key, out var open)) AddNav(label, icon, open, false); }
        Module("Aparência", "avatar", "appearance"); Module("Inventário", "bag", "inventory"); Module("Grupos", "people", "groups"); Module("Terrenos", "map", "land");
        AddNav("Atividades", "bell", () => _tabs.Select(TabFeed), false); AddNav("Favoritos", "star", () => _tabs.Select(TabFavorites), false); AddNav("Conquistas", "target", () => _tabs.Select(TabAchievements), false); AddNav("Estatísticas", "chart", () => _tabs.Select(TabStats), false);
        AddNav("Privacidade", "lock", () => { _tabs.Select(TabAbout); _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, _selfSettings); }, false);
        Module("Configurações", "settings", "settings");
        ApplyNavLayout();
    }

    private void AddNav(string label, string icon, Action? open, bool active)
    {
        var button = new Button { Text = " " + label, Icon = UiStyle.Icon(icon, active ? UiStyle.AccentText : UiStyle.Text, 20), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), TooltipText = label, ExpandIcon = false, ClipText = true };
        button.AddThemeFontSizeOverride("font_size", 16); button.AddThemeFontOverride("font", active ? UiStyle.Bold : UiStyle.Medium);
        button.AddThemeColorOverride("font_color", active ? UiStyle.Text : UiStyle.TextMuted); button.AddThemeColorOverride("font_hover_color", UiStyle.Text);
        var idle = UiStyle.Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 8, 0, 10, 6);
        var lit = UiStyle.Box(new Color(UiStyle.AccentFill, .75f), new Color(0, 0, 0, 0), 8, 0, 10, 6); lit.BorderWidthLeft = 3; lit.BorderColor = UiStyle.Accent;
        var hover = UiStyle.Box(new Color(1, 1, 1, .07f), new Color(0, 0, 0, 0), 8, 0, 10, 6);
        button.AddThemeStyleboxOverride("normal", active ? lit : idle); button.AddThemeStyleboxOverride("hover", active ? lit : hover); button.AddThemeStyleboxOverride("pressed", active ? lit : hover);
        button.AddThemeStyleboxOverride("hover_pressed", active ? lit : hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (open is not null) button.Pressed += () => open();
        _navBox.AddChild(button); _navButtons.Add(button);
    }

    private void ApplyNavLayout()
    {
        _sidebar.CustomMinimumSize = new Vector2(_navCollapsed ? SidebarNarrow : SidebarWide, 0);
        foreach (var button in _navButtons) { var label = button.TooltipText; button.Text = _navCollapsed ? string.Empty : " " + label; button.Alignment = _navCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left; }
        if (_sidebar.FindChild("NavCollapse", true, false) is Button collapse)
        { collapse.Icon = UiStyle.Icon(_navCollapsed ? "chevron-right" : "chevron-left", UiStyle.TextMuted, 18); collapse.TooltipText = _navCollapsed ? "Expandir o menu" : "Recolher o menu"; }
    }

    private void ToggleSidebar() { _navCollapsed = !_navCollapsed; ApplyNavLayout(); }

    private Control BuildPhotoColumn()
    {
        var column = new VBoxContainer { Name = "ProfilePhotoColumn", SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0), SizeFlagsStretchRatio = 1f }; column.AddThemeConstantOverride("separation", 6);
        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        frame.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .55f), 14, 1, 0, 0));
        column.AddChild(frame);
        _photo = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        frame.AddChild(_photo);
        var empty = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore }; empty.AddThemeConstantOverride("separation", 8);
        var glyph = UiStyle.IconRect("user", new Color(UiStyle.TextDisabled, .8f), 72); glyph.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; empty.AddChild(glyph);
        var note = Text("Sem foto de perfil", 15, false, UiStyle.TextMuted); note.HorizontalAlignment = HorizontalAlignment.Center; empty.AddChild(note);
        frame.AddChild(empty); _photoEmpty = empty;
        column.AddChild(BuildGalleryStrip());
        _photoColumn = column; return column;
    }

    private void UpdatePhotoEmpty() => _photoEmpty.Visible = _photo.Texture is null;

    private Control BuildInfoColumn()
    {
        var column = new VBoxContainer { Name = "ProfileInfoColumn", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.2f, CustomMinimumSize = new Vector2(320, 0) };
        _infoColumn = column; column.Resized += Adapt;
        column.AddThemeConstantOverride("separation", 8);

        // identity card
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 14);
        var avatarFrame = new PanelContainer { CustomMinimumSize = new Vector2(88, 88), ClipChildren = CanvasItem.ClipChildrenMode.AndDraw, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        avatarFrame.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, UiStyle.Accent, 12, 2, 0, 0));
        _avatarSmall = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered }; avatarFrame.AddChild(_avatarSmall); head.AddChild(avatarFrame);
        var identity = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; identity.AddThemeConstantOverride("separation", 2); head.AddChild(identity);
        _name = Text("", 26, true); _name.AutowrapMode = TextServer.AutowrapMode.Off; _name.ClipText = true; identity.AddChild(_name);
        var userRow = new HBoxContainer(); userRow.AddThemeConstantOverride("separation", 6); identity.AddChild(userRow);
        _user = Text("", 16, false, UiStyle.AccentText); _user.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _user.AutowrapMode = TextServer.AutowrapMode.Off; userRow.AddChild(_user);
        var copy = new Button { Icon = UiStyle.Icon("copy", UiStyle.TextMuted, 16), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Copiar o nome de usuário", CustomMinimumSize = new Vector2(26, 26) };
        copy.Pressed += () => { DisplayServer.ClipboardSet(Str(_profile, "username")); _status.Text = "Nome de usuário copiado."; }; _copyUser = copy; userRow.AddChild(copy);
        var presence = new HBoxContainer(); presence.AddThemeConstantOverride("separation", 7); identity.AddChild(presence);
        _presenceDot = new Panel { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; presence.AddChild(_presenceDot);
        _presenceText = Text("", 15, false, UiStyle.TextMuted); _presenceText.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _presenceText.AutowrapMode = TextServer.AutowrapMode.Off; presence.AddChild(_presenceText);
        _account = Text("", 15, true, UiStyle.AccentText); _account.AutowrapMode = TextServer.AutowrapMode.Off; _account.ClipText = true; identity.AddChild(_account);
        var identityBox = new VBoxContainer(); identityBox.AddThemeConstantOverride("separation", 10); identityBox.AddChild(BuildCoverFrame()); identityBox.AddChild(head); column.AddChild(Card(identityBox));

        // information cards (they wrap when the window is narrow)
        var cards = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cards.AddThemeConstantOverride("h_separation", 8); cards.AddThemeConstantOverride("v_separation", 8); column.AddChild(cards);
        _joinedValue = InfoCard(cards, "calendar", "Membro desde", out _joinedSub);
        _partnerValue = InfoCard(cards, "user-add", "Parceiro(a)", out _);
        _birthdayText = InfoCard(cards, "bell", "Aniversário", out _);
        _visibilityValue = InfoCard(cards, "eye", "Visibilidade", out _);
        _marriage = Text("", 16);
        _joined = Text("", 15, false, UiStyle.TextMuted);

        // tabs
        _tabs = new UiTabs { EqualWidth = false, Flat = true, FontSize = 15 };
        foreach (var (title, icon) in new[] { ("SOBRE", "user"), ("FEED", "bell"), ("GRUPOS", "people"), ("TERRENOS", "map"), ("FAVORITOS", "star"), ("CONQUISTAS", "target"), ("ESTATÍSTICAS", "chart"), ("VIDA REAL", "home"), ("CASAMENTO", "heart") }) _tabs.AddTab(title, icon);
        var tabScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 50) };
        _tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; tabScroll.AddChild(_tabs); column.AddChild(tabScroll);
        _pages.Add(BuildAbout()); _pages.Add(BuildFeedTab()); _pages.Add(BuildGroups()); _pages.Add(BuildParcelsTab()); _pages.Add(BuildFavoritesTab()); _pages.Add(BuildAchievementsTab()); _pages.Add(BuildStatsTab()); _pages.Add(BuildFirstLife()); _pages.Add(BuildMarriage());
        var scroll = _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 120) };
        var pageBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var page in _pages) pageBox.AddChild(page); scroll.AddChild(pageBox); column.AddChild(scroll);
        _tabs.TabChanged += index => { for (var i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index; };
        _tabs.Select(0); for (var i = 1; i < _pages.Count; i++) _pages[i].Visible = false;
        return column;
    }

    private static Label InfoCard(Container parent, string icon, string caption, out Label sub)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        row.AddChild(UiStyle.IconRect(icon, UiStyle.TextMuted, 24));
        var text = new VBoxContainer(); text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
        text.AddChild(Text(caption, 12, false, UiStyle.TextMuted));
        var value = Text("—", 15, true); value.AutowrapMode = TextServer.AutowrapMode.Off; value.ClipText = true; value.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; value.CustomMinimumSize = new Vector2(110, 0); text.AddChild(value);
        sub = Text("", 12, false, UiStyle.TextMuted); sub.AutowrapMode = TextServer.AutowrapMode.Off; sub.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; text.AddChild(sub);
        var card = Card(row, 10); card.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; card.CustomMinimumSize = new Vector2(150, 0); parent.AddChild(card);
        return value;
    }

    private Control BuildFirstLife()
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 8);
        inner.AddChild(Text(Heading("Vida real"), 15, true, UiStyle.AccentText));
        inner.AddChild(Text("O que a pessoa quer contar sobre ela fora do jogo (opcional).", 14, false, UiStyle.TextMuted));
        _firstLife = new TextEdit { CustomMinimumSize = new Vector2(0, 180), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Cidade, profissão, o que quiser compartilhar…" }; _firstLife.AddThemeFontSizeOverride("font_size", 16); inner.AddChild(_firstLife);
        return Card(inner);
    }

    private Control BuildGroups()
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 6);
        inner.AddChild(Text(Heading("Grupos"), 15, true, UiStyle.AccentText));
        _groupRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _groupRows.AddThemeConstantOverride("separation", 4); inner.AddChild(_groupRows);
        return Card(inner);
    }

    private Control BuildMarriage()
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 6);
        inner.AddChild(Text(Heading("Casamento"), 15, true, UiStyle.AccentText));
        _marriageRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _marriageRows.AddThemeConstantOverride("separation", 6); inner.AddChild(_marriageRows);
        var propose = new VBoxContainer(); propose.AddThemeConstantOverride("separation", 6); _proposeBox = propose; inner.AddChild(propose);
        _proposeMessage = new LineEdit { PlaceholderText = "Mensagem do pedido (opcional)", MaxLength = 300 }; propose.AddChild(_proposeMessage);
        propose.AddChild(SmallButton("Pedir em casamento", "Envia um pedido de casamento; vale por 14 dias", () => _ = ProposeAsync()));
        return Card(inner);
    }

    // ---- window behaviour -----------------------------------------------------------------------------------------------------

    /// <summary>Roughly 1350 × 760 on a Full HD screen, never larger than the usable area.</summary>
    private void PlaceDefault()
    {
        var view = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        var width = Math.Min(1350f, Math.Max(560f, view.X - 48)); var height = Math.Min(760f, Math.Max(380f, view.Y - Interface.WindowManager.TopLimit - 24));
        _window.Place(.5f, .5f, -width / 2, -height / 2 + Interface.WindowManager.TopLimit / 2, width / 2, height / 2 + Interface.WindowManager.TopLimit / 2);
    }

    private void ToggleExpanded()
    {
        if (_beforeExpand is { } before)
        {
            _beforeExpand = null; Interface.WindowManager.Apply(_window, before); return;
        }
        _beforeExpand = new Rect2(_window.Position, _window.Size);
        var view = _window.GetViewportRect().Size; var top = Interface.WindowManager.TopLimit;
        Interface.WindowManager.Apply(_window, new Rect2(16, top, Math.Max(560, view.X - 32), Math.Max(380, view.Y - top - 16)));
    }

    /// <summary>Adjusts the layout to the width the window has: narrower windows lose the picture column first and fold the navigation to icons.</summary>
    private void Adapt()
    {
        if (_window is null || _photoColumn is null) return;
        var width = _window.Size.X; var self = _profile.ValueKind == JsonValueKind.Object ? IsSelf : true;
        _photoColumn.Visible = width >= MediumWindow;
        _photoColumn.CustomMinimumSize = new Vector2(Mathf.Clamp(width * .27f, 240, 440), 0);
        _sidebar.Visible = self && _navButtons.Count > 1 && width >= 640;
        if (_aboutGrid is not null) { var columns = _infoColumn.Size.X >= 720 ? 2 : 1; if (_aboutGrid.Columns != columns) _aboutGrid.Columns = columns; }
        if (!_navUserChoice && _navButtons.Count > 0)
        {
            var collapse = width < WideWindow; if (collapse != _navCollapsed) { _navCollapsed = collapse; ApplyNavLayout(); }
        }
    }

    // ---- opening ---------------------------------------------------------------------------------------------------------------

    private void Show() { _window.Visible = true; _window.MoveToFront(); }
    public void Close() { if (_window is not null) _window.Visible = false; }

    public Task OpenSelfAsync() => OpenAsync(_api.ById(Me), Me);
    public Task OpenByIdAsync(Guid id) => OpenAsync(_api.ById(id), id);
    public async Task OpenByNameAsync(string name) { _status.Text = "Procurando…"; Show(); await OpenAsync(_api.ByName(name), Guid.Empty); }

    private Task DiscardAsync() => _viewing == Guid.Empty ? Task.CompletedTask : OpenAsync(_api.ById(_viewing), _viewing);

    private async Task OpenAsync(Task<ProfileReply> request, Guid id)
    {
        Show(); _status.Text = "Carregando o perfil…"; if (id != _viewing) _relation = string.Empty; _viewing = id;
        var reply = await request;
        if (!reply.Ok) { _status.Text = reply.Status == 404 ? "Não encontrei essa pessoa." : reply.Status == 0 ? "Sem conexão com o servidor. Tente de novo." : reply.Message; return; }
        _profile = reply.Body; _viewing = Guid.TryParse(Str(_profile, "id"), out var shown) ? shown : id; Fill(); _ = LoadFeedAsync(true); await LoadImagesAsync(); await LoadMarriageAsync();
    }

    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Flag(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static (string Label, Color Color) PresenceOf(string presence) => presence switch
    {
        "online" => ("Online", UiStyle.Success), "away" => ("Ausente", UiStyle.Warning), "busy" => ("Ocupado", UiStyle.Danger), _ => ("Offline", UiStyle.TextDisabled)
    };

    private void Fill()
    {
        _filling = true;
        var self = IsSelf; var p = _profile;
        _window.Title = self ? "Meu Perfil" : "Perfil";
        _name.Text = Str(p, "displayName"); _user.Text = Str(p, "username");
        var (presenceLabel, presenceColor) = PresenceOf(Str(p, "presence"));
        _presenceText.Text = presenceLabel; _presenceDot.AddThemeStyleboxOverride("panel", UiStyle.Box(presenceColor, new Color(0, 0, 0, .35f), 6, 1, 0, 0));
        var donor = Str(p, "accountType") == "donor";
        _account.Text = donor ? "★ Doador" + (DateTimeOffset.TryParse(Str(p, "donorSince"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var since) ? $" desde {since:MM/yyyy}" : "") + " — apoia o LifeVerse" : "Conta padrão";
        _account.AddThemeColorOverride("font_color", donor ? UiStyle.Orange : UiStyle.TextMuted);
        var hasJoined = DateTimeOffset.TryParse(Str(p, "joinedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var joined);
        _joined.Text = hasJoined ? $"Personagem criado em {joined:dd/MM/yyyy}" : string.Empty;
        _joinedValue.Text = hasJoined ? joined.ToString("dd/MM/yyyy") : "—";
        _joinedSub.Text = p.TryGetProperty("characterAgeYears", out var age) && age.ValueKind == JsonValueKind.Number ? $"{age.GetInt32()} ano(s) de LifeVerse" : string.Empty;
        _bio.Text = Str(p, "bio"); _firstLife.Text = Str(p, "firstLifeBio"); SetTags(Str(p, "interests"));
        foreach (var edit in new[] { _bio, _firstLife }) edit.Editable = self; _tagAdd.Visible = self;
        _birthdayText.Text = Str(p, "birthday") is { Length: > 0 } b ? b : "Perfil privado";
        _visibilityValue.Text = Flag(p, "privateProfile") || (self && p.TryGetProperty("settings", out var vis) && Flag(vis, "privateProfile")) ? "Privado" : "Público";
        _selfSettings.Visible = _save.Visible = _discard.Visible = self; FillGallery();
        _proposeBox.Visible = !self; _copyUser.Visible = true;
        if (self && p.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            _hideParcels.ButtonPressed = Flag(s, "hideParcels"); _showFavorites.ButtonPressed = Flag(s, "showFavorites"); _hideAnniversary.ButtonPressed = Flag(s, "hideAnniversary"); _hideStats.ButtonPressed = Flag(s, "hideStats"); _hideAchievements.ButtonPressed = Flag(s, "hideAchievements"); _hideGroups.ButtonPressed = Flag(s, "hideGroups"); _hideMarriage.ButtonPressed = Flag(s, "hideMarriage"); _privateProfile.ButtonPressed = Flag(s, "privateProfile");
        }
        BuildNav(); Adapt();
        FillGroups(); FillMarriageHeader(); FillFeatured(); FillSections(); FillFeedComposer(); RebuildActions(); if (!self && _viewing != Guid.Empty) _ = LoadRelationAsync(_viewing);
        _status.Text = Flag(p, "privateProfile") ? "Este perfil é privado: só o nome aparece." : string.Empty;
        _filling = false;
    }

    private void FillGroups()
    {
        foreach (var child in _groupRows.GetChildren()) { _groupRows.RemoveChild(child); child.QueueFree(); }
        var self = IsSelf;
        if (!_profile.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
        { _groupRows.AddChild(Text(self ? "Você não participa de nenhum grupo." : "Nenhum grupo à mostra.", 16, false, UiStyle.TextMuted)); return; }
        var index = 0;
        foreach (var group in groups.EnumerateArray())
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); var tag = Str(group, "tag"); Guid.TryParse(Str(group, "id"), out var groupId);
            row.AddChild(Text((tag.Length > 0 ? $"[{tag}] " : "") + Str(group, "name"), 17, true)); row.AddChild(Text(RoleLabel(Str(group, "role")), 15, false, UiStyle.TextMuted));
            if (self)
            {
                var captured = index; var box = new CheckBox { Text = " mostrar", ButtonPressed = Flag(group, "shown"), FocusMode = Control.FocusModeEnum.None, TooltipText = "Lista este grupo no seu perfil (só aparece se o grupo for público)" };
                box.Toggled += on => { if (!_filling) _ = SetGroupShown(captured, on); }; row.AddChild(box);
            }
            if (OpenGroup is not null && groupId != Guid.Empty) { var view = SmallButton("Ver", "Abre a página do grupo", () => OpenGroup?.Invoke(groupId)); view.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, 10, 4, 8)); row.AddChild(view); }
            _groupRows.AddChild(row); index++;
        }
    }

    private static string RoleLabel(string role) => role switch { "owner" => "dono", "admin" => "administrador", "moderator" => "moderador", "member" => "membro", "" => "", _ => role };

    private async Task SetGroupShown(int row, bool shown)
    {
        if (!_profile.TryGetProperty("groups", out var groups) || row >= groups.GetArrayLength() || !Guid.TryParse(Str(groups[row], "id"), out var id)) return;
        var reply = await _api.GroupShown(id, shown); _status.Text = reply.Ok ? (shown ? "Grupo à mostra no perfil." : "Grupo escondido do perfil.") : reply.Message;
    }

    // ---- photo --------------------------------------------------------------------------------------------------------------------



    // ---- saving -----------------------------------------------------------------------------------------------------------------

    public async Task<bool> SaveAsync()
    {
        if (!IsSelf) return false;
        var changes = new JsonObject
        {
            ["bio"] = _bio.Text, ["firstLifeBio"] = _firstLife.Text, ["interests"] = InterestsText, ["hideParcels"] = _hideParcels.ButtonPressed, ["showFavorites"] = _showFavorites.ButtonPressed, ["hideAnniversary"] = _hideAnniversary.ButtonPressed, ["hideStats"] = _hideStats.ButtonPressed, ["hideAchievements"] = _hideAchievements.ButtonPressed,
            ["hideGroups"] = _hideGroups.ButtonPressed, ["hideMarriage"] = _hideMarriage.ButtonPressed, ["privateProfile"] = _privateProfile.ButtonPressed
        };
        _status.Text = "Salvando…"; SetBusy(true);
        var reply = await _api.Update(changes);
        SetBusy(false);
        if (!reply.Ok) { _status.Text = reply.Status == 0 ? "Sem conexão com o servidor. Nada foi salvo." : reply.Message; return false; }
        _profile = reply.Body; Fill(); await LoadImagesAsync(); _status.Text = "Perfil salvo."; return true;
    }

    private void SetBusy(bool busy) { if (_save is Button save) save.Disabled = busy; if (_discard is Button discard) discard.Disabled = busy; }

    // ---- marriage --------------------------------------------------------------------------------------------------------------

    private void FillMarriageHeader()
    {
        if (_profile.TryGetProperty("marriage", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            _marriage.Text = $"♥ Casado(a) com {Str(m, "spouseName")}" + (DateTimeOffset.TryParse(Str(m, "since"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var since) ? $" desde {since:dd/MM/yyyy}" : "");
            _partnerValue.Text = Str(m, "spouseName");
        }
        else
        {
            _marriage.Text = Flag(_profile, "privateProfile") ? string.Empty : "Solteiro(a)";
            _partnerValue.Text = Flag(_profile, "privateProfile") ? "—" : "Nenhum";
        }
    }

    private async Task LoadMarriageAsync()
    {
        foreach (var child in _marriageRows.GetChildren()) { _marriageRows.RemoveChild(child); child.QueueFree(); }
        if (!IsSelf)
        {
            _marriageRows.AddChild(Text(_marriage.Text.Length > 0 ? _marriage.Text : "Casamento não informado.", 17));
            _proposeBox.Visible = !(_profile.TryGetProperty("married", out var married) && married.ValueKind == JsonValueKind.True); return;
        }
        var reply = await _api.MarriageMine(); if (!reply.Ok) { _marriageRows.AddChild(Text(reply.Message, 16, false, UiStyle.Orange)); return; }
        var body = reply.Body;
        if (body.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
        {
            _marriageRows.AddChild(Text($"♥ Você é casado(a) com {Str(current, "spouseName")}.", 17, true));
            _marriageRows.AddChild(SmallButton("Divorciar-se", "Encerra o casamento", () => _ = DivorceAsync()));
        }
        else _marriageRows.AddChild(Text("Você não está casado(a). Abra o perfil de outra pessoa para pedir em casamento.", 16, false, UiStyle.TextMuted));
        foreach (var item in body.GetProperty("incoming").EnumerateArray())
        {
            var id = Guid.Parse(Str(item, "id")); var row = new VBoxContainer(); row.AddChild(Text($"{Str(item, "fromName")} pediu você em casamento." + (Str(item, "message") is { Length: > 0 } msg ? $" “{msg}”" : ""), 16));
            var buttons = new HBoxContainer(); buttons.AddChild(SmallButton("Aceitar", "Casar", () => _ = AnswerAsync(id, "accept"))); buttons.AddChild(SmallButton("Recusar", "Recusar o pedido", () => _ = AnswerAsync(id, "decline"))); row.AddChild(buttons); _marriageRows.AddChild(row);
        }
        foreach (var item in body.GetProperty("outgoing").EnumerateArray())
        {
            var id = Guid.Parse(Str(item, "id")); var row = new HBoxContainer(); row.AddChild(Text($"Pedido enviado a {Str(item, "toName")} (aguardando).", 16));
            row.AddChild(SmallButton("Cancelar", "Cancela o pedido", () => _ = AnswerAsync(id, "cancel"))); _marriageRows.AddChild(row);
        }
    }

    public async Task ProposeAsync()
    {
        if (IsSelf || _viewing == Guid.Empty) return;
        var reply = await _api.Propose(_viewing, _proposeMessage.Text); _status.Text = reply.Ok ? "Pedido de casamento enviado." : reply.Message;
        if (reply.Ok) _proposeMessage.Text = string.Empty;
    }

    public async Task AnswerAsync(Guid id, string answer)
    {
        var reply = await _api.Answer(id, answer);
        _status.Text = reply.Ok ? answer switch { "accept" => "Vocês estão casados!", "decline" => "Pedido recusado.", _ => "Pedido cancelado." } : reply.Message;
        await RefreshOwnAsync();
    }

    public async Task DivorceAsync() { var reply = await _api.Divorce(); _status.Text = reply.Ok ? "Casamento encerrado." : reply.Message; await RefreshOwnAsync(); }

    private async Task RefreshOwnAsync()
    {
        if (!IsSelf) return;
        var status = _status.Text; var reply = await _api.ById(Me); if (reply.Ok) { _profile = reply.Body; Fill(); }
        await LoadMarriageAsync(); _status.Text = status;
    }

    public Task AnswerForTesting(int row, string answer) => AnswerByRowAsync(row, answer);

    private async Task AnswerByRowAsync(int row, string answer)
    {
        var reply = await _api.MarriageMine(); if (!reply.Ok) return;
        var list = (answer == "cancel" ? reply.Body.GetProperty("outgoing") : reply.Body.GetProperty("incoming")).EnumerateArray().ToList();
        if (row < list.Count) await AnswerAsync(Guid.Parse(Str(list[row], "id")), answer);
    }

    public Task DivorceForTesting() => DivorceAsync();
}
