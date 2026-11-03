using Godot;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// The voice control in the bottom bar, like Second Life's: hold the "Falar" button to talk while the mouse is down,
/// or click the padlock to leave the microphone open without holding anything. A thin meter under the button shows how
/// loud you are: green when the sound is clean, red when it is too loud.
public partial class VoiceBar : HBoxContainer
{
    private static readonly Color Green = new(.3f, .95f, .4f);
    private static readonly Color Red = new(1f, .2f, .17f);
    private VoiceChat _voice = null!;

    public GlyphButton TalkButton { get; private set; } = null!;
    public GlyphButton LockButton { get; private set; } = null!;

    public void Bind(VoiceChat voice)
    {
        _voice = voice;
        AddThemeConstantOverride("separation", 2);
        TalkButton = new GlyphButton(GlyphKind.Microphone, _voice) { Text = "Falar", CustomMinimumSize = new Vector2(96, 36), TooltipText = "Segure o botão do mouse para falar. Use fones de ouvido para evitar eco.", FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Right };
        TalkButton.ButtonDown += () => { if (_voice.Enabled) _voice.PushToTalk = true; };
        TalkButton.ButtonUp += () => _voice.PushToTalk = false;
        LockButton = new GlyphButton(GlyphKind.Lock, _voice) { ToggleMode = true, CustomMinimumSize = new Vector2(36, 36), TooltipText = "Cadeado: deixa o microfone aberto sem precisar segurar o botão.", FocusMode = FocusModeEnum.None };
        LockButton.Toggled += on => { if (_voice.Enabled) _voice.OpenMic = on; else LockButton.SetPressedNoSignal(false); };
        AddChild(TalkButton); AddChild(LockButton);
        _voice.SettingsChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree() { if (_voice is not null && IsInstanceValid(_voice)) _voice.SettingsChanged -= Refresh; }

    /// Keeps the padlock in step when the setting changes elsewhere (the menu) and greys everything out when voice is off.
    public void Refresh()
    {
        LockButton.SetPressedNoSignal(_voice.OpenMic);
        TalkButton.Disabled = LockButton.Disabled = !_voice.Enabled;
        TalkButton.TooltipText = _voice.Enabled ? "Segure o botão do mouse para falar. Use fones de ouvido para evitar eco." : "A voz está desligada (menu Comunicar).";
        TalkButton.QueueRedraw(); LockButton.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_voice.LocalLevel > .001f || _voice.Talking) TalkButton.QueueRedraw();
    }

    // For tests: the same effect as pressing and releasing the mouse on the buttons.
    public void PressTalk(bool down) { if (down) TalkButton.EmitSignal(BaseButton.SignalName.ButtonDown); else TalkButton.EmitSignal(BaseButton.SignalName.ButtonUp); }
    public void ClickLock() => LockButton.ButtonPressed = !LockButton.ButtonPressed;

    public enum GlyphKind { Microphone, Lock }

    /// A button that draws its own icon (microphone or padlock) and, for the talk button, the loudness meter.
    public partial class GlyphButton : Button
    {
        private readonly GlyphKind _kind;
        private readonly VoiceChat _voice;

        public GlyphButton(GlyphKind kind, VoiceChat voice) { _kind = kind; _voice = voice; }

        public override void _Draw()
        {
            var size = Size; var live = _voice.Enabled && (_kind == GlyphKind.Microphone ? _voice.PushToTalk || _voice.OpenMic || _voice.Talking : _voice.OpenMic);
            var hot = _kind == GlyphKind.Microphone && live && VoiceMeter.IsClipping(_voice.LocalPeak);
            var color = !_voice.Enabled ? new Color(.5f, .5f, .52f) : hot ? Red : live ? Green : new Color(.88f, .9f, .94f);
            if (live) DrawStyleBox(new StyleBoxFlat { BgColor = new Color(color, .13f), BorderColor = new Color(color, .8f), BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4 }, new Rect2(Vector2.Zero, size));
            var center = _kind == GlyphKind.Microphone ? new Vector2(20, size.Y / 2) : new Vector2(size.X / 2, size.Y / 2);
            if (_kind == GlyphKind.Microphone) DrawMicrophone(center, color); else DrawLock(center, color, _voice.OpenMic);
            if (!_voice.Enabled) DrawLine(center + new Vector2(-9, 9), center + new Vector2(9, -9), new Color(1f, .35f, .3f), 2f);
            if (_kind == GlyphKind.Microphone && live) DrawMeter(size);
        }

        private void DrawMicrophone(Vector2 c, Color color)
        {
            DrawStyleBox(new StyleBoxFlat { BgColor = color, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4 }, new Rect2(c.X - 4, c.Y - 10, 8, 13));
            DrawArc(new Vector2(c.X, c.Y - 3), 7f, 0f, Mathf.Pi, 14, color, 1.8f);
            DrawLine(new Vector2(c.X, c.Y + 4), new Vector2(c.X, c.Y + 9), color, 1.8f);
            DrawLine(new Vector2(c.X - 5, c.Y + 9), new Vector2(c.X + 5, c.Y + 9), color, 1.8f);
        }

        private void DrawLock(Vector2 c, Color color, bool locked)
        {
            DrawStyleBox(new StyleBoxFlat { BgColor = color, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2 }, new Rect2(c.X - 7, c.Y - 1, 14, 10));
            if (locked) { DrawArc(new Vector2(c.X, c.Y - 1), 5f, Mathf.Pi, Mathf.Tau, 12, color, 2f); }
            else
            {
                DrawArc(new Vector2(c.X, c.Y - 6), 5f, Mathf.Pi, Mathf.Tau, 12, color, 2f);
                DrawLine(new Vector2(c.X + 5, c.Y - 6), new Vector2(c.X + 5, c.Y - 1), color, 2f);
            }
        }

        /// Fills left to right with the loudness; the whole bar goes red when the peak is too loud.
        private void DrawMeter(Vector2 size)
        {
            var width = size.X - 10f; var hot = VoiceMeter.IsClipping(_voice.LocalPeak);
            DrawRect(new Rect2(5, size.Y - 7, width, 3), new Color(0, 0, 0, .35f));
            DrawRect(new Rect2(5, size.Y - 7, width * Mathf.Clamp(_voice.LocalLevel * 1.6f, 0f, 1f), 3), hot ? Red : Green);
        }
    }
}
