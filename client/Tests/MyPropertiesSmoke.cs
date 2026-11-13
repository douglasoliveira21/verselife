using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.RealEstate;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The "Minhas Propriedades" window against a pretend server: the seven tabs list what the server sent, the summary is the server's, the buttons follow the state of each property, offering, withdrawing, renewing and ending
/// send the right requests and the window shows the new state, receipts carry the payment, and an old server is told apart from a lost connection.
/// </summary>
public partial class MyPropertiesSmoke : Node3D
{
    private static readonly Guid IslandA = Guid.NewGuid(), IslandB = Guid.NewGuid();

    private sealed class P { public Guid Id = Guid.NewGuid(); public Guid Island; public string Code = "", Name = "", Category = "residencial"; public long? Sale, Rent; public int Days = 30; public string? Tenant; public Guid? Rental; public DateTimeOffset? Expires; }

    private sealed class Server
    {
        public readonly List<P> Mine = [];
        public long? IslandSale, IslandRent; public int IslandDays = 30;
        public bool Down, Gone; public readonly List<string> Calls = [];
        public readonly List<(Guid Id, string Title, bool Read)> Notices = [];
        public bool TenantRentalLive = true; public Guid TenantRental = Guid.NewGuid(); public Guid TenantParcel = Guid.NewGuid();
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private static string IslandName(Guid id) => id == IslandA ? "Ilha Tropical" : "Ilha Serenidade";

