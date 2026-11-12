using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The Terrenos e Parcelas and Vendas e Aluguéis tabs of the island window against a pretend server: the indicators are the server's numbers, the little map has the real parcels, the contracts are listed,
/// the buttons open the land management window on the right section (and start an offer), and a person who is not staff or an old server is told apart.
/// </summary>
public partial class IslandLandPagesSmoke : Node3D
{
    private static readonly Guid Island = Guid.NewGuid();

    private sealed class Server
    {
        public bool Staff = true, Gone, Empty;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static object Parcel(string code, string name, string kind, int cx, int cz, long? sale = null, long? rent = null) => new
        {
            id = Guid.NewGuid(), code, name, description = "", category = "residencial", kind, area = 256.0, cells = 16, runs = new[] { new[] { cz, cx, cx + 3 }, new[] { cz + 1, cx, cx + 3 }, new[] { cz + 2, cx, cx + 3 }, new[] { cz + 3, cx, cx + 3 } }, ownerId = Guid.NewGuid(), owner = "AliceVerse", ownedByIslandOwner = true,
            salePrice = sale, saleExclusive = false, rentPrice = rent, rentPeriodDays = 30, tenantId = (Guid?)null, tenant = (string?)null, rentStatus = (string?)null, rentExpiresAt = (DateTimeOffset?)null, rentalId = (Guid?)null,
            maxObjects = 200, objects = 20, cost = 60, maxCost = 0, scripts = false, builders = 0, accessMode = "public", buildMode = "owner", createdAt = DateTimeOffset.UtcNow, revision = 1,
        };

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            if (!path.EndsWith("/land/overview", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((404, "{}"));
            if (Gone) return Task.FromResult<(long, string)>((404, ""));
            if (!Staff) return Task.FromResult<(long, string)>((403, "{\"error\":\"forbidden\"}"));
            var parcels = Empty ? [] : new[] { Parcel("L-01", "Casa", "available", -30, -30), Parcel("L-02", "Loja", "sale", -24, -30, 90000), Parcel("L-03", "Galeria", "rented", -18, -30), Parcel("L-04", "Sítio", "sold", -30, -24), Parcel("L-05", "Praça", "common", -24, -24), Parcel("L-06", "Quiosque", "available", -18, -24, null, 700) };
            var contracts = Empty ? [] : new[] { new { id = Guid.NewGuid(), parcelId = Guid.NewGuid(), code = "L-03", parcel = "Galeria", landlord = "AliceVerse", tenant = "Carla", price = 3000L, periodDays = 30, status = "active", startsAt = DateTimeOffset.UtcNow.AddDays(-5), expiresAt = DateTimeOffset.UtcNow.AddDays(5), endedAt = (DateTimeOffset?)null, autoRenew = false, deposit = 0L }, new { id = Guid.NewGuid(), parcelId = Guid.NewGuid(), code = "L-09", parcel = "Antiga", landlord = "AliceVerse", tenant = "Duda", price = 500L, periodDays = 7, status = "ended", startsAt = DateTimeOffset.UtcNow.AddDays(-50), expiresAt = DateTimeOffset.UtcNow.AddDays(-40), endedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-40), autoRenew = false, deposit = 0L } };
            var view = new
            {
                island = new { id = Island, name = "Ilha Tropical", size = 256, coverImageUrl = (string?)null, runtimeState = "online", isPublic = true, owner = "AliceVerse", ownerId = Guid.NewGuid() },
                totals = new { area = 65536.0, areaParcelled = Empty ? 0.0 : 1536.0, areaFree = Empty ? 65536.0 : 64000.0, parcels = parcels.Length, available = Empty ? 0 : 2, onSale = Empty ? 0 : 1, rented = Empty ? 0 : 1, sold = Empty ? 0 : 1, common = Empty ? 0 : 1, monthlyRevenue = Empty ? 0L : 3000L, expiringSoon = Empty ? 0 : 1, objectsLimit = 5000, objectsUsed = 120, objectsShared = 1200 },
                categories = new[] { "residencial", "comercial" }, version = 1L, parcels, contracts, history = Array.Empty<object>(),
            };
            return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(view, Web)));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server(); var opened = new List<(Guid Island, string Section, bool Offer)>();
            var land = new IslandLandPage { Name = "Land", Theme = ViewerTheme.Create() }; var deals = new IslandDealsPage { Name = "Deals", Theme = ViewerTheme.Create() };
            foreach (var page in new IslandLandPageBase[] { land, deals }) { page.Bind(new IslandManagementApi(server.Handle)); page.SetIsland(Island); page.OpenLandManagement = (island, section, offer) => opened.Add((island, section, offer)); }
            AddChild(land); AddChild(deals); await Frames(3);

