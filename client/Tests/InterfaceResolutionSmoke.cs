using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 5 of the interface: at each common resolution (and with automatic scale) the bars fit the screen, nothing overlaps the bars wrongly and every window can be reached. Also switches resolution with every window open.</summary>
public partial class InterfaceResolutionSmoke : Node
{
    private static readonly Vector2I[] Sizes = [new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3440, 1440), new(3840, 2160)];

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(30);
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var window = GetWindow();
            // open every window at once: the hardest case for the layout (windows built on demand are opened through their own Toggle)
            foreach (var node in world.GetChildren().Concat(ui.GetChildren())) if (node is not FloatingWindow && node.HasMethod("Toggle")) { try { node.Call("Toggle"); } catch (Exception) { } }
            await Frames(10);
            var all = new List<FloatingWindow>(); Collect(world, all);
            foreach (var w in all) w.Visible = true;
            ui.SetInterfaceModeForTesting("full"); ui.OpenInterfaceSettingsForTesting();
            var report = new List<string>();

            foreach (var size in Sizes)
            {
                window.Size = size; await Frames(6); WindowManager.KeepAll(); await Frames(3);
                var view = GetViewport().GetVisibleRect().Size;
                var real = window.Size; var scale = window.ContentScaleFactor;
                var stretch = Math.Min(real.X / 1920f, real.Y / 1080f); var expectedView = new Vector2(real.X / (stretch * scale), real.Y / (stretch * scale));
                Require(Math.Abs(view.X - expectedView.X) < 4 && Math.Abs(view.Y - expectedView.Y) < 4, $"{size}: the viewport follows the window ({view} vs {expectedView}, window {real})");
                var top = Named(ui, "MenuBarPanel").GetGlobalRect(); var bottom = Named(ui, "ToolbarPanel").GetGlobalRect();
                var location = ui.LocationBarForTesting.GetGlobalRect(); var side = ui.SideBarForTesting.GetGlobalRect();
                Require(top.Position.Y >= -.5f && top.End.X <= view.X + 1 && top.Size.X >= view.X - 1, $"{size}: the menu bar spans the screen ({top} in {view})");
                Require(location.Position.Y >= top.End.Y - 1 && location.End.X <= view.X + 1, $"{size}: the location bar sits under the menu and fits ({location})");
                Require(bottom.Position.X >= -.5f && bottom.End.X <= view.X + 1 && bottom.End.Y <= view.Y + 1, $"{size}: the toolbar fits the screen ({bottom} in {view})");
                Require(side.Position.X >= -.5f && side.Position.Y >= location.End.Y - 1 && side.End.Y <= bottom.Position.Y + 1, $"{size}: the shortcut bar fits between the bars ({side})");
                var bad = new List<string>(); var checkedCount = 0;
                foreach (var w in all.Where(w => GodotObject.IsInstanceValid(w) && w.IsVisibleInTree()))
                {
                    checkedCount++; var r = w.GetGlobalRect(); var min = w.GetCombinedMinimumSize();
                    var fits = min.X <= view.X + 1 && min.Y <= view.Y - WindowManager.TopLimit + 1;
                    if (fits && (r.Position.X < -1 || r.Position.Y < WindowManager.TopLimit - 1 || r.End.X > view.X + 1 || r.End.Y > view.Y + 1)) bad.Add($"{w.Name} {r}");
                    if (!fits && (r.Position.X > view.X - 40 || r.Position.Y > view.Y - 40 || r.End.X < 40)) bad.Add($"{w.Name} (oversized) {r}");
                }
                Require(bad.Count == 0, $"{size}: windows outside the screen: {string.Join("; ", bad.Take(4))}");
                report.Add($"{size.X}x{size.Y} scale={scale:0.00} view={view.X:0}x{view.Y:0} windows={checkedCount} toolbarIconsOnly={ui.ToolbarIconsOnlyForTesting}");
            }

            // modes at the smallest and the largest screens
            foreach (var size in new[] { Sizes[0], Sizes[^1] })
            {
                window.Size = size; await Frames(6);
                ui.SetInterfaceModeForTesting("compact"); await Frames(2);
                var bottom = Named(ui, "ToolbarPanel").GetGlobalRect(); var view = GetViewport().GetVisibleRect().Size;
                Require(bottom.End.X <= view.X + 1, $"{size}: compact toolbar fits");
                ui.SetInterfaceModeForTesting("full"); await Frames(2);
            }
            window.Size = Sizes[3]; await Frames(4);
            foreach (var line in report) GD.Print("event=interface_resolution " + line);
            GD.Print("event=interface_resolution_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=interface_resolution_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static Control Named(Node root, string name) => root.FindChild(name, true, false) as Control ?? throw new InvalidOperationException($"no {name}");
    private static void Collect(Node node, List<FloatingWindow> into) { if (node is FloatingWindow w) into.Add(w); foreach (var child in node.GetChildren()) Collect(child, into); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
