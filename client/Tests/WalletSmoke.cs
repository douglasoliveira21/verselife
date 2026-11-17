using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

public partial class WalletSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var sent = new List<(string Method, string Path)>(); long balance = 3450; var ordersStatus = 503; var ordersBody = "{\"error\":\"payments_not_configured\"}"; var orderState = "pending";
            var window = new WalletWindow { Transport = (method, path, body) => { sent.Add((method.ToString(), path)); return path switch
            {
                "/api/v1/economy/wallet" => (200, $"{{\"availableCoins\":{balance},\"isBlocked\":false}}"),
                "/api/v1/economy/packages" => (200, "{\"paymentsEnabled\":true,\"packages\":[{\"id\":\"a\",\"name\":\"A\",\"coins\":1000,\"bonusPercent\":0,\"bonusCoins\":0,\"totalCoins\":1000,\"priceCents\":1000},{\"id\":\"b\",\"name\":\"B\",\"coins\":2500,\"bonusPercent\":10,\"bonusCoins\":250,\"totalCoins\":2750,\"priceCents\":2500}]}"),
                "/api/v1/economy/wallet/transactions" => (200, "[{\"type\":\"transfer\",\"status\":\"completed\",\"amount\":500,\"createdAt\":\"2026-11-16T14:20:00Z\"}]"),
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
            window.Close(); Require(!window.IsOpen, "the window closes");
            GD.Print("event=wallet_smoke result=passed"); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PrintErr($"event=wallet_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
