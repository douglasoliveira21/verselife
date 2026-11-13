using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>The "Criar Conta" form. The server needs an e-mail only to create the account (it is never used to sign in); the password rules are the server's (12 to 128 characters with upper case, lower case and a digit).</summary>
public partial class RegisterPanel : VBoxContainer
{
    public IconField User { get; } = new("user", "Escolha um nome de usuário");
    public IconField Email { get; } = new("mail", "Seu e-mail (só para criar a conta)");
    public IconField Password { get; } = new("lock", "Crie uma senha", secret: true);
    public IconField Confirm { get; } = new("lock", "Repita a senha", secret: true);
    public PrimaryButton Create { get; } = new("Criar conta  →", "Criando…");
    public Label Message { get; } = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(300, 0), Visible = false };

    /// <summary>Valid fields: user name, e-mail, password.</summary>
    public event Action<string, string, string>? Submitted;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 14);
        AddChild(User); AddChild(Email); AddChild(Password); AddChild(Confirm);
        var rules = new Label { Text = "A senha precisa ter de 12 a 128 caracteres, com letra maiúscula, minúscula e número.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; rules.AddThemeFontSizeOverride("font_size", 14); rules.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(rules);
        Message.AddThemeFontSizeOverride("font_size", 16); AddChild(Message); AddChild(Create);
        Create.Pressed += Submit; User.Submitted += _ => Email.Focus(); Email.Submitted += _ => Password.Focus(); Password.Submitted += _ => Confirm.Focus(); Confirm.Submitted += _ => Submit();
    }

    public static bool StrongPassword(string password) => password.Length is >= 12 and <= 128 && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
    public static bool LooksLikeEmail(string email) { var at = email.IndexOf('@'); return at > 0 && at < email.Length - 3 && email.IndexOf('.', at) > at + 1 && !email.Contains(' '); }

    public void Submit()
    {
        if (Create.Busy) return;
        var user = User.Text.Trim(); var email = Email.Text.Trim();
        User.Invalid = user.Length < 3; Email.Invalid = !LooksLikeEmail(email); Password.Invalid = !StrongPassword(Password.Text); Confirm.Invalid = Confirm.Text != Password.Text || Confirm.Text.Length == 0;
        if (User.Invalid) { Say("O nome de usuário precisa ter pelo menos 3 caracteres.", true); User.Focus(); return; }
        if (Email.Invalid) { Say("Informe um e-mail válido.", true); Email.Focus(); return; }
        if (Password.Invalid) { Say("A senha precisa ter de 12 a 128 caracteres, com maiúscula, minúscula e número.", true); Password.Focus(); return; }
        if (Confirm.Invalid) { Say("As senhas não são iguais.", true); Confirm.Focus(); return; }
        Say("", false); Submitted?.Invoke(user, email, Password.Text);
    }

    public void SetBusy(bool busy) { Create.Busy = busy; foreach (var field in new[] { User, Email, Password, Confirm }) field.Edit.Editable = !busy; }

    public void Say(string text, bool error) { Message.Text = text; Message.Visible = text.Length > 0; Message.AddThemeColorOverride("font_color", error ? new Color("#FF7A7A") : UiStyle.TextMuted); }
}
