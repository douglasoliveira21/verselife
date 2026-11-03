using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Imported models in the world and the inventory around them: a real GLB is built in memory, shown as a "mesh"
/// object, normalised, selectable and movable; folders, drag-and-drop moves, placing rules and thumbnails are checked.
/// Set LIFEVERSE_CAPTURE_PATH (windowed run) for screenshots.
/// </summary>
public partial class InventorySmoke : Node
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, BuildObjectState> _server = [];
    private BuildEditor _editor = null!;

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            _editor = world.GetNode<BuildEditor>("BuildEditor");
            _editor.OperationSink = Sink;
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control joinWindow) joinWindow.Visible = false;
            var cache = world.GetNode<ModelCache>("ModelCache");
            var inventory = world.GetNode<InventoryPanel>("InventoryPanel");
            await Frames(20);

            // A 2 x 1 x 4 m box exported as a real GLB: the importer must shrink it so its longest side is one metre.
            var bytes = BuildGlb(new Vector3(2, 1, 4));
            var modelAsset = Guid.NewGuid(); var textureAsset = Guid.NewGuid();
            Require(bytes.Length > 100 && bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F', "could not build a GLB");
            Require(cache.Preload(modelAsset, bytes), "the model cache rejected a valid GLB");
            Require(!ModelCache.Parse([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]).Node?.IsInsideTree() ?? true, "garbage must not produce a model");
            Require(ModelCache.Parse("{\"asset\":{\"version\":\"2.0\",\"uri\":\"outside.bin\"}}"u8.ToArray()).Node is null, "a glTF pointing at external files must not load");

            var folder = Guid.NewGuid(); var modelItem = Guid.NewGuid(); var textureItem = Guid.NewGuid();
            inventory.PopulateForTesting($"[{{\"id\":\"{folder}\",\"name\":\"Casas\"}}]",
                $"[{{\"itemId\":\"{modelItem}\",\"assetId\":\"{modelAsset}\",\"name\":\"Caixa\",\"category\":\"models\",\"mimeType\":\"model/gltf-binary\",\"contentHash\":\"h1\",\"size\":{bytes.Length},\"version\":1,\"favorite\":false,\"deleted\":false,\"folderId\":null}},{{\"itemId\":\"{textureItem}\",\"assetId\":\"{textureAsset}\",\"name\":\"Grama\",\"category\":\"textures\",\"mimeType\":\"image/png\",\"contentHash\":\"h2\",\"size\":100,\"version\":1,\"favorite\":true,\"deleted\":false,\"folderId\":null}}]");
            Require(inventory.ItemCountForTesting == 2, $"inventory should hold 2 items: {inventory.ItemCountForTesting}");
            var mine = inventory.TreeForTesting.GetRoot().GetChild(0);
            Require(mine.GetText(0) == "Meu Inventário" && mine.GetChildCount() == 1, $"expected the folder inside Meu Inventário: {mine.GetChildCount()}");
            Require(mine.GetChild(0).GetText(0).StartsWith("Casas (0)"), "the folder should be listed with its count");
            Require(inventory.CardCountForTesting == 2, $"both items should show as cards: {inventory.CardCountForTesting}");

            // Drag a model onto the folder: it moves locally at once and the PATCH carries the folder id.
            var requests = new List<string>();
            inventory.RequestSink = (method, path, body) => { requests.Add($"{method} {path} {body}"); return true; };
            inventory.MoveItem(modelItem, folder);
            Require(requests.Count == 1 && requests[0].StartsWith("Patch ") && requests[0].Contains(folder.ToString()) && requests[0].Contains(modelItem.ToString()), $"move request wrong: {string.Join("|", requests)}");
            Require(inventory.TreeForTesting.GetRoot().GetChild(0).GetChild(0).GetText(0).StartsWith("Casas (1)"), "the item should now be inside the folder");
            inventory.MoveItem(modelItem, null);
            Require(inventory.TreeForTesting.GetRoot().GetChild(0).GetChild(0).GetText(0).StartsWith("Casas (0)"), "dragging out of the folder should empty it");

            // Only models can be placed in the world.
            inventory.PlaceForTesting(textureItem, new Vector3(0, 0, -6));
            Require(_server.Count == 0, "a texture must not become a world object");

            inventory.PlaceForTesting(modelItem, new Vector3(0, 1, -6));
            Require(_server.Count == 1, "placing a model should create exactly one object");
            var state = _server.Values.Single();
            Require(state.PrimitiveType == "mesh" && state.GeometryJson.Contains(modelAsset.ToString()), $"unexpected object: {state.PrimitiveType} {state.GeometryJson}");
            await Frames(5);
            var view = _editor.ViewForTesting(state.Id) ?? throw new InvalidOperationException("the mesh object has no view");
            var model = view.GetNodeOrNull<Node3D>("Model") ?? throw new InvalidOperationException("the model did not appear inside the object");
            var proxy = view.GetNode<MeshInstance3D>("PrimitiveMesh");
            var size = ((BoxMesh)proxy.Mesh).Size;
            Require(Mathf.Abs(size.X - .5f) < .02f && Mathf.Abs(size.Y - .25f) < .02f && Mathf.Abs(size.Z - 1f) < .02f, $"model was not normalised to one metre: {size}");
            Require(((BoxShape3D)view.GetNode<StaticBody3D>("CollisionBody").GetChild<CollisionShape3D>(0).Shape).Size.IsEqualApprox(size), "collision does not match the model");
            Require(model.GetChildCount() > 0 && model.GetChild<Node3D>(0).Position.Length() < .01f, "the model should be centred on the object origin");
            Require(Mathf.Abs(view.Scale.X - 2f) < .001f, "a freshly placed model starts two metres across");
            Require(_editor.Active && _editor.Selection.Count == 1 && _editor.Selection[0] == view, "placing a model should select it with the handles on");

            // The same model again reuses the cache; moving the object keeps the model.
            inventory.PlaceForTesting(modelItem, new Vector3(4, 1, -6));
            Require(_server.Count == 2 && cache.CachedModels == 1, "the cache should hold one model for two objects");
            await Frames(3);
            Require(_editor.ViewForTesting(_server.Keys.Last())!.GetNodeOrNull<Node3D>("Model") is not null, "the second object should show the model too");
            await Capture("models");

            // A model that is not available leaves a visible marker instead of nothing.
            var broken = Guid.NewGuid(); var brokenState = new BuildObjectState { Id = broken, PrimitiveType = "mesh", Name = "Perdido", GeometryJson = $"{{\"assetId\":\"{Guid.NewGuid()}\"}}", PositionX = -4, PositionY = 1, PositionZ = -6 };
            _server[broken] = brokenState; _editor.LoadStatesForTesting([brokenState]);
            await Frames(5);
            Require(_editor.ViewForTesting(broken)!.GetNodeOrNull<Node3D>("Model") is null && _editor.ViewForTesting(broken)!.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is not null, "an unavailable model must keep its placeholder");

            // Thumbnails: generated placeholders per kind, and a real render of the model when a renderer exists.
            var placeholder = ThumbnailLoader.Placeholder("models").GetImage();
            Require(placeholder.GetWidth() == ThumbnailLoader.Size && placeholder.GetHeight() == ThumbnailLoader.Size, "placeholder has the wrong size");
            var loader = inventory.GetNode<ThumbnailLoader>("Thumbnails");
            var delivered = new List<Texture2D>();
            loader.Get(Guid.NewGuid(), "no-such-hash", "audio/ogg", "sounds", 100, delivered.Add);
            Require(delivered.Count == 1, "a sound should get its placeholder immediately");
            if (DisplayServer.GetName() != "headless")
            {
                var rendered = await loader.RenderForTesting(bytes);
                Require(rendered is not null && rendered.GetWidth() == ThumbnailLoader.Size, "the model thumbnail was not rendered");
                var covered = 0;
                for (var y = 0; y < rendered!.GetHeight(); y += 2) for (var x = 0; x < rendered.GetWidth(); x += 2) if (rendered.GetPixel(x, y).A > .5f) covered++;
                Require(covered > 20, $"the model thumbnail is empty ({covered} opaque samples)");
                var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
                if (!string.IsNullOrWhiteSpace(path)) rendered.SavePng($"{path}-thumbnail.png");
            }

            GD.Print("event=inventory_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=inventory_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static byte[] BuildGlb(Vector3 size)
    {
        var root = new Node3D();
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#c46a3a") } };
        root.AddChild(mesh); mesh.Owner = root;
        var document = new GltfDocument(); var state = new GltfState();
        document.AppendFromScene(root, state);
        var bytes = document.GenerateBuffer(state);
        root.Free();
        return bytes;
    }

    private bool Sink(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var operation = root.GetProperty("operation").GetString();
        if (operation != "create") return true;
        var payload = JsonSerializer.Deserialize<BuildObjectState>(root.GetProperty("object").GetRawText(), Web)!;
        payload.Id = Guid.NewGuid(); payload.Revision = 1;
        _server[payload.Id] = payload;
        _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, objectState = payload }, Web));
        return true;
    }

    private async Task Capture(string name)
    {
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png");
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
