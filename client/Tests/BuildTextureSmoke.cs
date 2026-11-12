using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 7: the TEXTURA tab in PBR and Blinn-Phong (maps, numbers, scale/offset/rotation per map, faces), materials as presets and files, and media on a surface.</summary>
public partial class BuildTextureSmoke : Node3D
{
    private static string StateJson(Guid id, string type, int revision, string material, float x = 0, string operation = "update") =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Peça " + type, description = "", primitiveType = type, positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = material, geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost = 2 } });

    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();
    private static string MaterialText(List<string> commands) => Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!;
    private static JsonElement Material(List<string> commands) => JsonDocument.Parse(MaterialText(commands)).RootElement.Clone();

    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D(); ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -.5f, 0) }); AddChild(ground);
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var tab = editor.Window.Texture; var id = Guid.NewGuid(); var normal = Guid.NewGuid(); var orm = Guid.NewGuid();
            Require(editor.Window.TabNamesForTesting.SequenceEqual(["GERAL", "OBJETO", "RECURSOS", "TEXTURA", "CONTEÚDO"]), "the four tabs");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 1, BuildPrimitiveFactory.DefaultMaterial, operation: "create")); editor.Selection.Set([id]); editor.Window.SelectTabForTesting(3); await Frames(2);

            // --- the two models ---------------------------------------------------------------------------------------
            Require(tab.TargetForTesting.Contains("Objeto inteiro") && tab.ModeForTesting == 1 && tab.BlinnVisibleForTesting && !tab.PbrVisibleForTesting, "a new object starts in Blinn-Phong, as the whole object");
            Require(tab.UvTabsForTesting(false).SequenceEqual(["DIFFUSE", "NORMAL", "SPECULAR"]) && tab.UvTabsForTesting(true).SequenceEqual(["ALL", "BASE COLOR", "NORMAL", "(O)RM", "EMISSIVE"]), "each model has the tabs of the maps it really has");
            commands.Clear(); tab.SelectModeForTesting(0);
            Require(Material(commands).GetProperty("shading").GetString() == "pbr", "the PBR tab changes the model of the material");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 2, MaterialText(commands))); await Frames(2);
            Require(tab.PbrVisibleForTesting && !tab.BlinnVisibleForTesting, "and shows the PBR controls");

            var capture = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); if (!string.IsNullOrWhiteSpace(capture) && DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng($"{capture}-texture-tab.png"); }
            // --- PBR maps ---------------------------------------------------------------------------------------------
            commands.Clear(); tab.PickForTesting("texture", "bricks", builtin: true);
            Require(Material(commands).GetProperty("builtin").GetString() == "bricks", "a system pattern goes on the base colour");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 3, MaterialText(commands))); await Frames(2);
            Require(tab.SlotNameForTesting("texture").Length > 0 && tab.SlotNameForTesting("texture") != "nenhuma" && tab.SlotClearVisibleForTesting("texture"), "the slot shows what is on it");
            commands.Clear(); tab.PickForTesting("normalMap", normal.ToString()); Require(Material(commands).GetProperty("normalMap").GetString() == normal.ToString(), "a normal map is stored");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 4, MaterialText(commands))); await Frames(2);
            commands.Clear(); tab.PickForTesting("ormMap", orm.ToString()); editor.Synchronizer.OnOperation(StateJson(id, "cube", 5, MaterialText(commands))); await Frames(2);
            tab.SetPbrForTesting(new Color("#12F2E0"), 30, "blend", 1.5, .25, .9, .6); tab.FlushForTesting();
            var pbr = Material(commands);
            Require(pbr.GetProperty("color").GetString() == "#12f2e0" && Math.Abs(pbr.GetProperty("transparency").GetDouble() - .7) < .001 && pbr.GetProperty("alphaMode").GetString() == "blend" && pbr.GetProperty("normalStrength").GetDouble() == 1.5 && pbr.GetProperty("roughness").GetDouble() == .25 && pbr.GetProperty("metallic").GetDouble() == .9 && pbr.GetProperty("aoStrength").GetDouble() == .6, "colour, alpha, normal strength, roughness, metallic and occlusion are stored");
            Require(pbr.GetProperty("builtin").GetString() == "bricks" && pbr.GetProperty("ormMap").GetString() == orm.ToString(), "changing numbers keeps the pictures");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 6, MaterialText(commands))); await Frames(2);
            tab.SetGlowForTesting(true, new Color("#FF8800"), 3); tab.FlushForTesting();
            Require(Material(commands).GetProperty("emission").GetString() == "#ff8800" && Material(commands).GetProperty("emissionStrength").GetDouble() == 3, "the emissive colour and intensity");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 7, MaterialText(commands))); await Frames(2);

            // --- scale, offset and rotation per map -----------------------------------------------------------------------
            tab.SetUvTabForTesting(true, 1); tab.SetUvForTesting(true, 2, 3, .5, .25, 45); tab.FlushForTesting();
            var uv = Material(commands).GetProperty("mapUv").GetProperty("texture");
            Require(uv.GetProperty("repeatX").GetDouble() == 2 && uv.GetProperty("repeatY").GetDouble() == 3 && uv.GetProperty("offsetX").GetDouble() == .5 && uv.GetProperty("offsetY").GetDouble() == .25 && uv.GetProperty("rotation").GetDouble() == 45, "the base colour has its own scale, offset and rotation");
            Require(!Material(commands).TryGetProperty("uvRepeatX", out _), "and the other maps are left alone");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 8, MaterialText(commands))); await Frames(2);
            tab.SetUvTabForTesting(true, 2); Require(tab.UvForTesting(true, 0) == 1, "the normal map still has the default scale");
            tab.SetUvTabForTesting(true, 1); Require(tab.UvForTesting(true, 0) == 2 && tab.UvForTesting(true, 4) == 45, "going back shows the base colour's own values");
            tab.SetUvTabForTesting(true, 0); tab.SetUvForTesting(true, 4, 4, 0, 0, 90); tab.FlushForTesting();
            Require(Material(commands).GetProperty("uvRepeatX").GetDouble() == 4 && Material(commands).GetProperty("uvRotation").GetDouble() == 90 && !Material(commands).TryGetProperty("mapUv", out _), "ALL gives every map the same mapping");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 9, MaterialText(commands))); await Frames(2);
            var built = MaterialBuilder.Build(JsonNode.Parse(MaterialText(commands))!.AsObject(), null, null) as ShaderMaterial;
            Require(built is not null && Math.Abs((float)built!.GetShaderParameter("normal_strength").AsDouble() - 1.5f) < .001f && built.GetShaderParameter("st_normal").AsVector4().X == 4f, "the renderer receives the strength and the mapping");

            // --- faces ---------------------------------------------------------------------------------------------------
            editor.SetTool(BuildTool.Face); editor.Faces.Toggle(id, 0, false); editor.Window.SelectTabForTesting(3); await Frames(2);
            Require(tab.TargetForTesting.Contains("Topo"), $"the target is the face chosen ({tab.TargetForTesting})");
            commands.Clear(); tab.PickForTesting("texture", "tiles", builtin: true);
            var faceLayer = Material(commands).GetProperty("faces")[0];
            Require(faceLayer.GetProperty("index").GetInt32() == 0 && faceLayer.GetProperty("builtin").GetString() == "tiles" && Material(commands).GetProperty("builtin").GetString() == "bricks", "a texture on a face does not touch the rest of the object");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 10, MaterialText(commands))); await Frames(2);
            commands.Clear(); tab.SetPbrForTesting(new Color("#FF0000"), 0, "none", 1, .5, 0, 1); tab.FlushForTesting();
            Require(Material(commands).GetProperty("faces")[0].GetProperty("color").GetString() == "#ff0000" && Material(commands).GetProperty("color").GetString() == "#12f2e0", "the colour of a face is the face's");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 11, MaterialText(commands))); await Frames(2);
            editor.Faces.Clear(); await Frames(2); commands.Clear(); tab.SetPbrForTesting(new Color("#00FF00"), 0, "none", 1, .5, 0, 1); tab.FlushForTesting();
            Require(Material(commands).GetProperty("color").GetString() == "#00ff00" && !Material(commands).GetProperty("faces")[0].TryGetProperty("color", out _), "changing the whole object takes the same setting off the faces");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 12, MaterialText(commands))); await Frames(2);

            // --- Blinn-Phong ---------------------------------------------------------------------------------------------
            commands.Clear(); tab.SelectModeForTesting(1); editor.Synchronizer.OnOperation(StateJson(id, "cube", 13, MaterialText(commands))); await Frames(2);
            tab.SetBlinnForTesting(new Color("#336699"), true, 40, 1.2, new Color("#FFFFAA"), 1.4, .8); tab.FlushForTesting();
            var blinn = Material(commands);
            Require(blinn.GetProperty("shading").GetString() == "blinnphong" && blinn.GetProperty("color").GetString() == "#336699" && blinn.GetProperty("fullBright").GetBoolean() && Math.Abs(blinn.GetProperty("transparency").GetDouble() - .6) < .001 && blinn.GetProperty("specularColor").GetString() == "#ffffaa" && blinn.GetProperty("specularStrength").GetDouble() == 1.4 && blinn.GetProperty("glossiness").GetDouble() == .8 && blinn.GetProperty("normalStrength").GetDouble() == 1.2, "diffuse colour, full bright, transparency, specular colour, intensity and gloss");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 14, MaterialText(commands))); await Frames(2);
            commands.Clear(); tab.PickForTesting("specularMap", Guid.NewGuid().ToString()); Require(Material(commands).TryGetProperty("specularMap", out _), "a specular map");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 15, MaterialText(commands))); await Frames(2);
            tab.SetGlowForTesting(false, new Color("#FFFFFF"), 2); tab.FlushForTesting(); Require(Material(commands).GetProperty("emissionStrength").GetDouble() == 2, "glow in Blinn-Phong");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 16, MaterialText(commands))); await Frames(2);

            // --- removing and materials as files ------------------------------------------------------------------------
            commands.Clear(); tab.PressClearForTesting();
            var cleared = Material(commands); Require(cleared.GetProperty("noTexture").GetBoolean() && cleared.GetProperty("color").GetString() == "#FFFFFF" && !cleared.TryGetProperty("builtin", out _) && !cleared.TryGetProperty("specularMap", out _) && !cleared.TryGetProperty("emission", out _), "Remover leaves it plain and white");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 17, MaterialText(commands))); await Frames(2);
            commands.Clear(); tab.ApplyPresetForTesting(0);
            Require(Material(commands).GetProperty("builtin").GetString() == "planks" && Material(commands).GetProperty("shading").GetString() == "pbr" && !Material(commands).TryGetProperty("noTexture", out _), "a ready-made material is applied whole");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 18, MaterialText(commands))); await Frames(2);
            var file = tab.ExportForTesting(); var exported = JsonNode.Parse(file)!.AsObject();
            Require(exported["format"]!.GetValue<string>() == "lifeverse.material" && exported["material"]!["builtin"]!.GetValue<string>() == "planks", "a material can be exported");
            commands.Clear(); var hostile = "{\"format\":\"lifeverse.material\",\"version\":1,\"material\":{\"color\":\"#00AAFF\",\"roughness\":99,\"texture\":\"" + Guid.NewGuid() + "\",\"light\":{\"energy\":8},\"script\":\"os.exit()\",\"shading\":\"pbr\",\"emission\":\"red\"}}";
            Require(tab.ImportForTesting(hostile) == 1, "a material file is imported");
            var imported = Material(commands);
            Require(imported.GetProperty("color").GetString() == "#00AAFF" && imported.GetProperty("roughness").GetDouble() == 1 && !imported.TryGetProperty("texture", out _) && !imported.TryGetProperty("script", out _) && !imported.TryGetProperty("emission", out _) && !imported.TryGetProperty("light", out _), "from a file only known keys come in, inside their ranges, and never somebody else's pictures");
            commands.Clear(); Require(tab.ImportForTesting("{\"nada\":1}") == 0 && commands.Count == 0, "a file that is not a material is refused");

            // --- media --------------------------------------------------------------------------------------------------
            var media = tab.MediaForTesting!; tab.SelectModeForTesting(2); await Frames(2);
            Require(media.WebNoteForTesting.Contains("navegador embutido"), "web pages are not offered, and the panel says what is missing");
            foreach (var (type, url, word) in new[] { ("image", "http://exemplo.com/a.png", "https"), ("image", "https://localhost/a.png", "público"), ("image", "https://192.168.0.5/a.png", "público"), ("image", "https://exemplo.com/a.gif", "PNG"), ("video", "https://exemplo.com/v.mp4", "ogv"), ("image", "https://user:pw@exemplo.com/a.png", "https"), ("image", "", "https") })
            {
                commands.Clear(); media.SetSourceForTesting(type, url); media.PressApplyForTesting();
                Require(commands.Count == 0 && media.StatusForTesting.Contains(word), $"{url}: refused with a reason ({media.StatusForTesting})");
            }
            var red = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8); red.Fill(Colors.Red); var png = red.SavePngToBuffer();
            BuildObjectView.MediaSettings.FetchOverride = address => address.EndsWith("/bom.png") ? png : address.EndsWith("/falso.png") ? new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 } : address.EndsWith("/falso.ogv") ? new byte[] { 1, 2, 3, 4, 5, 6 } : null;
            commands.Clear(); media.SetSourceForTesting("image", "https://exemplo.com/bom.png"); media.SetGlowForTesting(true, 2); media.PressApplyForTesting();
            var mediaLayer = Material(commands); var stored = mediaLayer.GetProperty("media");
            Require(stored.GetProperty("type").GetString() == "image" && stored.GetProperty("url").GetString() == "https://exemplo.com/bom.png" && stored.GetProperty("glow").GetBoolean() && mediaLayer.GetProperty("emissionStrength").GetDouble() == 2, "a valid picture is stored, with its glow");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 19, MaterialText(commands))); await Frames(3);
            var view = editor.Synchronizer.Views[id]; var surface = view.MediaSurfaces.FirstOrDefault();
            Require(surface is { Texture: not null, Failed: false } && Surface(view) is ShaderMaterial shown && shown.GetShaderParameter("has_albedo").AsBool() && shown.GetShaderParameter("albedo_tex").Obj == surface.Texture && shown.GetShaderParameter("has_emissive").AsBool(), "the world draws the picture on the surface, and lets it glow");
            var twin = new BuildObjectView(); AddChild(twin); twin.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", MaterialJson = MaterialText(commands), PhysicsJson = "{\"collision\":true}" }); await Frames(2);
            Require(!ReferenceEquals(Surface(twin), Surface(view)), "a surface with media is the object's own, never shared with another");
            twin.QueueFree();
            media.Refresh(); Require(media.StatusForTesting.Contains("Imagem no ar") && media.HasPreviewForTesting, $"the panel shows the picture that is on ({media.StatusForTesting})");
            // a parcel that does not allow media from people who only build there: the panel says so before the person tries (the server refuses it too)
            media.Refresh(); Require(media.RuleNoteForTesting.Length == 0, "no parcel known: no notice");
            media.ParcelAt = (x, z) => new LandParcelData { Name = "Casa", AllowMedia = false, Mine = false, State = "owned" }; media.Refresh();
            Require(media.RuleNoteForTesting.Contains("“Casa”") && media.RuleNoteForTesting.Contains("não permite mídia"), $"a parcel with media off and somebody else's: the notice names it (got {media.RuleNoteForTesting})");
            media.ParcelAt = (x, z) => new LandParcelData { Name = "Casa", AllowMedia = false, Mine = true, State = "own" }; media.Refresh(); Require(media.RuleNoteForTesting.Length == 0, "the owner is not told");
            media.ParcelAt = (x, z) => new LandParcelData { Name = "Casa", AllowMedia = false, Mine = false, State = "rented_by_me" }; media.Refresh(); Require(media.RuleNoteForTesting.Length == 0, "nor the tenant");
            media.ParcelAt = (x, z) => new LandParcelData { Name = "Casa", AllowMedia = true, Mine = false, State = "owned" }; media.Refresh(); Require(media.RuleNoteForTesting.Length == 0, "a parcel that allows media says nothing");
            media.ParcelAt = (x, z) => null; media.Refresh(); Require(media.RuleNoteForTesting.Length == 0, "open ground says nothing");
            Require(BuildPersistenceManager.RejectionText("media_not_allowed").Contains("não permite mídia"), "and when the server does refuse, the editor explains why");
            commands.Clear(); media.SetUvForTesting(2, 2, .5, 0); Require(Material(commands).GetProperty("mapUv").GetProperty("texture").GetProperty("repeatX").GetDouble() == 2, "scale and offset of the media");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 20, MaterialText(commands))); await Frames(2);

            foreach (var (type, name) in new[] { ("image", "falso.png"), ("video", "falso.ogv"), ("image", "ausente.png") })
            {
                var bad = new BuildObjectView(); AddChild(bad);
                bad.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", MaterialJson = "{\"media\":{\"type\":\"" + type + "\",\"url\":\"https://exemplo.com/" + name + "\",\"playing\":true}}", PhysicsJson = "{\"collision\":true}" }); await Frames(2);
                Require(bad.MediaSurfaces.FirstOrDefault() is { Failed: true, Texture: null }, $"{name}: something that is not a picture or a video is refused, not shown");
                bad.QueueFree();
            }
            BuildObjectView.MediaSettings.Enabled = false;
            var off = new BuildObjectView(); AddChild(off); off.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", MaterialJson = MaterialText(commands), PhysicsJson = "{\"collision\":true}" }); await Frames(2);
            var plainShown = off.MediaSurfaces.FirstOrDefault() is { Texture: null, Failed: false }; BuildObjectView.MediaSettings.Enabled = true;
            Require(plainShown, "with media off nothing is fetched or shown"); off.QueueFree();
            commands.Clear(); media.PressPlayForTesting(); Require(Material(commands).GetProperty("media").GetProperty("playing").GetBoolean(), "play toggles the stored state");
            commands.Clear(); media.PressRemoveForTesting();
            Require(!Material(commands).TryGetProperty("media", out _) && !Material(commands).TryGetProperty("emission", out _), "removing the media takes its glow with it");
            BuildObjectView.MediaSettings.FetchOverride = null;
            GD.Print("event=build_texture_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_texture_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static Material? Surface(BuildObjectView v) => v.GetNode<MeshInstance3D>("PrimitiveMesh").GetSurfaceOverrideMaterial(0);

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
