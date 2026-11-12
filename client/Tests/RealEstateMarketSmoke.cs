using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.RealEstate;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The real estate market window against a pretend server that filters, sorts and pages: tabs and filters turn into the right query, the cards show what the server sent, the pages work, the list view works,
/// buying and renting send the price the person saw and say what happened, what cannot be taken is disabled, visiting goes to the island, and a server without the market is told so.
/// </summary>
public partial class RealEstateMarketSmoke : Node3D
{
    private sealed record L(Guid Parcel, Guid Island, string IslandName, string Code, string Name, string Category, double Area, int Objects, string Offer, string State, long Price, int Days, bool Visit, DateTimeOffset At);

    private sealed class Server
    {
        public readonly List<L> All = [];
        public bool Down; public bool Gone; public string? LastQuery; public readonly List<string> Calls = [];
        public bool Insufficient;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            if (path.StartsWith("/api/v1/discovery/market?", StringComparison.Ordinal))
            {
                if (Gone) return Task.FromResult<(long, string)>((404, ""));
                LastQuery = path[(path.IndexOf('?') + 1)..];
                var q = LastQuery.Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x.Length > 1 ? x[1] : ""));
                var rows = All.Where(l => q.GetValueOrDefault("offer", "any") is "any" || l.Offer == q["offer"]).Where(l => !q.ContainsKey("category") || l.Category == q["category"])
                    .Where(l => q.GetValueOrDefault("onlyAvailable") != "true" || l.State != "rented").Where(l => q.GetValueOrDefault("withVisit") != "true" || l.Visit)
                    .Where(l => !q.ContainsKey("q") || l.Name.Contains(q["q"], StringComparison.OrdinalIgnoreCase) || l.IslandName.Contains(q["q"], StringComparison.OrdinalIgnoreCase))
                    .Where(l => !q.ContainsKey("minPrice") || l.Price >= long.Parse(q["minPrice"])).Where(l => !q.ContainsKey("maxPrice") || l.Price <= long.Parse(q["maxPrice"]))
                    .Where(l => !q.ContainsKey("minArea") || l.Area >= double.Parse(q["minArea"], System.Globalization.CultureInfo.InvariantCulture)).ToList();
                rows = q.GetValueOrDefault("sort") switch { "price" => [.. rows.OrderBy(l => l.Price)], "price_desc" => [.. rows.OrderByDescending(l => l.Price)], "area" => [.. rows.OrderByDescending(l => l.Area)], "prims" => [.. rows.OrderByDescending(l => l.Objects)], _ => [.. rows.OrderByDescending(l => l.At)] };
                var size = int.Parse(q["pageSize"]); var pages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)size)); var page = Math.Clamp(int.Parse(q["page"]), 1, pages);
                var items = rows.Skip((page - 1) * size).Take(size).Select(l => new { parcelId = l.Parcel, islandId = l.Island, islandName = l.IslandName, islandCover = (string?)null, code = l.Code, name = l.Name, description = "", category = l.Category, area = l.Area, maxObjects = l.Objects, offer = l.Offer, state = l.State, price = l.Price, rentPeriodDays = l.Days, ownerName = "Bruno", canVisit = l.Visit, updatedAt = l.At, rentMinPeriods = 1, rentMaxPeriods = 6, rentDeposit = l.Offer == "rent" ? 500L : 0L });
                return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { total = rows.Count, page, pageSize = size, items }, Web)));
            }
            var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
            var m = System.Text.RegularExpressions.Regex.Match(path, @"/islands/([^/]+)/land/parcels/([^/]+)/(buy|rent)$");
            if (m.Success)
            {
                var l = All.FirstOrDefault(x => x.Parcel == Guid.Parse(m.Groups[2].Value)); if (l is null) return Task.FromResult<(long, string)>((409, "{\"error\":\"conflict\"}"));
                if (Insufficient) return Task.FromResult<(long, string)>((409, "{\"error\":\"insufficient_balance\"}"));
                if (json!["expectedPrice"]!.GetValue<long>() != l.Price) return Task.FromResult<(long, string)>((409, "{\"error\":\"price_changed\"}"));
                if (m.Groups[3].Value == "buy") All.Remove(l); else All[All.IndexOf(l)] = l with { State = "rented" };
                return Task.FromResult<(long, string)>((200, "{}"));
            }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            string[] cats = ["residencial", "comercial", "comercial", "mista"];
            for (var i = 0; i < 20; i++)
            {
                var rent = i % 5 == 4;
                server.All.Add(new L(Guid.NewGuid(), i % 2 == 0 ? a : b, i % 2 == 0 ? "Ilha Tropical" : "Ilha Serenidade", $"L-{i + 1:00}", $"Terreno {i + 1}", cats[i % 4], 1000 + i * 500, 2000 + i * 100, rent ? "rent" : "sale", i == 3 ? "rented" : rent ? "rent" : "sale", 10000 + i * 1000, 30, i != 7, now.AddMinutes(-i)));
            }
            server.All[3] = server.All[3] with { Offer = "rent" };
            var window = new RealEstateMarketWindow { Name = "RealEstateMarketWindow" };
            Guid? visited = null; window.Visit = id => visited = id;
            window.UseTransportForTesting(server.Handle); AddChild(window); await Frames(3);

            // --- opening and the tabs ----------------------------------------------------------------------------------
            await window.OpenAsync(); await Frames(3);
            Require(window.IsOpen && window.TabForTesting == "terrenos", "the market opens on the land tab");
            Require(string.Join("|", window.TabTextsForTesting) == "Ilhas à Venda|Ilhas para Alugar|Homesteads|Terrenos|Comerciais|Residenciais|Todos", $"the seven tabs (got {string.Join("|", window.TabTextsForTesting)})");
            Require(!window.TabEnabledForTesting("ilhas_venda") && !window.TabEnabledForTesting("ilhas_aluguel") && !window.TabEnabledForTesting("homesteads") && window.TabEnabledForTesting("comerciais"), "the island tabs wait for the island sale; the land tabs work");
            Require(server.LastQuery!.Contains("offer=any") && server.LastQuery.Contains("onlyAvailable=true") && server.LastQuery.Contains("page=1") && server.LastQuery.Contains("pageSize=9"), $"the first query is what the filters say ({server.LastQuery})");
            Require(window.ListingsForTesting.Count == 9 && window.TotalTextForTesting == "Resultados (19 imóveis)", $"nine cards of the first page and the total of 19 (got {window.ListingsForTesting.Count}, {window.TotalTextForTesting})");
            Require(window.ListingsForTesting[0] == "L-01 — Terreno 1", "the newest comes first");
            var badge = window.BadgeForTesting(0); Require(badge.Contains("Terreno Residencial") && badge.Contains("Ilha Tropical") && badge.Contains("À Venda") && badge.Contains("1.000 m²") && badge.Contains("2.000 prims") && badge.Contains("L$ 10.000"), $"a card shows type, island, state, area, prims and price (got {badge})");

            // --- pages -------------------------------------------------------------------------------------------------------------
            Require(string.Join(",", window.PagesForTesting) == "‹,1,2,3,›", $"three pages (got {string.Join(",", window.PagesForTesting)})");
            await window.GoToPageForTesting(3); await Frames(2);
            Require(window.ListingsForTesting.Count == 1 && server.LastQuery!.Contains("page=3"), "the last page has the rest");
            await window.GoToPageForTesting(1); await Frames(2);

            // --- tabs and filters ----------------------------------------------------------------------------------------------------
            await window.ClickTabForTesting("comerciais"); await Frames(2);
            Require(server.LastQuery!.Contains("category=comercial") && window.ListingsForTesting.Count > 0, $"the commercial tab asks for commercial land ({server.LastQuery})");
            await window.ClickTabForTesting("residenciais"); await Frames(2); Require(server.LastQuery!.Contains("category=residencial"), "the residential tab");
            await window.ClickTabForTesting("todos"); await Frames(2);
            window.SetFiltersForTesting(deal: 2); await window.SearchForTesting(); await Frames(2);
            Require(server.LastQuery!.Contains("offer=rent") && window.TotalTextForTesting == "Resultados (4 imóveis)", $"rent only (got {window.TotalTextForTesting}, {server.LastQuery})");
            window.SetFiltersForTesting(deal: 0, minPrice: "20000", maxPrice: "25.000", minArea: "2.000"); await window.SearchForTesting(); await Frames(2);
            Require(server.LastQuery!.Contains("minPrice=20000") && server.LastQuery.Contains("maxPrice=25000") && server.LastQuery.Contains("minArea=2000"), $"price and area become numbers ({server.LastQuery})");
            window.SetFiltersForTesting(minPrice: "", maxPrice: "", minArea: "", query: "serenidade"); await window.SearchForTesting(); await Frames(2);
            Require(window.ListingsForTesting.Count > 0 && server.LastQuery!.Contains("q=serenidade"), "the text search is sent");
            window.SetFiltersForTesting(query: "nada disso existe"); await window.SearchForTesting(); await Frames(2);
            Require(window.ListingsForTesting.Count == 0 && window.NoteForTesting.Contains("Nenhum imóvel"), $"no results says so (got {window.NoteForTesting})");
            await window.ClearForTesting(); await window.SearchForTesting(); await Frames(2);
            Require(window.TotalTextForTesting == "Resultados (19 imóveis)", "clearing the filters brings everything back");
            window.SetFiltersForTesting(onlyAvailable: false); await window.SearchForTesting(); await Frames(2);
            Require(window.TotalTextForTesting == "Resultados (20 imóveis)" && server.LastQuery!.Contains("onlyAvailable=false"), "turning off “only available” also lists the rented");
            window.SetFiltersForTesting(onlyAvailable: true, withVisit: true); await window.SearchForTesting(); await Frames(2);
            Require(server.LastQuery!.Contains("withVisit=true") && window.TotalTextForTesting == "Resultados (18 imóveis)", $"only land people can visit (got {window.TotalTextForTesting})");
            window.SetFiltersForTesting(withVisit: false);

            // --- sorting and the list view ---------------------------------------------------------------------------------------------
            await window.SortForTesting(1); await Frames(2); Require(server.LastQuery!.Contains("sort=price") && window.ListingsForTesting[0] == "L-01 — Terreno 1", "cheapest first");
            await window.SortForTesting(2); await Frames(2); Require(server.LastQuery!.Contains("sort=price_desc") && window.ListingsForTesting[0] == "L-20 — Terreno 20", "dearest first");
            await window.SortForTesting(3); await Frames(2); Require(server.LastQuery!.Contains("sort=area"), "by area");
            await window.SortForTesting(4); await Frames(2); Require(server.LastQuery!.Contains("sort=prims"), "by prims");
            window.ToggleViewForTesting(true); await Frames(3); Require(window.AsListForTesting && window.ColumnsForTesting == 1 && window.ListingsForTesting.Count == 9, "the list view shows the same listings in one column");
            window.ToggleViewForTesting(false); await Frames(3); Require(window.ColumnsForTesting >= 2, "the grid has columns again");
            await window.SortForTesting(0); await Frames(2);

            // --- cards: what can be taken ---------------------------------------------------------------------------------------------------
            window.SetFiltersForTesting(onlyAvailable: false); await window.SearchForTesting(); await Frames(2);
            var rentedIndex = window.ListingsForTesting.ToList().FindIndex(t => t.StartsWith("L-04", StringComparison.Ordinal));
            Require(rentedIndex >= 0 && window.PrimaryForTesting(rentedIndex) == ("Alugado", true), "rented land has a disabled “Alugado” button");
            Require(window.BadgeForTesting(rentedIndex).Contains("Alugado"), "and says it is rented");
            var noVisit = window.ListingsForTesting.ToList().FindIndex(t => t.StartsWith("L-08", StringComparison.Ordinal)); Require(noVisit >= 0 && !window.VisitEnabledForTesting(noVisit), "land that takes no visits has Visitar disabled");
            window.SetFiltersForTesting(onlyAvailable: true); await window.SearchForTesting(); await Frames(2);

            // --- visiting -------------------------------------------------------------------------------------------------------------------
            window.VisitForTesting(0); Require(visited == server.All[0].Island && !window.IsOpen, "visiting goes to the island and closes the market");
            await window.OpenAsync(); await Frames(3);

            // --- buying ---------------------------------------------------------------------------------------------------------------------
            var firstSale = server.All.First(l => l.Offer == "sale" && l.State == "sale"); var before = server.All.Count;
            var index = window.ListingsForTesting.ToList().FindIndex(t => t.StartsWith(firstSale.Code, StringComparison.Ordinal)); Require(index >= 0 && window.PrimaryForTesting(index) == ("Comprar", false), "a sale card has Comprar");
            server.Insufficient = true; Require(!await window.BuyForTesting(index), "buying without money fails"); await Frames(2); Require(window.ToastForTesting.Contains("Saldo insuficiente"), $"and says why (got {window.ToastForTesting})");
            server.Insufficient = false; Require(await window.BuyForTesting(index), "buying works"); await Frames(3);
            Require(server.All.Count == before - 1 && window.ToastForTesting.Contains("Terreno comprado") && !window.ListingsForTesting.Any(t => t.StartsWith(firstSale.Code, StringComparison.Ordinal)), "the land is bought, the list refreshes without it");
            Require(server.Calls.Any(c => c.EndsWith($"/land/parcels/{firstSale.Parcel}/buy", StringComparison.Ordinal)), "the buy request went to the island of the land");

            // --- renting ---------------------------------------------------------------------------------------------------------------------
            window.SetFiltersForTesting(deal: 2); await window.SearchForTesting(); await Frames(2);
            Require(window.PrimaryForTesting(0) == ("Alugar", false) && window.BadgeForTesting(0).Contains("/ mensal"), $"a rent card shows the monthly price and Alugar (got {window.BadgeForTesting(0)})");
            var rentCode = window.ListingsForTesting[0].Split(' ')[0]; Require(await window.RentForTesting(0, 2), "renting works"); await Frames(3);
            Require(window.ToastForTesting.Contains("Terreno alugado") && server.All.First(l => l.Code == rentCode).State == "rented" && !window.ListingsForTesting.Any(t => t.StartsWith(rentCode, StringComparison.Ordinal)), "the rented land leaves the list of available");

            // --- trouble ---------------------------------------------------------------------------------------------------------------------------
            server.Gone = true; await window.SearchForTesting(); await Frames(2); Require(window.NoteForTesting.Contains("ainda não tem o mercado"), $"an old server is told apart (got {window.NoteForTesting})");
            server.Gone = false; server.Down = true; await window.SearchForTesting(); await Frames(2); Require(window.NoteForTesting.Contains("Sem conexão"), $"no connection is said (got {window.NoteForTesting})");
            server.Down = false; await window.SearchForTesting(); await Frames(2); Require(window.ListingsForTesting.Count > 0, "trying again recovers");

            // --- screens ---------------------------------------------------------------------------------------------------------------------------
            foreach (var view in new[] { new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1920, 1080), new Vector2(2560, 1440) })
            { window.SetViewportForTesting(view); await Frames(2); var size = window.PanelSizeForTesting; Require(size.X <= view.X - 31 && size.Y <= view.Y - 31, $"the window fits {view} (got {size})"); }
            window.SetViewportForTesting(new Vector2(1920, 1080)); await Frames(2);

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); window.SetViewportForTesting(new Vector2(1536, 1024)); await Frames(6);
                window.SetFiltersForTesting(deal: 0); await window.SearchForTesting(); await Frames(8);
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }
            GD.Print("event=real_estate_market_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=real_estate_market_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
