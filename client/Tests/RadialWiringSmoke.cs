using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 2 in the real viewer: a right click on an inventory card, an inventory folder and a script panel opens the radial menu for it, and its slices run the windows' own actions.</summary>
public partial class RadialWiringSmoke : Node
{
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static IEnumerable<Node> All(Node node) { yield return node; foreach (var child in node.GetChildren()) foreach (var below in All(child)) yield return below; }

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(30);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var menu = ui.Radial!;
            ui.PreviewForTesting("inventory"); await Frames(20);

            // ---- an inventory card
            var card = All(world).OfType<Control>().First(n => n.GetType().Name == "ItemCard" && n.HasMethod("SetThumbnail"));
            card.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right, Position = new Vector2(20, 20), GlobalPosition = new Vector2(700, 400) });
            await Frames(3);
            Require(menu.IsOpen && menu.Labels.Contains("Colocar no mundo") && menu.Labels.Contains("Enviar"), $"a right click on an item opens its radial menu: {string.Join(",", menu.Labels)}");
            Require(menu.Title.Length > 0, "the centre names the item");
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH") is { Length: > 0 } capture) { await Frames(15); GetViewport().GetTexture().GetImage().SavePng(capture + "-inventario.png"); }
            menu.Close();

            // ---- a folder of the tree
            var inventory = world.GetNode<InventoryPanel>("InventoryPanel"); var tree = All(inventory).OfType<Tree>().First();
            var folderItem = FindFolder(tree.GetRoot());
            if (folderItem is not null)
            {
                folderItem.Select(0); await Frames(2);
                tree.EmitSignal(Tree.SignalName.ItemMouseSelected, new Vector2(10, 10), (long)MouseButton.Right); await Frames(2);
                Require(menu.IsOpen && menu.Labels.SequenceEqual(["Nova subpasta", "Renomear", "Excluir"]), $"a right click on a folder opens the folder menu: {string.Join(",", menu.Labels)}");
                menu.Close();
            }
            else GD.Print("event=radial_wiring note=no-folder-in-sample");

            // ---- a script panel
            var hud = world.GetNode<ScriptHudLayer>("ScriptHudLayer"); var script = Guid.NewGuid(); var sent = new List<string>(); hud.Send = sent.Add;
            hud.OnOperation($"{{\"operation\":\"script_hud\",\"script\":\"{script}\",\"objectName\":\"Loja\",\"visible\":true,\"title\":\"Loja do Rafa\",\"elements\":[{{\"id\":\"t\",\"type\":\"text\",\"text\":\"Olá\"}}]}}");
            await Frames(3);
            Require(hud.Has(script), "the panel is shown");
            hud.BoxOf(script)!.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right, Position = new Vector2(10, 10), GlobalPosition = new Vector2(500, 300) });
            await Frames(2);
            Require(menu.IsOpen && menu.Labels.SequenceEqual(["Fechar painel", "Sobre o painel"]) && menu.Title == "Loja", $"a right click on a script panel opens its menu: {string.Join(",", menu.Labels)} / {menu.Title}");
            menu.PickForTesting(0); await Frames(2);
            Require(!hud.Has(script) && sent.Any(s => s.Contains("\"action\":\"close\"")), "Fechar painel closes it and tells the island");

            // ---- the payment window: balance, one key, pressing twice pays once
            var host = new Control(); host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); ui.AddChild(host); var payer = new PersonActions(); world.AddChild(payer); payer.Setup(host); var transfers = new List<string>(); var said = new List<string>(); payer.Said += said.Add; var rafaId = Guid.NewGuid();
            payer.Transport = (method, path, body) =>
            {
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{rafaId}\",\"username\":\"rafa\",\"displayName\":\"Rafa\",\"relation\":\"none\"}}]");
                if (path.EndsWith("/economy/wallet")) return (200, "{\"availableCoins\":120}");
                if (path.EndsWith("/economy/transfer")) { transfers.Add(body!); return (200, "{\"status\":\"completed\"}"); }
                return (200, "{}");
            };
            payer.AskPayment("Rafa"); await Frames(3);
            var window = All(host).First(n => n.Name == "AskPayment"); var field = All(window).OfType<LineEdit>().First(); var payButton = All(window).OfType<Button>().First(b => b.Text == "Pagar");
            Require(All(window).OfType<Label>().Any(l => l.Text.Contains("120")), "the window shows the balance");
            field.Text = "500"; payButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(transfers.Count == 0 && All(window).OfType<Label>().Any(l => l.Text.Contains("insuficiente")), "more than the balance is refused on the screen, nothing is sent");
            field.Text = "75"; payButton.EmitSignal(BaseButton.SignalName.Pressed); payButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(transfers.Count == 1 && transfers[0].Contains("\"amountCoins\":75") && transfers[0].Contains("idempotencyKey"), $"pressing Pagar twice sends one payment: {transfers.Count}");
            Require(!IsInstanceValid(window) || window.IsQueuedForDeletion(), "the window closes after a payment");

            // ---- teleports between two people: an invite arrives, the person answers, the right one moves
            var tpHost = new Control(); tpHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); ui.AddChild(tpHost);
            var tpActions = new PersonActions(); world.AddChild(tpActions); tpActions.Setup(tpHost);
            var me2 = Guid.NewGuid(); var other2 = Guid.NewGuid(); tpActions.Me = () => me2;
            var tpCalls = new List<string>(); var tpSaid = new List<string>(); var moves = new List<(float, float)>(); tpActions.Said += tpSaid.Add; tpActions.TeleportRequested += (x, z) => moves.Add((x, z));
            tpActions.Transport = (method, path, body) =>
            {
                tpCalls.Add($"{method} {path} {body}");
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{other2}\",\"username\":\"rafa\",\"displayName\":\"Rafa\",\"relation\":\"none\"}}]");
                if (path.EndsWith("/accept")) return (200, $"{{\"islandId\":\"{Guid.NewGuid()}\",\"moverUserId\":\"{me2}\",\"x\":40.0,\"y\":3.0,\"z\":12.0}}");
                if (path == "/api/v1/social/teleports") return body!.Contains("request") ? (409, "{\"error\":\"not_on_island\"}") : (200, "{\"inviteId\":\"x\"}");
                return (200, "{}");
            };
            var islandNow = Guid.NewGuid();
            tpActions.SendTeleportInvite("Rafa", islandNow, "offer");
            Require(tpCalls[^1].StartsWith("Post /api/v1/social/teleports") && tpCalls[^1].Contains(other2.ToString()) && tpCalls[^1].Contains("\"kind\":\"offer\"") && tpCalls[^1].Contains(islandNow.ToString()) && tpSaid[^1].Contains("Convite de teleporte enviado"), $"an offer goes to the API with the island: {tpCalls[^1]}");
            tpActions.SendTeleportInvite("Rafa", islandNow, "request");
            Require(tpSaid[^1].Contains("mesma ilha"), $"a refused request says why: {tpSaid[^1]}");
            var inviteId = Guid.NewGuid();
            tpActions.OnLiveEvent($"{{\"type\":\"teleport_invite\",\"data\":{{\"inviteId\":\"{inviteId}\",\"kind\":\"offer\",\"fromName\":\"Rafa\",\"islandId\":\"{islandNow}\"}}}}"); await Frames(3);
            var inviteWindow = All(tpHost).FirstOrDefault(n => n.Name == $"TeleportInvite_{inviteId:N}");
            Require(inviteWindow is not null && All(inviteWindow).OfType<Label>().Any(l => l.Text.Contains("Rafa") && l.Text.Contains("levar")), "an incoming offer shows who sent it and what it does");
            tpActions.OnLiveEvent($"{{\"type\":\"teleport_invite\",\"data\":{{\"inviteId\":\"{inviteId}\",\"kind\":\"offer\",\"fromName\":\"Rafa\"}}}}"); await Frames(2);
            Require(All(tpHost).Count(n => n.Name == $"TeleportInvite_{inviteId:N}") == 1, "the same invite never opens two windows");
            All(inviteWindow!).OfType<Button>().First(b => b.Text == "Aceitar").EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(tpCalls[^1].StartsWith($"Post /api/v1/social/teleports/{inviteId}/accept") && moves.Count == 1 && Math.Abs(moves[0].Item1 - 41.5f) < .01f && Math.Abs(moves[0].Item2 - 12f) < .01f, $"accepting asks the API and moves me beside the sender: {string.Join(";", moves)}");
            tpActions.OnLiveEvent($"{{\"type\":\"teleport_answer\",\"data\":{{\"status\":\"accepted\",\"byName\":\"Bia\",\"moverUserId\":\"{me2}\",\"x\":5.0,\"z\":6.0}}}}");
            Require(moves.Count == 2 && Math.Abs(moves[1].Item1 - 6.5f) < .01f, "a request I sent is accepted: I am the one who moves");
            tpActions.OnLiveEvent($"{{\"type\":\"teleport_answer\",\"data\":{{\"status\":\"accepted\",\"byName\":\"Bia\",\"moverUserId\":\"{other2}\",\"x\":5.0,\"z\":6.0}}}}");
            Require(moves.Count == 2, "when the other person is the one who moves, I do not move");
            tpActions.OnLiveEvent("{\"type\":\"teleport_answer\",\"data\":{\"status\":\"declined\",\"byName\":\"Bia\"}}");
            Require(tpSaid[^1].Contains("recusou") && moves.Count == 2, "a refusal only tells me");
            tpActions.OnLiveEvent("not json"); tpActions.OnLiveEvent("{\"type\":\"teleport_invite\",\"data\":{}}");

            // ---- the ground, with the real land data of the demo parcels (cells are 4 m: "Praça" is mine, "Loja do Rafa" is for sale)
            ui.PreviewForTesting("land"); await Frames(15);
            var manager = ui.ContextManager!;
            manager.OpenFor(new GroundTarget(new Vector3(10, 0, 10)), new Vector2(900, 500));
            Require(menu.EnabledLabels.Contains("Gerenciar") && menu.EnabledLabels.Contains("Construir") && !menu.EnabledLabels.Contains("Comprar"), $"on my own parcel I can manage and build, not buy: {string.Join(",", menu.EnabledLabels)}");
            menu.Close();
            manager.OpenFor(new GroundTarget(new Vector3(40, 0, 10)), new Vector2(900, 500));
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH") is { Length: > 0 } cap2) { await Frames(15); menu.HoverForTesting(new Vector2(-95, 25)); await Frames(15); GetViewport().GetTexture().GetImage().SavePng(cap2 + "-terreno.png"); }
            Require(menu.EnabledLabels.Contains("Comprar") && !menu.EnabledLabels.Contains("Gerenciar") && !menu.EnabledLabels.Contains("Construir"), $"on land for sale I can buy, not manage or build: {string.Join(",", menu.EnabledLabels)}");
            menu.Close();
            manager.OpenFor(new GroundTarget(new Vector3(300, 0, 300)), new Vector2(900, 500));
            Require(menu.EnabledLabels.Contains("Sobre a parcela") && !menu.EnabledLabels.Contains("Gerenciar"), "on land nobody owns I can only ask about it");
            menu.Close();

            GD.Print("event=radial_wiring result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=radial_wiring result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static TreeItem? FindFolder(TreeItem? item)
    {
        for (var node = item; node is not null; node = node.GetNext())
        {
            var meta = node.GetMetadata(0);
            if (meta.VariantType == Variant.Type.String && meta.AsString().StartsWith("folder:", StringComparison.Ordinal)) return node;
            if (FindFolder(node.GetFirstChild()) is { } found) return found;
        }
        return null;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
