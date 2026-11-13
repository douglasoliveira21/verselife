using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The Propriedade tab of the island window against a pretend server: it shows who owns the island and what kind it is, the owner can sell, rent out, withdraw and hand the island over, a renter can renew or leave,
/// everybody else only reads, the island of welcome has no sale, and the answers of the server show as sentences.
/// </summary>
public partial class IslandPropertySmoke : Node3D
{
    private static readonly Guid Island = Guid.NewGuid(), Lease = Guid.NewGuid();

    private sealed class Server
    {
        public bool IsOwner = true, IsTenant, Welcome, Gone;
        public bool Down = false;
        public long? Sale, Rent; public int Days = 30; public bool LeaseLive; public string Owner = "AliceVerse";
        public readonly List<string> Calls = [];
        public string RulesAccess = "public", RulesBuild = "owner"; public bool RulesScripts, RulesVoice = true, RulesMedia = true; public int? RulesBack; public bool RulesApplied;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private object View() => new
        {
            rules = new { accessMode = RulesAccess, buildMode = RulesBuild, allowScripts = RulesScripts, allowVoice = RulesVoice, allowMedia = RulesMedia, autoReturnMinutes = RulesBack }, canEditRules = IsOwner, islandId = Island, name = "Ilha Tropical", size = 256, region = "full", ownerId = Guid.NewGuid(), owner = Owner, createdAt = DateTimeOffset.UtcNow.AddDays(-30), parcels = 6, hosting = IsOwner ? new { plan = "Plano Ouro", status = "active", expiresAt = DateTimeOffset.UtcNow.AddDays(10), recoveryUntil = DateTimeOffset.UtcNow.AddDays(17), maxPlayers = 80, maxObjects = 20000 } : null, isOwner = IsOwner, isTenant = IsTenant, isWelcome = Welcome,
            sale = Sale is { } s ? new { price = s, exclusive = false, includeObjects = true, description = "" } : null,
            rentOffer = Rent is { } r ? new { price = r, periodDays = Days, minPeriods = 1, maxPeriods = 6, deposit = 0L, graceDays = 3 } : null,
            lease = LeaseLive ? new { id = Lease, tenant = "Bruno", landlord = Owner, price = 5000L, periodDays = 30, status = "active", startsAt = DateTimeOffset.UtcNow.AddDays(-5), expiresAt = DateTimeOffset.UtcNow.AddDays(25), deposit = 500L } : null,
            history = new[] { new { at = DateTimeOffset.UtcNow.AddDays(-2), type = "sale", amount = 30000L, from = "Bruno", to = "AliceVerse" } },
        };

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            if (path.EndsWith("/land/rules", StringComparison.Ordinal)) { if (!IsOwner) return Task.FromResult<(long, string)>((403, "{\"error\":\"forbidden\"}")); var rj = JsonNode.Parse(body!)!.AsObject(); RulesAccess = rj["accessMode"]!.GetValue<string>(); RulesBuild = rj["buildMode"]!.GetValue<string>(); RulesScripts = rj["allowScripts"]!.GetValue<bool>(); RulesVoice = rj["allowVoice"]!.GetValue<bool>(); RulesMedia = rj["allowMedia"]!.GetValue<bool>(); RulesBack = rj["autoReturnMinutes"]?.GetValue<int>(); RulesApplied = rj["applyToExisting"]!.GetValue<bool>(); return Task.FromResult<(long, string)>((200, "{}")); }
            var tail = path[(path.IndexOf("/real-estate", StringComparison.Ordinal) + "/real-estate".Length)..];
            var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
            (long, string) Ok() => (200, "{}");
            if (method == "GET" && tail == "") return Task.FromResult(Gone ? (404L, "") : (200L, JsonSerializer.Serialize(View(), Web)));
            if (!IsOwner && !IsTenant) return Task.FromResult<(long, string)>((403, "{\"error\":\"forbidden\"}"));
            if (tail == "/sale") { if (Welcome) return Task.FromResult<(long, string)>((400, "{\"error\":\"welcome_island\"}")); if (method == "DELETE") Sale = null; else { Sale = json!["price"]!.GetValue<long>(); Rent = null; } return Task.FromResult(Ok()); }
            if (tail == "/rental-offer") { if (method == "DELETE") Rent = null; else { Rent = json!["pricePerPeriod"]!.GetValue<long>(); Days = json["periodDays"]!.GetValue<int>(); Sale = null; } return Task.FromResult(Ok()); }
            if (tail == "/transfer") { var name = json!["name"]!.GetValue<string>(); if (name == "ninguem") return Task.FromResult<(long, string)>((400, "{\"error\":\"person_not_found\"}")); if (Sale is not null) return Task.FromResult<(long, string)>((409, "{\"error\":\"for_sale\"}")); Owner = name; IsOwner = false; return Task.FromResult(Ok()); }
            if (tail.EndsWith("/renew", StringComparison.Ordinal)) return Task.FromResult(Ok());
            if (tail.EndsWith("/end", StringComparison.Ordinal)) { LeaseLive = false; IsTenant = false; return Task.FromResult(Ok()); }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server(); var notes = new List<string>();
            var page = new IslandPropertyPage { Name = "Property", Theme = ViewerTheme.Create() }; page.Notify += text => notes.Add(text); page.Bind(new IslandManagementApi(server.Handle)); page.SetIsland(Island); AddChild(page); await Frames(3);

