using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The land management window against a pretend server: the numbers and the colours come from the parcels it is sent, the map picks the right parcel, the buttons follow who owns what, selling, renting, editing, marking land,
/// creating, dividing and joining send the right requests and the window shows the answer, the tables list what they should, and somebody who is not staff is told so.
/// </summary>
public partial class LandManagementSmoke : Node3D
{
    private static readonly Guid Island = Guid.Parse("a7f3e9d2-4b5e-4c3a-9f82-6e1b23d9f0a1");
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111"), Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class P
    {
        public Guid Id = Guid.NewGuid(); public string Code = "", Name = "", Category = "residencial"; public Guid Owner = Me; public HashSet<long> Cells = []; public long? Sale; public long? Rent; public int Days = 30; public Guid? Tenant; public Guid? RentalId; public int Revision = 1; public int Objects;
    }

    private sealed class Server
    {
        public readonly List<P> Parcels = [];
        public bool Staff = true; public readonly List<string> Calls = [];
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public static HashSet<long> Rect(int x0, int z0, int w, int h) { var set = new HashSet<long>(); for (var x = 0; x < w; x++) for (var z = 0; z < h; z++) set.Add(LandMath.Key(x0 + x, z0 + z)); return set; }
        private static int[][] Runs(HashSet<long> cells) => [.. cells.Select(LandMath.Unkey).GroupBy(c => c.Cz).SelectMany(g => { var xs = g.Select(c => c.Cx).OrderBy(x => x).ToList(); var runs = new List<int[]>(); var start = xs[0]; var prev = xs[0]; foreach (var x in xs.Skip(1)) { if (x != prev + 1) { runs.Add([g.Key, start, prev]); start = x; } prev = x; } runs.Add([g.Key, start, prev]); return runs; })];
        private string Kind(P p) => p.Category == "comum" ? "common" : p.Tenant is not null ? "rented" : p.Sale is not null ? "sale" : p.Owner != Me ? "sold" : "available";
        private string NameOf(Guid id) => id == Me ? "AliceVerse" : id == Other ? "Bruno" : "Carla";

        private object Overview()
        {
            var area = 256.0 * 256; var parcelled = Parcels.Sum(p => p.Cells.Count * 16.0); var kinds = Parcels.GroupBy(Kind).ToDictionary(g => g.Key, g => g.Count());
            int K(string k) => kinds.GetValueOrDefault(k);
            var live = Parcels.Where(p => p.Tenant is not null).ToList();
            return new
            {
                island = new { id = Island, name = "Ilha Tropical", size = 256, coverImageUrl = (string?)null, runtimeState = "online", isPublic = true, owner = "AliceVerse", ownerId = Me },
                totals = new { area, areaParcelled = parcelled, areaFree = area - parcelled, parcels = Parcels.Count, available = K("available"), onSale = K("sale"), rented = K("rented"), sold = K("sold"), common = K("common"), monthlyRevenue = live.Sum(p => (long)(p.Rent!.Value * 30.0 / p.Days)), expiringSoon = 0, objectsLimit = 5000, objectsUsed = Parcels.Sum(p => p.Objects), objectsShared = Parcels.Count * 200 },
                categories = new[] { "residencial", "comercial", "mista", "social", "comum" }, version = Parcels.Sum(p => (long)p.Revision),
                parcels = Parcels.OrderBy(p => p.Code).Select(p => new
                {
                    id = p.Id, code = p.Code, name = p.Name, description = "", category = p.Category, kind = Kind(p), area = p.Cells.Count * 16.0, cells = p.Cells.Count, runs = Runs(p.Cells), ownerId = p.Owner, owner = NameOf(p.Owner), ownedByIslandOwner = p.Owner == Me,
                    salePrice = p.Sale, saleExclusive = false, rentPrice = p.Rent, rentPeriodDays = p.Days, tenantId = p.Tenant, tenant = p.Tenant is { } t ? NameOf(t) : null, rentStatus = p.Tenant is null ? null : "active", rentExpiresAt = p.Tenant is null ? (DateTimeOffset?)null : Now.AddDays(12), rentalId = p.RentalId,
                    maxObjects = 200, objects = p.Objects, cost = p.Objects * 3, maxCost = 0, scripts = false, builders = 0, accessMode = "public", buildMode = "owner", spawnX = (double?)null, spawnZ = (double?)null, createdAt = Now.AddDays(-9), revision = p.Revision,
                }),
                contracts = live.Select(p => new { id = p.RentalId, parcelId = p.Id, code = p.Code, parcel = p.Name, landlord = NameOf(p.Owner), tenant = NameOf(p.Tenant!.Value), price = p.Rent, periodDays = p.Days, status = "active", startsAt = Now.AddDays(-18), expiresAt = Now.AddDays(12), endedAt = (DateTimeOffset?)null, autoRenew = true, deposit = 0 }),
                history = Parcels.Where(p => p.Owner != Me).Select(p => new { at = Now.AddDays(-2), kind = "money", what = "sale", actor = NameOf(p.Owner), parcelId = p.Id, code = p.Code, parcel = p.Name, amount = (long?)60000 })
                    .Concat(Parcels.Take(2).Select(p => new { at = Now.AddDays(-9), kind = "change", what = "claim", actor = "AliceVerse", parcelId = p.Id, code = p.Code, parcel = p.Name, amount = (long?)null })),
            };
        }

