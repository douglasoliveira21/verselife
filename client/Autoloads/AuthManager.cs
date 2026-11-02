using System.Text;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class AuthManager : Node
{
    [Signal] public delegate void RequestFinishedEventHandler(bool success, string message);
    private HttpRequest _http = null!;

    public override void _Ready()
    {
        _http = new HttpRequest();
        AddChild(_http);
        _http.RequestCompleted += OnRequestCompleted;
    }

    public Error Login(string login, string password) => Send("/api/v1/auth/login", new { login, password });
    public Error Register(string username, string email, string password) => Send("/api/v1/auth/register", new { username, email, password });

    private Error Send(string path, object body)
    {
        if (_http.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return Error.Busy;
        var app = GetNode<AppManager>("/root/AppManager");
        return _http.Request(app.ApiBaseUrl + path, ["Content-Type: application/json"], Godot.HttpClient.Method.Post, JsonSerializer.Serialize(body));
    }

    private void OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        try
        {
            var json = Encoding.UTF8.GetString(body);
            if (responseCode is >= 200 and < 300)
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var username = root.GetProperty("user").GetProperty("username").GetString() ?? string.Empty;
                GetNode<SessionManager>("/root/SessionManager").Set(
                    root.GetProperty("accessToken").GetString() ?? string.Empty,
                    root.GetProperty("refreshToken").GetString() ?? string.Empty,
                    username);
                EmitSignal(SignalName.RequestFinished, true, "Authenticated.");
                return;
            }
            var message = "Authentication failed.";
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var problem = JsonDocument.Parse(json);
                if (problem.RootElement.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
            }
            EmitSignal(SignalName.RequestFinished, false, message);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"event=auth_response_invalid error={exception.GetType().Name}");
            EmitSignal(SignalName.RequestFinished, false, "The server returned an invalid response.");
        }
    }
}