            // --- the owner of an island with no offer --------------------------------------------------------------------------------
            await page.LoadAsync(); await Frames(2);
            Require(page.TextForTesting.Contains("Hospedagem") && page.TextForTesting.Contains("Plano Ouro, até") && page.TextForTesting.Contains("80 pessoas"), $"the owner sees the hosting plan (got {page.TextForTesting})");
            Require(page.TextForTesting.Contains("Regras da ilha") && page.TextForTesting.Contains("Entrada: Livre") && page.TextForTesting.Contains("Construir: Só o dono") && page.TextForTesting.Contains("Scripts: não") && page.TextForTesting.Contains("Voz: sim") && page.HasButtonForTesting("Editar Regras"), $"the rules are shown and the owner can edit them (got {page.TextForTesting})");
            Require(await page.SetRulesForTesting("list", "everyone", true, false, true, 45, true), "saving the rules works"); await Frames(2);
            Require(server.RulesAccess == "list" && server.RulesBuild == "everyone" && server.RulesScripts && !server.RulesVoice && server.RulesBack == 45 && server.RulesApplied, "the rules reach the server as chosen");
            Require(page.TextForTesting.Contains("Entrada: Lista") && page.TextForTesting.Contains("Construir: Todos") && page.TextForTesting.Contains("Scripts: sim") && page.TextForTesting.Contains("Voz: não") && page.TextForTesting.Contains("45 min"), $"and the tab shows the new rules (got {page.TextForTesting})");
            var text = page.TextForTesting; Require(page.IsOwner && text.Contains("AliceVerse") && text.Contains("Full Region") && text.Contains("256 m × 256 m") && text.Contains("Sem anúncio") && text.Contains("Terrenos") && text.Contains("L$ 30.000"), $"owner, region, size, situation and history (got {text})");
            Require(page.HasButtonForTesting("Colocar Ilha à Venda") && page.HasButtonForTesting("Alugar Ilha") && page.ButtonEnabledForTesting("Transferir Propriedade") && !page.HasButtonForTesting("Retirar da Venda"), "the owner can sell, rent out and hand over");

            // --- selling and withdrawing ------------------------------------------------------------------------------------------------
            Require(await page.SellForTesting(80000, true), "selling works"); await Frames(2);
            Require(server.Sale == 80000 && page.TextForTesting.Contains("À venda por L$ 80.000") && page.HasButtonForTesting("Retirar da Venda") && !page.HasButtonForTesting("Alugar Ilha"), "the offer is shown and the sale buttons give way to withdrawing");
            Require(!page.ButtonEnabledForTesting("Transferir Propriedade"), "an island for sale cannot be handed over");
            Require(!await page.TransferForTesting("carla"), "and the server says so"); Require(notes.Last().Contains("Retire a ilha da venda"), $"in words (got {notes.Last()})");
            Require(await page.WithdrawForTesting(), "withdrawing works"); await Frames(2); Require(server.Sale is null && page.TextForTesting.Contains("Sem anúncio"), "the island is off the market");

