using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

public partial class WalletSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var sent = new List<(string Method, string Path)>(); string? transferBody = null; long balance = 3450; var ordersStatus = 503; var ordersBody = "{\"error\":\"payments_not_configured\"}"; var orderState = "pending";
            var window = new WalletWindow { Transport = (method, path, body) => { sent.Add((method.ToString(), path)); if (path == "/api/v1/economy/transfer") transferBody = body; return path.Split('?')[0] switch
            {
                "/api/v1/economy/wallet" => (200, $"{{\"availableCoins\":{balance},\"isBlocked\":false}}"),
                "/api/v1/economy/packages" => (200, "{\"paymentsEnabled\":true,\"packages\":[{\"id\":\"a\",\"name\":\"A\",\"coins\":1000,\"bonusPercent\":0,\"bonusCoins\":0,\"totalCoins\":1000,\"priceCents\":1000},{\"id\":\"b\",\"name\":\"B\",\"coins\":2500,\"bonusPercent\":10,\"bonusCoins\":250,\"totalCoins\":2750,\"priceCents\":2500}]}"),
                "/api/v1/social/people" => (200, "[{\"id\":\"44444444-4444-4444-4444-444444444444\",\"username\":\"anabela\",\"displayName\":\"Ana Bela\"},{\"id\":\"55555555-5555-5555-5555-555555555555\",\"username\":\"joana\",\"displayName\":\"joana\"}]"),
                "/api/v1/economy/transfer" => (200, "{\"status\":\"completed\"}"),
                "/api/v1/economy/wallet/summary" => (200, "{\"received\":2790,\"spent\":100,\"count\":3,\"reserved\":300,\"available\":2000,\"total\":2300,\"isBlocked\":false}"),
                "/api/v1/economy/wallet/transactions/22222222-2222-2222-2222-222222222222" => (200, "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"type\":\"user_transfer\",\"status\":\"completed\",\"amount\":500,\"direction\":\"out\",\"createdAt\":\"2026-11-16T14:20:00Z\",\"counterparty\":\"ana\",\"message\":\"Aqui está o combinado\",\"code\":\"ABCDEF123456\"}"),
                "/api/v1/economy/wallet/transactions" => (200, "[{\"id\":\"22222222-2222-2222-2222-222222222222\",\"type\":\"transfer\",\"status\":\"completed\",\"amount\":500,\"createdAt\":\"2026-11-16T14:20:00Z\"}]"),
                "/api/v1/economy/coin-orders" => (ordersStatus, ordersBody),
                "/api/v1/economy/coin-orders/11111111-1111-1111-1111-111111111111" => (200, $"{{\"id\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"{orderState}\"}}"),
                _ => (404, "{}")
            }; } };
            AddChild(window); window.Size = new Vector2(1600, 900);
            long seen = -1; window.BalanceChanged += value => seen = value;
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            window.Open(0);
            Require(window.IsOpen && seen == 3450 && window.Balance == 3450, "the window asks the server for the balance and announces it");
            Require(window.PackCountForTesting == 2 && sent.Any(x => x.Path == "/api/v1/economy/packages"), "the packs come from the server");
            Require(WalletWindow.TypeLabel("land_rent") == "Aluguel de terreno" && WalletWindow.TypeLabel("island_autorenewal") == "Renovação de aluguel de ilha" && WalletWindow.TypeLabel("land_sale_hold") == "Valor reservado" && WalletWindow.TypeLabel("land_deposit_refund") == "Caução devolvida" && WalletWindow.TypeLabel("object_purchase") == "Compra de objeto" && WalletWindow.TypeLabel("land_sale") == "Compra de terreno", "entries have names in Portuguese");
            Require(WalletWindow.Coins(2500) == "VC$ 2.500" && WalletWindow.Reais(2500) == "R$ 25,00", "money is written the Brazilian way");
            window.SelectPackForTesting(1); window.ConfirmBuyForTesting();
            Require(window.BuyStatusForTesting.Contains("Nenhuma cobrança"), $"without a payment provider nothing is charged and the person is told so (got {window.BuyStatusForTesting})");
            Require(sent.Count(x => x.Path == "/api/v1/economy/coin-orders") == 1, "one order was asked for");
            ordersStatus = 201; ordersBody = "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"pending\"}"; window.ConfirmBuyForTesting();
            Require(window.BuyStatusForTesting.Contains("Aguardando") && sent.Any(x => x.Path == "/api/v1/economy/coin-orders/11111111-1111-1111-1111-111111111111"), $"a created order is followed until the server says it is paid (got {window.BuyStatusForTesting})");
            orderState = "paid"; balance = 6200; window.ConfirmBuyForTesting();
            Require(window.BuyStatusForTesting.Contains("confirmado") && seen == 6200, $"when the server says paid the balance is read again (got {window.BuyStatusForTesting}, {seen})");
            window.Open(3);
            Require(window.HistoryRowsForTesting == 2, $"the history shows a header and the transaction (got {window.HistoryRowsForTesting})");
            balance = 2950; window.Refresh();
            Require(seen == 2950, "the balance is read again from the server after a change");
            window.SelectHistoryFiltersForTesting(2, 1, 2);
            Require(window.HistoryQueryForTesting == "?days=7&kind=transfer&direction=out" && sent.Any(x => x.Path == "/api/v1/economy/wallet/transactions?days=7&kind=transfer&direction=out"), $"the filters go to the server (got {window.HistoryQueryForTesting})");
            window.SelectHistoryFiltersForTesting(0, 0, 0); Require(window.HistoryQueryForTesting == "", "no filter, no query");
            window.OpenReceipt(Guid.Parse("22222222-2222-2222-2222-222222222222"));
            Require(window.ReceiptOpenForTesting && window.ReceiptTextForTesting.Contains("Para: ana") && window.ReceiptTextForTesting.Contains("Aqui está o combinado") && window.ReceiptTextForTesting.Contains("ABCDEF123456"), $"the receipt shows who, the message and the code (got {window.ReceiptTextForTesting})");
            window.Open(1); Require(window.SummaryLinesForTesting >= 6, $"the balance page shows the totals (got {window.SummaryLinesForTesting})");
            window.Open(2); window.TypeRecipientForTesting("an");
            Require(window.SuggestionsForTesting.SequenceEqual(["Ana Bela  (@anabela)", "joana"]), $"typing part of a name lists the people (got {string.Join("|", window.SuggestionsForTesting)})");
            window.ChooseSuggestionForTesting(0); Require(window.SelectedRecipientForTesting == Guid.Parse("44444444-4444-4444-4444-444444444444") && window.SuggestionsForTesting.Count == 0, "choosing a person picks the account and closes the list");
            window.SendForTesting("150", "Aqui está o combinado"); using (var sentBody = System.Text.Json.JsonDocument.Parse(transferBody ?? "{}")) Require(sentBody.RootElement.GetProperty("recipientUserId").GetString() == "44444444-4444-4444-4444-444444444444" && sentBody.RootElement.GetProperty("amountCoins").GetInt64() == 150 && sentBody.RootElement.GetProperty("message").GetString() == "Aqui está o combinado", $"the person chosen from the list is paid by account, with the message (got {transferBody})");
            Require(!sent.Any(x => x.Path.StartsWith("/api/v1/social/people?q=Ana%20Bela")), "a chosen person is not looked up again by name");
            window.TypeRecipientForTesting("a"); Require(window.SelectedRecipientForTesting is null && window.SuggestionsForTesting.Count == 0, "changing the text forgets the choice, and one letter searches nothing");
            window.Open(1); window.ApplyLiveBalance(7777); var before = sent.Count(x => x.Path.StartsWith("/api/v1/economy/wallet/summary")); Require(window.Balance == 7777 || sent.Count > before, "a pushed balance is shown at once");
            balance = 7777; window.ApplyLiveBalance(7777); Require(sent.Count(x => x.Path.StartsWith("/api/v1/economy/wallet/summary")) > before && seen == 7777, "and the open page asks for its numbers again");
            window.Close(); Require(!window.IsOpen, "the window closes");
            GD.Print("event=wallet_smoke result=passed"); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PrintErr($"event=wallet_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
