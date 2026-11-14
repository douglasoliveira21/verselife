using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 3 of the interface: the chat (tabs, minimize with unread count, transparency), the mini-map (opacity, full map) and the People window (nearby and recent).</summary>
public partial class InterfaceWindowsSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(30);
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var net = GetNode<NetworkManager>("/root/NetworkManager");
            var settings = ui.InterfaceSettingsForTesting;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // chat tabs
            
            var labels = string.Join("|", ui.ChatTabsForTesting!.GetChildren().OfType<Button>().Select(b => b.Text).Where(t => t.Length > 0));
            Require(labels.Contains("Local") && labels.Contains("Grupo") && labels.Contains("IM"), $"tabs Local / Grupo / IM ({labels})");

            // minimize and unread
            net.OnChatReceived(1, "RafaLima", "oi", now); await Frames(2);
            Require(ui.ChatWindowForTesting.Visible && !ui.ChatMinimizedForTesting, "a message shows the chat for a moment");
            ui.SetChatMinimizedForTesting(true); await Frames(2);
            Require(!ui.ChatWindowForTesting.Visible && ui.ChatBubbleForTesting.Visible && ui.ChatBubbleForTesting.Text == "Chat", "minimized: only the bubble shows");
            net.OnChatReceived(1, "RafaLima", "tudo bem?", now); net.OnChatReceived(2, "LuaFox", "e aí", now); await Frames(2);
            Require(ui.ChatUnreadForTesting == 2 && ui.ChatBubbleForTesting.Text.Contains("2 novas") && !ui.ChatWindowForTesting.Visible, $"two messages are counted on the bubble ({ui.ChatBubbleForTesting.Text})");
            ui.ChatBubbleForTesting.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(ui.ChatWindowForTesting.Visible && !ui.ChatBubbleForTesting.Visible && ui.ChatUnreadForTesting == 0, "the bubble restores the chat and clears the count");
            ui.SetChatOpacityForTesting(.5f); Require(Mathf.IsEqualApprox(settings.ChatOpacity, .5f), "chat transparency is saved");

            // mini-map
            var map = ui.MiniMapWindowForTesting; Require(map.EdgeResizable, "the mini-map resizes from its edges");
            ui.MiniMapOpacitySliderForTesting.Value = 60; await Frames(1);
            Require(Mathf.IsEqualApprox(settings.MiniMapOpacity, .6f), "mini-map opacity is saved");
            Require(Find<Button>(map, b => b.Text == "Mapa completo") is not null, "the mini-map has a button for the full map");

            // people
            var people = ui.GetNode<PeoplePanel>("People");
            people.NearbyProvider = () => [("LuaFox", 12.4f), ("RafaLima", 3f)];
            string? profile = null, message = null; people.ProfileByNameRequested += n => profile = n; people.MessageByNameRequested += n => message = n;
            people.Toggle(); people.SelectTabForTesting(1); await Frames(2);
            Require(people.NearbyForTesting.ItemCount == 2 && people.NearbyForTesting.GetItemText(0) == "RafaLima", "Perto lists people nearest first");
            Require(people.RecentNames.Count == 2 && people.RecentForTesting.ItemCount == 2, "those people become Recentes");
            Require(settings.RecentPeople.Contains("RafaLima") && settings.RecentPeople.Contains("LuaFox"), "recent people are saved for the account");
            people.NearbyForTesting.Select(0);
            var perfil = Find<Button>(people.NearbyForTesting.GetParent().GetParent(), b => b.Text == "Perfil"); Require(perfil is { Disabled: false }, "a selected person enables Perfil");
            perfil!.EmitSignal(BaseButton.SignalName.Pressed); Require(profile == "RafaLima", "Perfil asks for that person's profile");
            people.NearbyProvider = () => []; people.RefreshNearby();
            Require(people.NearbyForTesting.ItemCount == 0 && people.RecentForTesting.ItemCount == 2, "when they leave, Perto empties and Recente keeps them");
            people.SelectTabForTesting(2); await Frames(1);

            GD.Print("event=interface_windows_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=interface_windows_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static T? Find<T>(Node node, Func<T, bool> test) where T : Node { if (node is T t && test(t)) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
