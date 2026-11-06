using LifeVerse.Shared.Avatar;
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
            ModelImportDialog.SetPlacementSizeForTesting(2f); // the placement size is a saved preference; start from the default
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
            Require(requests.Count == 1 && requests[0].StartsWith("Post ") && requests[0].Contains("/inventory/move") && requests[0].Contains(folder.ToString()) && requests[0].Contains(modelItem.ToString()), $"move request wrong: {string.Join("|", requests)}");
            Require(inventory.TreeForTesting.GetRoot().GetChild(0).GetChild(0).GetText(0).StartsWith("Casas (1)"), "the item should now be inside the folder");
            inventory.MoveItem(modelItem, null);
            Require(inventory.TreeForTesting.GetRoot().GetChild(0).GetChild(0).GetText(0).StartsWith("Casas (0)"), "dragging out of the folder should empty it");

            // Inventory 2.0, phase 1: subfolders, server-side filters and order, pages, the bin.
            var casa = Guid.NewGuid(); var sala = Guid.NewGuid(); var quarto = Guid.NewGuid();
            inventory.PopulateForTesting($"[{{\"id\":\"{casa}\",\"name\":\"Casa\",\"parentId\":null,\"itemCount\":1}},{{\"id\":\"{sala}\",\"name\":\"Sala\",\"parentId\":\"{casa}\",\"itemCount\":0}},{{\"id\":\"{quarto}\",\"name\":\"Quarto\",\"parentId\":\"{casa}\",\"itemCount\":0}}]", $"[{{\"itemId\":\"{modelItem}\",\"assetId\":\"{modelAsset}\",\"name\":\"Caixa\",\"category\":\"models\",\"mimeType\":\"model/gltf-binary\",\"contentHash\":\"h1\",\"size\":10,\"version\":1,\"favorite\":false,\"deleted\":false,\"folderId\":\"{casa}\"}}]");
            var lines = inventory.TreeLinesForTesting();
            Require(lines[0] == "Meu Inventário" && lines[1].TrimEnd() == " Casa (1)" && lines[2] == "  Quarto (0)" && lines[3] == "  Sala (0)", $"subfolders must nest inside their folder: {string.Join(" | ", lines)}");
            Require(lines.Any(l => l == "Recentes") && lines.Any(l => l == "Favoritos") && lines.Any(l => l == "Lixeira"), "the sections of the inventory");
            requests.Clear();
            inventory.MoveFolderForTesting(casa, sala);
            Require(requests.Count == 0 && inventory.StatusForTesting.Contains("dentro dela"), $"a folder cannot go into its own subfolder: {inventory.StatusForTesting}"); // spared the round trip
            inventory.MoveFolderForTesting(sala, null);
            Require(requests.Count == 1 && requests[0].Contains("/inventory/move") && requests[0].Contains(sala.ToString()) && requests[0].Contains("\"itemIds\":[]"), $"moving a folder to the top is one move request: {string.Join("|", requests)}");
            inventory.SearchForTesting("sofá"); inventory.ChooseForTesting(1, 2);
            var url = inventory.UrlForTesting();
            Require(url.Contains("view=all") && url.Contains("search=sof%C3%A1") && url.Contains("types=models") && url.Contains("sort=name&dir=desc") && url.Contains("page=1&pageSize=50"), $"the list is asked with the filters: {url}");
            inventory.ChooseForTesting(0, 0); inventory.SearchForTesting(string.Empty);
            requests.Clear(); inventory.ChooseSectionForTesting($"folder:{casa}");
            Require(requests.Count >= 1 && requests[0].Contains($"folderId={casa}") && requests[0].Contains("includeSubfolders=true"), $"choosing a folder asks the server for it (with what is inside it): {string.Join("|", requests)}");
            requests.Clear(); inventory.ChooseSectionForTesting("recent");
            Require(requests.Count >= 1 && requests[0].Contains("view=recent") && !requests[0].Contains("folderId"), $"the recent section: {string.Join("|", requests)}");
            requests.Clear(); inventory.ChooseSectionForTesting("trash");
            Require(requests.Count >= 1 && requests[0].Contains("view=trash"), $"the bin section: {string.Join("|", requests)}");
            inventory.PopulatePageForTesting($"[{{\"itemId\":\"{Guid.NewGuid()}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"A\",\"category\":\"models\"}},{{\"itemId\":\"{Guid.NewGuid()}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"B\",\"category\":\"models\"}}]", 1, true, 5);
            Require(inventory.ItemCountForTesting == 2 && inventory.LoadMoreVisibleForTesting && inventory.StatusForTesting.Contains("2 de 5"), $"page one of five: {inventory.StatusForTesting}");
            inventory.PopulatePageForTesting($"[{{\"itemId\":\"{Guid.NewGuid()}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"C\",\"category\":\"models\"}}]", 2, false, 5);
            Require(inventory.ItemCountForTesting == 3 && !inventory.LoadMoreVisibleForTesting, "the next page is added to the list, and the button goes away at the end");
            inventory.TrashFoldersForTesting($"{{\"retentionDays\":30,\"folders\":[{{\"id\":\"{Guid.NewGuid()}\",\"name\":\"Apagada\",\"items\":4}}]}}");
            Require(inventory.TreeLinesForTesting().Any(l => l.Trim() == "Apagada (4)"), "a folder in the bin is listed under Lixeira with what it holds");
            requests.Clear(); inventory.ChooseSectionForTesting("all"); inventory.DeleteFolderForTesting(); inventory.MenuForTesting(15);
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains("/inventory/trash/empty")), $"emptying the bin: {string.Join("|", requests)}");
            inventory.PopulateForTesting($"[{{\"id\":\"{folder}\",\"name\":\"Casas\"}}]", $"[{{\"itemId\":\"{modelItem}\",\"assetId\":\"{modelAsset}\",\"name\":\"Caixa\",\"category\":\"models\",\"mimeType\":\"model/gltf-binary\",\"contentHash\":\"h1\",\"size\":{bytes.Length},\"version\":1,\"favorite\":false,\"deleted\":false}},{{\"itemId\":\"{textureItem}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Pedra\",\"category\":\"textures\",\"mimeType\":\"image/png\",\"contentHash\":\"h2\",\"size\":10,\"version\":1,\"favorite\":false,\"deleted\":false}}]");

            // Inventory 2.0, phase 2: previews of models, textures, animations and sounds.
            var preview = inventory.PreviewForTesting; var files = new Dictionary<Guid, byte[]>();
            AssetBytes.Provider = (asset, hash) => Task.FromResult(files.TryGetValue(asset, out var b) ? b : null);
            Guid Stock(byte[] data) { var id = Guid.NewGuid(); files[id] = data; return id; }
            var pngImage = Image.CreateEmpty(32, 16, false, Image.Format.Rgba8); pngImage.Fill(new Color(.9f, .3f, .2f, .5f));
            Guid[] ids = [Stock(bytes), Stock(pngImage.SavePngToBuffer()), Stock(HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Andar", 1f, [("upper_leg_l", 0, .8f), ("upper_leg_r", 0, -.8f)], HipsBob: .04f), new AnimationSpec("Acenar", 2f, [("upper_arm_r", 2, 1.5f)])] })), Stock(BuildWav(8000, 1.5)), Stock([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]), Stock(HumanoidTemplate.Build())];
            Require(AssetPreview.KindOf("models", "model/gltf-binary") == PreviewKind.Model && AssetPreview.KindOf("textures", "image/png") == PreviewKind.Texture && AssetPreview.KindOf("animations", "model/gltf-binary") == PreviewKind.Animation && AssetPreview.KindOf("sounds", "audio/wav") == PreviewKind.Audio && AssetPreview.KindOf("objects", "") == PreviewKind.None, "each kind of item has its preview");

            // a model: turns on its own, reports the real size, can be zoomed
            Require(await preview.OpenAsync(ids[0], "Caixa", "models", "model/gltf-binary", "", bytes.Length) == PreviewKind.Model && preview.Visible && preview.ModelForTesting is not null, "the model preview opens");
            Require(preview.InfoForTesting.Contains("triângulos") && preview.InfoForTesting.Contains("2") && preview.InfoForTesting.Contains("12"), $"the model's size and triangles are shown: {preview.InfoForTesting}");
            var near = preview.ModelDistanceForTesting; preview.ZoomForTesting(.5f); Require(preview.ModelDistanceForTesting < near, "zooming in brings the camera closer"); for (var i = 0; i < 40; i++) preview.ZoomForTesting(.5f); Require(preview.ModelDistanceForTesting > near * .1f, "zooming has a limit");
            await Frames(3);

            // a texture: real size, fits the window, zooms
            Require(await preview.OpenAsync(ids[1], "Tinta", "textures", "image/png", "", 200) == PreviewKind.Texture && preview.TextureSizeForTesting == new Vector2I(32, 16) && preview.InfoForTesting.Contains("32 × 16") && preview.InfoForTesting.Contains("transparência"), $"the texture preview: {preview.InfoForTesting}");
            preview.ActualSizeForTesting(); Require(Mathf.IsEqualApprox(preview.TextureZoomForTesting, 1f), "100% shows one pixel per pixel"); preview.ZoomPictureForTesting(2f); Require(Mathf.IsEqualApprox(preview.TextureZoomForTesting, 2f), "zoom in");
            for (var i = 0; i < 20; i++) preview.ZoomPictureForTesting(2f); Require(preview.TextureZoomForTesting <= 32.001f, "the zoom has a ceiling");

            // an animation: the clips of the file are listed, it plays, loops, pauses and can be scrubbed
            Require(await preview.OpenAsync(ids[2], "Passeio", "animations", "model/gltf-binary", "", 5000) == PreviewKind.Animation && preview.ClipNamesForTesting.SequenceEqual(["Andar", "Acenar"]), $"the clips are listed: {string.Join(",", preview.ClipNamesForTesting)}");
            var firstPose = preview.PoseSignatureForTesting; preview.AdvanceForTesting(.4f); Require(preview.PoseSignatureForTesting != firstPose && Mathf.Abs(preview.ClipTimeForTesting - .4f) < .001f, "time passes and the skeleton moves");
            preview.AdvanceForTesting(.8f); Require(preview.ClipTimeForTesting < .4f, $"the clip loops: {preview.ClipTimeForTesting}");
            preview.SetPlayingForTesting(false); var held = preview.ClipTimeForTesting; preview.AdvanceForTesting(.5f); Require(Mathf.IsEqualApprox(preview.ClipTimeForTesting, held), "paused means paused");
            preview.SetPlayingForTesting(true); preview.SetSpeedForTesting(2f); var before = preview.ClipTimeForTesting; preview.AdvanceForTesting(.1f); Require(Mathf.Abs(preview.ClipTimeForTesting - (before + .2f)) < .001f, "double speed");
            preview.SelectClipForTesting(1); Require(preview.ClipTimeForTesting == 0, "another clip starts from the beginning");
            Require(await preview.OpenAsync(ids[5], "Sem animação", "animations", "model/gltf-binary", "", 5000) == PreviewKind.Animation && preview.ProblemForTesting.Length > 0 && preview.ClipNamesForTesting.Length == 0, $"a file with no animation says so: {preview.ProblemForTesting}");

            // a sound: a WAV plays, has a length and a waveform; a damaged one is explained
            Require(await preview.OpenAsync(ids[3], "Toque", "sounds", "audio/wav", "", 30000) == PreviewKind.Audio && Mathf.Abs(preview.SoundLengthForTesting - 1.5) < .05 && preview.WavePeaksForTesting == 480 && preview.StreamForTesting is AudioStreamWav, $"the sound preview: length {preview.SoundLengthForTesting}");
            preview.PlaySoundForTesting(); await Frames(3); Require(preview.SoundPlayingForTesting, "the sound plays"); preview.StopSoundForTesting(); Require(!preview.SoundPlayingForTesting, "and stops");
            Require(await preview.OpenAsync(ids[4], "Estragado", "sounds", "audio/ogg", "", 13) == PreviewKind.Audio && preview.ProblemForTesting.Length > 0, "an unreadable sound is explained, not played");

            // what is fetched is checked against its hash, remembered, and the preview of other things is refused with a reason
            AssetBytes.ForgetAllForTesting(); var asset = ids[1]; var realHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(files[asset])).ToLowerInvariant();
            Require(await preview.OpenAsync(asset, "Tinta", "textures", "image/png", new string('0', 64), 200) == PreviewKind.Texture && preview.ProblemForTesting.Contains("diferente"), $"bytes that do not match the registered hash are refused: {preview.ProblemForTesting}");
            Require(AssetBytes.RememberedForTesting == 0, "and not remembered");
            await preview.OpenAsync(asset, "Tinta", "textures", "image/png", realHash, 200); Require(preview.TextureSizeForTesting.X == 32 && AssetBytes.RememberedForTesting == 1, "a matching copy is shown and remembered");
            files.Remove(asset); await preview.OpenAsync(asset, "Tinta", "textures", "image/png", realHash, 200); Require(preview.TextureSizeForTesting.X == 32, "the second time it comes from memory, with no download");
            Require(await preview.OpenAsync(Guid.NewGuid(), "Enorme", "models", "model/gltf-binary", "", AssetBytes.MaxBytes + 1) == PreviewKind.Model && preview.ProblemForTesting.Contains("Grande demais"), "a huge file is not downloaded");
            Require(await preview.OpenAsync(Guid.NewGuid(), "Script", "objects", "text/plain", "", 10) == PreviewKind.None && preview.ProblemForTesting.Contains("não tem pré-visualização"), "other kinds have no preview");
            Require(await preview.OpenAsync(Guid.NewGuid(), "Sumido", "textures", "image/png", "", 10) == PreviewKind.Texture && preview.ProblemForTesting.Contains("Não foi possível carregar"), "a download that fails is explained");
            preview.Close(); Require(!preview.Visible, "closing hides it");
            AssetBytes.Provider = null; AssetBytes.ForgetAllForTesting();

            // opened from the inventory: asks for the preview of the chosen item and notes that it was used
            requests.Clear(); AssetBytes.Provider = (asset2, _) => Task.FromResult<byte[]?>(pngImage.SavePngToBuffer());
            Require(await inventory.OpenPreviewForTesting(textureItem) == PreviewKind.Texture, "the item from the inventory opens in the preview"); preview.Close(); AssetBytes.Provider = null; AssetBytes.ForgetAllForTesting();

            // Inventory 2.0, phase 4: the sections for returned and lost objects, unzipping, and what is inside a construction.
            var treeNow = inventory.TreeLinesForTesting(); Require(treeNow.Contains("Devolvidos e recuperados") && treeNow.Contains("Objetos apagados"), $"the new sections are listed: {string.Join("|", treeNow)}");
            requests.Clear(); inventory.ChooseSectionForTesting("returned"); Require(requests.Count >= 1 && requests[0].Contains("view=returned"), $"returned and recovered items are their own list: {string.Join("|", requests)}"); inventory.ChooseSectionForTesting("all");
            Require(InventoryPanel.UnpackText(200, "{\"count\":3,\"folderName\":\"Casa\"}").Contains("3 arquivo(s) na pasta “Casa”") && InventoryPanel.UnpackText(422, "{\"error\":\"bad_files\",\"report\":{\"problems\":[{\"file\":\"a.txt\",\"error\":\"unsupported_type\"},{\"file\":\"../b.png\",\"error\":\"unsafe_path\"}]}}") is { } why && why.Contains("Nada foi importado: 2") && why.Contains("a.txt: tipo não aceito") && why.Contains("caminho perigoso"), "an unzip says what it made, or exactly which files stopped it");
            Require(InventoryPanel.UnpackText(400, "{\"error\":\"invalid_zip\"}").Contains("não é um .zip válido") && InventoryPanel.UnpackText(422, "{\"error\":\"too_many_files\"}").Contains("no máximo 100") && InventoryPanel.UnpackText(500, "garbage").Contains("HTTP 500"), "unzip refusals are explained");

            var packId = Guid.NewGuid(); AssetPreview.ContentsProvider = _ => (200, "{\"itemId\":\"" + packId + "\",\"name\":\"Casa\",\"objects\":3,\"linksets\":1,\"withScripts\":1,\"withAnimations\":0,\"parts\":[{\"index\":0,\"name\":\"Base\",\"shape\":\"mesh\",\"parent\":null,\"linked\":true,\"scripts\":1,\"animations\":0,\"otherContent\":0,\"hasPivot\":true},{\"index\":1,\"name\":\"Parede\",\"shape\":\"cube\",\"parent\":0,\"linked\":true,\"scripts\":0,\"animations\":0,\"otherContent\":0,\"hasPivot\":false},{\"index\":2,\"name\":\"Janela\",\"shape\":\"cube\",\"parent\":1,\"linked\":true,\"scripts\":0,\"animations\":0,\"otherContent\":2,\"hasPivot\":false}],\"assets\":[{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Estátua\",\"category\":\"models\",\"role\":\"model\",\"held\":false},{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Tinta\",\"category\":\"textures\",\"role\":\"texture\",\"held\":true}],\"missing\":1}");
            Require(await preview.OpenPackageAsync(packId, "Casa", 2) == PreviewKind.Package && preview.PartsForTesting == 3 && preview.InfoForTesting.Contains("3 peça(s)") && preview.InfoForTesting.Contains("1 com scripts") && preview.ProblemForTesting.Length == 0, $"the construction's parts are listed: {preview.InfoForTesting}");
            Require(preview.PartTextForTesting(0).StartsWith("Base (mesh)") && preview.PartTextForTesting(0).Contains("1 script(s)") && preview.PartTextForTesting(0).Contains("pivô próprio") && preview.PartTextForTesting(1).Contains("└ Parede") && preview.PartTextForTesting(2).StartsWith("      └ Janela") && preview.PartTextForTesting(2).Contains("2 item(ns) dentro"), $"the tree is drawn by depth: [{preview.PartTextForTesting(0)}] [{preview.PartTextForTesting(1)}] [{preview.PartTextForTesting(2)}]");
            AssetPreview.ContentsProvider = _ => (404, "{}"); await preview.OpenPackageAsync(packId, "Casa"); Require(preview.ProblemForTesting.Contains("não está mais") && preview.PartsForTesting == 0, "a construction that is gone is explained");
            AssetPreview.ContentsProvider = _ => (200, "garbage"); await preview.OpenPackageAsync(packId, "Casa"); Require(preview.ProblemForTesting.Contains("não pôde ser lido"), "a damaged answer is explained"); AssetPreview.ContentsProvider = null; preview.Close();

            inventory.OpenLostForTesting(); var lostWindow = inventory.LostWindowForTesting ?? throw new InvalidOperationException("no window for lost objects"); var lostCalls = new List<string>(); var lostA = Guid.NewGuid(); var lostB = Guid.NewGuid(); var recovered = false;
            lostWindow.Transport = (method, path, body) => { lostCalls.Add($"{method} {path} {body}"); if (method == "GET") return (200, recovered ? "{\"objects\":[]}" : "{\"objects\":[{\"id\":\"" + lostA + "\",\"name\":\"Sofá\",\"shape\":\"cube\",\"groupId\":\"" + Guid.NewGuid() + "\",\"deletedAt\":\"" + DateTimeOffset.UtcNow.AddDays(-2).ToString("O") + "\",\"expiresAt\":\"" + DateTimeOffset.UtcNow.AddDays(28).ToString("O") + "\"},{\"id\":\"" + lostB + "\",\"name\":\"Mesa\",\"shape\":\"cube\",\"deletedAt\":\"" + DateTimeOffset.UtcNow.AddDays(-1).ToString("O") + "\",\"expiresAt\":\"" + DateTimeOffset.UtcNow.AddDays(29).ToString("O") + "\"}]}"); recovered = true; return (200, "{}"); };
            lostWindow.Reload(); Require(lostWindow.Lost.Count == 2 && lostWindow.Lost[0].Linked && !lostWindow.Lost[1].Linked && lostWindow.Lost[0].Text.Contains("conjunto") && !lostWindow.RecoverEnabledForTesting, "the deleted objects are listed; nothing is chosen yet");
            lostWindow.SelectForTesting(0); Require(lostWindow.RecoverEnabledForTesting, "choosing enables recovering"); lostCalls.Clear(); lostWindow.RecoverForTesting(true);
            Require(lostCalls[0].StartsWith("POST /api/v1/inventory/lost/recover") && lostCalls[0].Contains(lostA.ToString()) && !lostCalls[0].Contains(lostB.ToString()), $"only the chosen object is sent: {lostCalls[0]}"); Require(lostWindow.NoteForTesting.Contains("Devolvidos e recuperados") && lostWindow.Lost.Count == 0, $"after recovering the list is read again: {lostWindow.NoteForTesting}");
            Require(LostObjectsWindow.Parse("garbage").Count == 0 && LostObjectsWindow.Parse("{\"objects\":[{\"id\":\"x\"}]}").Count == 0, "damaged lists are ignored");
            lostWindow.Transport = (method, path, body) => method == "GET" ? (200, "{\"objects\":[{\"id\":\"" + lostA + "\",\"name\":\"Sofá\",\"shape\":\"cube\",\"deletedAt\":\"2026-11-01T10:00:00Z\",\"expiresAt\":\"2026-12-01T10:00:00Z\"}]}") : (409, "{\"error\":\"already_recovered\"}"); lostWindow.Reload(); lostWindow.RecoverForTesting(false);
            Require(lostWindow.NoteForTesting.Contains("já recuperou"), $"a second recovery is explained: {lostWindow.NoteForTesting}");

            // Inventory 2.0, phase 5: versions of an asset and the update mark.
            requests.Clear(); inventory.ChooseSectionForTesting("updated"); Require(requests.Count >= 1 && requests[0].Contains("view=updated"), $"items with a new version are their own list: {string.Join("|", requests)}"); inventory.ChooseSectionForTesting("all");
            Require(inventory.TreeLinesForTesting().Contains("Com atualização"), "the section is listed");
            var upItem = Guid.NewGuid(); var upAsset = Guid.NewGuid();
            inventory.PopulatePageForTesting($"[{{\"itemId\":\"{upItem}\",\"assetId\":\"{upAsset}\",\"name\":\"Estátua\",\"category\":\"models\",\"mimeType\":\"model/gltf-binary\",\"contentHash\":\"h9\",\"size\":10,\"version\":3,\"hasUpdate\":true,\"isCreator\":false}}]", 1, false, 1);
            Require(inventory.CardCountForTesting == 1, "the item with an update is listed");
            inventory.OpenVersionsForTesting(upItem); var versions = inventory.VersionsWindowForTesting ?? throw new InvalidOperationException("no versions window"); var vcalls = new List<string>(); var owner = false; var published = false;
            string VersionsJson() => "{\"assetId\":\"" + upAsset + "\",\"name\":\"Estátua\",\"category\":\"models\",\"current\":" + (published ? 4 : 3) + ",\"isOwner\":" + (owner ? "true" : "false") + ",\"versions\":[" + (published ? "{\"version\":4,\"at\":\"2026-11-09T12:00:00Z\",\"size\":2048,\"mimeType\":\"model/gltf-binary\",\"notes\":\"mais leve\",\"current\":true}," : string.Empty) + "{\"version\":3,\"at\":\"2026-11-05T12:00:00Z\",\"size\":2048,\"mimeType\":\"model/gltf-binary\",\"notes\":\"corrige a porta\",\"current\":" + (published ? "false" : "true") + "},{\"version\":2,\"at\":\"2026-11-03T12:00:00Z\",\"size\":1500,\"mimeType\":\"model/gltf-binary\",\"notes\":\"cores\",\"current\":false},{\"version\":1,\"at\":\"2026-11-01T12:00:00Z\",\"size\":1024,\"mimeType\":\"model/gltf-binary\",\"notes\":\"\",\"current\":false}]}";
            versions.Transport = (method, path, body) => { vcalls.Add($"{method} {path}"); if (method == "GET") return (200, VersionsJson()); if (path.EndsWith("/versions") && method == "POST") { published = true; return (200, "{\"assetId\":\"" + upAsset + "\",\"version\":4,\"changed\":true}"); } return (200, "{}"); };
            vcalls.Clear(); versions.Reload(true);
            Require(versions.Versions.Count == 3 && versions.Versions[0].Current && !versions.IsOwnerForTesting && !versions.PublishVisibleForTesting && versions.Versions[0].Text.Contains("em uso") && versions.Versions[0].Text.Contains("corrige a porta"), $"a holder reads the versions and what changed: {versions.NoteForTesting}");
            Require(vcalls.Any(c => c == $"POST /api/v1/inventory/items/{upItem}/ack-update"), $"looking at the versions marks the update as seen: {string.Join("|", vcalls)}");
            owner = true; versions.Reload(); Require(versions.IsOwnerForTesting && versions.PublishVisibleForTesting && !versions.UseEnabledForTesting, "the creator can publish; nothing to switch to while the current one is chosen");
            versions.SelectForTesting(2); Require(versions.UseEnabledForTesting, "an older version can be chosen"); vcalls.Clear(); versions.UseForTesting(); Require(vcalls.Any(c => c == $"POST /api/v1/assets/{upAsset}/versions/1/activate") && versions.NoteForTesting.Contains("todos usam a versão 1"), $"going back asks the server: {string.Join("|", vcalls)} / {versions.NoteForTesting}");
            vcalls.Clear(); versions.PublishForTesting([1, 2, 3], "novo.glb", "mais leve"); Require(vcalls.Any(c => c == $"POST /api/v1/assets/{upAsset}/versions") && versions.Versions[0].Version == 4 && versions.NoteForTesting.Contains("avisado"), $"publishing sends the file and the note, and the list is read again: {versions.NoteForTesting}");
            Require(AssetVersionsWindow.Parse("garbage").Versions.Count == 0 && AssetVersionsWindow.Parse("{\"versions\":[{\"x\":1}]}").Versions.Count == 0, "damaged lists are ignored");
            versions.Transport = (method, path, body) => method == "GET" ? (200, VersionsJson()) : (409, "{\"error\":\"unchanged\"}"); versions.Reload(); versions.PublishForTesting([1], "igual.glb", ""); Require(versions.NoteForTesting.Contains("igual ao da versão em uso"), $"a refusal is explained: {versions.NoteForTesting}");
            versions.Transport = (method, path, body) => (404, "{\"error\":\"not_found\"}"); versions.Reload(); Require(versions.NoteForTesting.Contains("Não há histórico"), "an item without history is explained");
            inventory.PopulateForTesting($"[{{\"id\":\"{folder}\",\"name\":\"Casas\"}}]", $"[{{\"itemId\":\"{modelItem}\",\"assetId\":\"{modelAsset}\",\"name\":\"Caixa\",\"category\":\"models\",\"mimeType\":\"model/gltf-binary\",\"contentHash\":\"h1\",\"size\":{bytes.Length},\"version\":1,\"favorite\":false,\"deleted\":false}},{{\"itemId\":\"{textureItem}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Pedra\",\"category\":\"textures\",\"mimeType\":\"image/png\",\"contentHash\":\"h2\",\"size\":10,\"version\":1,\"favorite\":false,\"deleted\":false}}]");

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

            // The import window: real size, triangles and materials before anything is uploaded.
            var dialog = inventory.ImportDialogForTesting;
            string? confirmedPath = null; dialog.Confirmed += path => confirmedPath = path;
            dialog.Show("caixa.glb", bytes);
            await Frames(2);
            var report = dialog.Report!;
            Require(report.Valid && report.Triangles == 12, $"a box has twelve triangles: {report.Triangles}");
            Require(Mathf.Abs(report.Size.X - 2f) < .01f && Mathf.Abs(report.Size.Y - 1f) < .01f && Mathf.Abs(report.Size.Z - 4f) < .01f, $"the real size must be shown: {report.Size}");
            Require(dialog.CanConfirmForTesting && dialog.InfoForTesting.Contains("caixa.glb") && dialog.InfoForTesting.Contains("12"), $"the info panel must describe the model: {dialog.InfoForTesting}");
            dialog.ConfirmForTesting("300");
            Require(confirmedPath is null && dialog.ProblemForTesting.Contains("0,1 e 64"), "an absurd placement size must be refused with a reason");
            dialog.ConfirmForTesting("3,5");
            Require(confirmedPath == "caixa.glb" && Mathf.Abs(ModelImportDialog.PlacementSize - 3.5f) < .001f, $"confirming must hand over the file and remember the size: {confirmedPath} {ModelImportDialog.PlacementSize}");
            dialog.Show("caixa.glb", bytes); dialog.ConfirmForTesting("2"); // restore the default for the other tests
            Require(Mathf.Abs(ModelImportDialog.PlacementSize - 2f) < .001f, "the default size must be restored");
            dialog.Show("lixo.glb", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
            Require(dialog.Report is { Valid: false, Error: not null } && !dialog.CanConfirmForTesting, "a damaged file must not be importable");
            dialog.Show("vazio.glb", []);
            Require(!dialog.CanConfirmForTesting && dialog.ProblemForTesting.Length > 0, "an empty file must not be importable");
            dialog.Close();

            GD.Print("event=inventory_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=inventory_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>A mono 16-bit WAV of a pure tone, as a real file would arrive.</summary>
    private static byte[] BuildWav(int rate, double seconds)
    {
        var frames = (int)(rate * seconds); using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + frames * 2); w.Write("WAVE"u8.ToArray()); w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(frames * 2); for (var i = 0; i < frames; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 12000));
        return stream.ToArray();
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