            // --- Terrenos e Parcelas ----------------------------------------------------------------------------------------------------
            await land.LoadAsync(); await Frames(3); var text = land.TextForTesting;
            Require(land.StatusForTesting.Length == 0 && text.Contains("65.536 m²") && text.Contains("1.536 m²") && text.Contains("(2,3%)") && text.Contains("64.000 m²") && text.Contains("(97,7%)"), $"the area indicators are the server's numbers (got {text})");
            Require(string.Join("|", land.KpiTitlesForTesting) == "Área Total|Área Parcelada|Área Disponível|Parcelas|Vendidas|Alugadas|Disponíveis" && land.Loaded!.Totals.Parcels == 6 && land.Loaded.Totals.Sold == 1 && land.Loaded.Totals.Rented == 1 && land.Loaded.Totals.Available == 2, "the seven indicators");
            Require(land.MapParcelsForTesting == 6, "the little map has the real parcels");
            Require(text.Contains("À venda agora:") && text.Contains("Áreas comuns:") && text.Contains("5.000") && text.Contains("1.200") && text.Contains("120"), "sale, common areas and the prims shared out");
            Require(land.HasButtonForTesting("Abrir Gerenciamento de Terrenos"), "there is a way to the land management window");
            land.PressForTesting("Abrir Gerenciamento de Terrenos"); Require(opened.Count == 1 && opened[0] == (Island, "visao", false), "it opens the overview of this island");

            // --- Vendas e Aluguéis ----------------------------------------------------------------------------------------------------------
            await deals.LoadAsync(); await Frames(3); text = deals.TextForTesting;
            Require(string.Join("|", deals.KpiTitlesForTesting) == "Anúncios Ativos|Parcelas à Venda|Parcelas Alugadas|Contratos Ativos|Receita Mensal|Próximos Vencimentos", "the six indicators");
            Require(text.Contains("L$ 3.000") && text.Contains("(em 7 dias)"), $"revenue and the next due dates (got {text})");
            Require(deals.Loaded!.Parcels.Count(p => p.SalePrice is not null || (p.RentPrice is not null && p.Kind != "rented")) == 2, "two offers are active: one sale and one rent offer");
            Require(deals.ContractRowsForTesting == 1 && text.Contains("L-03 — Galeria") && text.Contains("Carla") && text.Contains("Ativo") && !text.Contains("Duda"), "only the contracts in force are listed");
            deals.PressForTesting("Gerenciar Vendas"); deals.PressForTesting("Gerenciar Aluguéis"); deals.PressForTesting("Criar Anúncio");
            Require(opened.Skip(1).SequenceEqual([(Island, "vendas", false), (Island, "alugueis", false), (Island, "vendas", true)]), "the three buttons open sales, rentals, and a new offer");

            // --- an island with no land ---------------------------------------------------------------------------------------------------------
            server.Empty = true; await land.LoadAsync(); await deals.LoadAsync(); await Frames(3);
            Require(land.TextForTesting.Contains("ainda não tem terrenos demarcados") && land.MapParcelsForTesting == 0 && deals.TextForTesting.Contains("Nenhum contrato de aluguel em andamento") && deals.ContractRowsForTesting == 0, "an island with no land says so");
            server.Empty = false;

            // --- trouble ------------------------------------------------------------------------------------------------------------------------------
            server.Staff = false; await land.LoadAsync(); await Frames(2); Require(land.StatusForTesting.Contains("Só o dono e os administradores") && land.Loaded is null, $"somebody who is not staff is told so (got {land.StatusForTesting})");
            server.Staff = true; server.Gone = true; await deals.LoadAsync(); await Frames(2); Require(deals.StatusForTesting.Contains("ainda não tem os dados de terrenos"), $"an old server is told apart (got {deals.StatusForTesting})");
            server.Gone = false; await deals.LoadAsync(); await Frames(2); Require(deals.StatusForTesting.Length == 0 && deals.Loaded is not null, "trying again recovers");

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); server.Staff = true; server.Gone = false; await land.LoadAsync(); await deals.LoadAsync(); var which = OS.GetEnvironment("LIFEVERSE_SECTION") == "deals" ? (Control)deals : land; land.Visible = which == land; deals.Visible = which == deals; which.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); await Frames(10);
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }
            GD.Print("event=island_land_pages_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=island_land_pages_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
