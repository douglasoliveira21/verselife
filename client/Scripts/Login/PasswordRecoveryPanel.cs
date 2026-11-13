using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// "Esqueceu sua senha?". The server has no password reset yet (it needs an e-mail provider, which is not configured), so this panel says so plainly instead of pretending to send something.
/// When the API gets the endpoint, only <see cref="Available"/> and <see cref="Requested"/> need to be wired here.
/// </summary>
public partial class PasswordRecoveryPanel : VBoxContainer
{
    /// <summary>Whether the server can reset passwords. False until the API has the flow.</summary>
    public static bool Available => false;

    public Button Back { get; } = new() { Text = "←  Voltar para o login", Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    public Label Explanation { get; } = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 16);
        var icon = UiStyle.IconRect("lock", UiStyle.Accent, 40); icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; AddChild(icon);
        var title = new Label { Text = "Recuperação de senha", HorizontalAlignment = HorizontalAlignment.Center }; title.AddThemeFontOverride("font", UiStyle.Bold); title.AddThemeFontSizeOverride("font_size", 24); AddChild(title);
        Explanation.Text = "A recuperação de senha por e-mail ainda não está disponível neste servidor. Se você perdeu o acesso, fale com a administração do LifeVerse informando o seu nome de usuário.";
        Explanation.HorizontalAlignment = HorizontalAlignment.Center; Explanation.AddThemeFontSizeOverride("font_size", 17); Explanation.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(Explanation);
        Back.AddThemeFontSizeOverride("font_size", 17); Back.AddThemeColorOverride("font_color", UiStyle.Accent); Back.AddThemeColorOverride("font_hover_color", Colors.White);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) Back.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        Back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; AddChild(Back);
    }
}