        private object Body()
        {
            var now = DateTimeOffset.UtcNow;
            var parcels = Mine.Select(p => new { id = p.Id, islandId = p.Island, island = IslandName(p.Island), code = p.Code, name = p.Name, category = p.Category, area = 1024.0, maxObjects = 1000, objects = 120, kind = p.Tenant is not null ? "rented" : p.Sale is not null ? "sale" : p.Rent is not null ? "rent" : "available", salePrice = p.Sale, rentPrice = p.Rent, rentPeriodDays = p.Days, tenant = p.Tenant, rentExpiresAt = p.Expires, rentalId = p.Rental, canVisit = true, imageUrl = p.Code == "L-01" ? "asset:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" : "", createdAt = now, revision = 1 }).ToList();
            var out_ = Mine.Where(p => p.Tenant is not null).Select(p => new { id = p.Rental, parcelId = p.Id, islandId = p.Island, island = IslandName(p.Island), code = p.Code, parcel = p.Name, landlord = "AliceVerse", tenant = p.Tenant, role = "landlord", price = p.Rent, periodDays = p.Days, status = "active", startsAt = now.AddDays(-5), expiresAt = p.Expires, endedAt = (DateTimeOffset?)null, autoRenew = false, deposit = 0, canVisit = true }).ToList<object>();
            var inn = TenantRentalLive ? new { id = TenantRental, parcelId = TenantParcel, islandId = IslandB, island = IslandName(IslandB), code = "L-09", parcel = "Loja da Praia", landlord = "Bruno", tenant = "AliceVerse", role = "tenant", price = 12000L, periodDays = 30, status = "active", startsAt = now.AddDays(-10), expiresAt = now.AddDays(5), endedAt = (DateTimeOffset?)null, autoRenew = false, deposit = 500L, canVisit = true } : null;
            var ended = new { id = Guid.NewGuid(), parcelId = Guid.NewGuid(), islandId = IslandB, island = IslandName(IslandB), code = "L-02", parcel = "Antiga", landlord = "Bruno", tenant = "AliceVerse", role = "tenant", price = 800L, periodDays = 7, status = "ended", startsAt = now.AddDays(-40), expiresAt = now.AddDays(-30), endedAt = (DateTimeOffset?)now.AddDays(-30), autoRenew = false, deposit = 0L, canVisit = false };
            var contracts = out_.Concat(inn is null ? [] : [inn]).Append(ended).ToList();
            var islandListings = new List<object>(); if (IslandSale is { } islandSale) islandListings.Add(new { kind = "sale", target = "island", parcelId = Guid.Empty, islandId = IslandA, island = IslandName(IslandA), code = "", name = "Ilha Tropical", area = 65536.0, price = islandSale, periodDays = 0, exclusive = false, since = now }); if (IslandRent is { } islandRent) islandListings.Add(new { kind = "rent", target = "island", parcelId = Guid.Empty, islandId = IslandA, island = IslandName(IslandA), code = "", name = "Ilha Tropical", area = 65536.0, price = islandRent, periodDays = IslandDays, exclusive = false, since = now });
            var listings = Mine.Where(p => p.Sale is not null).Select(p => (object)new { kind = "sale", parcelId = p.Id, islandId = p.Island, island = IslandName(p.Island), code = p.Code, name = p.Name, area = 1024.0, price = p.Sale, periodDays = 0, exclusive = false, since = now.AddDays(-1) })
                .Concat(Mine.Where(p => p.Rent is not null && p.Tenant is null).Select(p => (object)new { kind = "rent", parcelId = p.Id, islandId = p.Island, island = IslandName(p.Island), code = p.Code, name = p.Name, area = 1024.0, price = p.Rent, periodDays = p.Days, exclusive = false, since = now.AddDays(-2) })).ToList();
            listings.AddRange(islandListings);
            var monthly = Mine.Where(p => p.Tenant is not null).Sum(p => (long)(p.Rent!.Value * 30.0 / p.Days));
            return new
            {
                summary = new { total = 1 + Mine.Count + (inn is null ? 0 : 1), inUse = 1 + Mine.Count(p => p.Tenant is null) + (inn is null ? 0 : 1), rentedOut = Mine.Count(p => p.Tenant is not null), monthlyRevenue = monthly, expiringSoon = (inn is null ? 0 : 1) },
                islands = new[] { new { id = IslandA, name = "Ilha Tropical", coverImageUrl = (string?)null, state = "online", visitors = 12, maxPlayers = 60, objects = 12450, maxObjects = 30000, size = 256, region = "full", salePrice = IslandSale, rentPrice = IslandRent, rentPeriodDays = IslandDays, isPublic = true, parcels = Mine.Count(p => p.Island == IslandA), createdAt = now, hosting = new { plan = "Plano Ouro", status = "active", expiresAt = now.AddDays(10), recoveryUntil = now.AddDays(17), maxPlayers = 80, maxObjects = 20000 } } },
                parcels, contracts, listings,
                payments = new[] { new { id = Guid.NewGuid(), at = now.AddDays(-3), type = "sale", direction = "in", amount = 60000L, parcelId = Guid.NewGuid(), island = IslandName(IslandA), code = "L-04", parcel = "Casa do Bruno", other = "Bruno" }, new { id = Guid.NewGuid(), at = now.AddDays(-10), type = "rent_start", direction = "out", amount = 12500L, parcelId = TenantParcel, island = IslandName(IslandB), code = "L-09", parcel = "Loja da Praia", other = "Bruno" }, new { id = Guid.NewGuid(), at = now.AddDays(-4), type = "rent_payment", direction = "in", amount = 3000L, parcelId = Guid.NewGuid(), island = IslandName(IslandA), code = "L-03", parcel = "Centro", other = "Carla" } },
            };
        }

