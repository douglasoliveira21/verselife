using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LifeVerse.Unity.World
{
    public sealed class LoginResult { public bool Ok; public int Status; public string AccessToken = ""; public string Message = ""; }

    /// <summary>What the API gives for entering an island: where the island server listens and the single-use ticket (the same fields the Godot client reads).</summary>
    public sealed class EntryResult
    {
        public bool Ok, Starting, OtherEngine; public int Status; public string Token = "", Host = "", Message = ""; public int Port, ProtocolVersion;
    }

    public sealed class IslandSnapshotResult { public bool Ok; public int Status; public string TerrainJson = "{}", EnvironmentJson = "{}"; }

    /// <summary>
    /// The REST side of entering an island, as the Godot client does it: sign in, ask for the entry ticket of an island (the API answers 503 "starting" while the supervisor
    /// boots the island's server), read the saved terrain and environment and the list of objects. Every call is a Task; nothing here touches the Unity API, so it can be
    /// awaited from a coroutine or a test. The access token lives 15 minutes (the API's rule); refreshing it is not done here yet, and neither is the second factor.
    /// </summary>
    public sealed class IslandEntryClient : IDisposable
    {
        private readonly HttpClient _http; private readonly string _baseUrl;
        public string AccessToken { get; private set; } = "";

        /// <summary>This client is the Unity one: the API sends it only to islands that run the Unity island server (LifeVerse Wire 12), lists only those and starts it on one of them.</summary>
        public const string Engine = "unity", EngineHeader = "X-LifeVerse-Engine";

        public IslandEntryClient(string apiBaseUrl, TimeSpan? timeout = null) { _baseUrl = apiBaseUrl.TrimEnd('/'); _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) }; _http.DefaultRequestHeaders.Add(EngineHeader, Engine); }

                /// <summary>The refresh token and when the access token ends (15 minutes after signing in): the client asks for a new pair a minute before, or when the API refuses the old one.</summary>
        public string RefreshToken { get; private set; } = "";
        public DateTimeOffset ExpiresAt { get; private set; } = DateTimeOffset.MaxValue;
        public int Refreshes { get; private set; }
        /// <summary>The id of the signed-in person (from the login answer).</summary>
        public Guid UserId { get; private set; }
        /// <summary>Told when the session cannot be renewed (the refresh token was refused): the person has to sign in again.</summary>
        public event Action SessionExpired;
        private readonly SemaphoreSlim _refreshing = new SemaphoreSlim(1, 1);

        private void KeepSession(JsonElement answer)
        {
            UseToken(answer.GetProperty("accessToken").GetString() ?? "");
            if (answer.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var userId)) UserId = userId;
            RefreshToken = answer.TryGetProperty("refreshToken", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            ExpiresAt = answer.TryGetProperty("accessTokenExpiresAt", out var e) && e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), out var when) ? when : DateTimeOffset.MaxValue;
        }

        /// <summary>Asks for a new pair of tokens. False (and <see cref="SessionExpired"/>) when the API refuses.</summary>
        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            if (RefreshToken.Length == 0) return false;
            await _refreshing.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var stale = RefreshToken;
                using var response = await _http.PostAsync(_baseUrl + "/api/v1/auth/refresh", new StringContent(JsonSerializer.Serialize(new { refreshToken = stale }), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) { if ((int)response.StatusCode == 401 || (int)response.StatusCode == 400) { RefreshToken = ""; SessionExpired?.Invoke(); } return false; }
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)); KeepSession(document.RootElement); Refreshes++; return true;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return false; }
            finally { _refreshing.Release(); }
        }

        /// <summary>A request with the access token: renewed first when it is about to end, and once more when the API says 401 and a renewal works.</summary>
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string json, CancellationToken ct = default)
        {
            if (RefreshToken.Length > 0 && ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(60)) await RefreshAsync(ct).ConfigureAwait(false);
            HttpRequestMessage Make() => new HttpRequestMessage(method, url) { Content = json == null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
            var response = await _http.SendAsync(Make(), ct).ConfigureAwait(false);
            if ((int)response.StatusCode == 401 && RefreshToken.Length > 0 && await RefreshAsync(ct).ConfigureAwait(false)) { response.Dispose(); response = await _http.SendAsync(Make(), ct).ConfigureAwait(false); }
            return response;
        }

        /// <summary>Like <see cref="SendAsync"/> for a body that cannot be written twice (a file): <paramref name="make"/> builds it again for the retry after a renewal.</summary>
        public async Task<HttpResponseMessage> SendContentAsync(HttpMethod method, string url, Func<HttpContent> make, CancellationToken ct = default)
        {
            if (RefreshToken.Length > 0 && ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(60)) await RefreshAsync(ct).ConfigureAwait(false);
            var response = await _http.SendAsync(new HttpRequestMessage(method, url) { Content = make() }, ct).ConfigureAwait(false);
            if ((int)response.StatusCode == 401 && RefreshToken.Length > 0 && await RefreshAsync(ct).ConfigureAwait(false)) { response.Dispose(); response = await _http.SendAsync(new HttpRequestMessage(method, url) { Content = make() }, ct).ConfigureAwait(false); }
            return response;
        }

        /// <summary>Sends a file to the platform (POST /api/v1/assets/upload with the "file" and "category" fields: objects, textures, sounds, animations, clothing, accessories or models; up to 100 MB). Returns the status and the body.</summary>
        public Task<(int Status, string Body)> UploadAsync(string fileName, byte[] bytes, string category, string path = "/api/v1/assets/upload", CancellationToken ct = default) =>
            FileCallAsync(HttpMethod.Post, path, fileName, bytes, category, ct);

        /// <summary>Any call that carries one file as multipart form data (an upload, a new version of an asset).</summary>
        public async Task<(int Status, string Body)> FileCallAsync(HttpMethod method, string path, string fileName, byte[] bytes, string category, CancellationToken ct = default)
        {
            try
            {
                HttpContent Make()
                {
                    var form = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(MimeOf(fileName)); form.Add(file, "file", System.IO.Path.GetFileName(fileName));
                    if (!string.IsNullOrEmpty(category)) form.Add(new StringContent(category), "category"); return form;
                }
                using var response = await SendContentAsync(method, _baseUrl + path, Make, ct).ConfigureAwait(false);
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return (0, ""); }
        }

        public static string MimeOf(string fileName) => System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
        {
            ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", ".glb" => "model/gltf-binary", ".gltf" => "model/gltf+json", ".dae" => "model/vnd.collada+xml",
            ".wav" => "audio/wav", ".mp3" => "audio/mpeg", ".ogg" => "audio/ogg", _ => "application/octet-stream",
        };

        /// <summary>Any call of the platform API as the signed-in person: the status and the body ("" when it could not be reached, status 0). <paramref name="path"/> starts with /api/.</summary>
        public async Task<(int Status, string Body)> ApiAsync(string method, string path, string json = null, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(new HttpMethod(method), _baseUrl + path, json, ct).ConfigureAwait(false);
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return (0, ""); }
        }

        public void UseToken(string accessToken) { AccessToken = accessToken; _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(accessToken) ? null : new AuthenticationHeaderValue("Bearer", accessToken); }

        public async Task<LoginResult> LoginAsync(string login, string password, CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.PostAsync(_baseUrl + "/api/v1/auth/login", new StringContent(JsonSerializer.Serialize(new { login, password }), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new LoginResult { Status = (int)response.StatusCode, Message = LoginMessage((int)response.StatusCode) };
                using var document = JsonDocument.Parse(body);
                var token = document.RootElement.GetProperty("accessToken").GetString() ?? "";
                KeepSession(document.RootElement); return new LoginResult { Ok = token.Length > 0, Status = (int)response.StatusCode, AccessToken = token };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return new LoginResult { Message = "Sem resposta da API: " + exception.Message }; }
        }

        /// <summary>Creates an account (the API signs the new person in and answers with the same tokens as a login).</summary>
        public async Task<LoginResult> RegisterAsync(string username, string email, string password, CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.PostAsync(_baseUrl + "/api/v1/auth/register", new StringContent(JsonSerializer.Serialize(new { username, email, password }), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); var status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode) return new LoginResult { Status = status, Message = status == 409 ? "Esse nome de usuário ou e-mail já está em uso." : status == 400 || status == 422 ? "Os dados não foram aceitos: confira o nome, o e-mail e a senha (de 6 a 128 caracteres, com maiúscula, minúscula e número)." : LoginMessage(status) };
                using var document = JsonDocument.Parse(body); var token = document.RootElement.GetProperty("accessToken").GetString() ?? "";
                KeepSession(document.RootElement); return new LoginResult { Ok = token.Length > 0, Status = status, AccessToken = token };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return new LoginResult { Message = "Sem resposta da API: " + exception.Message }; }
        }

        private static string LoginMessage(int status) => status switch { 401 => "Usuário ou senha incorretos.", 429 => "Muitas tentativas. Espere um pouco.", _ => $"Entrada recusada pela API ({status})." };

        public async Task<EntryResult> RequestEntryAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Post, $"{_baseUrl}/api/v1/worlds/islands/{islandId}/entry-token", string.Empty, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); var status = (int)response.StatusCode;
                if (status == 503 && IsStarting(body)) return new EntryResult { Starting = true, Status = status, Message = "Iniciando a ilha…" };
                if (!response.IsSuccessStatusCode) { var mismatch = EngineMismatch(body); return new EntryResult { Status = status, OtherEngine = mismatch != null, Message = mismatch ?? EntryFailure(status) }; }
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                return new EntryResult
                {
                    Ok = true, Status = status, Token = root.GetProperty("token").GetString() ?? "", Host = root.GetProperty("host").GetString() ?? "",
                    Port = root.GetProperty("port").GetInt32(), ProtocolVersion = root.GetProperty("protocolVersion").GetInt32(),
                };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return new EntryResult { Message = "Sem resposta da API: " + exception.Message }; }
        }

        /// <summary>The API answers 503 with state "starting" (or "running" before the first heartbeat) while the island's server comes up: keep asking.</summary>
        /// <summary>The words of the API when the island runs on the other engine (409 "engine_mismatch"), or null.</summary>
        public static string EngineMismatch(string body)
        {
            try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); var r = d.RootElement; return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e) && e.GetString() == "engine_mismatch" ? (r.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "Esta ilha ainda roda no LifeVerse clássico.") : null; }
            catch (JsonException) { return null; }
        }

        public static bool IsStarting(string body)
        {
            try { using var document = JsonDocument.Parse(body); return document.RootElement.TryGetProperty("state", out var state) && (state.GetString() == "starting" || state.GetString() == "running"); }
            catch (JsonException) { return false; }
        }

        public static string EntryFailure(int status) => status switch
        {
            503 => "Esta ilha está sem servidor ativo no momento (503).", 404 => "Ilha não encontrada (404). Confira o ID.", 403 => "Você não tem permissão para entrar nesta ilha (403).",
            401 => "Sessão expirada (401). Entre novamente.", 409 => "Crie o avatar antes de entrar em uma ilha (409).", _ => $"Entrada recusada pela API ({status}).",
        };

        public async Task<IslandSnapshotResult> GetSnapshotAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, $"{_baseUrl}/api/v1/worlds/islands/{islandId}/snapshot", null, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new IslandSnapshotResult { Status = (int)response.StatusCode };
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return new IslandSnapshotResult
                {
                    Ok = true, Status = 200, TerrainJson = document.RootElement.GetProperty("terrainJson").GetString() ?? "{}", EnvironmentJson = document.RootElement.GetProperty("environmentJson").GetString() ?? "{}",
                };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return new IslandSnapshotResult(); }
        }

        /// <summary>The objects of the island: a JSON array, as the API sends it (null when it could not be read).</summary>
        /// <summary>The public islands, for the list on the login screen (no sign-in needed).</summary>
        public async Task<string> GetIslandsAsync(CancellationToken ct = default)
        {
            try { using var response = await _http.GetAsync(_baseUrl + "/api/v1/worlds/islands", ct).ConfigureAwait(false); return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null; }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return null; }
        }

        public async Task<string> GetObjectsAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, $"{_baseUrl}/api/v1/worlds/islands/{islandId}/objects", null, ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return null; }
        }

        /// <summary>The look of the island (hour, weather, fog, light, sky) and its music, as the API sends it: {"look":{...},"media":{...}}; null when it could not be read.</summary>
        public async Task<string> GetEnvironmentAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, $"{_baseUrl}/api/v1/worlds/islands/{islandId}/environment", null, ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return null; }
        }

        /// <summary>The bytes of an asset (a texture, a model): from the island's own endpoint first, then from the person's own files, as the Godot client does. Null when neither has it.</summary>
        public async Task<byte[]> GetAssetAsync(Guid islandId, Guid assetId, CancellationToken ct = default)
        {
            foreach (var url in new[] { $"{_baseUrl}/api/v1/worlds/islands/{islandId}/assets/{assetId}/file", $"{_baseUrl}/api/v1/assets/{assetId}/download" })
            {
                try
                {
                    using var response = await SendAsync(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { }
            }
            return null;
        }

        /// <summary>A binary answer of the API (a picture): the status and the bytes (empty when the server did not answer).</summary>
        public async Task<(int Status, byte[] Bytes)> GetBytesAsync(string path, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, _baseUrl + path, null, ct).ConfigureAwait(false);
                return ((int)response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false) : new byte[0]);
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return (0, new byte[0]); }
        }

        public void Dispose() => _http.Dispose();
    }
}
