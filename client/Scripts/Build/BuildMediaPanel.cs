using System.Text.Json.Nodes;
using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The MÍDIA mode of the Textura tab: show a picture or a video from a public https address on the object or on the chosen faces, play and pause it, loop it, make it glow and place it
/// with scale and offset. The address is checked here (and again by the server) before anything is stored; what each person's computer plays is their own choice. Embedded web pages
/// are not offered: that needs a browser component the client does not have yet (CEF or a system WebView), and a fake page would be worse than none.
/// </summary>
public partial class BuildMediaPanel : VBoxContainer
{
    public static readonly string[] Types = ["image", "video"];

    private BuildMaterialManager _material = null!;
    private BuildSelectionManager _selection = null!;
    private OptionButton _type = null!;
    private LineEdit _url = null!;
    private CheckBox _loop = null!, _glow = null!, _enabled = null!;
    private SpinBox _glowStrength = null!;
    private readonly SpinBox[] _uv = new SpinBox[4];
    private Button _apply = null!, _remove = null!, _play = null!;
    private TextureRect _preview = null!;
    private Label _status = null!, _webNote = null!;
    private bool _filling;
    private double _uvTimer;

    public event Action<string>? Said;

    public string StatusForTesting => _status.Text;
    public string WebNoteForTesting => _webNote.Text;
    public string TypeForTesting => Types[Math.Max(0, _type.Selected)];
    public bool HasPreviewForTesting => _preview.Texture is not null;
    public bool PlayingForTesting => _play.Text.Contains("Pausar");
    public void SetSourceForTesting(string type, string url) { _type.Select(Array.IndexOf(Types, type)); _url.Text = url; }
    public void PressApplyForTesting() => _apply.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressRemoveForTesting() => _remove.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressPlayForTesting() => _play.EmitSignal(BaseButton.SignalName.Pressed);
    public void SetGlowForTesting(bool on, double strength) { _glow.ButtonPressed = on; _glowStrength.Value = strength; }
    public void SetLoopForTesting(bool on) => _loop.ButtonPressed = on;
    public void SetUvForTesting(double scaleU, double scaleV, double offsetU, double offsetV) { _uv[0].Value = scaleU; _uv[1].Value = scaleV; _uv[2].Value = offsetU; _uv[3].Value = offsetV; _uvTimer = .01; Flush(); }

    public void Initialize(BuildSelectionManager selection, BuildMaterialManager material) { _selection = selection; _material = material; }