            // --- renting out --------------------------------------------------------------------------------------------------------------
            Require(await page.RentOutForTesting(3000, 14, 200), "offering for rent works"); await Frames(2);
            Require(server.Rent == 3000 && server.Days == 14 && page.TextForTesting.Contains("Para alugar por L$ 3.000 a cada 14 dias") && page.HasButtonForTesting("Retirar Oferta de Aluguel"), "the rent offer is shown");
            Require(await page.WithdrawForTesting(), "withdrawing the rent offer works"); await Frames(2); Require(server.Rent is null, "the offer is gone");

            // --- handing over -----------------------------------------------------------------------------------------------------------------
            Require(!await page.TransferForTesting("ninguem"), "an unknown person is refused"); Require(notes.Last().Contains("Não encontrei ninguém"), $"in words (got {notes.Last()})");
            Require(await page.TransferForTesting("carla"), "handing over works"); await Frames(2);
            Require(!page.IsOwner && page.TextForTesting.Contains("carla") && !page.HasButtonForTesting("Colocar Ilha à Venda") && page.TextForTesting.Contains("Você pode ver as informações"), "after the gift the person only reads");

            // --- a rented island ---------------------------------------------------------------------------------------------------------------
            server.IsOwner = true; server.Owner = "AliceVerse"; server.LeaseLive = true; await page.LoadAsync(); await Frames(2);
            text = page.TextForTesting; Require(text.Contains("Alugada para Bruno") && text.Contains("L$ 5.000 a cada 30 dias") && text.Contains("L$ 500"), $"the lease is shown (got {text})");
            Require(page.HasButtonForTesting("Encerrar Aluguel") && !page.ButtonEnabledForTesting("Encerrar Aluguel") && !page.HasButtonForTesting("Colocar Ilha à Venda"), "the owner cannot end it before the end or sell meanwhile");

            // --- the renter -------------------------------------------------------------------------------------------------------------------------
            server.IsOwner = false; server.IsTenant = true; await page.LoadAsync(); await Frames(2);
            Require(page.IsTenant && page.HasButtonForTesting("Renovar") && page.HasButtonForTesting("Sair do Aluguel") && !page.HasButtonForTesting("Transferir Propriedade"), "the renter can renew or leave, not hand the island over");
            Require(await page.RenewForTesting(2), "renewing works"); Require(server.Calls.Any(c => c.EndsWith($"/leases/{Lease}/renew", StringComparison.Ordinal)), "the renew request is sent");
            Require(await page.LeaveForTesting(), "leaving works"); await Frames(2); Require(!server.LeaseLive && !page.IsTenant, "the lease is over");

            // --- somebody else, the island of welcome, trouble --------------------------------------------------------------------------------------
            server.IsOwner = false; server.IsTenant = false; await page.LoadAsync(); await Frames(2); Require(!page.HasButtonForTesting("Colocar Ilha à Venda") && !page.HasButtonForTesting("Editar Regras") && page.TextForTesting.Contains("Só o proprietário"), "an administrator only reads");
            server.IsOwner = true; server.Welcome = true; await page.LoadAsync(); await Frames(2); Require(!page.HasButtonForTesting("Colocar Ilha à Venda") && page.TextForTesting.Contains("ilha de boas-vindas"), "the welcome island has no sale");
            server.Gone = true; await page.LoadAsync(); await Frames(2); Require(page.StatusForTesting.Contains("ainda não tem a propriedade"), $"an old server is told apart (got {page.StatusForTesting})");

            GD.Print("event=island_property_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=island_property_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
