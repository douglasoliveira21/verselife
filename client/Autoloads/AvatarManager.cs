using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;

namespace LifeVerse.Client.Autoloads;

public partial class AvatarManager : Node
{
    [Signal] public delegate void AvatarLoadedEventHandler(bool success, string message);
    [Signal] public delegate void AvatarSavedEventHandler(bool success, string message);
    public AvatarAppearanceData Current { get; private set; } = CreateDefault();
    private HttpRequest _http = null!;
    private bool _saving;

    public override void _Ready()
    {
        _http = new HttpRequest();
        AddChild(_http);
        _http.RequestCompleted += OnRequestCompleted;
    }

    public Error Load()
    {
        _saving = false;
        return Send(Godot.HttpClient.Method.Get, string.Empty);
    }

    public Error Save(AvatarAppearanceData value)
    {
        _saving = true;
        return Send(Godot.HttpClient.Method.Put, JsonSerializer.Serialize(value));
    }

    private Error Send(Godot.HttpClient.Method method, string body)
    {
        if (_http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return Error.Busy;
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated) return Error.Unauthorized;
        var app = GetNode<AppManager>("/root/AppManager");
        return _http.Request(app.ApiBaseUrl + "/api/v1/users/me/avatar",
            ["Content-Type: application/json", $"Authorization: Bearer {session.AccessToken}"], method, body);
    }

    private void OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        var wasSaving = _saving;
        try
        {
            var json = Encoding.UTF8.GetString(body);
            if (responseCode is >= 200 and < 300)
            {
                Current = JsonSerializer.Deserialize<AvatarAppearanceData>(json) ?? CreateDefault();
                if (wasSaving) EmitSignal(SignalName.AvatarSaved, true, "Avatar saved.");
                else EmitSignal(SignalName.AvatarLoaded, true, "Avatar loaded.");
                return;
            }
            var message = responseCode == 401 ? "Session expired. Please sign in again." : "Could not load avatar.";
            if (wasSaving) EmitSignal(SignalName.AvatarSaved, false, message);
            else EmitSignal(SignalName.AvatarLoaded, false, message);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"event=avatar_response_invalid error={exception.GetType().Name}");
            if (wasSaving) EmitSignal(SignalName.AvatarSaved, false, "The server returned an invalid response.");
            else EmitSignal(SignalName.AvatarLoaded, false, "The server returned an invalid response.");
        }
    }

    public static AvatarAppearanceData CreateDefault() => new()
    {
        EquippedItems =
        [
            new EquippedItemData { Slot = "top", ItemKey = "tee_basic", TintColor = "#3AAFC1" },
            new EquippedItemData { Slot = "bottom", ItemKey = "pants_basic", TintColor = "#263B56" },
            new EquippedItemData { Slot = "shoes", ItemKey = "sneakers_basic", TintColor = "#E7E9EE" },
            new EquippedItemData { Slot = "accessory", ItemKey = "none", TintColor = "#FFFFFF" }
        ]
    };
}
