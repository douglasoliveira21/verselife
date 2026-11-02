using System.Net.Http.Json;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

public partial class ClientPersistenceSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var app = GetNode<AppManager>("/root/AppManager");
            var username = OS.GetEnvironment("LIFEVERSE_TEST_USER");
            var password = OS.GetEnvironment("LIFEVERSE_TEST_PASSWORD");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Test credentials are required.");
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(app.ApiBaseUrl + "/api/v1/auth/login", new { login = username, password });
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            GetNode<SessionManager>("/root/SessionManager").Set(
                root.GetProperty("accessToken").GetString() ?? throw new InvalidOperationException("Missing access token."),
                root.GetProperty("refreshToken").GetString() ?? throw new InvalidOperationException("Missing refresh token."), username);

            var avatars = GetNode<AvatarManager>("/root/AvatarManager");
            var completion = new TaskCompletionSource<(bool Success, string Message)>();
            void Loaded(bool success, string message) => completion.TrySetResult((success, message));
            avatars.AvatarLoaded += Loaded;
            var error = avatars.Load();
            if (error != Error.Ok) throw new InvalidOperationException($"Avatar load did not start: {error}");
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            avatars.AvatarLoaded -= Loaded;
            if (!result.Success) throw new InvalidOperationException(result.Message);
            if (avatars.Current.DisplayName != "Persistent Resident" || avatars.Current.BaseModel != "human_b" || avatars.Current.HairStyle != "mohawk")
                throw new InvalidOperationException("Client received unexpected persisted appearance.");

            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>();
            AddChild(rig);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            rig.ApplyAppearance(avatars.Current);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("event=client_persistence_smoke result=passed display_name=Persistent Resident base=human_b hair=mohawk");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=client_persistence_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }
}