        private static (long, string) Ok(object? body = null) => (200, JsonSerializer.Serialize(body ?? new { }, Web));

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            if (path == "/api/v1/properties/mine") return Task.FromResult(Gone ? (404L, "") : Ok(Body()));
            if (path.StartsWith("/api/v1/properties/notices", StringComparison.Ordinal) && method == "GET") return Task.FromResult(Ok(new { unread = Notices.Count(n => !n.Read), items = Notices.Select(n => new { id = n.Id, kind = "rent_grace", title = n.Title, body = "Detalhes", at = DateTimeOffset.UtcNow, read = n.Read }) }));
            if (path == "/api/v1/properties/notices/read") { var ids = JsonNode.Parse(body!)!["ids"]?.AsArray().Select(x => Guid.Parse(x!.GetValue<string>())).ToHashSet(); for (var i = 0; i < Notices.Count; i++) if (ids is null || ids.Contains(Notices[i].Id)) Notices[i] = Notices[i] with { Read = true }; return Task.FromResult(Ok()); }
            var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
            if (path.EndsWith("/real-estate/sale", StringComparison.Ordinal)) { if (method == "DELETE") IslandSale = null; else { if (IslandRent is not null) return Task.FromResult<(long, string)>((409, "{\"error\":\"conflict\"}")); IslandSale = json!["price"]!.GetValue<long>(); } return Task.FromResult(Ok()); }
            if (path.EndsWith("/real-estate/rental-offer", StringComparison.Ordinal)) { if (method == "DELETE") IslandRent = null; else { if (IslandSale is not null) return Task.FromResult<(long, string)>((409, "{\"error\":\"conflict\"}")); IslandRent = json!["pricePerPeriod"]!.GetValue<long>(); IslandDays = json["periodDays"]!.GetValue<int>(); } return Task.FromResult(Ok()); }
            var m = System.Text.RegularExpressions.Regex.Match(path, @"/islands/([^/]+)/land/(parcels|rentals)/([^/]+)/(sale|rental-offer|renew|end)$");
            if (!m.Success) return Task.FromResult<(long, string)>((404, "{}"));
            var id = Guid.Parse(m.Groups[3].Value); var action = m.Groups[4].Value;
            if (action == "renew") { if (id != TenantRental) return Task.FromResult<(long, string)>((404, "{}")); return Task.FromResult(Ok()); }
            if (action == "end") { if (id == TenantRental) { TenantRentalLive = false; return Task.FromResult(Ok()); } var owned = Mine.FirstOrDefault(p => p.Rental == id); if (owned is null) return Task.FromResult<(long, string)>((404, "{}")); if (owned.Expires > DateTimeOffset.UtcNow) return Task.FromResult<(long, string)>((409, "{\"error\":\"still_running\"}")); owned.Tenant = null; owned.Rental = null; owned.Rent = null; return Task.FromResult(Ok()); }
            var p = Mine.FirstOrDefault(x => x.Id == id); if (p is null) return Task.FromResult<(long, string)>((404, "{}"));
            if (action == "sale") { if (method == "DELETE") p.Sale = null; else { if (p.Rent is not null) return Task.FromResult<(long, string)>((409, "{\"error\":\"conflict\"}")); p.Sale = json!["price"]!.GetValue<long>(); } return Task.FromResult(Ok()); }
            if (method == "DELETE") p.Rent = null; else { if (p.Sale is not null) return Task.FromResult<(long, string)>((409, "{\"error\":\"conflict\"}")); p.Rent = json!["pricePerPeriod"]!.GetValue<long>(); p.Days = json["periodDays"]!.GetValue<int>(); }
            return Task.FromResult(Ok());
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server();
            server.Mine.Add(new P { Island = IslandA, Code = "L-01", Name = "Residencial Norte" });
            server.Mine.Add(new P { Island = IslandA, Code = "L-02", Name = "Residencial Sul", Sale = 120000 });
            server.Mine.Add(new P { Island = IslandA, Code = "L-03", Name = "Centro Comercial", Category = "comercial", Tenant = "Carla", Rental = Guid.NewGuid(), Rent = 3000, Expires = DateTimeOffset.UtcNow.AddDays(-1) });
            var window = new MyPropertiesWindow { Name = "MyPropertiesWindow" }; Guid? visited = null, manageIsland = null, manageLand = null;
            window.Visit = id => visited = id; window.ManageIsland = id => manageIsland = id; window.ManageLand = id => manageLand = id;
            window.UseTransportForTesting(server.Handle); AddChild(window); await Frames(3);

