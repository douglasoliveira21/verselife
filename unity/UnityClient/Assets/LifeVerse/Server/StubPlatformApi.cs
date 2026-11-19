using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LifeVerse.Unity.Server
{
    /// <summary>A small HTTP server with the island routes of the real API (login, entry ticket, snapshot, objects), for checking the client's REST code in-process.</summary>
    public sealed class StubPlatformApi : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        public readonly string Url;
        public readonly List<string> Requests = new List<string>();
        public int StartingAnswers = 1;           // the first entry requests answer 503 "starting", like an island whose server is booting
        public string EntryToken = "", Host = "127.0.0.1", TerrainJson = "{}", EnvironmentJson = "{}", ObjectsJson = "[]";
        public string LookJson = "{\"look\":{}}";
        public readonly Dictionary<string, byte[]> Assets = new Dictionary<string, byte[]>();   // asset id (lower-case, with hyphens) -> bytes
        public int Port, ProtocolVersion = 11;
        public string ValidLogin = "dono", ValidPassword = "segredo";
        public string BaseAccessToken = "token-de-acesso";
        /// <summary>How many times the pair of tokens was renewed (the old access token stops working at each).</summary>
        public int Generation, RefreshGeneration;
        public readonly Guid UserId = Guid.NewGuid();
        /// <summary>When true the entry is refused with 409 until an avatar is saved (PUT /api/v1/users/me/avatar), as the API does for a new account.</summary>
        public bool RequireAvatar, AvatarSaved;
        public string AccessToken => Generation == 0 ? BaseAccessToken : BaseAccessToken + "-" + Generation;
        public string RefreshToken => "refresh-" + RefreshGeneration;
        public int Refreshes;
        /// <summary>Makes the current access token stop working, as if its 15 minutes had passed: the next request is refused with 401 until the client refreshes.</summary>
        public void ExpireAccessToken() => Generation++;
        /// <summary>Makes the refresh token stop working too: the person has to sign in again.</summary>
        public void ExpireSession() { Generation++; RefreshGeneration++; }
        /// <summary>Canned answers for the platform routes: "METHOD /path" (without the query) → JSON.</summary>
        public readonly Dictionary<string, string> Routes = new Dictionary<string, string>();
        public readonly List<(string Route, string Body)> Bodies = new List<(string, string)>();
        /// <summary>Whether a request with a body matching the test arrived (safe to call while the server thread is adding).</summary>
        public bool ReceivedAny(Func<(string Route, string Body), bool> test) { lock (Bodies) return Bodies.Any(test); }

        public StubPlatformApi()
        {
            var port = new System.Random().Next(30000, 50000);
            while (true) { try { Url = $"http://127.0.0.1:{port}/"; _listener.Prefixes.Clear(); _listener.Prefixes.Add(Url); _listener.Start(); break; } catch (HttpListenerException) { port++; } }
            Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); } catch { return; }
                _ = Task.Run(() => Handle(context));
            }
        }

        private void Handle(HttpListenerContext context)
        {
            var request = context.Request; var path = request.Url.AbsolutePath; var body = new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd();
            lock (Requests) Requests.Add(request.HttpMethod + " " + path);
            int status = 200; string json = "{}";
            var authorized = request.Headers["Authorization"] == "Bearer " + AccessToken;
            if (request.HttpMethod == "POST" && path == "/api/v1/auth/login")
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.GetProperty("login").GetString() == ValidLogin && document.RootElement.GetProperty("password").GetString() == ValidPassword)
                    json = JsonSerializer.Serialize(new { accessToken = AccessToken, refreshToken = RefreshToken, accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), user = new { id = UserId, username = ValidLogin } });
                else status = 401;
            }
            else if (request.HttpMethod == "POST" && path == "/api/v1/auth/refresh")
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.GetProperty("refreshToken").GetString() == RefreshToken) { Generation++; RefreshGeneration++; Refreshes++; json = JsonSerializer.Serialize(new { accessToken = AccessToken, refreshToken = RefreshToken, accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), user = new { id = UserId, username = ValidLogin } }); }
                else status = 401;
            }
            else if (request.HttpMethod == "GET" && path == "/api/v1/worlds/islands" && Routes.TryGetValue("GET /api/v1/worlds/islands", out var publicIslands)) json = publicIslands;   // public: no sign-in needed
            else if (!authorized) status = 401;
            else if (request.HttpMethod == "PUT" && path == "/api/v1/users/me/avatar") { AvatarSaved = true; lock (Bodies) Bodies.Add(("PUT " + path, body)); json = "{}"; }
            else if (Routes.TryGetValue(request.HttpMethod + " " + path, out var canned)) { json = canned; if (body.Length > 0) lock (Bodies) Bodies.Add((request.HttpMethod + " " + path, body)); }
            else if (path.EndsWith("/entry-token") && request.HttpMethod == "POST")
            {
                if (RequireAvatar && !AvatarSaved) { status = 409; json = "{}"; }
                else if (StartingAnswers > 0) { StartingAnswers--; status = 503; json = "{\"state\":\"starting\"}"; }
                else json = JsonSerializer.Serialize(new { token = EntryToken, host = Host, port = Port, protocolVersion = ProtocolVersion });
            }
            else if (path.EndsWith("/snapshot")) json = JsonSerializer.Serialize(new { terrainJson = TerrainJson, environmentJson = EnvironmentJson });
            else if (path.EndsWith("/objects")) json = ObjectsJson;
            else if (path.EndsWith("/environment")) json = LookJson;
            else if (path.Contains("/assets/") && path.EndsWith("/file") && Assets.TryGetValue(path.Split('/')[path.Split('/').Length - 2], out var asset))
            {
                context.Response.StatusCode = 200; context.Response.ContentType = "application/octet-stream"; context.Response.ContentLength64 = asset.Length;
                context.Response.OutputStream.Write(asset, 0, asset.Length); context.Response.Close(); return;
            }
            else status = 404;
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.Close();
        }

        public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
    }
}
