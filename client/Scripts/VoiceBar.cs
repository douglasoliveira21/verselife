using Godot;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// The voice control in the bottom bar: one teal-outlined pill with the "Falar" button (hold the mouse button to talk) and
/// a padlock segment (click to leave the microphone open without holding anything). A thin meter under the label shows
/// how loud you are: green when the sound is clean, red when it is too loud.
public partial class VoiceBar : HBoxContainer
{
    private VoiceChat _voice = null!;
    private bool _ruleAllowed = true;
    private string _ruleWhere = "";

    /// <summary>Asks whether voice works where the avatar stands: (allowed, the name of the parcel that switches it off). Set by the main UI from the land presence.</summary>
    public Func<(bool Allowed, string Where)>? VoiceRule { get; set; }
    public bool BlockedForTesting => !_ruleAllowed;

    public GlyphButton TalkButton { get; private set; } = null!;
    public GlyphButton LockButton { get; private set; } = null!;

    public void Bind(VoiceChat voice)
    {
        _voice = voice;
        AddThemeConstantOverride("separation", 0);
        TalkButton = new GlyphButton(GlyphKind.Microphone, _voice) { Text = "Falar", CustomMinimumSize = new Vector2(112, 46), FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Right };
        TalkButton.ButtonDown += () => { if (_voice.Enabled && !_voice.BlockedHere) _voice.PushToTalk = true; };
        TalkButton.ButtonUp += () => _voice.PushToTalk = false;
        LockButton = new GlyphButton(GlyphKind.Lock, _voice) { ToggleMode = true, CustomMinimumSize = new Vector2(46, 46), FocusMode = FocusModeEnum.None };
        LockButton.Toggled += on => { if (_voice.Enabled && !_voice.BlockedHere) _voice.OpenMic = on; else LockButton.SetPressedNoSignal(false); };
        AddChild(TalkButton); AddChild(LockButton);
        _voice.SettingsChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree() { if (_voice is not null && IsInstanceValid(_voice)) _voice.SettingsChanged -= Refresh; }

    /// Keeps the padlock in step when the setting changes elsewhere (the menu) and greys everything out when voice is off.
    public void Refresh()
    {
        LockButton.SetPressedNoSignal(_voice.OpenMic);
        var blocked = !_ruleAllowed; _voice.BlockedHere = blocked;
        TalkButton.Disabled = LockButton.Disabled = !_voice.Enabled || blocked;
        TalkButton.Text = blocked ? "Sem voz" : "Falar";
        TalkButton.TooltipText = blocked ? $"A voz está desligada neste terreno{(_ruleWhere.Length > 0 ? $" ({_ruleWhere})" : "")}: quem está dentro não fala nem ouve." : _voice.Enabled ? "Segure o botão do mouse para falar. Use fones de ouvido para evitar eco." : "A voz está desligada (menu Comunicar).";
        LockButton.TooltipText = "Cadeado: deixa o microfone aberto sem precisar segurar o botão.";
        TalkButton.QueueRedraw(); LockButton.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (VoiceRule?.Invoke() is { } rule && (rule.Allowed != _ruleAllowed || rule.Where != _ruleWhere)) { _ruleAllowed = rule.Allowed; _ruleWhere = rule.Where; Refresh(); }
        if (_voice.LocalLevel > .001f || _voice.Talking) TalkButton.QueueRedraw();
    }

    // For tests: the same effect as pressing and releasing the mouse on the buttons.
    public void PressTalk(bool down) { if (down) TalkButton.EmitSignal(BaseButton.SignalName.ButtonDown); else TalkButton.EmitSignal(BaseButton.SignalName.ButtonUp); }
    public void ClickLock() => LockButton.ButtonPressed = !LockButton.ButtonPressed;

    public enum GlyphKind { Microphone, Lock }

    /// A button that draws its own icon (microphone or padlock) and, for the talk button, the loudness meter.
    public partial class GlyphButton : Button
    {
        private static readonly Color Gold = new("#F6C44C");
        private readonly GlyphKind _kind;
        private readonly VoiceChat _voice;

        public GlyphButton(GlyphKind kind, VoiceChat voice)
        {
            _kind = kind; _voice = voice;
            AddThemeFontOverride("font", UiStyle.Bold);
            AddThemeFontSizeOverride("font_size", 16);
            Style();
        }

        /// Two halves of one pill: rounded on the outer side only, sharing the teal outline.
        private void Style()
        {
            foreach (var (state, kind) in new[] { ("normal", UiStyle.ButtonState.Normal), ("hover", UiStyle.ButtonState.Hover), ("pressed", UiStyle.ButtonState.Pressed), ("hover_pressed", UiStyle.ButtonState.Pressed), ("disabled", UiStyle.ButtonState.Disabled) })
            {
                var box = UiStyle.ButtonBox(UiStyle.ButtonKind.Primary, kind, _kind == GlyphKind.Microphone ? 44 : 6, 6, 12);
                if (_kind == GlyphKind.Microphone) { box.CornerRadiusTopRight = box.CornerRadiusBottomRight = 0; box.BorderWidthRight = 1; }
                else { box.CornerRadiusTopLeft = box.CornerRadiusBottomLeft = 0; box.BorderWidthLeft = 1; }
                AddThemeStyleboxOverride(state, box);
            }
            AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            AddThemeColorOverride("font_color", Colors.White); AddThemeColorOverride("font_hover_color", Colors.White); AddThemeColorOverride("font_pressed_color", Colors.White);
            AddThemeColorOverride("font_hover_pressed_color", Colors.White); AddThemeColorOverride("font_disabled_color", UiStyle.TextDisabled);
        }

        public override void _Draw()
        {
            var size = Size;
            var live = _voice.Enabled && (_kind == GlyphKind.Microphone ? _voice.PushToTalk || _voice.OpenMic || _voice.Talking : _voice.OpenMic);
            var hot = live && VoiceMeter.IsClipping(_voice.LocalPeak);
            if (hot) DrawStyleBox(new StyleBoxFlat { BgColor = new Color(UiStyle.Danger, .12f), BorderColor = UiStyle.DangerBright, BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = _kind == GlyphKind.Microphone ? 2 : 0, BorderWidthRight = _kind == GlyphKind.Lock ? 2 : 0, CornerRadiusTopLeft = _kind == GlyphKind.Microphone ? 12 : 0, CornerRadiusBottomLeft = _kind == GlyphKind.Microphone ? 12 : 0, CornerRadiusTopRight = _kind == GlyphKind.Lock ? 12 : 0, CornerRadiusBottomRight = _kind == GlyphKind.Lock ? 12 : 0 }, new Rect2(Vector2.Zero, size));
            else if (live) DrawStyleBox(new StyleBoxFlat { BgColor = new Color(UiStyle.Accent, .14f), CornerRadiusTopLeft = _kind == GlyphKind.Microphone ? 12 : 0, CornerRadiusBottomLeft = _kind == GlyphKind.Microphone ? 12 : 0, CornerRadiusTopRight = _kind == GlyphKind.Lock ? 12 : 0, CornerRadiusBottomRight = _kind == GlyphKind.Lock ? 12 : 0 }, new Rect2(Vector2.Zero, size));
            var disabledColor = new Color(UiStyle.TextDisabled);
            if (_kind == GlyphKind.Microphone)
            {
                var color = !_voice.Enabled ? disabledColor : hot ? UiStyle.DangerBright : Colors.White;
                DrawIcon(!_voice.Enabled ? "mic-off" : "mic", color, new Vector2(24, size.Y / 2 - 1));
                if (live) DrawMeter(size);
            }
            else
            {
                var color = !_voice.Enabled ? disabledColor : Gold;
                DrawIcon(_voice.OpenMic ? "lock" : "unlock", color, new Vector2(size.X / 2, size.Y / 2));
            }
        }

        private void DrawIcon(string name, Color color, Vector2 center)
        {
            const int px = 22;
            DrawTextureRect(UiStyle.Icon(name, color, px), new Rect2(center - new Vector2(px / 2f, px / 2f), new Vector2(px, px)), false);
        }

        /// Fills left to right with the loudness; the whole bar goes red when the peak is too loud.
        private void DrawMeter(Vector2 size)
        {
            var width = size.X - 40f; var hot = VoiceMeter.IsClipping(_voice.LocalPeak);
            DrawRect(new Rect2(16, size.Y - 9, width, 3), new Color(0, 0, 0, .4f));
            DrawRect(new Rect2(16, size.Y - 9, width * Mathf.Clamp(_voice.LocalLevel * 1.6f, 0f, 1f), 3), hot ? UiStyle.DangerBright : UiStyle.SuccessBright);
        }
    }
}
