using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>The terrain editor: its tools and what each one sends, the packed brush options, who may sculpt where, and a preview that leaves the real ground untouched.</summary>
public partial class TerrainEditorSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = new Node3D(); AddChild(world);
            var terrain = new IslandTerrain { Name = "IslandTerrain" }; world.AddChild(terrain);
            var land = new LandManager { Name = "LandManager" }; world.AddChild(land);
            var editor = new TerrainEditor { Name = "TerrainEditor" }; world.AddChild(editor);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Require(editor.LayerButtonsForTesting == 8 && IslandTerrain.Layers.Length == 8 && IslandTerrain.LayerNames.Distinct().Count() == 8, "eight ground materials to choose from");
            editor.SetActive(true);
            Require(editor.WindowVisibleForTesting && editor.Active, "the window opens");
            Require(!editor.LevelRowVisibleForTesting && !editor.PaintBoxVisibleForTesting, "the raise tool shows neither the level nor the materials");
            editor.SelectToolForTesting(TerrainTool.Level); Require(editor.LevelRowVisibleForTesting && !editor.PaintBoxVisibleForTesting, "levelling asks for a height");
            editor.SelectToolForTesting(TerrainTool.Paint); Require(editor.PaintBoxVisibleForTesting && !editor.LevelRowVisibleForTesting, "painting asks for a material");
            Require(!editor.IslandBoxVisibleForTesting, "the sea and map import are for island editors only");

            // what each tool sends
            editor.SetBrushForTesting(6, 50, 40, 5);
            editor.SelectToolForTesting(TerrainTool.Raise);
            var raise = editor.RequestAt();
            Require(raise.Tool == TerrainTool.Raise && Math.Abs(raise.Radius - 6) < .01f && Math.Abs(raise.Strength - .2f) < .001f, $"raising moves by metres: {raise.Strength}");
            Require(BrushCodec.TryUnpack(raise.Packed, out var layer0, out var soft) && layer0 == 0 && !soft.Square && Math.Abs(soft.Hardness - .6f) < .05f && soft.TargetHeight is null, "the edge softness travels with the layer");
            editor.SelectShapeForTesting(true); editor.SelectToolForTesting(TerrainTool.Level);
            var level = editor.RequestAt();
            Require(level.Tool == TerrainTool.Level && Math.Abs(level.Strength - .5f) < .001f && BrushCodec.TryUnpack(level.Packed, out _, out var leveled) && leveled.Square && Math.Abs(leveled.TargetHeight!.Value - 5f) < .01f, "levelling sends a square brush and the height");
            editor.SelectLayerForTesting(6); editor.SelectToolForTesting(TerrainTool.Paint);
            Require(BrushCodec.TryUnpack(editor.RequestAt().Packed, out var painted, out _) && painted == 6, "painting sends the chosen material");
            foreach (var tool in new[] { TerrainTool.Lower, TerrainTool.Flatten, TerrainTool.Smooth, TerrainTool.Noise, TerrainTool.Erode, TerrainTool.Restore })
            { editor.SelectToolForTesting(tool); Require(editor.RequestAt().Tool == tool, $"{tool} is a tool of its own"); }

            // who may sculpt where
            var sent = new List<(TerrainTool, Vector3, float, float, int)>();
            editor.SubmitOverride = (tool, point, radius, strength, packed) => { sent.Add((tool, point, radius, strength, packed)); return true; };
            editor.SelectToolForTesting(TerrainTool.Raise);
            Require(!editor.SubmitAt(new Vector3(8, 0, 8)) && sent.Count == 0 && editor.StatusForTesting.Contains("dono"), "without island rights or a parcel nothing is sent");
            land.Apply("{\"size\":256,\"version\":1,\"parcels\":[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Casa\",\"description\":\"\",\"ownerName\":\"Eu\",\"mine\":true,\"state\":\"own\",\"area\":256,\"cells\":16,\"maxObjects\":100,\"maxCost\":2000,\"objects\":0,\"cost\":0,\"revision\":1,\"canEditTerrain\":true,\"runs\":[[0,0,3],[1,0,3],[2,0,3],[3,0,3]]}]}");
            Require(land.HasTerrainRights && editor.Mask is not null, "owning a parcel gives a mask of the ground that may change");
            Require(editor.SubmitAt(new Vector3(8, 0, 8)) && sent.Count == 1 && sent[0].Item1 == TerrainTool.Raise, "with a parcel the edit is sent (the server checks again)");

            // the preview shows the result and leaves the ground exactly as it was
            var map = terrain.Map; var snapshot = map.Serialize(); var revision = map.Revision;
            editor.SelectShapeForTesting(false); editor.SetBrushForTesting(5, 80, 100, 5); editor.SelectToolForTesting(TerrainTool.Raise);
            editor.UpdatePreview(new Vector3(8, 0, 8));
            Require(editor.PreviewVertices > 0 && map.Revision == revision && map.Serialize().SequenceEqual(snapshot), "the preview does not change the ground or its revision");
            foreach (var tool in new[] { TerrainTool.Lower, TerrainTool.Level, TerrainTool.Flatten, TerrainTool.Smooth, TerrainTool.Noise, TerrainTool.Erode, TerrainTool.Restore })
            { editor.SelectToolForTesting(tool); editor.UpdatePreview(new Vector3(8, 0, 8)); Require(map.Revision == revision && map.Serialize().SequenceEqual(snapshot), $"previewing {tool} leaves the ground alone"); }
            editor.SelectToolForTesting(TerrainTool.Paint); editor.UpdatePreview(new Vector3(8, 0, 8));
            Require(editor.PreviewVertices == 0 && map.Revision == revision, "painting only shows the brush outline");
            editor.SelectToolForTesting(TerrainTool.Raise); editor.UpdatePreview(new Vector3(80, 0, 80));
            Require(editor.PreviewVertices > 0 && map.Serialize().SequenceEqual(snapshot), "outside the parcel the preview is drawn too (in red) and still changes nothing");

            editor.RefreshForTesting();
            Require(editor.StatusForTesting.Contains("Entre em uma ilha"), "offline, the status says to enter an island");
            editor.SetActive(false);
            Require(!editor.WindowVisibleForTesting && !editor.Active, "the window closes");

            GD.Print("event=terrain_editor_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=terrain_editor_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
