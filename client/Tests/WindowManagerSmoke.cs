using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 1 of the interface: windows resize from every edge, remember their place per account, stay on screen and go back to the default layout.</summary>
public partial class WindowManagerSmoke : Node
{
    private const string File = "user://smoke_wm.cfg";

    private async Task<FloatingWindow> Make(Control root, string name)
    {
        var w = new FloatingWindow(name); w.Place(0, 0, 200, 100, 520, 400); root.AddChild(w);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return w;
    }

    public override async void _Ready()
    {
        try
        {
            var root = new Control(); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(root);
            WindowManager.Settings = InterfaceSettings.Load("wm", File, null);
            var w = await Make(root, "Janela de teste");
            Require(WindowManager.Count == 1 && w.Position == new Vector2(200, 100) && w.Size == new Vector2(320, 300), $"registered where the game placed it ({w.Position} {w.Size})");

            var right = w.GetNode("EdgeHandles").GetChildCount(); Require(right == 8, "four edges and four corners");
            w.ResizeEdge(1, 0, new Vector2(60, 0)); Require(w.Size.X == 380 && w.Position.X == 200, "the right edge grows to the right");
            w.ResizeEdge(-1, 0, new Vector2(-40, 0)); Require(w.Position.X == 160 && w.Size.X == 420, "the left edge grows to the left and the right side stays");
            w.ResizeEdge(0, -1, new Vector2(0, -20)); Require(w.Position.Y == 80 && w.Size.Y == 320, "the top edge");
            w.ResizeEdge(0, 1, new Vector2(0, 30)); Require(w.Size.Y == 350 && w.Position.Y == 80, "the bottom edge");
            w.ResizeEdge(1, 1, new Vector2(-5000, -5000)); Require(w.Size.X >= FloatingWindow.EdgeMinimumSize.X && w.Size.Y >= FloatingWindow.EdgeMinimumSize.Y, "never below the minimum size");
            w.ResizeEdge(-1, -1, new Vector2(-5000, -5000)); Require(w.Position.X >= 0 && w.Position.Y >= WindowManager.TopLimit, "never off the top or left of the screen");
            w.ResizeEdge(1, 1, new Vector2(9000, 9000)); var view = w.GetViewportRect().Size; Require(w.Position.X + w.Size.X <= view.X + .01f && w.Position.Y + w.Size.Y <= view.Y + .01f, "never past the right or bottom of the screen");

            // remembered per account, restored by a new window with the same name
            w.Position = new Vector2(300, 150); w.Size = new Vector2(400, 260); WindowManager.Remember(w);
            Require(WindowManager.Settings!.Windows.ContainsKey("Janela_de_teste"), "the layout is saved");
            w.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            WindowManager.Settings = InterfaceSettings.Load("wm", File, null);
            var again = await Make(root, "Janela de teste");
            Require(again.Position == new Vector2(300, 150) && again.Size == new Vector2(400, 260), $"it comes back where it was left ({again.Position} {again.Size})");

            // a smaller screen / bigger scale: the window is brought back inside
            again.Position = new Vector2(view.X + 500, view.Y + 500); WindowManager.KeepAll();
            Require(again.Position.X <= view.X - again.Size.X + .01f && again.Position.Y <= view.Y - again.Size.Y + .01f, "a window left outside is brought back");
            again.Size = new Vector2(view.X + 800, view.Y + 800); WindowManager.KeepAll();
            Require(again.Size.X <= view.X + .01f && again.Size.Y <= view.Y + .01f, "a window larger than the screen is shrunk");

            // restore
            WindowManager.ResetLayout();
            Require(again.Position == new Vector2(200, 100) && again.Size == new Vector2(320, 300) && WindowManager.Settings!.Windows.Count == 0, $"the default layout comes back ({again.Position} {again.Size})");
            Require(WindowManager.IdOf("Perfil: Ana/1=") == "Perfil__Ana_1_", "ids are safe file keys");

            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(File));
            GD.Print("event=window_manager_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=window_manager_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
