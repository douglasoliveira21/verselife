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
        public int Port, ProtocolVersion = 11;
        public string ValidLogin = "dono", ValidPassword = "segredo", AccessToken = "token-de-acesso";

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
                    json = JsonSerializer.Serialize(new { accessToken = AccessToken, refreshToken = "r", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), user = new { id = Guid.NewGuid(), username = ValidLogin } });
                else status = 401;
            }
            else if (!authorized) status = 401;
            else if (path.EndsWith("/entry-token") && request.HttpMethod == "POST")
            {
                if (StartingAnswers > 0) { StartingAnswers--; status = 503; json = "{\"state\":\"starting\"}"; }
                else json = JsonSerializer.Serialize(new { token = EntryToken, host = Host, port = Port, protocolVersion = ProtocolVersion });
            }
            else if (path.EndsWith("/snapshot")) json = JsonSerializer.Serialize(new { terrainJson = TerrainJson, environmentJson = EnvironmentJson });
            else if (path.EndsWith("/objects")) json = ObjectsJson;
            else status = 404;
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.Close();
        }

        public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
    }
}
