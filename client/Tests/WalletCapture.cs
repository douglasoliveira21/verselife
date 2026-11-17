using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Visual QA of the wallet window with canned answers (WALLET_PAGE = the page to show). Set LIFEVERSE_CAPTURE_PATH.</summary>
public partial class WalletCapture : Node
{
    public override async void _Ready()
    {
        var viewport = new SubViewport { Size = new Vector2I(1600, 900), RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; AddChild(viewport);
        var back = new ColorRect { Color = new Color("#0B1117") }; back.SetAnchorsPreset(Control.LayoutPreset.FullRect); viewport.AddChild(back);
        var window = new WalletWindow { Transport = (method, path, body) => path.Split('?')[0] switch
        {
            "/api/v1/economy/wallet" => (200, "{\"availableCoins\":3450,\"isBlocked\":false}"),
            "/api/v1/economy/packages" => (200, "{\"paymentsEnabled\":false,\"packages\":[{\"id\":\"inicial\",\"name\":\"Inicial\",\"coins\":500,\"bonusPercent\":0,\"bonusCoins\":0,\"totalCoins\":500,\"priceCents\":500},{\"id\":\"b\",\"name\":\"Básico\",\"coins\":1000,\"bonusPercent\":0,\"bonusCoins\":0,\"totalCoins\":1000,\"priceCents\":1000},{\"id\":\"i\",\"name\":\"Intermediário\",\"coins\":2500,\"bonusPercent\":10,\"bonusCoins\":250,\"totalCoins\":2750,\"priceCents\":2500},{\"id\":\"a\",\"name\":\"Avançado\",\"coins\":5000,\"bonusPercent\":15,\"bonusCoins\":750,\"totalCoins\":5750,\"priceCents\":5000},{\"id\":\"p\",\"name\":\"Premium\",\"coins\":10000,\"bonusPercent\":20,\"bonusCoins\":2000,\"totalCoins\":12000,\"priceCents\":10000}]}"),
            "/api/v1/economy/wallet/summary" => (200, "{\"received\":2790,\"spent\":100,\"count\":3,\"reserved\":300,\"available\":2000,\"total\":2300,\"isBlocked\":false}"),
            "/api/v1/economy/wallet/transactions" => (200, "[{\"id\":\"22222222-2222-2222-2222-222222222222\",\"type\":\"user_transfer\",\"status\":\"completed\",\"amount\":500,\"direction\":\"out\",\"createdAt\":\"2026-11-16T14:20:00Z\"},{\"id\":\"33333333-3333-3333-3333-333333333333\",\"type\":\"coin_purchase\",\"status\":\"completed\",\"amount\":2750,\"direction\":\"in\",\"createdAt\":\"2026-11-15T10:02:00Z\"}]"),
            _ => (404, "{}")
        } };
        window.Size = new Vector2(1600, 900); viewport.AddChild(window);
        for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        window.Open(int.TryParse(OS.GetEnvironment("WALLET_PAGE"), out var page) ? page : 0);
        for (var i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = viewport.GetTexture().GetImage().SavePng(OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")); GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
