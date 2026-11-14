using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 4 of the interface: the display modes (completo, compacto, imersivo) and the Interface settings window.</summary>
public partial class InterfaceModesSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(30);
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var s = ui.InterfaceSettingsForTesting;

            ui.SetInterfaceModeForTesting("full"); await Frames(2);
            Require(ui.BarVisibleForTesting("top") && ui.BarVisibleForTesting("location") && ui.BarVisibleForTesting("side") && ui.BarVisibleForTesting("bottom"), "full: every bar shows");
            ui.SetInterfaceModeForTesting("compact"); await Frames(2);
            Require(ui.BarVisibleForTesting("top") && ui.BarVisibleForTesting("bottom") && !ui.BarVisibleForTesting("location") && !ui.BarVisibleForTesting("side") && ui.ToolbarIconsOnlyForTesting, "compact: menu and toolbar (icons only), no location or shortcut bar");
            Require(s.Mode == "compact", "the mode is saved");
            ui.SetInterfaceModeForTesting("immersive"); await Frames(2);
            Require(!ui.BarVisibleForTesting("top") && !ui.BarVisibleForTesting("bottom") && !ui.BarVisibleForTesting("location") && !ui.BarVisibleForTesting("side"), "immersive: no bar");
            ui.ToggleImmersiveRevealForTesting(); await Frames(2);
            Require(ui.ImmersiveRevealForTesting && ui.BarVisibleForTesting("top") && ui.BarVisibleForTesting("bottom"), "F10 shows the interface for a while");
            ui.ToggleImmersiveRevealForTesting(); await Frames(2);
            Require(!ui.BarVisibleForTesting("top"), "and hides it again");
            ui.SetInterfaceModeForTesting("nonsense"); Require(s.Mode == "full", "an unknown mode falls back to full");
            ui.ToggleImmersiveRevealForTesting(); Require(!ui.ImmersiveRevealForTesting, "F10 does nothing outside immersive mode");

            // window opacity and the settings window
            ui.OpenInterfaceSettingsForTesting(); await Frames(2);
            var w = ui.InterfaceWindowForTesting; Require(w is { Visible: true }, "Configurações → Interface opens");
            WindowManager.SetWindowOpacity(.5f); Require(Mathf.IsEqualApprox(s.WindowOpacity, .5f), "window opacity is saved");
            ui.SetInterfaceModeForTesting("compact"); s.AutoScale = false; s.Scale = 1.25f; s.CompactToolbar = true; s.ChatOpacity = .4f; s.Save();
            ui.ResetInterfaceForTesting(); await Frames(2);
            Require(s.Mode == "full" && s.AutoScale && !s.CompactToolbar && Mathf.IsEqualApprox(s.ChatOpacity, .8f) && Mathf.IsEqualApprox(s.WindowOpacity, .92f) && s.SideToolbarOpen && s.Windows.Count == 0, "restore everything puts the defaults back");
            Require(ui.BarVisibleForTesting("location") && ui.BarVisibleForTesting("side"), "and the bars are back");
            Require(Find<OptionButton>(w!, o => o.ItemCount == 3 && o.GetItemText(0) == "Completo") is not null && Find<OptionButton>(w!, o => o.GetItemText(0) == "Automático") is not null, "the window has the mode and scale choices");

            GD.Print("event=interface_modes_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=interface_modes_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static T? Find<T>(Node node, Func<T, bool> test) where T : Node { if (node is T t && test(t)) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
