using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The look of the full avatar screen: a header, a panel on the left with tabs (body, face and hair, clothes, looks, advanced), a stage on the right with the avatar
/// and the camera buttons, and a bar at the bottom with the message and the buttons. The controls are the ones the other parts of the editor create; here they are
/// only put in order and dressed in the style of the rest of the game (UiStyle).
/// </summary>
public partial class CharacterCreator
{
    private UiTabs _tabBar = null!;
    private readonly List<Control> _pages = [];
    private Tween? _cameraTween;
    private float _cameraHeight = 1.25f;
    private bool _faceView;

    private static Label Heading(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 13); label.AddThemeColorOverride("font_color", UiStyle.AccentText);
        return label;
    }

    private static Label Caption(string text, int size = 15)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(170, 0), VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label;
    }

    private static void Detach(Node node) => node.GetParent()?.RemoveChild(node);

    private static HBoxContainer Line(string caption, Control control)
    {
        Detach(control);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        row.AddChild(Caption(caption)); control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; control.CustomMinimumSize = new Vector2(Mathf.Max(control.CustomMinimumSize.X, 120), Mathf.Max(control.CustomMinimumSize.Y, 34));
        row.AddChild(control); return row;
    }

    private HBoxContainer SliderLine(string caption, HSlider slider)
    {
        Detach(slider);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); row.AddChild(Caption(caption));
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; slider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; slider.CustomMinimumSize = new Vector2(120, 26); row.AddChild(slider);
        var value = new Label { CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Right }; value.AddThemeFontSizeOverride("font_size", 14); value.AddThemeColorOverride("font_color", UiStyle.Text);
        void Show() => value.Text = slider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        slider.ValueChanged += _ => Show(); Show(); row.AddChild(value); return row;
    }

    private static Control Card(Control heading, List<Control> rest) => Card([heading, .. rest]);
    private static Control Card(params Control[] children)
    {
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .55f), new Color(UiStyle.Divider, .7f), 10, 1, 14, 12));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); foreach (var child in children) box.AddChild(child); card.AddChild(box); return card;
    }

    private static Theme MakeTheme()
    {
        var theme = new Theme();
        theme.SetFont("font", "Label", UiStyle.Regular); theme.SetFont("font", "Button", UiStyle.Medium); theme.SetFont("font", "OptionButton", UiStyle.Medium); theme.SetFont("font", "LineEdit", UiStyle.Regular);
        theme.SetFont("font", "CheckBox", UiStyle.Regular);
        theme.SetColor("font_color", "Label", UiStyle.Text);
        foreach (var type in new[] { "Button", "OptionButton", "ColorPickerButton" })
        {
            theme.SetStylebox("normal", type, UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, 12, 6, 8));
            theme.SetStylebox("hover", type, UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, 12, 6, 8));
            theme.SetStylebox("pressed", type, UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Pressed, 12, 6, 8));
            theme.SetStylebox("disabled", type, UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Disabled, 12, 6, 8));
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            theme.SetColor("font_color", type, UiStyle.Text); theme.SetColor("font_hover_color", type, UiStyle.Text); theme.SetColor("font_pressed_color", type, UiStyle.Text); theme.SetColor("font_disabled_color", type, UiStyle.TextDisabled);
        }
        var field = UiStyle.Box(UiStyle.FieldFill, UiStyle.Divider, 8, 1, 12, 7);
        theme.SetStylebox("normal", "LineEdit", field); theme.SetStylebox("focus", "LineEdit", UiStyle.Box(UiStyle.FieldFill, UiStyle.Accent, 8, 1, 12, 7)); theme.SetStylebox("read_only", "LineEdit", field);
        theme.SetColor("font_color", "LineEdit", UiStyle.Text); theme.SetColor("font_placeholder_color", "LineEdit", UiStyle.TextDisabled); theme.SetColor("caret_color", "LineEdit", UiStyle.Accent);
        theme.SetStylebox("panel", "PopupMenu", UiStyle.Box(UiStyle.PanelFillSolid, UiStyle.BorderSoft, 8, 1, 6, 6));
        theme.SetStylebox("hover", "PopupMenu", UiStyle.Box(UiStyle.AccentFill, UiStyle.Accent, 6, 0, 8, 4)); theme.SetColor("font_color", "PopupMenu", UiStyle.Text); theme.SetColor("font_hover_color", "PopupMenu", UiStyle.Text);
        theme.SetStylebox("slider", "HSlider", new StyleBoxFlat { BgColor = new Color(UiStyle.ButtonFill, 1), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginTop = 3, ContentMarginBottom = 3 });
        var fill = new StyleBoxFlat { BgColor = UiStyle.Accent, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginTop = 3, ContentMarginBottom = 3 };
        theme.SetStylebox("grabber_area", "HSlider", fill); theme.SetStylebox("grabber_area_highlight", "HSlider", fill);
        theme.SetStylebox("panel", "ItemList", UiStyle.Box(UiStyle.FieldFill, UiStyle.Divider, 8, 1, 6, 6)); theme.SetStylebox("selected", "ItemList", UiStyle.Box(UiStyle.AccentFill, UiStyle.Accent, 6, 1, 6, 4));
        theme.SetStylebox("selected_focus", "ItemList", UiStyle.Box(UiStyle.AccentFill, UiStyle.Accent, 6, 1, 6, 4)); theme.SetColor("font_color", "ItemList", UiStyle.Text);
        return theme;
    }

    /// <summary>Takes the controls the editor created and puts them in the new screen (called once, at the end of _Ready).</summary>
    private void ApplyDesign()
    {
        var fields = GetNode<VBoxContainer>("Layout/Panel/Margin/Fields");
        var oldLayout = GetNode<HBoxContainer>("Layout"); var oldPanel = GetNode<PanelContainer>("Layout/Panel"); var preview = GetNode<SubViewportContainer>("Layout/Preview");
        GetNode<Label>("Title").Visible = false;
        var background = GetNode<ColorRect>("Background"); background.Color = new Color("#0B1117");
        var glow = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Texture = Glow(), StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        glow.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(glow); MoveChild(glow, background.GetIndex() + 1);

        // what the other parts of the editor made, in the order they made it
        var children = fields.GetChildren().Cast<Control>().ToList();
        List<Control> Between(string from, string? to)
        {
            var start = children.FindIndex(c => c is Label l && l.Text == from); if (start < 0) return [];
            var end = to is null ? children.Count : children.FindIndex(start + 1, c => c is Label l && l.Text == to); if (end < 0) end = children.Count;
            return children.GetRange(start, end - start);
        }
        var looks = Between("Looks salvos", "Corpo personalizado (GLB)");
        var models = Between("Corpo personalizado (GLB)", "Roupas e acessórios personalizados (GLB)");
        var customWear = Between("Roupas e acessórios personalizados (GLB)", "Animações");
        var animations = Between("Animações", null);
        var message = _message; var buttons = GetNode<HBoxContainer>("Layout/Panel/Margin/Fields/Buttons"); var sensitivity = _sensitivity; var invert = _invertY;
        foreach (var group in new[] { looks, models, customWear, animations }) foreach (var control in group) Detach(control);
        Detach(message); Detach(buttons);

        var theme = MakeTheme();

        // ---- the left panel: the name, the tabs, the page, the buttons
        Detach(oldLayout);
        var root = new MarginContainer { Name = "Screen", Theme = theme }; root.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) root.AddThemeConstantOverride("margin_" + side, 28);
        AddChild(root);
        var layout = new HBoxContainer(); layout.AddThemeConstantOverride("separation", 24); root.AddChild(layout);

        var left = new PanelContainer { CustomMinimumSize = new Vector2(500, 0) }; left.AddThemeStyleboxOverride("panel", UiStyle.Glass(.92f, UiStyle.RadiusLarge, 0));
        layout.AddChild(left);
        var leftBox = new VBoxContainer(); leftBox.AddThemeConstantOverride("separation", 0); left.AddChild(leftBox);

        var head = new MarginContainer(); foreach (var side in new[] { "left", "right", "top" }) head.AddThemeConstantOverride("margin_" + side, 22); head.AddThemeConstantOverride("margin_bottom", 10);
        var headBox = new VBoxContainer(); headBox.AddThemeConstantOverride("separation", 4); head.AddChild(headBox); leftBox.AddChild(head);
        var title = new Label { Text = "Personalizar avatar" }; title.AddThemeFontOverride("font", UiStyle.Bold); title.AddThemeFontSizeOverride("font_size", 26); headBox.AddChild(title);
        var sub = new Label { Text = "Cada mudança aparece no boneco ao lado. Nada é guardado até você salvar." }; sub.AddThemeFontSizeOverride("font_size", 14); sub.AddThemeColorOverride("font_color", UiStyle.TextMuted); headBox.AddChild(sub);
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var nameRow = Line("Nome", _displayName); nameRow.GetChild<Label>(0).CustomMinimumSize = new Vector2(60, 0); headBox.AddChild(nameRow);

        var tabsHolder = new MarginContainer(); tabsHolder.AddThemeConstantOverride("margin_left", 16); tabsHolder.AddThemeConstantOverride("margin_right", 16); leftBox.AddChild(tabsHolder);
        _tabBar = new UiTabs { Flat = true, FontSize = 15, EqualWidth = true, Name = "Tabs" }; tabsHolder.AddChild(_tabBar);
        var pageArea = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true }; leftBox.AddChild(pageArea);

        VBoxContainer Page(string name)
        {
            var scroll = new ScrollContainer { Name = "Page" + name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; scroll.SetAnchorsPreset(LayoutPreset.FullRect);
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 14);
            margin.AddChild(box); scroll.AddChild(margin); pageArea.AddChild(scroll); _pages.Add(scroll); _tabBar.AddTab(name); return box;
        }

        // Corpo: the base, the skin and the proportions
        var body = Page("Corpo");
        body.AddChild(Card(Heading("Base"), Line("Modelo", _baseModel), Line("Cor da pele", _skin)));
        var proportions = new List<Control> { Heading("Proporções") };
        foreach (var key in BodyOrder) proportions.Add(SliderLine(BodyLabels[key], _body[key]));
        body.AddChild(Card([.. proportions]));

        // Rosto e cabelo
        var face = Page("Rosto");
        var faceSliders = new List<Control> { Heading("Traços") };
        foreach (var key in FaceOrder) faceSliders.Add(SliderLine(FaceLabels[key], _face[key]));
        face.AddChild(Card([.. faceSliders]));
        face.AddChild(Card(Heading("Cabelo e barba"), Line("Estilo do cabelo", _hairStyle), Line("Cor do cabelo", _hair), Line("Barba", _beard), Line("Cor dos olhos", _eyes)));

        // Roupas
        var wear = Page("Roupas");
        var basics = new List<Control> { Heading("Peças principais"), Line("Parte de cima", _top), Line("Parte de baixo", _bottom), Line("Calçados", _shoes), Line("Acessório", _accessory) };
        wear.AddChild(Card([.. basics]));
        var extras = new List<Control> { Heading("Complementos") };
        foreach (var slot in ExtraSlots) extras.Add(Line(SlotLabels[slot], _extra[slot]));
        wear.AddChild(Card([.. extras]));

        // Looks
        var looksPage = Page("Looks");
        looksPage.AddChild(Card(LabelToHeading(looks), looks));

        // Avançado
        var advanced = Page("Avançado");
        advanced.AddChild(Card(Heading("Câmera do jogo"), Line("Sensibilidade", sensitivity), InvertLine(invert)));
        advanced.AddChild(Card(LabelToHeading(models), models));
        advanced.AddChild(Card(LabelToHeading(customWear), customWear));
        advanced.AddChild(Card(LabelToHeading(animations), animations));
        foreach (var group in new[] { looks, models, customWear, animations }) foreach (var control in group) Tame(control);
        _tabBar.TabChanged += ShowPage; ShowPage(0);
        _cameraDistance = 2.7f; _cameraHeight = 1.0f; _camera.Position = new Vector3(0, _cameraHeight, _cameraDistance);

        // the bottom of the panel: the message and the buttons
        var footer = new PanelContainer(); footer.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(UiStyle.TitleFill, .9f), BorderColor = UiStyle.Divider, BorderWidthTop = 1, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 14, CornerRadiusBottomLeft = UiStyle.RadiusLarge, CornerRadiusBottomRight = UiStyle.RadiusLarge });
        var footerBox = new VBoxContainer(); footerBox.AddThemeConstantOverride("separation", 8); footer.AddChild(footerBox); leftBox.AddChild(footer);
        message.CustomMinimumSize = Vector2.Zero; message.AddThemeFontSizeOverride("font_size", 14); message.AddThemeColorOverride("font_color", UiStyle.Orange); footerBox.AddChild(message);
        foreach (var button in buttons.GetChildren().Cast<Button>()) { button.CustomMinimumSize = new Vector2(0, 40); button.FocusMode = FocusModeEnum.None; }
        buttons.AddThemeConstantOverride("separation", 8); footerBox.AddChild(buttons);
        var texts = new Dictionary<string, string> { ["Cancel"] = "Cancelar", ["Save"] = "Salvar avatar", ["Undo"] = "Desfazer", ["Restore"] = "Restaurar" };
        foreach (var button in buttons.GetChildren().Cast<Button>()) if (texts.TryGetValue(button.Name, out var t) || texts.TryGetValue(button.Text switch { "Cancel" => "Cancel", "Save avatar" => "Save", "Undo" => "Undo", "Restore" => "Restore", _ => "" }, out t)) button.Text = t;
        UiStyle.StyleButton(_save, UiStyle.ButtonKind.Primary, 12, 8, 8); _save.SizeFlagsHorizontal = SizeFlags.ExpandFill; _save.AddThemeFontOverride("font", UiStyle.Bold);
        buttons.MoveChild(_save, buttons.GetChildCount() - 1);

        // ---- the stage on the right
        Detach(preview);
        var stage = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; stage.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#0E151B"), UiStyle.BorderSoft, UiStyle.RadiusLarge, 1, 0, 0)); stage.ClipContents = true;
        layout.AddChild(stage);
        var stageBox = new Control(); stage.AddChild(stageBox);
        preview.SetAnchorsPreset(LayoutPreset.FullRect); preview.SizeFlagsHorizontal = SizeFlags.ExpandFill; stageBox.AddChild(preview);
        var tag = new Label { Text = "Arraste para girar  ·  roda do mouse para aproximar" }; tag.AddThemeFontSizeOverride("font_size", 13); tag.AddThemeColorOverride("font_color", UiStyle.TextMuted); tag.MouseFilter = MouseFilterEnum.Ignore;
        tag.SetAnchorsPreset(LayoutPreset.CenterTop); tag.GrowHorizontal = GrowDirection.Both; tag.OffsetTop = 16; stageBox.AddChild(tag);
        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass }; bar.AddThemeConstantOverride("separation", 8); bar.Theme = theme;
        Button Cam(string text, Action action) { var b = new Button { Text = text, CustomMinimumSize = new Vector2(120, 38), FocusMode = FocusModeEnum.None }; b.Pressed += action; bar.AddChild(b); return b; }
        Cam("Corpo inteiro", () => SetFaceView(false)); Cam("Rosto", () => SetFaceView(true)); Cam("◀ Girar", () => _preview.RotateY(-Mathf.Pi / 4)); Cam("Girar ▶", () => _preview.RotateY(Mathf.Pi / 4));
        bar.SetAnchorsPreset(LayoutPreset.CenterBottom); bar.GrowHorizontal = GrowDirection.Both; bar.GrowVertical = GrowDirection.Begin; bar.OffsetBottom = -18; stageBox.AddChild(bar);

        oldLayout.QueueFree();
    }

    /// <summary>The managers of looks, models and animations were made for a wide panel: their labels wrap and their buttons shorten their text instead of widening the page.</summary>
    private static void Tame(Node node)
    {
        if (node is Button button) { button.ClipText = true; button.AddThemeFontSizeOverride("font_size", 13); button.CustomMinimumSize = new Vector2(0, button.CustomMinimumSize.Y); }
        else if (node is Label label && label.AutowrapMode != TextServer.AutowrapMode.Off) { label.CustomMinimumSize = new Vector2(0, label.CustomMinimumSize.Y); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        else if (node is Label row && row.CustomMinimumSize.X > 120) row.CustomMinimumSize = new Vector2(120, row.CustomMinimumSize.Y);
        foreach (var child in node.GetChildren()) Tame(child);
    }

    private Control LabelToHeading(List<Control> group)
    {
        if (group.Count > 0 && group[0] is Label label) { var text = label.Text; group.RemoveAt(0); label.QueueFree(); return Heading(text); }
        return Heading("");
    }

    private static HBoxContainer InvertLine(CheckBox invert)
    {
        Detach(invert); invert.Text = "Inverter o eixo Y da câmera"; var row = new HBoxContainer(); row.AddChild(invert); return row;
    }

    private void ShowPage(int index) { for (var i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index; }

    /// <summary>The camera of the stage: the whole body, or close to the face.</summary>
    private void SetFaceView(bool face)
    {
        _faceView = face; _cameraTween?.Kill(); _cameraTween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        var distance = face ? 1.15f : 2.7f; var height = face ? 1.62f : 1.0f; _cameraDistance = distance; _cameraHeight = height;
        _cameraTween.TweenProperty(_camera, "position", new Vector3(0, height, distance), .35);
    }

    private static Texture2D Glow()
    {
        var gradient = new Gradient { Colors = [new Color(.07f, .22f, .26f, .55f), new Color(.043f, .066f, .09f, 0f)], Offsets = [0f, 1f] };
        return new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.72f, .45f), FillTo = new Vector2(.72f, 1.05f), Width = 512, Height = 512 };
    }
}
