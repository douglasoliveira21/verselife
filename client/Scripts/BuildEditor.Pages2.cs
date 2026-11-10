using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// The Textura, Recursos and Conteúdo tabs, laid out like the references in client/Assets/ui/construir. The controls keep writing to the
/// same material, light and content keys of the object as before.
public partial class BuildEditor
{
    private int _source;
    private readonly List<Button> _sourceButtons = [];
    private Label _previewName = null!, _previewInfo = null!, _appliedLabel = null!;
    private static ImageTexture? _placeholder;

    private static ImageTexture Placeholder
    {
        get
        {
            if (_placeholder is not null) return _placeholder;
            var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); image.Fill(new Color("#2A3239"));
            return _placeholder = ImageTexture.CreateFromImage(image);
        }
    }

    // ---- the Textura tab ---------------------------------------------------------------------------------------


    private int _applyToken;

    /// Material changes (colour, shine, opacity, glow) are sent a moment after the person lets go of the control, not on every tick.
    private void ScheduleMaterialApply()
    {
        if (_filling) return;
        var token = ++_applyToken;
        GetTree().CreateTimer(.35).Timeout += () => { if (token == _applyToken && _selection.Count > 0) ApplySelected(); };
    }

    private void SelectSource(int index)
    {
        _source = index;
        for (var i = 0; i < _sourceButtons.Count; i++) _sourceButtons[i].SetPressedNoSignal(i == index);
        RefreshLibrary();
    }

    // ---- the Recursos tab --------------------------------------------------------------------------------------

    // ---- the Conteúdo tab --------------------------------------------------------------------------------------

    private int _contentSelected = -1;
    private readonly List<PanelContainer> _contentRowNodes = [];
    private readonly List<string> _contentTexts = [];
    private VBoxContainer _contentRows = null!;
    private Label _contentCount = null!;

    private static string CategoryIcon(string category) => category switch
    {
        "scripts" => "code", "textures" => "image", "sounds" => "volume", "animations" => "run", "models" => "cube", "packages" => "layers", "clothing" => "avatar",
        "accessories" => "star", "objects" => "cube", "notecards" => "list", _ => "folder"
    };

    private static PanelContainer PanelWith(StyleBox box) { var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; panel.AddThemeStyleboxOverride("panel", box); return panel; }

    private static StyleBoxFlat ContentRowBox(bool selected) =>
        selected ? UiStyle.Box(new Color(UiStyle.AccentFill, .55f), UiStyle.Accent, 12, 2, 10, 6) : UiStyle.Box(new Color(UiStyle.FieldFill, .30f), new Color(0, 0, 0, 0), 12, 0, 10, 6);

    private void SelectContent(int index)
    {
        _contentSelected = index; _scriptSelected = Guid.Empty;
        for (var i = 0; i < _contentRowNodes.Count; i++) _contentRowNodes[i].AddThemeStyleboxOverride("panel", ContentRowBox(i == index));
        if (index >= 0 && index < Entries().Count) _contentName.Text = Entries()[index]["name"]?.GetValue<string>() ?? "";
    }

}
