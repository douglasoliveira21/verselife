using Godot;

namespace LifeVerse.Client.Scripts;

/// The look of the build window (client/Assets/ui/construir): tool buttons with the icon above the name, icon rows, label-and-field
/// forms, orange action buttons with icons, a framed "Terreno e limites" card and a footer line. Only layout lives here; what the
/// buttons do is in the other parts of the editor.
public partial class BuildEditor
{
    private Label _infoLeft = null!, _infoRight = null!, _buildHereLabel = null!;

    /// An action button with an icon in front of its text.
    private static Button IconButton(string icon, string text, string tip, Action action, UiStyle.ButtonKind kind = UiStyle.ButtonKind.Secondary, int height = 44)
    {
        var button = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(button, kind, 12, 6); button.AddThemeFontSizeOverride("font_size", 19); button.AddThemeFontOverride("font", UiStyle.Medium);
        button.AddThemeConstantOverride("icon_max_width", 28); button.AddThemeColorOverride("icon_normal_color", Colors.White); button.AddThemeColorOverride("icon_hover_color", Colors.White); button.AddThemeColorOverride("icon_pressed_color", Colors.White);
        button.Pressed += action;
        return button;
    }

    private static Label RowLabel(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 20);
        return label;
    }

    private static Label Muted(string text, int size = 18)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static HBoxContainer Row(int gap = 12) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", gap); return row; }

    // ---- the Geral tab ---------------------------------------------------------------------------------------

    /// Generated miniature drawings keep the palette recognisable without loading an
    /// external asset per primitive.
    private static Texture2D PrimitivePaletteIcon(string primitive, Color color, int size)
    {
        if (primitive == "cube") return UiStyle.Icon("cube", color, size);
        var shape = primitive switch
        {
            "sphere" => "<circle cx='12' cy='12' r='8'/><path d='M4 12h16M12 4c3 3 3 13 0 16M12 4c-3 3-3 13 0 16'/>",
            "cylinder" => "<ellipse cx='12' cy='5.5' rx='6.5' ry='2.5'/><path d='M5.5 5.5v13c0 3.3 13 3.3 13 0v-13M5.5 18.5c0-3.3 13-3.3 13 0'/>",
            "prism" => "<path d='M4 18l6-12 6 12zM10 6l8 3v11l-8-2'/>",
            "torus" or "ring" => "<ellipse cx='12' cy='12' rx='8.5' ry='5.5'/><ellipse cx='12' cy='12' rx='3.5' ry='2.2'/>",
            "tube" => "<ellipse cx='12' cy='5.5' rx='6.5' ry='2.5'/><ellipse cx='12' cy='5.5' rx='3' ry='1.1'/><path d='M5.5 5.5v13c0 3.3 13 3.3 13 0v-13M9 5.5v13M15 5.5v13'/>",
            "pyramid" => "<path d='M12 3l8 16H4zM12 3v16M4 19l8 2 8-2'/>",
            "cone" => "<path d='M12 3l7 15H5z'/><ellipse cx='12' cy='18' rx='7' ry='2.2'/>",
            "hemisphere" => "<path d='M4 14a8 8 0 0 1 16 0z'/><ellipse cx='12' cy='14' rx='8' ry='2.5'/>",
            "plane" => "<path d='M4 8l12-4 4 12-12 4zM8 6l4 12M16 4l4 12'/>",
            _ => "<path d='M4 18l8-14 8 14z'/>"
        };
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' stroke='#{color.ToHtml(false)}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{shape}</svg>";
        var image = new Image();
        return image.LoadSvgFromString(svg, size * 2f / 24f) == Error.Ok ? ImageTexture.CreateFromImage(image) : UiStyle.Icon("cube", color, size);
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
        var heading = new Label { Text = "Terreno e limites" }; heading.AddThemeFontOverride("font", UiStyle.Bold); heading.AddThemeFontSizeOverride("font_size", 22); title.AddChild(heading);

        var body = Row(14); inside.AddChild(body);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 4); body.AddChild(text);
        _limitsLabel = new Label { Text = "Os limites aparecem quando você entra em uma ilha.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _limitsLabel.AddThemeFontSizeOverride("font_size", 19); text.AddChild(_limitsLabel);
        _buildHereLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _buildHereLabel.AddThemeFontSizeOverride("font_size", 19); text.AddChild(_buildHereLabel);
        var buttons = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) }; buttons.AddThemeConstantOverride("separation", 8); body.AddChild(buttons);
        buttons.AddChild(IconButton("undo", "Devolver ao dono", "Manda os objetos selecionados para o inventário de quem os fez (dono do terreno ou administrador)", ReturnSelection, height: 40));
        buttons.AddChild(IconButton("history", "Histórico", "Mostra quem mexeu no objeto selecionado", ShowHistory, height: 40));

        _historyDialog = new AcceptDialog { Title = "Histórico do objeto", OkButtonText = "Fechar", MinSize = new Vector2I(460, 320) };
        _historyText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 240) };
        _historyDialog.AddChild(_historyText); AddChild(_historyDialog);
    }
}
