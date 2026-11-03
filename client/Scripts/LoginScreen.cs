using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class LoginScreen : Control
{
    private LineEdit _login = null!;
    private LineEdit _email = null!;
    private LineEdit _password = null!;
    private Label _message = null!;
    private Button _loginButton = null!;
    private Button _registerButton = null!;
    private AuthManager _auth = null!;
    private UpdateChecker? _updates;
    private Button? _download;
    private bool UpdateRequired => _updates?.Status == LifeVerse.Shared.Updates.UpdateStatus.Required;

    public override void _Ready()
    {
        _login = GetNode<LineEdit>("Panel/Margin/Content/Login");
        _email = GetNode<LineEdit>("Panel/Margin/Content/Email");
        _password = GetNode<LineEdit>("Panel/Margin/Content/Password");
        _message = GetNode<Label>("Panel/Margin/Content/Message");
        _loginButton = GetNode<Button>("Panel/Margin/Content/Buttons/LoginButton");
        _registerButton = GetNode<Button>("Panel/Margin/Content/Buttons/RegisterButton");
        GetNode<Label>("Panel/Margin/Content/Version").Text = $"Version {AppManager.Version}";
        _auth = GetNode<AuthManager>("/root/AuthManager");
        _auth.RequestFinished += OnRequestFinished;
        _loginButton.Pressed += Login;
        _registerButton.Pressed += Register;
        _updates = GetNode<AppManager>("/root/AppManager").Updates;
        _updates.Checked += ShowUpdate; ShowUpdate();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_auth)) _auth.RequestFinished -= OnRequestFinished;
        if (_updates is not null && IsInstanceValid(_updates)) _updates.Checked -= ShowUpdate;
    }

    private void Login()
    {
        SetBusy(true);
        var error = _auth.Login(_login.Text, _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, false, $"Could not start request: {error}");
    }

    private void Register()
    {
        if (string.IsNullOrWhiteSpace(_email.Text)) { _message.Text = "Email is required for registration."; return; }
        SetBusy(true);
        var error = _auth.Register(_login.Text, _email.Text, _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, true, $"Could not start request: {error}");
    }

    private void OnRequestFinished(bool success, bool newAccount, string message)
    {
        SetBusy(false);
        _message.Text = message;
        if (success)
        {
            var ui = GetNode<UIManager>("/root/UIManager");
            if (newAccount) ui.ShowCharacterCreator();
            else ui.ShowWorld();
        }
    }

    private void SetBusy(bool busy)
    {
        _loginButton.Disabled = busy || UpdateRequired;
        _registerButton.Disabled = busy || UpdateRequired;
        _message.Text = busy ? "Connecting…" : UpdateRequired ? _updates!.Describe() : string.Empty;
    }

    /// A newer client is only mentioned; one below the minimum blocks login until it is updated.
    private void ShowUpdate()
    {
        if (_updates is null || _updates.Status is not (LifeVerse.Shared.Updates.UpdateStatus.Available or LifeVerse.Shared.Updates.UpdateStatus.Required)) return;
        _message.Text = _updates.Describe();
        _loginButton.Disabled = _registerButton.Disabled = UpdateRequired;
        if (_download is not null || _updates.Release is not { HasSafeDownloadLink: true }) return;
        _download = new Button { Text = "Baixar atualização" };
        _download.Pressed += _updates.OpenDownload;
        _message.GetParent().AddChild(_download);
    }
}
