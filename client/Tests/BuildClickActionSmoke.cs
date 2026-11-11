using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The click action of an object, for anybody in the world: touch, sit, buy, pay, open, zoom, ignore and none each do their own thing.</summary>
public partial class BuildClickActionSmoke : Node3D
{
    private static readonly Guid Ana = Guid.NewGuid();

    private BuildObjectView Make(Node parent, string action, string permissions = "{}", string content = "[]", Vector3? at = null)
    {
        var view = new BuildObjectView(); parent.AddChild(view);
        view.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Caixa de " + action, PrimitiveType = "cube", PositionX = (at ?? Vector3.Zero).X, PositionY = (at ?? new Vector3(0, 1, 0)).Y, PositionZ = (at ?? Vector3.Zero).Z, OwnerUserId = Ana, CreatorUserId = Ana, ClickAction = action, PermissionsJson = permissions, ContentJson = content, MaterialJson = "{}", PhysicsJson = "{\"collision\":true}", Revision = 3 });
        return view;
    }

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player");
            var actions = world.GetNodeOrNull<BuildClickActions>("BuildClickActions");
            if (actions is null) { actions = new BuildClickActions { Name = "BuildClickActions" }; world.AddChild(actions); }
            await Frames(10);
            actions.Names.LookupOverride = id => id == Ana ? "Ana Souza" : null;
            var touched = new List<Guid>(); actions.TouchSink = touched.Add;

            // touch, none, ignore
            var touch = Make(world, "touch", at: new Vector3(4, 1, 4)); actions.Perform(touch);
            Require(touched.SequenceEqual([touch.State.Id]), "touch reaches the object (and its script)");
            actions.Perform(Make(world, "none", at: new Vector3(6, 1, 4))); actions.Perform(Make(world, "ignore", at: new Vector3(8, 1, 4)));
            Require(touched.Count == 1, "none and ignore do nothing themselves");
            Require(BuildClickActions.Ignores(Make(world, "ignore", at: new Vector3(10, 1, 4))) && !BuildClickActions.Ignores(touch), "an object that ignores clicks is known as one that lets them through");

            // buy
            var sink = new List<string>(); actions.Purchase.OperationSink = json => { sink.Add(json); return true; };
            var shop = Make(world, "buy", "{\"saleType\":\"copy\",\"salePrice\":1500}", at: new Vector3(-4, 1, 4)); actions.Perform(shop); await Frames(2);
            Require(actions.Purchase.IsOpen && actions.Purchase.PriceForTesting == "1.500 moedas" && actions.Purchase.SellerForTesting == "Vendedor: Ana Souza" && actions.Purchase.GetsForTesting.Contains("cópia"), $"the purchase shows the price, the seller and what comes with it ({actions.Purchase.PriceForTesting} / {actions.Purchase.SellerForTesting})");
            actions.Purchase.PressBuyForTesting();
            var command = JsonDocument.Parse(sink[^1]).RootElement;
            Require(command.GetProperty("operation").GetString() == "buy" && command.GetProperty("objectId").GetGuid() == shop.State.Id && command.GetProperty("properties").GetProperty("expectedPrice").GetInt64() == 1500 && command.GetProperty("properties").GetProperty("idempotencyKey").GetString()!.Length >= 8, "Comprar sends the price that was shown and a key");
            Require(!actions.Purchase.CanBuyForTesting, "a second press while waiting does nothing");
            actions.Purchase.OnRejected("insufficient_balance");
            Require(actions.Purchase.StatusForTesting.Contains("Saldo insuficiente"), "the refusal is explained");
            actions.Purchase.OnOperation(JsonSerializer.Serialize(new { operation = "bought", objectId = shop.State.Id, buyerUserId = Guid.NewGuid() }));
            Require(actions.Purchase.StatusForTesting.Contains("outra pessoa"), "somebody else bought it first");
            actions.Perform(Make(world, "buy", "{}", at: new Vector3(-6, 1, 4)));
            Require(!actions.Purchase.CanBuyForTesting && actions.Purchase.GetsForTesting.Contains("não está à venda"), "an object that is no longer for sale cannot be bought");

            // pay
            var toPay = Make(world, "pay", "{\"saleType\":\"original\",\"salePrice\":40}", at: new Vector3(-8, 1, 4)); actions.Perform(toPay);
            Require(actions.Payment.IsOpen && actions.Payment.AmountForTesting == 40, "paying starts with the price of the object");
            actions.Payment.TransferOverride = (to, amount, key) => (200, "{}"); actions.Payment.AmountForTesting = 55; actions.Payment.PressPayForTesting();
            Require(actions.Payment.LastRequestForTesting is { Amount: 55 } req && req.To == Ana && req.Key.Length >= 8 && actions.Payment.StatusForTesting.Contains("55"), "the payment goes to the owner with the amount chosen");
            actions.Perform(toPay); actions.Payment.TransferOverride = (_, _, _) => (409, "{\"error\":\"insufficient_balance\"}"); actions.Payment.PressPayForTesting();
            Require(actions.Payment.StatusForTesting.Contains("Saldo insuficiente"), "a payment the ledger refuses says why");

            // open
            var chest = Make(world, "open", content: "[{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Campainha\",\"category\":\"sounds\"},{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Quadro\",\"category\":\"textures\"}]", at: new Vector3(-10, 1, 4)); actions.Perform(chest);
            Require(actions.Contents.IsOpen && actions.Contents.RowsForTesting.SequenceEqual(["Campainha  ·  Som", "Quadro  ·  Textura"]) && !actions.Contents.CopyVisibleForTesting, "somebody else's object shows what it holds, without a copy button");

            // zoom
            var zoomed = Make(world, "zoom", at: new Vector3(0, 1, -6)); actions.Perform(zoomed); await Frames(5);
            Require(actions.Zoom is { Active: true } z && z.Target.DistanceTo(zoomed.GlobalPosition) < .01f && GetViewport().GetCamera3D() == z, "zoom flies the camera to the object");
            actions.Zoom!.OrbitForTesting(1f, .2f); Require(actions.Zoom.GlobalPosition.DistanceTo(zoomed.GlobalPosition) > 1f, "the camera orbits the object");
            actions.Zoom.Exit(); await Frames(3);
            Require(!actions.Zoom.Active && GetViewport().GetCamera3D() != actions.Zoom, "leaving the zoom gives the camera back");

            // sit
            var chair = Make(world, "sit", at: new Vector3(player.GlobalPosition.X + 1.5f, 0.5f, player.GlobalPosition.Z)); await Frames(3);
            Require(actions.SitOn(chair), "sitting on an object works");
            await Frames(3);
            var seat = chair.GetNodeOrNull<InteractableSeat>("ObjectSeat");
            Require(seat is { IsOccupied: true } && player.GlobalPosition.DistanceTo(chair.GlobalPosition + Vector3.Up * (chair.State.ScaleY / 2f + .02f)) < .1f, $"the avatar sits on top of the object (distance {player.GlobalPosition.DistanceTo(chair.GlobalPosition)})");
            GD.Print("event=build_click_action_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_click_action_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
