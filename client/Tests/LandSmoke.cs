using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Tests;

/// <summary>The territorial grid on the client: reading the parcels the server sends, selecting areas, what the window says about them, the overlay and creating a parcel.</summary>
public partial class LandSmoke : Node
{
    private static string Parcel(string name, string owner, string state, bool mine, int cx0, int cz0, int cx1, int cz1, int objects, string extra = "") =>
        $"{{\"id\":\"{Guid.NewGuid()}\",\"name\":\"{name}\",\"description\":\"\",\"ownerName\":\"{owner}\",\"mine\":{(mine ? "true" : "false")},\"state\":\"{state}\",\"area\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1) * 16},\"cells\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1)},\"maxObjects\":100,\"maxCost\":2000,\"objects\":{objects},\"cost\":{objects * 20},\"revision\":1,\"runs\":[{string.Join(",", Enumerable.Range(cz0, cz1 - cz0 + 1).Select(z => $"[{z},{cx0},{cx1}]"))}]{extra}}}";

    public override async void _Ready()
    {
        try
        {
            var world = new Node3D(); AddChild(world);
            var land = new LandManager { Name = "LandManager" }; world.AddChild(land);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var said = new List<string>(); land.Said += said.Add;
            var calls = new List<string>();
            // Praça: cells x 0..5, z 0..3 (metres 0..24 x 0..16); Loja: cells x 6..9, z 0..3 (metres 24..40)
            var listing = "{\"size\":256,\"version\":5,\"parcels\":[" + Parcel("Praça", "Marina", "own", true, 0, 0, 5, 3, 7) + "," + Parcel("Loja", "Rafa", "owned", false, 6, 0, 9, 3, 2) + "]}";
            land.Transport = (method, path, body) => { calls.Add($"{method} {path} {body}"); return (200, listing); };

            land.Apply(listing);
            Require(land.Parcels.Count == 2 && land.IslandSize == 256 && land.Version == 5, "the parcels of the island are read");
            Require(land.ParcelAt(8, 8)?.Name == "Praça" && land.ParcelAt(30, 8)?.Name == "Loja" && land.ParcelAt(-50, -50) is null, "a point on the ground finds its parcel by cell");
            Require(land.Parcels[0].CellCount == 24 && land.Parcels[0].Cells.Count == 24 && land.Parcels[0].Mine, "cells are rebuilt from the rows");

            land.SetActive(true);
            Require(land.Active && land.OverlayVisibleForTesting && land.OverlayVerticesForTesting > 0, "the tool draws the parcels over the ground");
            Require(calls.Any(c => c.StartsWith("Get ")), "opening the tool asks the server for the current land");

            land.DragForTesting(1, 1, 9, 5);
            Require(land.Selection.Count == 3 * 2 && land.InfoOpen, $"dragging selects the cells it touches and opens the window: {land.Selection.Count}");
            Require(land.InfoNameForTesting == "Praça" && land.InfoStatusForTesting == "Seu terreno" && land.InfoObjectsForTesting.Contains('7') && land.InfoObjectsForTesting.Contains("100"), $"inside one parcel the window describes it: {land.InfoNameForTesting}/{land.InfoStatusForTesting}/{land.InfoObjectsForTesting}");
            Require(land.InfoAreaForTesting.Contains("96") && land.InfoAreaForTesting.Contains("384"), $"a partial selection says its own area and the parcel's: {land.InfoAreaForTesting}");
            Require(!land.ClaimBoxVisibleForTesting, "an owned area cannot be claimed");

            land.DragForTesting(60, 60, 70, 68);
            Require(land.InfoStatusForTesting == "Disponível" && land.ClaimBoxVisibleForTesting && land.InfoNameForTesting == "Terreno livre", "free ground offers to become a parcel");
            land.DragForTesting(20, 4, 30, 6); // crosses from Praça into Loja
            Require(land.InfoStatusForTesting == "Seleção mista", $"a mix is called a mix: {land.InfoStatusForTesting}");

            land.DragForTesting(-1000, -1000, -900, -900);
            Require(land.Selection.Count == 0, "cells outside the island are dropped");

            // creating a parcel
            said.Clear(); calls.Clear();
            land.DragForTesting(60, 60, 75, 67);
            Require(land.Selection.Count == 4 * 2, $"the free selection has its cells: {land.Selection.Count}");
            land.ClaimSelection("");
            Require(said[^1].Contains("nome") && calls.Count == 0, "a parcel needs a name before anything is sent");
            land.Transport = (method, path, body) =>
            {
                calls.Add($"{method} {path} {body}");
                if (method == Godot.HttpClient.Method.Post) return (201, "{\"id\":\"" + Guid.NewGuid() + "\"}");
                return (200, "{\"size\":256,\"version\":6,\"parcels\":[" + Parcel("Casa Nova", "Marina", "own", true, 15, 15, 18, 16, 0) + "]}");
            };
            land.ClaimSelection("Casa Nova");
            var post = calls.First(c => c.StartsWith("Post /claim"));
            Require(post.Contains("Casa Nova") && post.Contains("\"cells\":[[") && calls.Count == 2, $"the new parcel is sent with its cells: {post}");
            Require(land.Parcels.Count == 1 && land.Parcels[0].Name == "Casa Nova" && land.Selection.Count == 8 && said.Any(s => s.Contains("criado")), "the answer is read and the new parcel is selected");

            // an area that already has an owner is not even sent
            land.DragForTesting(60, 60, 75, 67); said.Clear(); calls.Clear();
            land.ClaimSelection("Outra");
            Require(said.Count == 1 && calls.Count == 0, "owned land is refused on the client before asking");

            // refusals from the server are said plainly
            foreach (var (status, body, text) in new[] { (403, "{}", "dono"), (409, "{\"error\":\"overlap\"}", "ocupa"), (409, "{\"error\":\"name_taken\"}", "nome"), (400, "{\"error\":\"not_contiguous\"}", "peça só"), (0, "", "conexão") })
            {
                land.DragForTesting(-100, -100, -90, -90); said.Clear();
                land.Transport = (_, _, _) => (status, body);
                land.ClaimSelection("X");
                Require(said.Count == 1 && said[0].Contains(text), $"status {status} says '{text}': {string.Join("|", said)}");
            }

            land.ClearSelection(); land.CloseInfo(); land.SetActive(false);
            Require(!land.OverlayVisibleForTesting && !land.Active, "closing everything hides the overlay");

            land.SelectCells(LandMath.Between(0, 0, 7, 7), true);
            Require(land.OverlayVisibleForTesting && land.InfoOpen, "a selection alone still shows its outline");

            // ---- selling, renting and buying ----
            var wallet = 0; land.WalletChanged += () => wallet++;
            string Listing(string parcel) => "{\"size\":256,\"version\":9,\"parcels\":[" + parcel + "]}";
            var current = "";
            LandParcelData Show(string parcelJson) { current = Listing(parcelJson); land.Apply(current); land.SelectCells([.. land.Parcels[0].Cells]); land.ShowTradeForTesting(); return land.Parcels[0]; }
            var calls2 = new List<string>(); var reply = (Status: 200, Body: "{}");
            land.Transport = (method, path, body) => { calls2.Add($"{method} {path} {body}"); return path.EndsWith("/history") ? (200, "[{\"type\":\"sale\",\"amount\":900,\"at\":\"2026-11-03T12:00:00Z\"},{\"type\":\"rent_listed\",\"amount\":100,\"at\":\"2026-11-02T12:00:00Z\"}]") : method == Godot.HttpClient.Method.Get ? (200, current) : reply; };

            // 1. my own parcel: offer it for sale
            var mine = Show(Parcel("Casa", "Marina", "own", true, 0, 0, 3, 3, 4));
            Require(land.OwnerSaleVisibleForTesting && land.OwnerRentVisibleForTesting && !land.BuyBoxVisibleForTesting && !land.ContractVisibleForTesting, "the owner is offered selling and renting, not buying");
            Require(land.HistoryForTesting.Contains("Venda") && land.HistoryForTesting.Contains("L$ 900"), $"the history of my parcel is listed: {land.HistoryForTesting}");
            said.Clear(); calls2.Clear();
            land.SetSaleForTesting("abc", true); land.OfferSaleForTesting();
            Require(said.Count == 1 && !calls2.Any(c => c.StartsWith("Post")), "a price that is not a number is refused before asking");
            land.SetSaleForTesting("1.500", false); said.Clear(); land.OfferSaleForTesting();
            var offer = calls2.First(c => c.Contains("/sale"));
            Require(offer.StartsWith("Post /parcels/") && offer.Contains("\"price\":1500") && offer.Contains("\"includeObjects\":false") && said.Any(s => s.Contains("à venda")) && wallet == 1, $"the sale offer carries the price and the objects choice: {offer}");
            reply = (409, "{\"error\":\"rented\"}"); said.Clear(); land.OfferSaleForTesting();
            Require(said[^1].Contains("alugado"), $"a refusal is said plainly: {said[^1]}");
            reply = (200, "{}");

            // 2. my own parcel: offer it for rent
            land.SetRentOfferForTesting("100", 0, "3", "1", "0"); said.Clear(); calls2.Clear(); land.OfferRentForTesting();
            Require(said.Count == 1 && calls2.Count == 0, "a maximum below the minimum is refused before asking");
            land.SetRentOfferForTesting("100", 1, "1", "6", "50"); land.OfferRentForTesting();
            var rentOffer = calls2.First(c => c.Contains("rental-offer"));
            Require(rentOffer.Contains("\"pricePerPeriod\":100") && rentOffer.Contains("\"periodDays\":14") && rentOffer.Contains("\"maxPeriods\":6") && rentOffer.Contains("\"depositCoins\":50"), $"the rental offer carries its terms: {rentOffer}");

            // 3. a parcel for sale that is not mine: buy it
            Show(Parcel("Loja", "Rafa", "sale", false, 4, 0, 7, 3, 2, ",\"salePrice\":2500,\"saleIncludesObjects\":true,\"saleDescription\":\"Esquina boa\""));
            Require(land.BuyBoxVisibleForTesting && !land.OwnerSaleVisibleForTesting && land.BuySummaryForTesting.Contains("L$ 2.500") && land.BuySummaryForTesting.Contains("Rafa"), $"someone else's parcel for sale offers buying: {land.BuySummaryForTesting}");
            land.OpenBuyForTesting();
            Require(land.BuyWindowVisibleForTesting, "buying asks for a confirmation first");
            calls2.Clear(); said.Clear(); wallet = 0; land.ConfirmBuy();
            var buy = calls2.First(c => c.Contains("/buy"));
            Require(buy.Contains("\"expectedPrice\":2500") && buy.Contains("idempotencyKey") && !land.BuyWindowVisibleForTesting && said.Any(s => s.Contains("comprou")) && wallet == 1, $"the purchase sends the price the person saw and a key: {buy}");
            foreach (var (status, body, text) in new[] { (409, "{\"error\":\"insufficient_balance\"}", "Saldo"), (409, "{\"error\":\"conflict\"}", "mais rápida"), (409, "{\"error\":\"price_changed\"}", "preço mudou"), (0, "", "conexão") })
            {
                reply = (status, body); said.Clear(); wallet = 0; land.OpenBuyForTesting(); land.ConfirmBuy();
                Require(said.Count == 1 && said[0].Contains(text) && wallet == 0, $"{status} says '{text}': {string.Join("|", said)}");
            }
            reply = (200, "{}");

            // 4. a parcel for rent that is not mine: rent it
            Show(Parcel("Quiosque", "Lua", "rent", false, 8, 0, 11, 3, 0, ",\"rentPrice\":100,\"rentPeriodDays\":10,\"rentMinPeriods\":2,\"rentMaxPeriods\":6,\"rentDeposit\":50"));
            Require(land.RentBoxVisibleForTesting && !land.BuyBoxVisibleForTesting, "a parcel offered for rent offers renting");
            land.OpenRentForTesting(3);
            Require(land.RentWindowVisibleForTesting && land.RentTotalForTesting == "L$ 350", $"the total is rent for the periods plus the deposit: {land.RentTotalForTesting}");
            calls2.Clear(); said.Clear(); land.ConfirmRent();
            var rent = calls2.First(c => c.Contains("/rent"));
            Require(rent.Contains("\"periods\":3") && rent.Contains("\"expectedPrice\":100") && rent.Contains("idempotencyKey") && said.Any(s => s.Contains("alugou")), $"the rental sends periods, the price seen and a key: {rent}");

            // 5. my contract as the tenant
            var rentalId = Guid.NewGuid();
            Show(Parcel("Quiosque", "Lua", "rented_by_me", false, 8, 0, 11, 3, 0, $",\"rentalId\":\"{rentalId}\",\"tenantName\":\"Marina\",\"tenantIsMe\":true,\"rentStatus\":\"grace\",\"rentExpiresAt\":\"2026-12-01T12:00:00Z\",\"rentNextCharge\":100,\"rentAutoRenew\":true"));
            Require(land.ContractVisibleForTesting && land.ContractTextForTesting.Contains("Alugado por você") && land.ContractTextForTesting.Contains("tolerância") && land.ContractTextForTesting.Contains("L$ 100") && land.ContractTextForTesting.Contains("automática"), $"the tenant sees the contract: {land.ContractTextForTesting}");
            calls2.Clear(); land.RenewForTesting(); land.EndForTesting();
            Require(calls2.Any(c => c.Contains($"/rentals/{rentalId}/renew") && c.Contains("\"periods\":1")) && calls2.Any(c => c.Contains($"/rentals/{rentalId}/end")), "renewing and ending go to the contract");

            // 6. someone else's contract, or nothing on offer
            Show(Parcel("Casa", "Rafa", "owned", false, 0, 0, 3, 3, 0));
            Require(land.TradeNoteForTesting.Contains("não está à venda") && !land.BuyBoxVisibleForTesting && !land.RentBoxVisibleForTesting, "a parcel with no offer says so");
            Require(land.HistoryForTesting.Contains("dono"), "the history is only for the owner and the tenant");

            // ---- administration: access, splitting, joining, objects, history ----
            var casaId = Guid.Empty;
            var lucas = Guid.NewGuid(); var entryA = Guid.NewGuid(); var entryB = Guid.NewGuid(); var objA = Guid.NewGuid(); var objB = Guid.NewGuid(); var objC = Guid.NewGuid();
            string AccessJson() => "{\"mode\":\"list\",\"entries\":[{\"id\":\"" + entryA + "\",\"userId\":\"" + lucas + "\",\"userName\":\"Lucas\",\"kind\":\"ban\",\"reason\":\"\",\"expiresAt\":\"2030-12-01T00:00:00Z\",\"createdAt\":\"2026-11-03T12:00:00Z\"},{\"id\":\"" + entryB + "\",\"userId\":\"" + Guid.NewGuid() + "\",\"userName\":\"Ana\",\"kind\":\"allow\",\"reason\":\"\",\"expiresAt\":null,\"createdAt\":\"2026-11-03T12:00:00Z\"}]}";
            string ObjectJson(Guid id, string name, string owner, bool scripts, bool authorized) => "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"ownerUserId\":\"" + Guid.NewGuid() + "\",\"ownerName\":\"" + owner + "\",\"primitiveType\":\"cube\",\"cost\":5,\"hasScripts\":" + (scripts ? "true" : "false") + ",\"x\":1,\"y\":1,\"z\":1,\"createdAt\":\"2026-11-03T12:00:00Z\",\"authorized\":" + (authorized ? "true" : "false") + "}";
            var calls3 = new List<string>(); var answer = (Status: 200, Body: "{}");
            var listing3 = "";
            land.Transport = (method, path, body) =>
            {
                calls3.Add($"{method} {path} {body}");
                if (method == Godot.HttpClient.Method.Get && path.EndsWith("/access")) return (200, AccessJson());
                if (method == Godot.HttpClient.Method.Get && path.EndsWith("/objects")) return (200, "[" + ObjectJson(objA, "Mesa", "Marina", false, true) + "," + ObjectJson(objB, "Caixa", "Rafa", true, false) + "," + ObjectJson(objC, "Vaso", "Lua", false, false) + "]");
                if (method == Godot.HttpClient.Method.Get && path.EndsWith("/audit")) return (200, "[{\"operation\":\"split\",\"actorName\":\"Marina\",\"summary\":\"{}\",\"at\":\"2026-11-03T12:00:00Z\"},{\"operation\":\"eject\",\"actorName\":\"Marina\",\"summary\":\"{}\",\"at\":\"2026-11-03T12:01:00Z\"}]");
                if (method == Godot.HttpClient.Method.Get && path.EndsWith("/history")) return (200, "[]");
                if (method == Godot.HttpClient.Method.Get) return (200, listing3);
                return answer;
            };
            land.People.Transport = (method, path, body) => (200, "[{\"id\":\"" + lucas + "\",\"username\":\"lucas\",\"displayName\":\"Lucas\",\"relation\":\"none\"}]");
            var casa = Parcel("Casa", "Marina", "own", true, 0, 0, 3, 3, 4, ",\"accessMode\":\"list\",\"buildMode\":\"list\",\"allowScripts\":true,\"autoReturnMinutes\":30,\"othersObjects\":2,\"othersCost\":10");
            var anexo = Parcel("Anexo", "Marina", "own", true, 4, 0, 7, 3, 1);
            var alheio = Parcel("Alheio", "Rafa", "owned", false, 8, 0, 11, 3, 0);
            listing3 = "{\"size\":256,\"version\":11,\"parcels\":[" + casa + "," + anexo + "," + alheio + "]}"; land.Apply(listing3);
            casaId = land.Parcels.First(p => p.Name == "Casa").Id;
            land.SelectCells([.. land.Parcels.First(p => p.Name == "Casa").Cells]);

            // access
            land.ShowPageForTesting(3);
            Require(land.AccessEntries.Count == 2 && land.AccessEntries[0].UserName == "Lucas" && land.AccessEntries[0].Kind == "ban" && land.AccessEntries[0].ExpiresAt is not null && land.AccessRowsForTesting == 2, "the access list of my parcel is read");
            Require(land.AccessNoteForTesting.Length == 0, "the owner has the access controls");
            Require(land.BuildModeShownForTesting == 1 && land.AllowScriptsShownForTesting && land.AutoReturnShownForTesting == 3 && land.Parcels.First(x => x.Name == "Casa").OthersObjects == 2, "the building rules of the parcel are shown");
            calls3.Clear(); land.SetBuildOptionsForTesting(2, false, 1); land.ApplyBuildOptionsForTesting();
            var options = calls3.First(c => c.Contains("/options"));
            Require(options.StartsWith("Put /parcels/") && options.Contains("\"buildMode\":\"everyone\"") && options.Contains("\"allowScripts\":false") && options.Contains("\"autoReturnMinutes\":5"), $"the building rules go to the server: {options}");
            calls3.Clear(); land.SetBuildOptionsForTesting(0, false, 0); land.ApplyBuildOptionsForTesting();
            Require(calls3.First(c => c.Contains("/options")).Contains("\"autoReturnMinutes\":null"), "no automatic return is sent as null");
            calls3.Clear(); land.SetEntryForTesting("Lucas", 2, 0); land.AddEntryForTesting();
            Require(calls3.First(c => c.StartsWith("Post") && c.Contains("/access")).Contains("\"kind\":\"build\""), "a person can be put on the builders list");
            calls3.Clear(); land.SetModeForTesting(2); land.ApplyModeForTesting();
            Require(calls3.Any(c => c.StartsWith("Put /parcels/") && c.Contains("/access-mode") && c.Contains("\"mode\":\"private\"")), "the mode goes to the server");
            calls3.Clear(); land.SetEntryForTesting("Lucas", 1, 2); land.AddEntryForTesting();
            var ban = calls3.First(c => c.Contains("/access") && c.StartsWith("Post"));
            Require(ban.Contains(lucas.ToString()) && ban.Contains("\"kind\":\"ban\"") && ban.Contains("\"minutes\":60"), $"a ban carries the person found by name and the time: {ban}");
            calls3.Clear(); land.SetEntryForTesting("Lucas", 0, 0); land.AddEntryForTesting();
            Require(calls3.First(c => c.StartsWith("Post")).Contains("\"kind\":\"allow\"") && calls3.First(c => c.StartsWith("Post")).Contains("\"minutes\":null"), "permitting has no time limit");
            calls3.Clear(); land.SetEntryForTesting("Lucas", 0, 0); land.EjectForTesting();
            Require(calls3.Any(c => c.StartsWith("Post") && c.Contains("/eject") && c.Contains(lucas.ToString())), "ejecting asks the server to send the person out");
            calls3.Clear(); land.RemoveEntryForTesting(0);
            Require(calls3.Any(c => c.StartsWith("Delete") && c.Contains($"/access/{entryA}")), "removing someone from the list");
            said.Clear(); land.SetEntryForTesting("", 0, 0); calls3.Clear(); land.AddEntryForTesting();
            Require(said.Count == 1 && !calls3.Any(c => c.StartsWith("Post")), "a name is needed");
            answer = (409, "{\"error\":\"protected\"}"); said.Clear(); land.SetEntryForTesting("Lucas", 1, 0); land.AddEntryForTesting();
            Require(said.Any(s => s.Contains("administrador")), $"a protected person cannot be banned: {string.Join("|", said)}");
            answer = (200, "{}");

            // objects
            land.ShowPageForTesting(1);
            Require(land.ObjectsShownForTesting == 3, "the objects of the parcel are listed");
            land.SetFilterForTesting(2); Require(land.ObjectsShownForTesting == 2, "the filter keeps others' objects");
            land.SetFilterForTesting(3); Require(land.ObjectsShownForTesting == 1, "the filter keeps objects with scripts");
            land.SetFilterForTesting(0);
            said.Clear(); calls3.Clear(); land.ReturnForTesting("others", "");
            Require(said.Count == 1 && !calls3.Any(c => c.StartsWith("Post")), "a reason is required to return objects");
            land.SelectObjectsForTesting(); said.Clear(); land.ReturnForTesting("ids", "limpeza");
            Require(said.Count == 1 && !calls3.Any(c => c.StartsWith("Post")), "returning chosen objects needs some chosen");
            land.SelectObjectsForTesting(1, 2); land.ReturnForTesting("ids", "limpeza");
            var returned = calls3.First(c => c.Contains("/objects/return"));
            Require(returned.Contains("\"scope\":\"ids\"") && returned.Contains(objB.ToString()) && returned.Contains(objC.ToString()) && returned.Contains("limpeza"), $"the chosen objects go back with the reason: {returned}");
            calls3.Clear(); land.ReturnForTesting("others", "sem permissão");
            Require(calls3.First(c => c.Contains("/objects/return")).Contains("\"scope\":\"others\""), "returning all unauthorized objects");

            // history
            land.ShowPageForTesting(4);
            Require(land.AuditRowsForTesting == 2, "the administrative history is listed");

            // splitting
            land.SelectCells(LandMath.Between(8, 0, 15, 15));
            Require(land.SplitEnabledForTesting && land.SplitNoteForTesting.Contains("Casa"), $"part of one of my parcels can be split off: {land.SplitNoteForTesting}");
            said.Clear(); calls3.Clear(); land.SplitForTesting();
            Require(said.Count == 1 && !calls3.Any(c => c.Contains("/split")), "the new parcel needs a name");
            land.SetSplitNameForTesting("Metade"); said.Clear(); land.SplitForTesting();
            var split = calls3.First(c => c.Contains("/split"));
            Require(split.StartsWith("Post /parcels/" + casaId) && split.Contains("\"name\":\"Metade\"") && split.Contains("[[2,0]") && said.Any(s => s.Contains("dividido")), $"the cells to cut and the name are sent: {split}");
            land.SelectCells([.. land.Parcels.First(p => p.Name == "Casa").Cells]);
            Require(!land.SplitEnabledForTesting, "the whole parcel cannot be split off");
            answer = (409, "{\"error\":\"limit_too_low\"}"); land.SelectCells(LandMath.Between(8, 0, 15, 15)); land.SetSplitNameForTesting("Outra"); said.Clear(); land.SplitForTesting();
            Require(said.Any(s => s.Contains("limite")), "a refusal is explained"); answer = (200, "{}");

            // joining
            land.SelectCells(LandMath.Between(0, 0, 31, 15));
            Require(land.MergeEnabledForTesting && land.MergeNoteForTesting.Contains("Casa") && land.MergeNoteForTesting.Contains("Anexo"), $"two of my parcels can be joined: {land.MergeNoteForTesting}");
            calls3.Clear(); said.Clear(); land.MergeForTesting(1);
            var merge = calls3.First(c => c.Contains("/merge"));
            Require(merge.StartsWith("Post /merge") && merge.Contains(casaId.ToString()) && merge.Contains("primaryId") && said.Any(s => s.Contains("unidos")), $"the parcels and the one whose settings win are sent: {merge}");
            land.SelectCells(LandMath.Between(0, 0, 47, 15));
            Require(!land.MergeEnabledForTesting, "someone else's parcel in the selection blocks joining");
            answer = (409, "{\"error\":\"not_adjacent\"}"); land.SelectCells(LandMath.Between(0, 0, 31, 15)); said.Clear(); land.MergeForTesting(0);
            Require(said.Any(s => s.Contains("não se encostam")), "a refusal is explained"); answer = (200, "{}");

            // someone else's parcel: nothing to manage
            land.SelectCells([.. land.Parcels.First(p => p.Name == "Alheio").Cells]); land.ShowPageForTesting(3);
            Require(land.AccessNoteForTesting.Contains("proprietário"), "access belongs to the owner");

            GD.Print("event=land_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=land_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
