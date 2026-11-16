using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>
/// "Anexar objeto em…": the points of the body grouped by region. Choosing one reports the point and whether what is already there should be replaced; the window does nothing else, so the
/// same window serves "attach", "replace" and "change point".
/// </summary>
public sealed partial class AttachPointWindow : Node
{
    private static readonly (string Region, string Label)[] Tabs = [(AttachmentPoints.Head, "Cabeça e rosto"), (AttachmentPoints.Torso, "Tronco"), (AttachmentPoints.Arms, "Braços e mãos"), (AttachmentPoints.Legs, "Pernas e pés"), (AttachmentPoints.Special, "Pontos especiais"), (AttachmentPoints.Hud, "HUD")];

    private FloatingWindow _window = null!;
    private Label _title = null!, _note = null!;
    private HBoxContainer _tabs = null!;
    private GridContainer _grid = null!;
    private CheckBox _replace = null!;
    private Func<string, int> _count = _ => 0;
    private IReadOnlyCollection<string> _profiles = [];
    private string _region = AttachmentPoints.Head, _current = string.Empty;
    private Action<string, bool>? _chosen;

    public bool IsOpen => _window is { Visible: true };
    public string Region => _region;
    public bool ReplaceChecked { get => _replace.ButtonPressed; set => _replace.ButtonPressed = value; }
    public IReadOnlyList<string> VisiblePoints => [.. _grid.GetChildren().OfType<Button>().Select(b => (string)b.GetMeta("point"))];

    public override void _Ready() => Build();

    /// <param name="title">The object's name.</param>
    /// <param name="current">The point it is on now (empty when it is not worn).</param>
    /// <param name="count">How many objects are on each point right now.</param>
    /// <param name="chosen">Called with the key of the point and whether to replace what is there.</param>
    public void Open(string title, string current, Func<string, int> count, Action<string, bool> chosen, bool replaceByDefault = false, IReadOnlyCollection<string>? profiles = null)
    {
        _title.Text = $"Anexar “{title}” em:"; _current = current; _count = count; _chosen = chosen; _profiles = profiles ?? []; _replace.ButtonPressed = replaceByDefault; _note.Text = string.Empty;
        _region = AttachmentPoints.Regions.FirstOrDefault(r => r != AttachmentPoints.Hud && current.Length > 0 && AttachmentPoints.Find(current)?.Region == r) ?? AttachmentPoints.Head;
        Fill(); _window.Visible = true; _window.MoveToFront();
    }

    public void Close() { if (_window is not null) _window.Visible = false; }

    /// <summary>Picks a region as a click on its tab would.</summary>
    public void ShowRegion(string region) { _region = region; Fill(); }

    /// <summary>Picks a point as a click on its button would. False when that point is not in the region shown.</summary>
    public bool Pick(string point)
    {
        var button = _grid.GetChildren().OfType<Button>().FirstOrDefault(b => (string)b.GetMeta("point") == point); if (button is null) return false;
        button.EmitSignal(BaseButton.SignalName.Pressed); return true;
    }

    private void Build()
    {
        _window = new FloatingWindow("Anexar ao corpo", icon: "user") { Name = "AttachPointWindow", Visible = false };
        _window.Place(.5f, .5f, -300, -230, 300, 230); _window.CloseRequested += Close;
        var content = _window.Content; content.AddThemeConstantOverride("separation", 10);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; _title.AddThemeFontSizeOverride("font_size", 18); _title.AddThemeFontOverride("font", UiStyle.Bold); content.AddChild(_title);
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 6); content.AddChild(_tabs);
        _grid = new GridContainer { Columns = 3, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _grid.AddThemeConstantOverride("h_separation", 8); _grid.AddThemeConstantOverride("v_separation", 8); content.AddChild(_grid);
        _replace = new CheckBox { Text = "Substituir o que já está no ponto", FocusMode = Control.FocusModeEnum.None, TooltipText = "Tira os objetos que estão no ponto escolhido antes de colocar este" }; _replace.AddThemeFontSizeOverride("font_size", 15); content.AddChild(_replace);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeFontSizeOverride("font_size", 14); _note.AddThemeColorOverride("font_color", UiStyle.Orange); content.AddChild(_note);
        AddChild(_window);
    }

    /// <summary>Shows a message on the window (what the server answered when the choice was refused).</summary>
    public void Say(string text) => _note.Text = text;

    private void Fill()
    {
        foreach (var child in _tabs.GetChildren()) child.QueueFree();
        foreach (var child in _grid.GetChildren()) child.QueueFree();
        foreach (var (region, label) in Tabs)
        {
            var tab = new Button { Text = label, ToggleMode = true, ButtonPressed = region == _region, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            UiStyle.StyleButton(tab, region == _region ? UiStyle.ButtonKind.Primary : UiStyle.ButtonKind.Secondary, 10, 6); tab.AddThemeFontSizeOverride("font_size", 14);
            var r = region; tab.Pressed += () => ShowRegion(r); _tabs.AddChild(tab);
        }
        // HUD points sit on the screen, not on the body
        foreach (var point in AttachmentPoints.For(_profiles).Where(p => p.Region == _region))
        {
            var count = _count(point.Key); var here = point.Key == _current;
            var button = new Button { Text = count > 0 ? $"{point.Name} ({count})" : point.Name, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = here ? "O objeto está neste ponto" : point.SecondLife is { } sl ? $"Second Life: {sl}" : string.Empty, Disabled = here, ClipText = true };
            UiStyle.StyleButton(button, here ? UiStyle.ButtonKind.Primary : UiStyle.ButtonKind.Secondary, 10, 6); button.AddThemeFontSizeOverride("font_size", 14); button.SetMeta("point", point.Key);
            var key = point.Key; button.Pressed += () => _chosen?.Invoke(key, _replace.ButtonPressed); _grid.AddChild(button);
        }
    }
}
