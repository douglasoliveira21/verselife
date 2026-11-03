using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Load test of the build view: an island with 2000 objects (mostly look-alikes, some unique shapes). It measures the time to build them,
/// to move every one of them (which must not rebuild anything) and to rebuild them all, and checks that look-alikes share their meshes.
/// The numbers are printed so they can be compared between versions; the limits asserted are generous so a slow computer does not fail.
/// </summary>
public partial class BuildLoadSmoke : Node
{
    private const int Objects = 2000;

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            var editor = world.GetNode<BuildEditor>("BuildEditor");
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control joinWindow) joinWindow.Visible = false;
            for (var i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            BuildObjectView.ClearCachesForTesting();

            var random = new Random(7);
            var shapes = new[] { "cube", "cube", "cube", "cube", "cube", "cube", "cube", "cylinder", "cylinder", "sphere" };
            var states = Enumerable.Range(0, Objects).Select(i =>
            {
                var shape = shapes[i % shapes.Length];
                var unique = i % 20 == 0; // one object in twenty has its own twist, hollow and size
                return new BuildObjectState
                {
                    Id = Guid.NewGuid(), PrimitiveType = shape, Name = $"Objeto {i}", Revision = 1,
                    PositionX = i % 50 * 3, PositionY = .5f, PositionZ = i / 50 * 3,
                    ScaleX = 1 + i % 3, ScaleY = 1 + i % 2, ScaleZ = 1 + i % 3,
                    GeometryJson = unique && shape != "sphere" ? $"{{\"twist\":{random.Next(10, 120)},\"hollow\":{(random.Next(1, 8) / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}}}" : "{}",
                    MaterialJson = $"{{\"color\":\"{(i % 4 == 0 ? "#C98B5A" : i % 4 == 1 ? "#8FA9C9" : "#A8C7D8")}\"}}", PhysicsJson = "{\"collision\":true}"
                };
            }).ToList();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            editor.LoadStatesForTesting(states);
            var buildMs = clock.ElapsedMilliseconds;
            var meshes = BuildObjectView.CachedMeshesForTesting; var materials = BuildObjectView.CachedMaterialsForTesting;
            var nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            Require(editor.ObjectCountForTesting == Objects, $"all objects must exist: {editor.ObjectCountForTesting}");

            clock.Restart();
            foreach (var state in states) { state.PositionX += 1.5f; state.Revision++; }
            editor.LoadStatesForTesting(states);
            var moveMs = clock.ElapsedMilliseconds;

            clock.Restart();
            foreach (var state in states) { state.Name += "*"; state.Revision++; }
            BuildObjectView.ClearCachesForTesting();
            editor.LoadStatesForTesting(states);
            var rebuildMs = clock.ElapsedMilliseconds;

            GD.Print($"event=build_load_metrics objects={Objects} build_ms={buildMs} move_ms={moveMs} rebuild_ms={rebuildMs} shared_meshes={meshes} shared_materials={materials} nodes={nodes:F0} nodes_per_object={nodes / Objects:F1}");
            Require(meshes <= Objects / 10, $"look-alikes must share meshes: {meshes} meshes for {Objects} objects");
            Require(materials <= 80, $"look-alikes must share materials: {materials}");
            Require(moveMs * 3 < Math.Max(rebuildMs, 30) || moveMs < 60, $"moving every object must be much cheaper than rebuilding them: move {moveMs} ms, rebuild {rebuildMs} ms");
            Require(buildMs < 60_000, $"building {Objects} objects took {buildMs} ms");
            Require(nodes / Objects <= 7, $"too many nodes per object: {nodes / Objects:F1}");
            GD.Print("event=build_load_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=build_load_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