            // --- the summary and the tabs ------------------------------------------------------------------------------------
            await window.OpenAsync(); await Frames(3);
            Require(window.IsOpen && window.TabForTesting == "ilhas", "the window opens on the islands");
            Require(string.Join("|", window.TabTextsForTesting) == "Minhas Ilhas|Meus Terrenos|Meus Aluguéis|Propriedades Alugadas|Meus Anúncios|Pagamentos|Contratos", $"the seven tabs (got {string.Join("|", window.TabTextsForTesting)})");
            var kpi = window.KpiValuesForTesting; Require(string.Join("|", kpi) == "5|4|1|L$ 3.000|1", $"the summary is the server's: total, in use, rented out, monthly revenue, expiring (got {string.Join("|", kpi)})");
            Require(string.Join("|", window.KpiTitlesForTesting) == "Imóveis Totais|Em Uso|Alugados|Receita Mensal|Próximos Vencimentos", "the five indicators");

            // --- islands --------------------------------------------------------------------------------------------------------
            var text = window.TextForTesting; Require(text.Contains("Minhas Ilhas (1)") && text.Contains("Ilha Tropical") && text.Contains("12.450 / 30.000") && text.Contains("12 / 60") && text.Contains("Online"), $"the island card shows name, prims, visitors and state (got {text})");
            Require(text.Contains("Hospedagem: Plano Ouro, até") && window.Data.Islands[0].Hosting?.MaxPlayers == 80, $"the island card shows the hosting plan (got {text})");
            Require(window.ButtonEnabledForTesting("Gerenciar") && window.ButtonEnabledForTesting("Visitar") && window.ButtonEnabledForTesting("Anunciar"), "manage, visit and offer work for an island with no offer");
            window.PressForTesting("Gerenciar"); Require(manageIsland == IslandA, "Gerenciar opens the island management"); window.PressForTesting("Visitar"); Require(visited == IslandA && !window.IsOpen, "Visitar goes to the island and closes the window");
            await window.OpenAsync(); await Frames(3);

            // --- offering the island itself ---------------------------------------------------------------------------------------------
            var theIsland = window.Data.Islands[0];
            Require(await window.OfferIslandSaleForTesting(theIsland, 400000), "offering the island for sale works"); await Frames(3);
            Require(server.IslandSale == 400000 && window.TextForTesting.Contains("À venda por L$ 400.000") && window.HasButtonForTesting("Retirar anúncio") && window.Data.Listings.Any(l => l.Target == "island" && l.Kind == "sale"), "the island shows its offer and a withdraw button, and the offer is listed");
            window.SelectTabForTesting("anuncios"); await Frames(2); Require(window.RowsForTesting("anuncios") == 2 && window.TextForTesting.Contains("Ilha Tropical"), "the island offer is in the offers tab");
            Require(await window.WithdrawForTesting(window.Data.Listings.First(l => l.Target == "island")), "withdrawing the island offer works"); await Frames(3); Require(server.IslandSale is null, "the island is off the market");
            Require(await window.OfferIslandRentForTesting(window.Data.Islands[0], 9000, 30), "renting the island out works"); await Frames(3); Require(server.IslandRent == 9000, "the island rent offer is saved");
            Require(await window.WithdrawForTesting(window.Data.Listings.First(l => l.Target == "island")), "and withdrawn"); await Frames(3); Require(server.IslandRent is null, "gone again");
            window.SelectTabForTesting("ilhas"); await Frames(2);

            // --- parcels ---------------------------------------------------------------------------------------------------------
            Require(window.Data.Parcels.Single(p => p.Code == "L-01").Image.StartsWith("asset:", StringComparison.Ordinal) && window.Data.Parcels.Where(p => p.Code != "L-01").All(p => p.Image.Length == 0), "the picture of a parcel comes with it");
            window.SelectTabForTesting("terrenos"); await Frames(2); text = window.TextForTesting;
            Require(window.RowsForTesting("terrenos") == 3 && text.Contains("Meus Terrenos (3)") && text.Contains("L-01 — Residencial Norte") && text.Contains("À venda por L$ 120.000") && text.Contains("Alugado para Carla"), $"the parcels with their offers and tenant (got {text})");
            window.PressForTesting("Gerenciar"); Require(manageLand == IslandA, "Gerenciar opens the land management of that island");