        private static (long, string) Ok(object? body = null) => (200, JsonSerializer.Serialize(body ?? new { }, Web));
        private static (long, string) Fail(long status, string error) => (status, JsonSerializer.Serialize(new { error }, Web));

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            var land = $"/api/v1/worlds/islands/{Island}/land";
            if (path == $"{land}/overview") return Task.FromResult(Staff ? Ok(Overview()) : Fail(403, "forbidden"));
            var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
            P Find(string rest) => Parcels.First(p => p.Id == Guid.Parse(rest.Split('/')[0]));
            if (path.StartsWith($"{land}/parcels/", StringComparison.Ordinal))
            {
                var rest = path[$"{land}/parcels/".Length..]; var p = Find(rest); var action = rest[(rest.IndexOf('/') + 1)..];
                if (action == "category") { var c = json!["category"]!.GetValue<string>(); if (!new[] { "residencial", "comercial", "mista", "social", "comum" }.Contains(c)) return Task.FromResult(Fail(400, "bad_category")); p.Category = c; p.Revision++; return Task.FromResult(Ok()); }
                if (action == "sale") { if (p.Owner != Me) return Task.FromResult(Fail(403, "forbidden")); if (method == "DELETE") { p.Sale = null; return Task.FromResult(Ok()); } p.Sale = json!["price"]!.GetValue<long>(); p.Revision++; return Task.FromResult(Ok()); }
                if (action == "rental-offer") { if (method == "DELETE") { p.Rent = null; return Task.FromResult(Ok()); } p.Rent = json!["pricePerPeriod"]!.GetValue<long>(); p.Days = json["periodDays"]!.GetValue<int>(); p.Revision++; return Task.FromResult(Ok()); }
                if (action == "split")
                {
                    var take = json!["cells"]!.AsArray().Select(c => LandMath.Key(c![0]!.GetValue<int>(), c[1]!.GetValue<int>())).ToHashSet(); if (take.Count >= p.Cells.Count) return Task.FromResult(Fail(409, "whole_parcel"));
                    foreach (var k in take) p.Cells.Remove(k); Parcels.Add(new P { Code = $"L-{Parcels.Count + 1:00}", Name = json["name"]!.GetValue<string>(), Owner = p.Owner, Cells = take }); return Task.FromResult(Ok());
                }
            }
            if (path == $"{land}/claim")
            {
                var cells = json!["cells"]!.AsArray().Select(c => LandMath.Key(c![0]!.GetValue<int>(), c[1]!.GetValue<int>())).ToHashSet();
                if (Parcels.Any(p => p.Cells.Overlaps(cells))) return Task.FromResult(Fail(409, "overlap"));
                var fresh = new P { Code = $"L-{Parcels.Count + 1:00}", Name = json["name"]!.GetValue<string>(), Cells = cells }; Parcels.Add(fresh); return Task.FromResult(Ok(new { id = fresh.Id, area = cells.Count * 16.0, cells = cells.Count }));
            }
            if (path == $"{land}/merge")
            {
                var ids = json!["parcelIds"]!.AsArray().Select(x => Guid.Parse(x!.GetValue<string>())).ToList(); var primary = Parcels.First(p => p.Id == Guid.Parse(json["primaryId"]!.GetValue<string>()));
                foreach (var other in Parcels.Where(p => ids.Contains(p.Id) && p != primary).ToList()) { primary.Cells.UnionWith(other.Cells); Parcels.Remove(other); }
                return Task.FromResult(Ok());
            }
            if (path.StartsWith($"{land}/rentals/", StringComparison.Ordinal)) { var p = Parcels.First(x => x.RentalId == Guid.Parse(path[$"{land}/rentals/".Length..].Split('/')[0])); p.Tenant = null; p.RentalId = null; p.Rent = null; return Task.FromResult(Ok()); }
            if (method == "PATCH" && path.StartsWith($"/api/v1/worlds/islands/{Island}/parcels/", StringComparison.Ordinal))
            {
                var p = Parcels.First(x => x.Id == Guid.Parse(path.Split('/')[^1])); if (json!["expectedRevision"]!.GetValue<int>() != p.Revision) return Task.FromResult(Fail(409, "conflict"));
                p.Name = json["name"]!.GetValue<string>(); p.Revision++; return Task.FromResult(Ok());
            }
            return Task.FromResult((404L, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server(); var now = server.Now;
            server.Parcels.Add(new P { Code = "L-01", Name = "Residencial Norte", Cells = Server.Rect(-30, -30, 8, 8), Objects = 40 });
            server.Parcels.Add(new P { Code = "L-02", Name = "Residencial Sul", Cells = Server.Rect(-20, -30, 8, 8), Sale = 120000, Category = "residencial", Objects = 15 });
            server.Parcels.Add(new P { Code = "L-03", Name = "Centro Comercial", Cells = Server.Rect(-10, -30, 10, 8), Category = "comercial", Tenant = Other, RentalId = Guid.NewGuid(), Rent = 12000, Objects = 80 });
            server.Parcels.Add(new P { Code = "L-04", Name = "Casa do Bruno", Cells = Server.Rect(-30, -20, 6, 6), Owner = Other, Objects = 22 });
            server.Parcels.Add(new P { Code = "L-05", Name = "Praça Central", Cells = Server.Rect(-20, -20, 6, 6), Category = "comum" });
            var window = new LandManagementWindow { Name = "LandManagementWindow", IslandProvider = () => Island, MyUser = () => Me };
            window.UseTransportForTesting(server.Handle); AddChild(window); await Frames(3);

            // --- opening and the numbers ---------------------------------------------------------------------------------
            await window.OpenAsync(); await Frames(3);
            Require(window.IsOpen && !window.FailureVisibleForTesting, "the window opens");
            Require(string.Join("|", window.MenuForTesting) == "Visão Geral|Mapa de Parcelas|Demarcação|Lista de Terrenos|Vendas|Aluguéis|Contratos|Prims e Recursos|Permissões|Histórico", $"the ten sections of the menu (got {string.Join("|", window.MenuForTesting)})");
            Require(window.NameForTesting == "Ilha Tropical" && window.AreaLineForTesting.Contains("65.536 m²"), $"the header shows the island and its area (got {window.AreaLineForTesting})");
            Require(window.StatsForTesting.Contains("Parcelas: 5") && window.StatsForTesting.Contains("Vendidas: 1") && window.StatsForTesting.Contains("Alugadas: 1") && window.StatsForTesting.Contains("Disponíveis: 1"), $"the header counts the parcels (got {window.StatsForTesting})");
            var kpi = window.KpiValuesForTesting;
            Require(kpi[0] == "65.536 m²" && kpi[3] == "1" && kpi[4] == "1" && kpi[5] == "L$ 12.000", $"the six indicators come from the server ({string.Join("|", kpi)})");
            Require(kpi[1] == "4.480 m²", $"the area marked into parcels is the sum of the parcels ({kpi[1]})");
            Require(window.KpiVisibleForTesting && window.Map.ParcelCountForTesting == 5, "the overview shows the indicators and the map has the real parcels");

            // --- picking on the map -----------------------------------------------------------------------------------------
            window.Map.ClickCellForTesting(-28, -28); await Frames(2);
            Require(window.DetailTitleForTesting == "L-01 – Residencial Norte", $"clicking a parcel picks it (got {window.DetailTitleForTesting})");
            Require(window.DetailForTesting("owner") == "AliceVerse" && window.DetailForTesting("area").StartsWith("1.024 m²", StringComparison.Ordinal), $"the details show owner and area ({window.DetailForTesting("area")})");
            Require(window.ButtonEnabledForTesting("vender") && window.ButtonEnabledForTesting("alugar") && window.ButtonEnabledForTesting("editar") && window.ButtonEnabledForTesting("inspecionar"), "own free land can be sold, rented, edited and inspected");
            window.Map.ClickCellForTesting(-28, -18); await Frames(2);
            Require(window.DetailTitleForTesting.StartsWith("L-04", StringComparison.Ordinal) && !window.ButtonEnabledForTesting("vender") && !window.ButtonEnabledForTesting("alugar") && !window.ButtonEnabledForTesting("dividir"), "land of somebody else cannot be sold, rented or cut from here");
            Require(window.ButtonEnabledForTesting("editar"), "but the staff can still edit what it is for");
            window.Map.ClickCellForTesting(-8, -28); await Frames(2);
            Require(window.DetailForTesting("tenant").Contains("Bruno") && !window.ButtonEnabledForTesting("vender"), "rented land shows the tenant and cannot be sold");
            window.Map.ClickCellForTesting(0, 20); await Frames(2);
            Require(window.DetailTitleForTesting.StartsWith("Escolha", StringComparison.Ordinal), "empty ground picks nothing");

            // --- lists ----------------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("lista"); await Frames(2);
            Require(window.RowsForTesting("lista") == 5, "the list has every parcel");
            window.SetListFilterForTesting("sold"); await Frames(2); Require(window.RowsForTesting("lista") == 1, "filter: sold");
            window.SetListFilterForTesting("comercial"); await Frames(2); Require(window.RowsForTesting("lista") == 1, "filter: commercial");
            window.SetListFilterForTesting("all", "norte"); await Frames(2); Require(window.RowsForTesting("lista") == 1, "search by name");
            window.SetListFilterForTesting("all"); await Frames(2);
            window.SelectSectionForTesting("vendas"); await Frames(2); Require(window.RowsForTesting("vendas") == 1, "one parcel for sale");
            window.SelectSectionForTesting("alugueis"); await Frames(2); Require(window.RowsForTesting("alugueis") == 1, "one contract in force");
            window.SelectSectionForTesting("contratos"); await Frames(2); Require(window.RowsForTesting("contratos") == 1, "one contract listed");
            window.SelectSectionForTesting("prims"); await Frames(2); Require(window.RowsForTesting("prims") == 5, "resources per parcel");
            window.SelectSectionForTesting("permissoes"); await Frames(2); Require(window.RowsForTesting("permissoes") == 5, "permissions per parcel");
            window.SelectSectionForTesting("historico"); await Frames(2); Require(window.RowsForTesting("historico") == 3, "the history lists what the server sent");

            // --- selling, renting, editing --------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("visao"); await Frames(2);
            window.SelectParcelForTesting(server.Parcels[0].Id); await window.SellForTesting(80000); await Frames(2);
            Require(server.Parcels[0].Sale == 80000 && window.Data!.Totals.OnSale == 2, "selling sends the price and the overview counts the offer");
            await window.WithdrawSaleForTesting(); await Frames(2); Require(server.Parcels[0].Sale is null && window.Data!.Totals.OnSale == 1, "withdrawing a sale takes the offer back");
            await window.RentOfferForTesting(500, 14); await Frames(2); Require(server.Parcels[0].Rent == 500 && server.Parcels[0].Days == 14, "renting out sends the price and the period");
            await window.WithdrawRentOfferForTesting(); await Frames(2); Require(server.Parcels[0].Rent is null, "withdrawing a rental offer");
            await window.SetCategoryForTesting("comercial"); await Frames(2); Require(server.Parcels[0].Category == "comercial", "changing what the land is for");
            await window.SetCategoryForTesting("fabrica"); await Frames(2); Require(server.Parcels[0].Category == "comercial" && window.ToastForTesting.Contains("inválida"), $"an unknown purpose is refused and said ({window.ToastForTesting})");
            window.SelectSectionForTesting("alugueis"); await Frames(2); await window.EndRentalForTesting(server.Parcels[2].RentalId!.Value); await Frames(2);
            Require(window.Data!.Totals.Rented == 0, "ending a rental frees the parcel");

            // --- marking land ----------------------------------------------------------------------------------------------------------
            window.SelectSectionForTesting("demarcacao"); await Frames(2);
            Require(!window.KpiVisibleForTesting && window.SelectionVisibleForTesting && !window.DetailVisibleForTesting, "marking shows the selection panel");
            window.MarkForTesting((0, 0), (3, 2)); await Frames(2);
            Require(window.SelectionForTesting("cells") == "12" && window.SelectionForTesting("area") == "192 m²" && window.SelectionForTesting("size") == "16 m × 12 m", $"the selection counts cells, area and size (got {window.SelectionForTesting("cells")}, {window.SelectionForTesting("area")}, {window.SelectionForTesting("size")})");
            Require(window.SelectionForTesting("conflict") == "Nenhum" && window.ButtonEnabledForTesting("criar"), "free ground can be turned into a parcel");
            window.MarkForTesting((1, 1), (1, 1), remove: true); await Frames(2); Require(window.SelectionForTesting("cells") == "11", "reducing takes cells off");
            window.UndoForTesting(); await Frames(2); Require(window.SelectionForTesting("cells") == "12", "undo brings them back");
            window.RedoForTesting(); await Frames(2); Require(window.SelectionForTesting("cells") == "11", "redo takes them off again");
            window.MarkForTesting((-30, -30), (-29, -29)); await Frames(2);
            Require(window.SelectionForTesting("conflict").Contains("já pertencem") && !window.ButtonEnabledForTesting("criar"), "marking over an existing parcel is a conflict and cannot be created");
            window.Map.ClearCells(); window.MarkForTesting((0, 0), (3, 2)); await Frames(2);
            await window.CreateParcelForTesting("Mirante", "social"); await Frames(3);
            Require(window.Data!.Parcels.Count == 6 && server.Parcels.Any(p => p.Name == "Mirante") && window.Map.SelectedCells.Count == 0, "creating adds the parcel and clears the marking");
            Require(server.Calls.Any(c => c.EndsWith("/category", StringComparison.Ordinal)) && server.Parcels.First(p => p.Name == "Mirante").Category == "social", "the purpose chosen is saved");

            // --- dividing and joining ----------------------------------------------------------------------------------------------------
            var big = server.Parcels[0]; window.SelectParcelForTesting(big.Id); window.Map.Select(big.Id); window.MarkForTesting((-30, -30), (-27, -23)); await Frames(2);
            Require(window.ButtonEnabledForTesting("cortar"), "part of an own parcel can be cut");
            await window.SplitForTesting("Metade Norte"); await Frames(3);
            Require(window.Data!.Parcels.Count == 7 && server.Parcels.Any(p => p.Name == "Metade Norte"), "dividing makes a new parcel");
            var half = server.Parcels.First(p => p.Name == "Metade Norte"); window.Map.Select(big.Id); window.Map.Picked.Add(half.Id); await Frames(2);
            await window.MergeForTesting(big.Id); await Frames(3);
            Require(window.Data!.Parcels.Count == 6 && !server.Parcels.Any(p => p.Name == "Metade Norte"), "joining brings them back to one");

            // --- not staff -------------------------------------------------------------------------------------------------------------------
            server.Staff = false; await window.LoadAsync(); await Frames(2);
            Require(window.FailureVisibleForTesting && window.FailureTextForTesting.Contains("administradores"), $"somebody who is not staff is told so (got {window.FailureTextForTesting})");
            server.Staff = true; await window.LoadAsync(); await Frames(2); Require(!window.FailureVisibleForTesting, "trying again recovers");

            // --- screens -----------------------------------------------------------------------------------------------------------------------
            foreach (var view in new[] { new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1920, 1080), new Vector2(2560, 1440) })
            {
                window.SetViewportForTesting(view); await Frames(2); var size = window.PanelSizeForTesting;
                Require(size.X <= view.X - 31 && size.Y <= view.Y - 31, $"the window fits {view} (got {size})");
            }
            window.SetViewportForTesting(new Vector2(1920, 1080)); await Frames(2);

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); window.SetViewportForTesting(new Vector2(1536, 1024)); await Frames(6);
                window.SelectSectionForTesting(OS.GetEnvironment("LIFEVERSE_SECTION") is { Length: > 0 } section ? section : "visao"); window.SelectParcelForTesting(server.Parcels.First(p => p.Code == "L-03").Id); await Frames(8);
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }

            GD.Print("event=land_management_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=land_management_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
