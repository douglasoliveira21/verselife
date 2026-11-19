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
        public bool Ok, Starting; public int Status; public string Token = "", Host = "", Message = ""; public int Port, ProtocolVersion;
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

        public IslandEntryClient(string apiBaseUrl, TimeSpan? timeout = null) { _baseUrl = apiBaseUrl.TrimEnd('/'); _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) }; }

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
                UseToken(token); return new LoginResult { Ok = token.Length > 0, Status = (int)response.StatusCode, AccessToken = token };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException) { return new LoginResult { Message = "Sem resposta da API: " + exception.Message }; }
        }

        private static string LoginMessage(int status) => status switch { 401 => "Usuário ou senha incorretos.", 429 => "Muitas tentativas. Espere um pouco.", _ => $"Entrada recusada pela API ({status})." };

        public async Task<EntryResult> RequestEntryAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.PostAsync($"{_baseUrl}/api/v1/worlds/islands/{islandId}/entry-token", new StringContent(string.Empty), ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); var status = (int)response.StatusCode;
                if (status == 503 && IsStarting(body)) return new EntryResult { Starting = true, Status = status, Message = "Iniciando a ilha…" };
                if (!response.IsSuccessStatusCode) return new EntryResult { Status = status, Message = EntryFailure(status) };
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
                using var response = await _http.GetAsync($"{_baseUrl}/api/v1/worlds/islands/{islandId}/snapshot", ct).ConfigureAwait(false);
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
        public async Task<string> GetObjectsAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.GetAsync($"{_baseUrl}/api/v1/worlds/islands/{islandId}/objects", ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { return null; }
        }

        /// <summary>The look of the island (hour, weather, fog, light, sky) and its music, as the API sends it: {"look":{...},"media":{...}}; null when it could not be read.</summary>
        public async Task<string> GetEnvironmentAsync(Guid islandId, CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.GetAsync($"{_baseUrl}/api/v1/worlds/islands/{islandId}/environment", ct).ConfigureAwait(false);
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
                    using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException) { }
            }
            return null;
        }

        public void Dispose() => _http.Dispose();
    }
}
