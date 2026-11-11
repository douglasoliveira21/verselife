using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>The "Criar objeto" screen: a grid of the shapes that can be created. Choosing one arms the placement mode of the editor.</summary>
public partial class BuildCreationPanel : VBoxContainer
{
    private readonly Dictionary<string, Button> _buttons = [];
    private readonly Dictionary<string, Button> _presetButtons = [];
    public event Action<string>? PresetRequested;
    public IReadOnlyCollection<string> PresetsForTesting => _presetButtons.Keys;
    public void PickPresetForTesting(string id) => _presetButtons[id].EmitSignal(BaseButton.SignalName.Pressed);
    private Label _hint = null!;

    /// <summary>A shape was chosen in the grid.</summary>
    public event Action<string>? PrimitivePicked;
    /// <summary>"Importar modelo 3D": the person wants to bring a model into the inventory to place it.</summary>
    public event Action? ImportRequested;
    private Button _import = null!;
    public void PressImportForTesting() => _import.EmitSignal(BaseButton.SignalName.Pressed);

    public IReadOnlyCollection<string> TypesForTesting => _buttons.Keys;
    public string HintForTesting => _hint.Text;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var title = new Label { Text = "CRIAR OBJETO" }; title.AddThemeFontOverride("font", UiStyle.Bold); title.AddThemeFontSizeOverride("font_size", 20); title.AddThemeColorOverride("font_color", UiStyle.AccentText); AddChild(title);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 8); AddChild(grid);
        foreach (var entry in BuildPrimitiveFactory.Entries)
        {
            var type = entry.Type;
            var button = new Button
            {
                Text = entry.Label, Icon = BuildIcons.Primitive(type, 52), IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, ExpandIcon = false, ToggleMode = true,
                CustomMinimumSize = new Vector2(112, 96), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, TooltipText = entry.Hint
            };
            button.AddThemeFontSizeOverride("font_size", 17); button.AddThemeConstantOverride("icon_max_width", 56); button.AddThemeConstantOverride("h_separation", 2); UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 6, 6, 14);
            button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, 6, 6, 14));
            button.Pressed += () => PrimitivePicked?.Invoke(type);
            _buttons[type] = button; grid.AddChild(button);
        }
        _import = new Button { Text = " Importar modelo 3D (malha)", Icon = UiStyle.Icon("download", UiStyle.Text, 22), TooltipText = "Envia um modelo GLB ao seu inventário; depois coloque-o no mundo pelo inventário", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) };
        UiStyle.StyleButton(_import, UiStyle.ButtonKind.Secondary, 10, 6, 14); _import.AddThemeFontSizeOverride("font_size", 18); _import.Pressed += () => ImportRequested?.Invoke(); AddChild(_import);
        var presetTitle = new Label { Text = "PREDEFINIDOS" }; presetTitle.AddThemeFontOverride("font", UiStyle.Bold); presetTitle.AddThemeFontSizeOverride("font_size", 18); presetTitle.AddThemeColorOverride("font_color", UiStyle.AccentText); AddChild(presetTitle);
        var presets = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; presets.AddThemeConstantOverride("h_separation", 6); presets.AddThemeConstantOverride("v_separation", 6); AddChild(presets);
        foreach (var preset in LifeVerse.Shared.Building.BuildPresets.All)
        {
            var id = preset.Id; var button = new Button { Text = preset.Name, TooltipText = $"{preset.Description} ({preset.Parts} peça(s)). Mostra a prévia e um clique coloca.", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 6, 5, 8); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += () => PresetRequested?.Invoke(id); _presetButtons[id] = button; presets.AddChild(button);
        }
        _hint = new Label { Text = "Escolha uma forma e clique no mundo para colocá-la.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _hint.AddThemeColorOverride("font_color", UiStyle.TextMuted); _hint.AddThemeFontSizeOverride("font_size", 16); AddChild(_hint);
    }

    /// <summary>Marks the shape being placed (null when none) and says what to do next.</summary>
    public void SetArmed(string? type)
    {
        foreach (var (key, button) in _buttons) button.SetPressedNoSignal(type is not null && key.Equals(type, StringComparison.OrdinalIgnoreCase));
        _hint.Text = type is null ? "Escolha uma forma e clique no mundo para colocá-la."
            : $"Clique no terreno ou em um objeto para colocar: {BuildPrimitiveFactory.LabelOf(type)}. Shift + clique coloca vários; Esc cancela.";
    }

    public void PickForTesting(string type) => _buttons[type].EmitSignal(BaseButton.SignalName.Pressed);
}
