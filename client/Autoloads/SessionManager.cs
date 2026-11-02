using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class SessionManager : Node
{
    [Signal] public delegate void SessionChangedEventHandler(bool authenticated);
    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public void Set(string accessToken, string refreshToken, string username)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Username = username;
        EmitSignal(SignalName.SessionChanged, true);
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        Username = string.Empty;
        EmitSignal(SignalName.SessionChanged, false);
    }
}
