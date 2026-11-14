using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 2 of the interface: the location bar (history, favorites), the top bar buttons, the customizable bottom toolbar and the collapsible shortcut bar.</summary>
public partial class InterfaceBarsSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var layout = ToolbarLayout.Order(["a", "b", "c"], ["c", "zzz", "a"]);
            Require(string.Join(",", layout) == "c,a,b", "saved order first, unknown ids ignored, the rest after");
            Require(string.Join(",", ToolbarLayout.Move(["a", "b", "c"], "c", -1)) == "a,c,b" && string.Join(",", ToolbarLayout.Move(["a", "b", "c"], "a", -1)) == "a,b,c", "moving stops at the ends");
            Require(!ToolbarLayout.CanHide(["a", "b"], ["a"], "b") && ToolbarLayout.CanHide(["a", "b"], [], "b"), "the last visible button cannot be hidden");

            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(30);
            var ui = world.GetNode<ViewerUI>("ViewerUI");
            var bar = ui.LocationBarForTesting; Require(bar.Visible && bar.Size.Y >= ViewerUI.LocationBarHeight - 1 && bar.Position.Y >= 37, $"the location bar sits under the menu ({bar.Position} {bar.Size})");
            Require(Button("Comprar L$") is not null, "the top bar has Comprar L$");
            Require(Tooltip("Loja (marketplace)") is not null && Tooltip("Configurações") is not null && Tooltip("Áudio: volume geral") is not null, "Loja, Áudio and Configurações are in the top bar");
            Require(Tooltip("Voltar para a ilha anterior") is { Disabled: true }, "with no history back is off");

            // history
            Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
            ui.NoteIsland(a); ui.NoteIsland(b); ui.NoteIsland(c);
            Require(ui.NavigationHistoryForTesting.Count == 3 && ui.NavigationIndexForTesting == 2, "three islands visited");
            Require(Tooltip("Voltar para a ilha anterior") is { Disabled: false } && Tooltip("Avançar para a próxima ilha") is { Disabled: true }, "back is on, forward is off");
            ui.NoteIsland(c); Require(ui.NavigationHistoryForTesting.Count == 3, "the same island twice is one entry");

            // favorites
            var settings = ui.InterfaceSettingsForTesting; settings.Favorites.Clear(); settings.Favorites.Add((a, "Ilha A")); settings.Save();
            Require(settings.Favorites.Count == 1, "favorites are kept in the account settings");

            // toolbar
            var ids = ui.ToolbarIdsForTesting.ToList(); Require(ids.Contains("mover") && ids.Contains("gestos") && ids.Contains("inventario") && ids.Contains("minimapa"), "the toolbar has its buttons");
            ui.ToolbarRestoreForTesting(); ui.ToolbarMoveForTesting("inventario", -1); await Frames(2);
            var order = ToolbarLayout.Order(ids, settings.ToolbarOrder); Require(order.IndexOf("inventario") == ids.IndexOf("inventario") - 1, "a button moves one place to the left");
            ui.ToolbarShowForTesting("foto", false); await Frames(2); Require(!ui.ToolbarShownForTesting("foto") && settings.ToolbarHidden.Contains("foto"), "a button can be hidden");
            ui.ToolbarShowForTesting("foto", true); Require(ui.ToolbarShownForTesting("foto"), "and shown again");
            ui.SetToolbarCompactForTesting(true); Require(ui.ToolbarIconsOnlyForTesting && settings.CompactToolbar, "compact mode shows icons only");
            ui.ToolbarRestoreForTesting(); Require(!settings.CompactToolbar && settings.ToolbarHidden.Count == 0 && settings.ToolbarOrder.Count == 0, "restore puts the default bar back");

            // shortcut bar
            Require(ui.SideBarForTesting.Visible && ui.SideBarOpenForTesting, "the shortcut bar starts open");
            ui.ToggleSideBarForTesting(); Require(!ui.SideBarOpenForTesting && !settings.SideToolbarOpen, "it collapses and remembers it");
            ui.ToggleSideBarForTesting(); Require(ui.SideBarOpenForTesting, "and opens again");

            GD.Print("event=interface_bars_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=interface_bars_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private Button? Button(string text) => Find<Button>(this, b => b.Text == text);
    private Button? Tooltip(string tip) => Find<Button>(this, b => b.TooltipText == tip) ?? Find<MenuButton>(this, b => b.TooltipText == tip);
    private static T? Find<T>(Node node, Func<T, bool> test) where T : Node { if (node is T t && test(t)) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
