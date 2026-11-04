using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The login screen in the game's glass style: user and password are all that is needed, with an option to remember them.
/// "Criar conta" reveals the email field that only registration needs.
public partial class LoginScreen : Control
{
    private LineEdit _login = null!;
    private LineEdit _email = null!;
    private LineEdit _password = null!;
    private Label _message = null!;
    private Button _loginButton = null!;
    private Button _registerButton = null!;
    private CheckBox _remember = null!;
    private AuthManager _auth = null!;
    private UpdateChecker? _updates;
    private Button? _download;
    private bool _registerMode;
    private bool UpdateRequired => _updates?.Status == LifeVerse.Shared.Updates.UpdateStatus.Required;

    public override void _Ready()
    {
        Theme = ViewerTheme.Create();
        _login = GetNode<LineEdit>("Panel/Margin/Content/Login");
        _email = GetNode<LineEdit>("Panel/Margin/Content/Email");
        _password = GetNode<LineEdit>("Panel/Margin/Content/Password");
        _message = GetNode<Label>("Panel/Margin/Content/Message");
        _loginButton = GetNode<Button>("Panel/Margin/Content/Buttons/LoginButton");
        _registerButton = GetNode<Button>("Panel/Margin/Content/Buttons/RegisterButton");
        GetNode<Label>("Panel/Margin/Content/Version").Text = $"Versão {AppManager.Version}";
        Style();
        _auth = GetNode<AuthManager>("/root/AuthManager");
        _auth.RequestFinished += OnRequestFinished;
        _loginButton.Pressed += Login;
        _registerButton.Pressed += Register;
        _login.TextSubmitted += _ => _password.GrabFocus();
        _password.TextSubmitted += _ => { if (_registerMode) Register(); else Login(); };
        _email.TextSubmitted += _ => Register();
        _updates = GetNode<AppManager>("/root/AppManager").Updates;
        _updates.Checked += ShowUpdate; ShowUpdate();
        if (SavedLogin.Load() is { } saved) { _login.Text = saved.Login; _password.Text = saved.Password; _remember.ButtonPressed = true; _loginButton.GrabFocus(); }
        else _login.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_auth)) _auth.RequestFinished -= OnRequestFinished;
        if (_updates is not null && IsInstanceValid(_updates)) _updates.Checked -= ShowUpdate;
    }

    /// Glass card, neon title, flat fields with icons' worth of spacing, and the remember-me switch.
    private void Style()
    {
        GetNode<Panel>("Background").AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("#0B1118") });
        var card = GetNode<PanelContainer>("Panel");
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .94f), UiStyle.BorderSoft, UiStyle.RadiusLarge, 1, 0, 0, shadow: 24));
        card.OffsetLeft = -230; card.OffsetRight = 230; card.OffsetTop = -330; card.OffsetBottom = 330;
        var content = GetNode<VBoxContainer>("Panel/Margin/Content");
        content.AddThemeConstantOverride("separation", 14);

        var title = GetNode<Label>("Panel/Margin/Content/Title");
        var logo = UiStyle.Brand(true, 170); logo.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; content.AddChild(logo); content.MoveChild(logo, title.GetIndex()); title.Visible = false;
        title.Text = "LIFEVERSE"; title.AddThemeFontOverride("font", UiStyle.Bold); title.AddThemeFontSizeOverride("font_size", 38); title.AddThemeColorOverride("font_color", UiStyle.Accent);
        var subtitle = GetNode<Label>("Panel/Margin/Content/Subtitle");
        subtitle.Text = "Entre no seu mundo"; subtitle.AddThemeColorOverride("font_color", UiStyle.TextMuted); subtitle.AddThemeFontSizeOverride("font_size", 18);

        _login.PlaceholderText = "Nome de usuário"; _login.CustomMinimumSize = new Vector2(0, 50); _login.AddThemeFontSizeOverride("font_size", 19);
        _password.PlaceholderText = "Senha"; _password.CustomMinimumSize = new Vector2(0, 50); _password.AddThemeFontSizeOverride("font_size", 19);
        _email.PlaceholderText = "E-mail (só para criar a conta)"; _email.CustomMinimumSize = new Vector2(0, 50); _email.AddThemeFontSizeOverride("font_size", 19); _email.Visible = false;
        content.MoveChild(_email, _password.GetIndex());

        _remember = new CheckBox { Text = " Lembrar usuário e senha", TooltipText = "Guarda o login neste computador (criptografado) para entrar direto da próxima vez." };
        _remember.AddThemeFontSizeOverride("font_size", 17);
        content.AddChild(_remember); content.MoveChild(_remember, _password.GetIndex() + 1);

        _loginButton.Text = "Entrar"; _loginButton.CustomMinimumSize = new Vector2(0, 52); UiStyle.StyleButton(_loginButton, UiStyle.ButtonKind.Primary, 12, 8); _loginButton.AddThemeFontSizeOverride("font_size", 20);
        _registerButton.Text = "Criar conta"; _registerButton.CustomMinimumSize = new Vector2(0, 52); UiStyle.StyleButton(_registerButton, UiStyle.ButtonKind.Secondary, 12, 8); _registerButton.AddThemeFontSizeOverride("font_size", 20);
        var recovery = GetNode<Button>("Panel/Margin/Content/Recovery");
        recovery.Text = "Recuperar senha — indisponível"; recovery.AddThemeFontSizeOverride("font_size", 15);
        recovery.TooltipText = "Indisponível até que um provedor de e-mail seja configurado.";
        _message.AddThemeColorOverride("font_color", UiStyle.Orange); _message.AddThemeFontSizeOverride("font_size", 16);
        GetNode<Label>("Panel/Margin/Content/Version").AddThemeColorOverride("font_color", UiStyle.TextDisabled);
    }

    private void Login()
    {
        if (string.IsNullOrWhiteSpace(_login.Text) || _password.Text.Length == 0) { _message.Text = "Digite o usuário e a senha."; return; }
        SetBusy(true);
        var error = _auth.Login(_login.Text.Trim(), _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, false, $"Não foi possível iniciar o pedido: {error}");
    }

    private void Register()
    {
        if (!_registerMode)
        {
            _registerMode = true; _email.Visible = true; _email.GrabFocus();
            _registerButton.Text = "Confirmar cadastro"; _loginButton.Text = "Voltar";
            _loginButton.Pressed -= Login; _loginButton.Pressed += LeaveRegister;
            _message.Text = "Informe o e-mail e confirme para criar a conta.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_email.Text)) { _message.Text = "O e-mail é obrigatório para criar a conta."; return; }
        if (string.IsNullOrWhiteSpace(_login.Text) || _password.Text.Length == 0) { _message.Text = "Digite o usuário e a senha."; return; }
        SetBusy(true);
        var error = _auth.Register(_login.Text.Trim(), _email.Text.Trim(), _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, true, $"Não foi possível iniciar o pedido: {error}");
    }

    private void LeaveRegister()
    {
        _registerMode = false; _email.Visible = false;
        _registerButton.Text = "Criar conta"; _loginButton.Text = "Entrar";
        _loginButton.Pressed -= LeaveRegister; _loginButton.Pressed += Login;
        _message.Text = string.Empty;
    }

    private void OnRequestFinished(bool success, bool newAccount, string message)
    {
        SetBusy(false);
        _message.Text = success ? string.Empty : message;
        if (!success) return;
        if (_remember.ButtonPressed) SavedLogin.Save(_login.Text.Trim(), _password.Text); else SavedLogin.Clear();
        var ui = GetNode<UIManager>("/root/UIManager");
        if (newAccount) ui.ShowCharacterCreator();
        else ui.ShowWorld();
    }

    private void SetBusy(bool busy)
    {
        _loginButton.Disabled = busy || (UpdateRequired && !_registerMode);
        _registerButton.Disabled = busy || UpdateRequired;
        if (busy) _message.Text = "Conectando…";
        else if (UpdateRequired) _message.Text = _updates!.Describe();
    }

    /// A newer client is only mentioned; one below the minimum blocks login until it is updated.
    private void ShowUpdate()
    {
        if (_updates is null || _updates.Status is not (LifeVerse.Shared.Updates.UpdateStatus.Available or LifeVerse.Shared.Updates.UpdateStatus.Required)) return;
        _message.Text = _updates.Describe();
        _loginButton.Disabled = _registerButton.Disabled = UpdateRequired;
        if (_download is not null || _updates.Release is not { HasSafeDownloadLink: true }) return;
        _download = new Button { Text = "Baixar atualização" };
        UiStyle.StyleButton(_download, UiStyle.ButtonKind.Primary);
        _download.Pressed += _updates.OpenDownload;
        _message.GetParent().AddChild(_download);
    }
}
