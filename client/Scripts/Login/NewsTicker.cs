using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// The strip of news in the middle of the bottom bar. It shows only what the client really knows (a new version, a required update); with nothing to say it says so.
/// Several items rotate every few seconds with a fade. Events and featured islands can be added with <see cref="SetItems"/> when the API provides them.
/// </summary>
public partial class NewsTicker : HBoxContainer
{
    public const string Empty = "Sem novidades no momento.";
    private Label _text = null!;
    private TextureRect _icon = null!;
    private List<string> _items = [];
    private int _index;
    private double _wait;
    private Tween? _fade;

    public string TextNow => _text.Text;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10); Alignment = AlignmentMode.Center; MouseFilter = MouseFilterEnum.Ignore;
        _icon = UiStyle.IconRect("bell", UiStyle.Accent, 18); AddChild(_icon);
        _text = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(420, 0) }; _text.AddThemeFontOverride("font", UiStyle.Medium); _text.AddThemeFontSizeOverride("font_size", 15); AddChild(_text);
        SetItems([]);
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items = [.. items.Where(i => !string.IsNullOrWhiteSpace(i))]; _index = 0; _wait = 6;
        _text.Text = _items.Count > 0 ? _items[0] : Empty; _text.AddThemeColorOverride("font_color", _items.Count > 0 ? UiStyle.Text : UiStyle.TextMuted); _icon.Visible = _items.Count > 0;
    }

    public override void _Process(double delta)
    {
        if (_items.Count < 2) return; _wait -= delta; if (_wait > 0) return; _wait = 6;
        _index = (_index + 1) % _items.Count; _fade?.Kill(); _fade = CreateTween();
        _fade.TweenProperty(_text, "modulate:a", 0f, .25f); _fade.TweenCallback(Callable.From(() => _text.Text = _items[_index])); _fade.TweenProperty(_text, "modulate:a", 1f, .25f);
    }
}
