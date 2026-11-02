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
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_auth)) _auth.RequestFinished -= OnRequestFinished;
    }

    private void Login()
    {
        SetBusy(true);
        var error = _auth.Login(_login.Text, _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, $"Could not start request: {error}");
    }

    private void Register()
    {
        if (string.IsNullOrWhiteSpace(_email.Text)) { _message.Text = "Email is required for registration."; return; }
        SetBusy(true);
        var error = _auth.Register(_login.Text, _email.Text, _password.Text);
        if (error != Error.Ok) OnRequestFinished(false, $"Could not start request: {error}");
    }

    private void OnRequestFinished(bool success, string message)
    {
        SetBusy(false);
        _message.Text = message;
        if (success) GetNode<UIManager>("/root/UIManager").ShowWorld();
    }

    private void SetBusy(bool busy)
    {
        _loginButton.Disabled = busy;
        _registerButton.Disabled = busy;
        _message.Text = busy ? "Connecting…" : string.Empty;
    }
}