            // --- offering and withdrawing --------------------------------------------------------------------------------------------
            var free = window.Data.Parcels.First(p => p.Code == "L-01"); var rented = window.Data.Parcels.First(p => p.Code == "L-03");
            Require(await window.OfferSaleForTesting(free, 50000), "offering for sale works"); await Frames(3);
            Require(server.Mine[0].Sale == 50000 && window.Data.Listings.Any(l => l.Code == "L-01" && l.Kind == "sale"), "the offer is saved and listed");
            Require(!await window.OfferRentForTesting(free, 500, 14), "a parcel cannot be for sale and for rent at once"); await Frames(2); Require(window.ToastForTesting.Length > 0, $"and the person is told (got {window.ToastForTesting})");
            window.SelectTabForTesting("anuncios"); await Frames(2); Require(window.RowsForTesting("anuncios") == 2, "two offers in the offers tab");
            Require(await window.WithdrawForTesting(window.Data.Listings.First(l => l.Code == "L-01")), "withdrawing works"); await Frames(3);
            Require(server.Mine[0].Sale is null && window.Data.Listings.Count == 1, "the offer is gone");
            Require(await window.OfferRentForTesting(free, 500, 14), "offering for rent works"); await Frames(3); Require(server.Mine[0].Rent == 500 && server.Mine[0].Days == 14, "the rent terms are saved");
            _ = rented;

            // --- rentals out -----------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("alugueis"); await Frames(2); Require(window.RowsForTesting("alugueis") == 1, "one parcel rented out");
            Require(window.TextForTesting.Contains("Carla") && window.TextForTesting.Contains("L$ 3.000"), "the tenant and the value are shown");
            var landlord = window.Data.Contracts.First(c => c.Role == "landlord");
            Require(await window.EndForTesting(landlord), "an expired contract can be ended"); await Frames(3); Require(server.Mine[2].Tenant is null && window.Data.Summary.RentedOut == 0, "the parcel is free again");

            // --- rentals in -------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("alugadas"); await Frames(2); Require(window.RowsForTesting("alugadas") == 1 && window.TextForTesting.Contains("Loja da Praia") && window.TextForTesting.Contains("Bruno"), "the one parcel she rents, from Bruno");
            Require(window.ButtonEnabledForTesting("Renovar") && window.ButtonEnabledForTesting("Sair") && window.ButtonEnabledForTesting("Visitar"), "renew, leave and visit are there");
            var tenant = window.Data.Contracts.First(c => c.Role == "tenant" && c.Live);
            Require(await window.RenewForTesting(tenant, 2), "renewing works"); await Frames(2); Require(server.Calls.Any(c => c.EndsWith($"/rentals/{tenant.Id}/renew", StringComparison.Ordinal)) && window.ToastForTesting.Contains("renovado"), "the renew request was sent and the person is told");
            Require(await window.EndForTesting(tenant), "leaving works"); await Frames(3); Require(window.RowsForTesting("alugadas") == 0 && window.ToastForTesting.Contains("saiu"), "the rental is gone from the list");

            // --- payments -----------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("pagamentos"); await Frames(2); Require(window.RowsForTesting("pagamentos") == 3 && window.TextForTesting.Contains("Recebido: L$ 63.000") && window.TextForTesting.Contains("Pago: L$ 12.500"), $"payments and totals (got {window.TextForTesting})");
            window.SetPaymentFilterForTesting("sell"); await Frames(2); Require(window.RowsForTesting("pagamentos") == 1, "filter: sales");
            window.SetPaymentFilterForTesting("rent_out"); await Frames(2); Require(window.RowsForTesting("pagamentos") == 1, "filter: rent paid");
            window.SetPaymentFilterForTesting("rent_in"); await Frames(2); Require(window.RowsForTesting("pagamentos") == 1, "filter: rent received");
            window.SetPaymentFilterForTesting("buy"); await Frames(2); Require(window.RowsForTesting("pagamentos") == 0, "filter: purchases");
            window.SetPaymentFilterForTesting("all"); await Frames(2);
            var receipt = window.ReceiptForTesting(window.Data.Payments[0]); Require(receipt.StartsWith("RECIBO ", StringComparison.Ordinal) && receipt.Contains("L$ 60.000") && receipt.Contains("Casa do Bruno") && receipt.Contains("Taxas: nenhuma"), $"the receipt carries the payment (got {receipt})");

