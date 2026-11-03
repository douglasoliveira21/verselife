using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class SessionManager : Node
{
    [Signal] public delegate void SessionChangedEventHandler(bool authenticated);
    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    /// <summary>Account id read from the access token subject; used only to recognise our own replicated actions.</summary>
    public Guid UserId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public void Set(string accessToken, string refreshToken, string username)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Username = username;
        UserId = ParseSubject(accessToken);
        EmitSignal(SignalName.SessionChanged, true);
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        Username = string.Empty;
        UserId = Guid.Empty;
        EmitSignal(SignalName.SessionChanged, false);
    }

    private static Guid ParseSubject(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return Guid.Empty;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("sub", out var subject) && Guid.TryParse(subject.GetString(), out var id) ? id : Guid.Empty;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return Guid.Empty; }
    }
}