    /// <summary>The same rules the server applies: https, a public host name, no user name, and a file kind that the chosen media can play.</summary>
    public static string? Validate(string type, string url)
    {
        if (url.Length is 0 or > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return "Use um endereço https completo, por exemplo https://exemplo.com/imagem.png.";
        var host = uri.Host.ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal) || !host.Contains('.') || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _)) return "O endereço precisa ser de um site público (não de uma rede privada nem um número de IP).";
        var extensions = type == "image" ? new[] { ".png", ".jpg", ".jpeg", ".webp" } : new[] { ".ogv" };
        if (!extensions.Any(e => uri.AbsolutePath.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return type == "image" ? "A imagem precisa ser um arquivo PNG, JPG ou WebP." : "O vídeo precisa ser um arquivo Ogg Theora (.ogv).";
        return null;
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Heading("MÍDIA"));
        _type = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None, TooltipText = "O tipo de fonte" }; _type.AddItem("Imagem (PNG, JPG, WebP)"); _type.AddItem("Vídeo (Ogg Theora .ogv)"); AddChild(Row("TIPO", _type));
        _url = new LineEdit { PlaceholderText = "https://…", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), MaxLength = 500 }; _url.TextSubmitted += _ => Apply(); AddChild(Row("FONTE", _url));
        _webNote = new Label { Text = "Páginas da web: indisponível. O cliente ainda não tem um navegador embutido (a dependência técnica é o CEF ou um WebView do sistema).", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _webNote.AddThemeFontSizeOverride("font_size", 15); _webNote.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_webNote);
        _preview = new TextureRect { CustomMinimumSize = new Vector2(0, 150), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ClipContents = true }; var frame = new PanelContainer(); frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#121A20"), UiStyle.Border, 8, 1, 3, 3)); frame.AddChild(_preview); AddChild(frame);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _status.AddThemeFontSizeOverride("font_size", 16); _status.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_status);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6); AddChild(buttons);
        _apply = Button("Aplicar", "check", "Mostra esta mídia no alvo", Apply); _remove = Button("Remover", "trash", "Tira a mídia do alvo", () => { if (_material.RemoveMedia() > 0) Said?.Invoke("Mídia removida."); }); _play = Button("Reproduzir", "play", "Liga ou pausa a reprodução do vídeo", TogglePlay);
        buttons.AddChild(_apply); buttons.AddChild(_remove); buttons.AddChild(_play);
        _loop = Check("Repetir", "O vídeo recomeça quando termina"); _loop.Toggled += _ => { if (!_filling && _material.Media is not null) Apply(); }; AddChild(_loop);
        _glow = Check("Glow", "A mídia brilha por conta própria"); _glowStrength = Spin(0, 16, .1, 1, "Intensidade do brilho"); _glow.Toggled += _ => { if (!_filling && _material.Media is not null) Apply(); }; _glowStrength.ValueChanged += _ => { if (!_filling && _material.Media is not null && _glow.ButtonPressed) { _uvTimer = .4; } };
        var glowRow = new HBoxContainer(); glowRow.AddThemeConstantOverride("separation", 8); glowRow.AddChild(_glow); glowRow.AddChild(_glowStrength); AddChild(glowRow);
        var scale = new HBoxContainer(); scale.AddThemeConstantOverride("separation", 6); scale.AddChild(Small("ESCALA", 70)); scale.AddChild(Small("U", 14)); _uv[0] = Spin(.01, 100, .01, 1, "Repetições na horizontal"); scale.AddChild(_uv[0]); scale.AddChild(Small("V", 14)); _uv[1] = Spin(.01, 100, .01, 1, "Repetições na vertical"); scale.AddChild(_uv[1]); AddChild(scale);
        var offset = new HBoxContainer(); offset.AddThemeConstantOverride("separation", 6); offset.AddChild(Small("DESLOC.", 70)); offset.AddChild(Small("U", 14)); _uv[2] = Spin(-100, 100, .01, 0, "Deslocamento horizontal"); offset.AddChild(_uv[2]); offset.AddChild(Small("V", 14)); _uv[3] = Spin(-100, 100, .01, 0, "Deslocamento vertical"); offset.AddChild(_uv[3]); AddChild(offset);
        foreach (var spin in _uv) spin.ValueChanged += _ => { if (!_filling) _uvTimer = .4; };
        _enabled = Check("Reproduzir mídia neste computador", "Cada pessoa escolhe: desligado, as superfícies com mídia ficam lisas e nada é baixado"); _enabled.SetPressedNoSignal(BuildObjectView.MediaSettings.Enabled); _enabled.Toggled += on => { BuildObjectView.MediaSettings.Enabled = on; Said?.Invoke(on ? "Mídia ligada: vale para as próximas vezes que o objeto for desenhado." : "Mídia desligada neste computador."); }; AddChild(_enabled);
    }

    private static Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }
    private static Label Small(string text, int width) { var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }
    private static CheckBox Check(string text, string tip) { var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 17); return box; }
    private static SpinBox Spin(double min, double max, double step, double value, string tip) { var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(80, 36), SelectAllOnFocus = true, UpdateOnTextChanged = false }; s.GetLineEdit().AddThemeFontSizeOverride("font_size", 16); return s; }
    private static HBoxContainer Row(string caption, Control control) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); row.AddChild(Small(caption, 70)); control.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(control); return row; }
    private static Button Button(string text, string icon, string tip, Action action) { var b = new Button { Text = " " + text, Icon = UiStyle.Icon(icon, UiStyle.Text, 20), TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = SizeFlags.ExpandFill }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 8, 4, 12); b.AddThemeFontSizeOverride("font_size", 16); b.Pressed += action; return b; }

    // ---- what the person does ----------------------------------------------------------------------------------------------

    private void Apply()
    {
        var type = Types[Math.Max(0, _type.Selected)]; var url = _url.Text.Trim();
        if (Validate(type, url) is { } problem) { _status.Text = problem; return; }
        var playing = _material.Media?["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) ? on : type == "video";
        if (_material.SetMedia(type, url, playing, _loop.ButtonPressed, _glow.ButtonPressed, _glowStrength.Value) > 0) { _status.Text = "Carregando a mídia…"; Said?.Invoke("Mídia enviada ao servidor."); }
    }

    private void TogglePlay()
    {
        if (_material.Media is not { } media) { _status.Text = "Aplique uma mídia primeiro."; return; }
        var playing = media["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) && on;
        var type = media["type"]?.GetValue<string>() ?? "image"; var url = media["url"]?.GetValue<string>() ?? string.Empty;
        if (_material.SetMedia(type, url, !playing, _loop.ButtonPressed, _glow.ButtonPressed, _glowStrength.Value) > 0) Said?.Invoke(playing ? "Mídia pausada." : "Mídia tocando.");
    }

    public override void _Process(double delta)
    {
        if (_uvTimer > 0) { _uvTimer -= delta; if (_uvTimer <= 0) Flush(); }
    }

    private void Flush()
    {
        _uvTimer = 0;
        if (_material.Media is null) return;
        var type = Types[Math.Max(0, _type.Selected)]; var url = _url.Text.Trim();
        if (Validate(type, url) is null) _material.SetMedia(type, url, _material.Media["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) && on, _loop.ButtonPressed, _glow.ButtonPressed, _glowStrength.Value);
        _material.SetUv("texture", _uv[0].Value, _uv[1].Value, _uv[2].Value, _uv[3].Value, _material.Uv("texture").Rotation);
        Refresh();
    }

    // ---- showing the surface ------------------------------------------------------------------------------------------------

    public void Refresh()
    {
        if (_material is null || _type is null || _uvTimer > 0) return;
        _filling = true;
        var media = _material.Media; var active = _selection.Views().LastOrDefault();
        if (media is not null)
        {
            var type = media["type"]?.GetValue<string>() ?? "image"; _type.Select(Math.Max(0, Array.IndexOf(Types, type))); _url.Text = media["url"]?.GetValue<string>() ?? string.Empty;
            var playing = media["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) && on; _play.Text = playing ? " Pausar" : " Reproduzir"; _loop.SetPressedNoSignal(media["loop"] is JsonValue l && l.TryGetValue<bool>(out var lp) && lp);
            var glow = media["glow"] is JsonValue g && g.TryGetValue<bool>(out var gl) && gl; _glow.SetPressedNoSignal(glow); _glowStrength.SetValueNoSignal(_material.Number("emissionStrength", 1));
            var surface = active?.MediaSurfaces.FirstOrDefault(); _preview.Texture = surface?.Texture;
            _status.Text = !BuildObjectView.MediaSettings.Enabled ? "A mídia está desligada neste computador." : surface is null ? "Mídia guardada." : surface.Failed ? "Não foi possível carregar essa mídia (endereço fora do ar, arquivo grande demais ou formato inválido)." : surface.Texture is null ? "Carregando a mídia…" : type == "video" ? (surface.VideoPlaying ? "Vídeo tocando (sem som)." : "Vídeo carregado.") : "Imagem no ar.";
        }
        else { _preview.Texture = null; _play.Text = " Reproduzir"; _status.Text = "Escolha o tipo, cole o endereço e aperte Aplicar."; }
        var uv = _material.Uv("texture"); _uv[0].SetValueNoSignal(uv.RepeatX); _uv[1].SetValueNoSignal(uv.RepeatY); _uv[2].SetValueNoSignal(uv.OffsetX); _uv[3].SetValueNoSignal(uv.OffsetY);
        foreach (var control in new Control[] { _apply, _remove, _play }) ((BaseButton)control).Disabled = _selection.Count == 0;
        _filling = false;
    }
}
