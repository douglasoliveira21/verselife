using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.LoginUi;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The login screen against a tiny pretend API that the real AuthManager talks to over HTTP: the three layers, the fields and their checks, the eye, Enter, one request at a time, wrong credentials, success with "lembrar de mim",
/// registration, the recovery notice, the status of the servers, the window controls and the layout at several window sizes.
/// </summary>
public partial class LoginSmoke : Control
{
    private TcpServer _tcp = new();
    private readonly List<(string Method, string Path, string Body)> _requests = [];
    private readonly List<StreamPeerTcp> _open = [];
    private bool _healthy = true, _slow;
    private double _slowUntil;
    private string _loginAnswer = "ok";

    public override void _Process(double delta)
    {
        while (_tcp.IsConnectionAvailable()) _open.Add(_tcp.TakeConnection());
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var peer = _open[i]; peer.Poll(); if (peer.GetStatus() != StreamPeerTcp.Status.Connected) { _open.RemoveAt(i); continue; }
            var available = peer.GetAvailableBytes(); if (available <= 0) continue;
            var raw = Encoding.UTF8.GetString(peer.GetData(available)[1].AsByteArray()); var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal); if (split < 0) continue;
            var head = raw[..split].Split("\r\n"); var line = head[0].Split(' '); var body = raw[(split + 4)..];
            var length = head.Select(h => h.Split(':')).Where(p => p[0].Equals("content-length", StringComparison.OrdinalIgnoreCase)).Select(p => int.Parse(p[1].Trim())).FirstOrDefault();
            if (Encoding.UTF8.GetByteCount(body) < length) continue; // the rest of the body is still coming
            _requests.Add((line[0], line[1], body)); var (code, json) = Answer(line[0], line[1], body);
            var payload = Encoding.UTF8.GetBytes(json); var response = $"HTTP/1.1 {code} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            peer.PutData(Encoding.UTF8.GetBytes(response)); peer.PutData(payload); peer.DisconnectFromHost(); _open.RemoveAt(i);
        }
    }

    private (int, string) Answer(string method, string path, string body)
    {
        if (path == "/health") return _healthy ? (200, "Healthy") : (503, "Unhealthy");
        if (path == "/api/v1/auth/login")
        {
            var login = JsonDocument.Parse(body).RootElement.GetProperty("login").GetString();
            return _loginAnswer == "ok" && login == "ana" ? (200, Auth("ana")) : (401, "{\"title\":\"Invalid credentials.\",\"status\":401}");
        }
        if (path == "/api/v1/auth/register")
        {
            var name = JsonDocument.Parse(body).RootElement.GetProperty("username").GetString();
            return name == "ocupado" ? (409, "{\"title\":\"Username or email is already registered.\",\"status\":409}") : (201, Auth(name!));
        }
        return (404, "{}");
    }

    private static string Auth(string user) => "{\"accessToken\":\"access-" + user + "\",\"refreshToken\":\"refresh-" + user + "\",\"user\":{\"username\":\"" + user + "\"}}";

    public override async void _Ready()
    {
        try
        {
            SavedLogin.Clear(); var port = 18800 + (int)(OS.GetProcessId() % 150); Require(_tcp.Listen((ushort)port, "127.0.0.1") == Error.Ok, "the pretend API must listen");
            var app = GetNode<AppManager>("/root/AppManager"); typeof(AppManager).GetProperty("ApiBaseUrl")!.SetValue(app, $"http://127.0.0.1:{port}");
            var login = GD.Load<PackedScene>("res://Scenes/Login.tscn").Instantiate<LoginScreen>(); var entered = new List<bool>(); login.EnterOverride = isNew => entered.Add(isNew); AddChild(login); await Frames(10);

            // ---- the three layers ----
            Require(login.Background.HasPicture && ResourceLoader.Exists(LoginBackground.ImagePath) && LoginBackground.ImagePath.EndsWith("login_background.png", StringComparison.Ordinal), "the background is a picture in its own file");
            Require(login.Background.Picture.StretchMode == TextureRect.StretchModeEnum.KeepAspectCovered, "the picture covers the window keeping its proportions");
            Require(login.Logo.FindChild("Logo", true, false) is TextureRect { Texture: not null } && LoginLogo.LogoPath == "res://Assets/brand/logo.png" && login.Logo.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).SequenceEqual([LoginLogo.Subtitle]), "the logo is the one the game already had, with the subtitle");
            Require(login.Panel.Size.X >= LoginPanel.Width - 1 && login.Panel.Size.X <= LoginPanel.Width + 1, $"the card is {LoginPanel.Width} px wide (got {login.Panel.Size.X})");
            var texts = login.Panel.Form.FindChildren("*", "Button", true, false).OfType<Button>().Select(b => b.Text.Trim()).ToList(); Require(texts.Contains("Esqueceu sua senha?") && texts.Contains("Entrar no LifeVerse  →"), "the form has the recovery link and the button");
            Require(login.Panel.Form.User.Edit.PlaceholderText == "Nome de usuário" && login.Panel.Form.Password.Edit.PlaceholderText == "Sua senha" && login.Panel.Form.Password.Edit.Secret, "placeholders and a masked password");
            Require(login.FindChildren("*", "Button", true, false).OfType<Button>().All(b => !b.Text.Contains("Google") && !b.Text.Contains("Discord") && !b.Text.Contains("Steam") && !b.Text.Contains("Apple")), "no linked accounts");
            Require(login.VersionLabel.Text == $"Versão {AppManager.Version}  •  Build dev", $"version and build (got {login.VersionLabel.Text})");
            Require(new[] { login.Controls.Settings, login.Controls.Minimize, login.Controls.Close }.All(b => b.Visible), "settings, minimize and close");

            // ---- music and light ----
            Require(login.Music.Looping && login.Music.Playing == !LoginMusic.Muted && LoginMusic.TrackPath.EndsWith("login_theme.mp3", StringComparison.Ordinal), "the theme plays in a loop");
            Require(login.Lights.BeamCount >= 4 && login.Lights.MoteCount > 10 && login.Lights.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Add } && login.Lights.MouseFilter == MouseFilterEnum.Ignore, "light beams and sparks, drawn additively, that never catch the mouse");
            var angle = login.Lights.FirstBeamAngle; await Frames(60); Require(Mathf.Abs(login.Lights.FirstBeamAngle - angle) > .0005f, "the beams move");
            var wasMuted = LoginMusic.Muted; login.Controls.Sound.EmitSignal(BaseButton.SignalName.Pressed); Require(LoginMusic.Muted != wasMuted, "the speaker button mutes and unmutes"); login.Controls.Sound.EmitSignal(BaseButton.SignalName.Pressed); Require(LoginMusic.Muted == wasMuted, "and back");

            // ---- status of the servers ----
            await Wait(login.Status, ServerStatus.State.Online); Require(login.Status.TextNow == "Servidores Online", $"healthy servers (got {login.Status.TextNow})");
            _healthy = false; login.Status.Start($"http://127.0.0.1:{port}"); await Wait(login.Status, ServerStatus.State.Offline); Require(login.Status.TextNow == "Servidores Indisponíveis", "unhealthy servers"); _healthy = true;

            // ---- checks before anything is sent ----
            var form = login.Panel.Form; var before = _requests.Count(r => r.Path.Contains("/auth/", StringComparison.Ordinal));
            form.User.Text = ""; form.Password.Text = ""; form.Enter.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(form.Message.Visible && form.Message.Text.Contains("nome de usuário e a senha") && form.User.Invalid && form.Password.Invalid, "empty fields are refused with a message");
            form.User.Text = "ana"; form.User.Edit.EmitSignal(LineEdit.SignalName.TextChanged, "ana"); form.Enter.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(form.Message.Text == "Digite a senha." && !form.User.Invalid && form.Password.Invalid, "a missing password is named");
            Require(_requests.Count(r => r.Path.Contains("/auth/", StringComparison.Ordinal)) == before, "nothing was sent for invalid fields");
            form.Password.Eye!.EmitSignal(BaseButton.SignalName.Pressed); Require(form.Password.Revealed, "the eye shows the password"); form.Password.Eye.EmitSignal(BaseButton.SignalName.Pressed); Require(!form.Password.Revealed, "and hides it again");

            // ---- wrong credentials, one request at a time ----
            _loginAnswer = "bad"; form.Password.Text = "errada"; form.Password.Edit.EmitSignal(LineEdit.SignalName.TextSubmitted, "errada"); await Frames(1);
            Require(form.Enter.Busy && form.Enter.Text.Contains("Entrando") && !form.User.Edit.Editable, "Enter sends and the button shows it is working");
            form.Submit(); form.Submit(); await Until(() => !form.Enter.Busy); await Frames(2);
            Require(_requests.Count(r => r.Path == "/api/v1/auth/login") == 1, $"clicking again while waiting sends nothing more (sent {_requests.Count(r => r.Path == "/api/v1/auth/login")})");
            Require(form.Message.Text == "Usuário ou senha incorretos." && form.User.Edit.Editable && entered.Count == 0 && !GetNode<SessionManager>("/root/SessionManager").IsAuthenticated, $"wrong credentials are explained and nothing starts (got {form.Message.Text})");

            // ---- a good login with "Lembrar de mim" ----
            _loginAnswer = "ok"; form.Password.Text = "SenhaCerta123"; form.Remember.ButtonPressed = true; form.Submit(); await Until(() => entered.Count > 0);
            Require(entered.SequenceEqual([false]) && GetNode<SessionManager>("/root/SessionManager").IsAuthenticated && GetNode<SessionManager>("/root/SessionManager").AccessToken == "access-ana", "the session comes from the API and the game goes on");
            Require(SavedLogin.Load() is { Login: "ana" } && _requests.First(r => r.Path == "/api/v1/auth/login").Body.Contains("\"login\":\"ana\"", StringComparison.Ordinal), "remember keeps the login; the API got a login, not an e-mail");
            Require(!File.Exists(ProjectSettings.GlobalizePath("user://login.dat")) || !File.ReadAllText(ProjectSettings.GlobalizePath("user://login.dat")).Contains("SenhaCerta123", StringComparison.Ordinal), "the password is never in the file in plain text");
            SavedLogin.Clear(); login.Panel.SetBusy(false);

            // ---- registration ----
            login.Panel.RegisterTab.EmitSignal(BaseButton.SignalName.Pressed); await Frames(15);
            var register = login.Panel.Register; Require(login.Panel.Mode == LoginMode.Register && register.Visible && !form.Visible, "Criar Conta opens the registration");
            register.User.Text = "bia"; register.Email.Text = "bia"; register.Password.Text = "fraca"; register.Confirm.Text = "fraca"; register.Submit(); await Frames(2);
            Require(register.Message.Text.Contains("e-mail válido") && register.Email.Invalid, "a bad e-mail is refused"); register.Email.Text = "bia@exemplo.com"; register.Submit();
            Require(register.Message.Text.Contains("12 a 128"), "a weak password is refused with the rule"); register.Password.Text = "SenhaForte1234"; register.Confirm.Text = "outra"; register.Submit();
            Require(register.Message.Text == "As senhas não são iguais.", "different passwords are refused");
            Require(!_requests.Any(r => r.Path == "/api/v1/auth/register"), "nothing was sent yet");
            register.User.Text = "ocupado"; register.Confirm.Text = "SenhaForte1234"; register.Submit(); await Until(() => !register.Create.Busy); await Frames(2);
            Require(register.Message.Text == "Esse nome de usuário ou e-mail já está em uso." && entered.Count == 1, $"a name that is taken is explained (got {register.Message.Text})");
            register.User.Text = "bia"; register.Submit(); await Until(() => entered.Count > 1);
            Require(entered[1] && _requests.Last(r => r.Path == "/api/v1/auth/register").Body.Contains("bia@exemplo.com", StringComparison.Ordinal), "a new account goes on to create the character");
            login.Panel.SetBusy(false);

            // ---- recovery ----
            login.Panel.LoginTab.EmitSignal(BaseButton.SignalName.Pressed); await Frames(15); Require(login.Panel.Mode == LoginMode.Login && form.Visible, "Entrar goes back to the form");
            form.Forgot.EmitSignal(BaseButton.SignalName.Pressed); await Frames(15);
            Require(login.Panel.Mode == LoginMode.Recovery && login.Panel.Recovery.Visible && login.Panel.Recovery.Explanation.Text.Contains("ainda não está disponível") && !PasswordRecoveryPanel.Available, "the recovery says the truth about what the server offers");
            login.Panel.Recovery.Back.EmitSignal(BaseButton.SignalName.Pressed); await Frames(10); Require(login.Panel.Mode == LoginMode.Login, "and goes back");

            // ---- settings dialog ----
            login.Controls.OpenSettings(); await Frames(6); var dialog = login.Controls.Dialog!; Require(dialog.FindChild("Fullscreen", true, false) is CheckBox && dialog.FindChild("Resolution", true, false) is OptionButton && dialog.FindChild("Volume", true, false) is HSlider, "settings: full screen, resolution, volume");
            dialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);

            // ---- layout at several window sizes ----
            foreach (var (w, h) in new[] { (1366, 768), (1920, 1080), (2560, 1440), (3840, 2160), (1280, 1024), (1000, 1000) })
            {
                GetWindow().Size = new Vector2I(w, h); await Frames(6); var view = login.GetViewportRect(); var panel = login.Panel.GetGlobalRect(); var logo = login.Logo.GetGlobalRect();
                Require(view.Encloses(panel) && view.Encloses(logo) && logo.End.Y <= panel.Position.Y + 4 && panel.End.Y <= view.End.Y - 58, $"{w}x{h}: logo and card fit, above the bottom bar (view {view}, card {panel}, logo {logo})");
            }

            GD.Print("event=login_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=login_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Wait(ServerStatus status, ServerStatus.State state) => await Until(() => status.Current == state);
    private async Task Until(Func<bool> done) { for (var i = 0; i < 600 && !done(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Require(done(), "timed out waiting"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
