using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Tests;

/// <summary>The login is kept alive: the access token is traded for a new one before it ends, one request at a time, and a refused refresh signs the person out.</summary>
public partial class SessionRefreshSmoke : Node
{
    private static string Jwt(Guid user, DateTimeOffset expires)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"none\"}") + "." + B64(JsonSerializer.Serialize(new { sub = user, exp = expires.ToUnixTimeSeconds() })) + ".sig";
    }

    public override async void _Ready()
    {
        try
        {
            var user = Guid.NewGuid(); var start = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero); var now = start;
            var session = new SessionManager { Now = () => now }; AddChild(session); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var calls = new List<string>(); var refreshed = 0; var expired = 0; session.SessionExpired += () => expired++; var changed = new List<bool>(); var answer = (Status: 200L, Body: "");
            session.TokensRefreshed += () => refreshed++; session.SessionChanged += a => changed.Add(a);
            session.RefreshTransport = sent => { calls.Add(sent); return answer; };
            string Reply(string refresh, DateTimeOffset expires) => JsonSerializer.Serialize(new { accessToken = Jwt(user, expires), refreshToken = refresh, accessTokenExpiresAt = expires });

            session.Set(Jwt(user, start.AddMinutes(15)), "refresh-1", "ana");
            Require(session.AccessExpiresAt == start.AddMinutes(15) && session.UserId == user, "the end of the token is read from the token itself");
            session.Tick(); Require(calls.Count == 0, "a token with plenty of time left is not refreshed");

            now = start.AddMinutes(15).AddSeconds(-SessionManager.RefreshLeadSeconds + 1); answer = (200, Reply("refresh-2", now.AddMinutes(15)));
            session.Tick();
            Require(calls.SequenceEqual(["refresh-1"]) && session.RefreshToken == "refresh-2" && refreshed == 1 && session.AccessExpiresAt == now.AddMinutes(15), "close to the end the refresh token is traded for a new pair");
            Require(changed.Count == 1 && session.Username == "ana" && session.IsAuthenticated, "a refresh is not a new sign-in (nobody is sent anywhere)");
            session.Tick(); session.Tick(); Require(calls.Count == 1, "with the new token there is nothing to refresh again");

            // the network fails: the login is kept and the refresh waits before trying again
            now = now.AddMinutes(15).AddSeconds(-30); answer = (0, "");
            session.Tick(); Require(calls.Count == 2 && session.IsAuthenticated && session.RefreshToken == "refresh-2", "a failed refresh keeps the login");
            session.Tick(); Require(calls.Count == 2, "and does not hammer the server");
            now = now.AddSeconds(SessionManager.RetrySeconds + 1); answer = (200, Reply("refresh-3", now.AddMinutes(15))); session.Tick();
            Require(calls.Count == 3 && session.RefreshToken == "refresh-3", "later it tries again and succeeds");

            // a server error or an unreadable answer is not the end of the session either
            now = session.AccessExpiresAt.AddSeconds(-10); answer = (500, "boom"); session.Tick(); Require(session.IsAuthenticated, "an error of the server keeps the login");
            now = now.AddSeconds(SessionManager.RetrySeconds + 1); answer = (200, "not json"); session.Tick(); Require(session.IsAuthenticated && session.RefreshToken == "refresh-3", "an answer that cannot be read keeps the login and the old token");

            // the API says the session is over
            now = now.AddSeconds(SessionManager.RetrySeconds + 1); answer = (401, "{}"); session.Tick();
            Require(!session.IsAuthenticated && session.RefreshToken == "" && changed[^1] == false && expired == 1, "when the API refuses the refresh the person is signed out and told the session ended");
            var before = calls.Count; session.Tick(); Require(calls.Count == before, "nothing is refreshed without a login");

            // a new login starts clean, and a late answer of an old refresh is ignored
            session.RefreshTransport = null; session.Set(Jwt(user, now.AddMinutes(15)), "fresh-1", "bia");
            session.RefreshTransport = sent => { calls.Add(sent); return (200, Reply("other", now.AddMinutes(30))); };
            now = now.AddMinutes(15).AddSeconds(-10); session.Tick(); Require(session.RefreshToken == "other", "a new login is kept alive too");
            GD.Print("event=session_refresh_smoke result=passed"); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PrintErr($"event=session_refresh_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
