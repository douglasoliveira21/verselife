using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Updates;

namespace LifeVerse.Client.Tests;

/// The login screen reacts to what the update check found: a newer version is announced, one below the minimum blocks
/// login, and a broken answer changes nothing. No network involved.
public partial class UpdateSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            SavedLogin.Clear();
            Require(SavedLogin.Load() is null, "no saved login at first");
            SavedLogin.Save("maria", "s3nha forte");
            Require(SavedLogin.Load() is { Login: "maria", Password: "s3nha forte" }, "the saved login must round-trip");
            SavedLogin.Clear();
            Require(!SavedLogin.Exists, "clearing removes the file");
            var updates = GetNode<AppManager>("/root/AppManager").Updates;
            var login = GD.Load<PackedScene>("res://Scenes/Login.tscn").Instantiate<Control>();
            AddChild(login);
            await Frames(3);
            var loginButton = login.GetNode<Button>("Panel/Margin/Content/Buttons/LoginButton");
            var message = login.GetNode<Label>("Panel/Margin/Content/Message");
            Require(!loginButton.Disabled && message.Text.Length == 0, "a fresh login screen has no update message");

            updates.Apply(200, "not json");
            Require(updates.Status == UpdateStatus.Unknown && !loginButton.Disabled, "a broken answer must change nothing");

            updates.Apply(200, $"{{\"latest\":\"99.0.0\",\"minimum\":\"{AppManager.Version}\",\"downloadUrl\":\"https://example.com/lifeverse.zip\"}}");
            Require(updates.Status == UpdateStatus.Available, $"expected Available, got {updates.Status}");
            Require(message.Text.Contains("99.0.0") && !loginButton.Disabled, $"a newer version is announced without blocking: '{message.Text}'");
            Require(login.FindChild("*", true, false) is not null && FindButton(login, "Baixar atualização") is not null, "a download button should appear");

            updates.Apply(200, "{\"latest\":\"99.0.0\",\"minimum\":\"98.0.0\"}");
            Require(updates.Status == UpdateStatus.Required, $"expected Required, got {updates.Status}");
            Require(loginButton.Disabled && login.GetNode<Button>("Panel/Margin/Content/Buttons/RegisterButton").Disabled, "login and register must be blocked");
            Require(message.Text.Contains("obrigatória"), $"the message should explain: '{message.Text}'");

            updates.Apply(200, $"{{\"latest\":\"{AppManager.Version}\",\"minimum\":\"{AppManager.Version}\"}}");
            Require(updates.Status == UpdateStatus.UpToDate, "same version is up to date");
            updates.Apply(500, "{}");
            Require(updates.Status == UpdateStatus.Unknown, "a server error means unknown");
            GD.Print("event=update_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=update_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static Button? FindButton(Node root, string text) => root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
