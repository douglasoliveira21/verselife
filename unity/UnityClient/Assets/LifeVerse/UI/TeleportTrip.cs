using System;
using System.Collections;
using System.Text.Json;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A trip to another island as a controlled transfer, the same steps the Godot client takes: ask the API to prepare it (it checks the destination, the access and the room, and reserves a place), ask for the single-use ticket
    /// (waiting while the destination server starts), then the game leaves the island and connects with that ticket, and the trip is done only when the API says the destination server confirmed the arrival. Until the ticket is
    /// in hand nothing has changed for the person, who stays where they are and can give up; a trip left half done earlier (a closed game) is cancelled and tried again once.
    /// </summary>
    public sealed class TeleportTrip
    {
        public const int MaxServerWaits = 15, ArrivalChecks = 24;
        public float ServerWaitSeconds = 4f, ArrivalCheckSeconds = .5f;

        public Guid Destination, Source;
        public float? X, Z;
        public Guid Transfer { get; private set; }
        public EntryResult Ticket { get; private set; }
        public string Message { get; private set; } = "";
        public bool CanRetry { get; private set; }
        public bool Failed { get; private set; }
        public bool Arrived { get; private set; }
        public bool Cancelled { get; private set; }
        public string Step { get; private set; } = "idle";
        private string _key = Guid.NewGuid().ToString("N");

        public static string CodeOf(string body)
        {
            try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null; }
            catch (JsonException) { return null; }
        }

        /// <summary>What the person is told for each way the API can refuse, and whether trying again can help.</summary>
        public static (string Message, bool Retry) Explain(int status, string code) => (code, status) switch
        {
            ("island_not_found", _) => ("Essa ilha não existe mais.", false),
            ("engine_mismatch", _) => ("Essa ilha ainda roda no LifeVerse clássico: este cliente não consegue entrar nela.", false),
            ("island_private", _) => ("Essa ilha é privada e você não tem acesso a ela.", false),
            ("access_denied", _) => ("Você não tem permissão para entrar nessa ilha.", false),
            ("island_closed", _) => ("Essa ilha está temporariamente fechada.", false),
            ("island_full", _) => ("A ilha de destino está lotada no momento. Tente de novo em instantes.", true),
            ("invalid_destination", _) => ("O ponto de destino não fica dentro dessa ilha.", false),
            ("same_island", _) => ("Você já está nessa ilha.", false),
            ("avatar_required", _) => ("Crie o seu avatar antes de viajar.", false),
            ("token_used", _) or ("expired", _) or ("cancelled", _) or ("reservation_expired", _) => ("A reserva dessa viagem acabou. Tente de novo.", true),
            (_, 401) => ("Sua sessão expirou. Entre novamente.", false),
            (_, 0) => ("Não foi possível falar com o servidor. Verifique a conexão e tente de novo.", true),
            _ => ("O servidor recusou a viagem (" + status + ").", true),
        };

        private void Fail(string message, bool retry) { Failed = true; Message = message; CanRetry = retry; Step = "failed"; }

        /// <summary>Prepares the trip and gets the ticket. Ends with <see cref="Ticket"/> set, or <see cref="Failed"/> and the reason.</summary>
        public IEnumerator Prepare(IslandEntryClient api)
        {
            Failed = false; Message = ""; Ticket = null; var staleRetried = false;
            while (true)
            {
                Step = "preparing";
                var body = JsonSerializer.Serialize(new { destinationIslandId = Destination, sourceIslandId = Source == Guid.Empty ? (Guid?)null : Source, x = X, z = Z, idempotencyKey = _key });
                var prepare = api.ApiAsync("POST", "/api/v1/worlds/teleports/prepare", body); while (!prepare.IsCompleted) yield return null;
                var (status, text) = prepare.Result;
                if (status == 200 || status == 201)
                {
                    try { using var d = JsonDocument.Parse(text); if (d.RootElement.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var transfer) && d.RootElement.TryGetProperty("status", out var state) && state.GetString() == "prepared") { Transfer = transfer; break; } }
                    catch (JsonException) { }
                    Fail("A resposta do servidor não foi entendida.", true); yield break;
                }
                if (status == 409 && CodeOf(text) == "transfer_in_progress" && !staleRetried)
                {
                    // a trip left half done earlier: give it up and start this one
                    string other = null; try { using var d = JsonDocument.Parse(text); if (d.RootElement.TryGetProperty("transfer", out var t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("id", out var tid)) other = tid.GetString(); } catch (JsonException) { }
                    if (Guid.TryParse(other, out var stale)) { staleRetried = true; var cancel = api.ApiAsync("POST", "/api/v1/worlds/teleports/" + stale + "/cancel"); while (!cancel.IsCompleted) yield return null; _key = Guid.NewGuid().ToString("N"); continue; }
                }
                var (message, retry) = Explain(status, CodeOf(text)); Fail(message, retry); yield break;
            }
            for (var waits = 0; ; waits++)
            {
                Step = "connecting";
                var connect = api.ApiAsync("POST", "/api/v1/worlds/teleports/" + Transfer + "/connect"); while (!connect.IsCompleted) yield return null;
                var (status, text) = connect.Result;
                if (status == 200)
                {
                    try
                    {
                        using var d = JsonDocument.Parse(text); var r = d.RootElement;
                        if (r.TryGetProperty("token", out var token) && r.TryGetProperty("host", out var host) && r.TryGetProperty("port", out var port) && r.TryGetProperty("protocolVersion", out var protocol))
                        { Ticket = new EntryResult { Ok = true, Token = token.GetString() ?? "", Host = host.GetString() ?? "", Port = port.GetInt32(), ProtocolVersion = protocol.GetInt32(), Status = 200 }; Step = "ticket"; yield break; }
                    }
                    catch (JsonException) { }
                    yield return Abandon(api, "A resposta do servidor não foi entendida.", true); yield break;
                }
                if (status == 503 && CodeOf(text) == "server_starting")
                {
                    if (waits >= MaxServerWaits) { yield return Abandon(api, "A ilha de destino não iniciou a tempo. Tente de novo em instantes.", true); yield break; }
                    var until = Time.realtimeSinceStartup + ServerWaitSeconds; while (Time.realtimeSinceStartup < until) yield return null; continue;
                }
                var (message, retry) = Explain(status, CodeOf(text)); yield return Abandon(api, message, retry); yield break;
            }
        }

        private IEnumerator Abandon(IslandEntryClient api, string message, bool retry)
        {
            Fail(message, retry); if (Transfer != Guid.Empty) { var cancel = api.ApiAsync("POST", "/api/v1/worlds/teleports/" + Transfer + "/cancel"); while (!cancel.IsCompleted) yield return null; }
        }

        /// <summary>The person gave up before leaving the island: the reservation is released.</summary>
        public IEnumerator Cancel(IslandEntryClient api)
        {
            Cancelled = true; Step = "cancelled"; if (Transfer != Guid.Empty) { var cancel = api.ApiAsync("POST", "/api/v1/worlds/teleports/" + Transfer + "/cancel"); while (!cancel.IsCompleted) yield return null; }
        }

        /// <summary>After connecting: the trip counts only when the API says the destination server confirmed the person is there.</summary>
        public IEnumerator ConfirmArrival(IslandEntryClient api)
        {
            Step = "arriving";
            for (var i = 0; i < ArrivalChecks; i++)
            {
                var look = api.ApiAsync("GET", "/api/v1/worlds/teleports/" + Transfer); while (!look.IsCompleted) yield return null;
                var (status, text) = look.Result; string state = null; try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text); if (d.RootElement.TryGetProperty("status", out var s)) state = s.GetString(); } catch (JsonException) { }
                if (status == 200 && state == "arrived") { Arrived = true; Step = "arrived"; yield break; }
                if (status == 200 && (state == "cancelled" || state == "expired" || state == "failed")) { Fail("A ilha de destino não confirmou a sua chegada.", true); yield break; }
                var until = Time.realtimeSinceStartup + ArrivalCheckSeconds; while (Time.realtimeSinceStartup < until) yield return null;
            }
            Fail("A ilha de destino não confirmou a sua chegada a tempo.", true);
        }
    }
}
