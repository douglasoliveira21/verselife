using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>The "Entrar" form: user name, password (with the eye), "Lembrar de mim", "Esqueceu sua senha?" and the big button. It only collects and checks the fields; the screen does the signing in.</summary>
public partial class LoginForm : VBoxContainer
{
    public IconField User { get; } = new("user", "Nome de usuário");
    public IconField Password { get; } = new("lock", "Sua senha", secret: true);
    public CheckBox Remember { get; } = new() { Text = "  Lembrar de mim", FocusMode = FocusModeEnum.None, TooltipText = "Guarda o usuário e a senha neste computador, criptografados, para entrar direto da próxima vez." };
    public Button Forgot { get; } = new() { Text = "Esqueceu sua senha?", Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    public PrimaryButton Enter { get; } = new("Entrar no LifeVerse  →", "Entrando…");
    public Label Message { get; } = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(300, 0), Visible = false };

    /// <summary>Valid fields: user name and password.</summary>
    public event Action<string, string, bool>? Submitted;
    public event Action? ForgotRequested;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 16);
        AddChild(User); AddChild(Password);
        var options = new HBoxContainer(); options.AddThemeConstantOverride("separation", 8); AddChild(options);
        Remember.AddThemeFontSizeOverride("font_size", 16); Remember.AddThemeColorOverride("font_color", UiStyle.TextMuted); Remember.AddThemeColorOverride("font_hover_color", UiStyle.Text); options.AddChild(Remember);
        options.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        Forgot.AddThemeFontSizeOverride("font_size", 16); Forgot.AddThemeColorOverride("font_color", UiStyle.Accent); Forgot.AddThemeColorOverride("font_hover_color", Colors.White); Forgot.AddThemeColorOverride("font_pressed_color", Colors.White);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) Forgot.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        Forgot.Pressed += () => ForgotRequested?.Invoke(); options.AddChild(Forgot);
        Message.AddThemeFontSizeOverride("font_size", 16); AddChild(Message);
        AddChild(Enter);
        Enter.Pressed += Submit; User.Submitted += _ => Password.Focus(); Password.Submitted += _ => Submit();
    }

    public void Submit()
    {
        if (Enter.Busy) return; // one request at a time
        var login = User.Text.Trim();
        User.Invalid = login.Length == 0; Password.Invalid = Password.Text.Length == 0;
        if (User.Invalid && Password.Invalid) { Say("Digite o nome de usuário e a senha.", true); User.Focus(); return; }
        if (User.Invalid) { Say("Digite o nome de usuário.", true); User.Focus(); return; }
        if (Password.Invalid) { Say("Digite a senha.", true); Password.Focus(); return; }
        Say("", false); Submitted?.Invoke(login, Password.Text, Remember.ButtonPressed);
    }

    public void SetBusy(bool busy) { Enter.Busy = busy; User.Edit.Editable = !busy; Password.Edit.Editable = !busy; }

    /// <summary>A line under the options: red for errors, muted for information. An empty text hides it.</summary>
    public void Say(string text, bool error)
    {
        Message.Text = text; Message.Visible = text.Length > 0; Message.AddThemeColorOverride("font_color", error ? new Color("#FF7A7A") : UiStyle.TextMuted);
    }
}