            // --- contracts -----------------------------------------------------------------------------------------------------------------
            server.TenantRentalLive = true; await window.LoadAsync(); await Frames(2);
            window.SelectTabForTesting("contratos"); await Frames(2); Require(window.RowsForTesting("contratos") >= 2, "contracts listed");
            var all = window.RowsForTesting("contratos"); window.SetContractFilterForTesting("ended"); await Frames(2); Require(window.RowsForTesting("contratos") >= 1 && window.RowsForTesting("contratos") < all, "filter: ended");
            window.SetContractFilterForTesting("live"); await Frames(2); Require(window.RowsForTesting("contratos") == all - window.Data.Contracts.Count(c => !c.Live), "filter: in force");

            // --- notices -----------------------------------------------------------------------------------------------------------------------
            Require(window.NoticeStripForTesting.Length == 0 && window.UnreadForTesting == 0, "no notices, no strip");
            var told = new List<string>(); window.NoticeArrived = told.Add;
            var firstNotice = Guid.NewGuid(); server.Notices.Add((firstNotice, "Aluguel vencido", false));
            Require(await window.PollNoticesForTesting() == 1 && told.Count == 1 && told[0] == "Aluguel vencido", "a new notice is told once");
            Require(window.UnreadForTesting == 1 && window.NoticeStripForTesting.Contains("1 aviso novo"), $"the strip says it (got {window.NoticeStripForTesting})");
            Require(await window.PollNoticesForTesting() == 0 && told.Count == 1, "and not told again");
            server.Notices.Add((Guid.NewGuid(), "Contrato terminou", false)); await window.PollNoticesForTesting();
            Require(window.UnreadForTesting == 2 && window.NoticeStripForTesting.Contains("2 avisos novos") && told.Count == 2, "two unread");
            await window.MarkNoticesReadForTesting(firstNotice); Require(window.UnreadForTesting == 1 && server.Notices[0].Read, "one marked read");
            window.PressNoticesForTesting("Marcar como Lidos"); await Frames(4); await Task.Delay(50); await Frames(2);
            Require(window.UnreadForTesting == 0 && window.NoticeStripForTesting.Length == 0 && server.Notices.All(n => n.Read), "all read, the strip goes away");

            // --- trouble ---------------------------------------------------------------------------------------------------------------------
            server.Gone = true; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Contains("ainda não tem as suas propriedades"), $"an old server is told apart (got {window.NoteForTesting})");
            server.Gone = false; server.Down = true; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Contains("Sem conexão"), "no connection is said");
            server.Down = false; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Length == 0, "trying again recovers");

            foreach (var view in new[] { new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1920, 1080), new Vector2(2560, 1440) })
            { window.SetViewportForTesting(view); await Frames(2); var size = window.PanelSizeForTesting; Require(size.X <= view.X - 31 && size.Y <= view.Y - 31, $"the window fits {view} (got {size})"); }
            window.SetViewportForTesting(new Vector2(1920, 1080)); await Frames(2);

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); window.SetViewportForTesting(new Vector2(1536, 1024)); await Frames(6);
                server.TenantRentalLive = true; await window.LoadAsync(); window.SelectTabForTesting(OS.GetEnvironment("LIFEVERSE_SECTION") is { Length: > 0 } section ? section : "terrenos"); await Frames(8);
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }
            GD.Print("event=my_properties_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=my_properties_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
