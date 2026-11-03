using Godot;

namespace LifeVerse.Client.Scripts;

/// The look of the build window (client/Assets/ui/construir): tool buttons with the icon above the name, icon rows, label-and-field
/// forms, orange action buttons with icons, a framed "Terreno e limites" card and a footer line. Only layout lives here; what the
/// buttons do is in the other parts of the editor.
public partial class BuildEditor
{
    private Label _infoLeft = null!, _infoRight = null!, _buildHereLabel = null!;

    /// A tool toggle with its icon above its name, like the tool row of the design.
    private Button StackedTool(string icon, string text, string tip, Action action)
    {
        var button = ToolToggle(string.Empty, tip, action);
        button.CustomMinimumSize = new Vector2(0, 64);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(Control.LayoutPreset.FullRect); column.AddThemeConstantOverride("separation", 2);
        var glyph = UiStyle.IconRect(icon, UiStyle.Text, 28); glyph.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", UiStyle.Medium); label.AddThemeFontSizeOverride("font_size", 17);
        column.AddChild(glyph); column.AddChild(label); button.AddChild(column);
        return button;
    }

    /// An action button with an icon in front of its text.
    private static Button IconButton(string icon, string text, string tip, Action action, UiStyle.ButtonKind kind = UiStyle.ButtonKind.Secondary, int height = 44)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(button, kind, 12, 6); button.AddThemeFontSizeOverride("font_size", 17); button.AddThemeFontOverride("font", UiStyle.Medium);
        button.AddThemeColorOverride("icon_normal_color", Colors.White); button.AddThemeColorOverride("icon_hover_color", Colors.White); button.AddThemeColorOverride("icon_pressed_color", Colors.White);
        button.Pressed += action;
        return button;
    }

    private static Label RowLabel(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private static Label Muted(string text, int size = 16)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static HBoxContainer Row(int gap = 12) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", gap); return row; }

    // ---- the Geral tab ---------------------------------------------------------------------------------------

    private Control BuildGeneral()
    {
        var box = Section(); box.AddThemeConstantOverride("separation", 12);

        var nameRow = Row(); box.AddChild(nameRow);
        var nameLabel = RowLabel("Nome:"); nameLabel.CustomMinimumSize = new Vector2(104, 0); nameRow.AddChild(nameLabel);
        _name = Field("Nome do objeto", 160); _name.TextSubmitted += _ => ApplySelected(); nameRow.AddChild(_name);

        var descriptionRow = Row(); box.AddChild(descriptionRow);
        var descriptionLabel = RowLabel("Descrição:"); descriptionLabel.CustomMinimumSize = new Vector2(104, 0); descriptionLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin; descriptionLabel.CustomMinimumSize = new Vector2(104, 40);
        descriptionRow.AddChild(descriptionLabel);
        _description = new TextEdit { PlaceholderText = "Descrição (opcional)", WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, 68), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _description.AddThemeFontSizeOverride("font_size", 17);
        _description.TextChanged += () => { if (_description.Text.Length > 4000) _description.Text = _description.Text[..4000]; };
        descriptionRow.AddChild(_description);

        var apply = Row(); box.AddChild(apply);
        apply.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var applyButton = IconButton("check", "Aplicar nome e descrição", "Envia as alterações ao servidor", ApplySelected, height: 42); applyButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; applyButton.CustomMinimumSize = new Vector2(280, 42);
        apply.AddChild(applyButton);

        // who made it and what it is, in two columns
        var info = Row(14); box.AddChild(info);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 2);
        _infoLeft = Muted("Selecione um objeto para ver criador, proprietário e identificador.", 16); _infoLeft.AddThemeColorOverride("font_color", new Color("#C3CCD3")); left.AddChild(_infoLeft);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 2);
        _infoRight = Muted(string.Empty, 15); _infoRight.AddThemeColorOverride("font_color", new Color("#C3CCD3")); _infoRight.AutowrapMode = TextServer.AutowrapMode.Arbitrary; right.AddChild(_infoRight);
        info.AddChild(left); info.AddChild(new VSeparator()); info.AddChild(right);
        box.AddChild(new HSeparator());

        var primitiveRow = Row(); box.AddChild(primitiveRow);
        primitiveRow.AddChild(RowLabel("Criar nova primitiva:"));
        _primitive = new OptionButton { CustomMinimumSize = new Vector2(240, 42), FocusMode = Control.FocusModeEnum.None };
        foreach (var (id, label) in Primitives) { _primitive.AddIconItem(UiStyle.Icon("cube", UiStyle.Text, 20), label); _primitive.SetItemMetadata(_primitive.ItemCount - 1, id); }
        primitiveRow.AddChild(_primitive);

        BuildPackageSection(box);
        BuildCollabSection(box);
        return box;
    }

    /// "Nome da construção:" with the two orange buttons that keep the selection in the inventory.
    private void BuildPackageSection(VBoxContainer box)
    {
        var row = Row(10); box.AddChild(row);
        row.AddChild(RowLabel("Nome da construção:"));
        _packageName = Field("Nome da construção", 160); row.AddChild(_packageName);
        var save = IconButton("copy", "Salvar cópia", "Guarda a seleção no inventário e deixa os objetos no mundo", () => SaveSelection(take: false), height: 42); save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        var take = IconButton("download", "Retirar", "Guarda a seleção no inventário e tira os objetos do mundo", () => SaveSelection(take: true), height: 42); take.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        row.AddChild(save); row.AddChild(take);
    }

    /// The framed "Terreno e limites" card: usage of the island and the parcel on the left, return and history on the right.
    private void BuildCollabSection(VBoxContainer box)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .45f), new Color(UiStyle.Border, .75f), 12, 1, 14, 12));
        box.AddChild(card);
        var inside = new VBoxContainer(); inside.AddThemeConstantOverride("separation", 8); card.AddChild(inside);
        var title = Row(10); inside.AddChild(title);
        title.AddChild(UiStyle.IconRect("layers", UiStyle.Text, 24));
        var heading = new Label { Text = "Terreno e limites" }; heading.AddThemeFontOverride("font", UiStyle.Bold); heading.AddThemeFontSizeOverride("font_size", 20); title.AddChild(heading);

        var body = Row(14); inside.AddChild(body);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 4); body.AddChild(text);
        _limitsLabel = new Label { Text = "Os limites aparecem quando você entra em uma ilha.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _limitsLabel.AddThemeFontSizeOverride("font_size", 17); text.AddChild(_limitsLabel);
        _buildHereLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _buildHereLabel.AddThemeFontSizeOverride("font_size", 17); text.AddChild(_buildHereLabel);
        var buttons = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) }; buttons.AddThemeConstantOverride("separation", 8); body.AddChild(buttons);
        buttons.AddChild(IconButton("undo", "Devolver ao dono", "Manda os objetos selecionados para o inventário de quem os fez (dono do terreno ou administrador)", ReturnSelection, height: 40));
        buttons.AddChild(IconButton("history", "Histórico", "Mostra quem mexeu no objeto selecionado", ShowHistory, height: 40));

        _historyDialog = new AcceptDialog { Title = "Histórico do objeto", OkButtonText = "Fechar", MinSize = new Vector2I(460, 320) };
        _historyText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 240) };
        _historyDialog.AddChild(_historyText); AddChild(_historyDialog);
    }
}
