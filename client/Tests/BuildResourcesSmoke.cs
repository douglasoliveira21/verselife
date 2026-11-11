using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 6: the RECURSOS tab changes the real object (light, reflection, physics shape and numbers, mesh animation) and the world shows it within its budgets.</summary>
public partial class BuildResourcesSmoke : Node3D
{
    private static string StateJson(Guid id, string type, int revision, string material = "{}", string physics = "{\"collision\":true}", float x = 0, string operation = "update") =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Peça " + type, description = "", primitiveType = type, positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = material, geometryJson = "{}", physicsJson = physics, permissionsJson = "{}", contentJson = "[]", revision, cost = 2 } });

    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();
    private static JsonElement Material(List<string> commands) => JsonDocument.Parse(Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!).RootElement.Clone();
    private static string PhysicsOf(List<string> commands) => Last(commands).GetProperty("object").GetProperty("physicsJson").GetString()!;

    private static Node3D RiggedModel()
    {
        var model = new Node3D { Name = "Model" }; var skeleton = new Skeleton3D(); skeleton.AddBone("raiz"); skeleton.AddBone("braco"); skeleton.SetBoneParent(1, 0); model.AddChild(skeleton);
        var library = new AnimationLibrary();
        foreach (var (name, length) in new[] { ("Andar", 1.0f), ("Pular", 2.0f) }) { var clip = new Animation { Length = length }; var track = clip.AddTrack(Animation.TrackType.Value); clip.TrackSetPath(track, ".:position"); clip.TrackInsertKey(track, 0, Vector3.Zero); clip.TrackInsertKey(track, length, Vector3.Up); library.AddAnimation(name, clip); }
        var player = new AnimationPlayer { Name = "AnimationPlayer" }; player.AddAnimationLibrary("", library); model.AddChild(player); return model;
    }

    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var tab = editor.Window.Resources; var id = Guid.NewGuid();
            Require(editor.Window.TabNamesForTesting.SequenceEqual(["GERAL", "OBJETO", "RECURSOS", "TEXTURA", "CONTEÚDO"]), "the tabs that work: GERAL, OBJETO and RECURSOS");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 1, operation: "create")); editor.Selection.Set([id]); editor.Window.SelectTabForTesting(2); await Frames(2);

            // --- light --------------------------------------------------------------------------------------------------
            Require(!tab.LightOnForTesting && !tab.LightRowsVisibleForTesting, "no light by default");
            commands.Clear(); tab.ToggleLightForTesting(true);
            Require(Material(commands).GetProperty("light").GetProperty("energy").GetDouble() > 0, "Emitir luz sends a light");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 2, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2); // the server confirms the light
            tab.SetLightForTesting(3, 12, 2, new Color("#12F2E0")); tab.ToggleShadowForTesting(true); tab.FlushForTesting();
            var light = Material(commands).GetProperty("light");
            Require(light.GetProperty("energy").GetDouble() == 3 && light.GetProperty("range").GetDouble() == 12 && light.GetProperty("falloff").GetDouble() == 2 && light.GetProperty("color").GetString() == "#12f2e0" && light.GetProperty("shadow").GetBoolean(), "intensity, range, falloff, colour and shadows are sent");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 3, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2);
            Require(tab.LightOnForTesting && tab.ShadowForTesting && tab.LightRowsVisibleForTesting, "the stored light comes back to the controls");
            var lamp = editor.Synchronizer.Views[id].GetNodeOrNull<OmniLight3D>("ObjectLight");
            Require(lamp is { OmniRange: 12f, LightEnergy: 3f } && lamp.ShadowEnabled, "the world really lights the scene, with a shadow");

            // the world keeps its budget: only a few lights cast shadows
            var many = new List<BuildObjectView>();
            for (var i = 0; i < 6; i++)
            {
                var v = new BuildObjectView(); AddChild(v); many.Add(v);
                v.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", MaterialJson = "{\"light\":{\"energy\":1,\"range\":4,\"shadow\":true}}", PhysicsJson = "{\"collision\":true}" });
            }
            await Frames(2);
            var casting = many.Concat([editor.Synchronizer.Views[id]]).Count(v => v.GetNodeOrNull<OmniLight3D>("ObjectLight") is { ShadowEnabled: true });
            Require(casting == BuildObjectView.ObjectLight.MaxShadows, $"no more than {BuildObjectView.ObjectLight.MaxShadows} lights cast shadows at once (got {casting})");
            foreach (var v in many) v.QueueFree();

            commands.Clear(); tab.ToggleLightForTesting(false);
            Require(!Material(commands).TryGetProperty("light", out _) && !Material(commands).TryGetProperty("projector", out _), "turning the light off takes it (and its projector) away");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 3)); await Frames(2);
            Require(editor.Synchronizer.Views[id].GetNodeOrNull("ObjectLight") is null, "and the world stops lighting");

            commands.Clear(); tab.ToggleReflectionForTesting(true); editor.Synchronizer.OnOperation(StateJson(id, "cube", 4, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2); // confirmed by the server
            commands.Clear(); tab.SetReflectionForTesting(.8, 12); tab.FlushForTesting();
            commands.Clear(); tab.ToggleReflectionForTesting(true); tab.SetReflectionForTesting(.8, 12); tab.FlushForTesting();
            var reflection = Material(commands).GetProperty("reflection");
            Require(Math.Abs(reflection.GetProperty("intensity").GetDouble() - .8) < .001 && reflection.GetProperty("range").GetDouble() == 12, "Reflexo sends intensity and range");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 4, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2);
            var probe = editor.Synchronizer.Views[id].GetNodeOrNull<ReflectionProbe>("ObjectReflection");
            Require(probe is { Intensity: > .79f and < .81f } && probe.Size.X == 12f && tab.ReflectionOnForTesting, "the world has a real reflection probe");
            var probes = new List<BuildObjectView>();
            for (var i = 0; i < 6; i++) { var v = new BuildObjectView(); AddChild(v); probes.Add(v); v.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", MaterialJson = "{\"reflection\":{\"intensity\":0.5,\"range\":6}}", PhysicsJson = "{\"collision\":true}" }); }
            await Frames(2);
            var working = probes.Concat([editor.Synchronizer.Views[id]]).Count(v => v.GetNodeOrNull<ReflectionProbe>("ObjectReflection") is { Visible: true });
            Require(working == BuildObjectView.ObjectReflection.MaxProbes, $"no more than a few probes work at once (got {working}, active {BuildObjectView.ObjectReflection.ActiveForTesting})");
            foreach (var v in probes) v.QueueFree();
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 6));

            // --- physics shape and numbers ------------------------------------------------------------------------------
            await Frames(2);
            Require(tab.ShapeChoicesForTesting.SequenceEqual(["none", "box", "convex"]) && tab.ShapeForTesting == "box", "a box offers none, prim and convex hull (the exact mesh is for models)");
            commands.Clear(); tab.ChooseShapeForTesting("convex");
            Require(PhysicsRules.Read(PhysicsOf(commands)).CollisionShape == "convex", "Casco convexo is sent");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 7, physics: PhysicsOf(commands)));
            Require(!tab.NumbersEditableForTesting && tab.PhysicsNoteForTesting.Contains("Física ligada"), "mass, friction and bounce wait for the object to be physical");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 8, physics: "{\"collision\":true,\"shape\":\"convex\",\"mode\":\"physical\",\"mass\":1,\"friction\":0.5,\"bounce\":0}")); await Frames(2);
            Require(tab.NumbersEditableForTesting, "a physical object has its numbers");
            commands.Clear(); tab.SetNumbersForTesting(25, .9, .6); tab.FlushForTesting();
            var numbers = PhysicsRules.Read(PhysicsOf(commands)); Require(numbers.Mass == 25 && numbers.Friction == .9f && Math.Abs(numbers.Bounce - .6f) < .001f && numbers.Physical, "mass, friction and bounce are sent without losing the physical mode");
            commands.Clear(); tab.ChooseShapeForTesting("none");
            Require(commands.Count == 0 && editor.Window.StatusForTesting.Contains("físico"), "a physical object cannot be given no shape: the combination is blocked and explained");

            // --- animation of a rigged mesh -----------------------------------------------------------------------------
            var cubeNote = tab.AnimationNoteForTesting; Require(!tab.AnimationEnabledForTesting && cubeNote.Contains("importados"), "a primitive cannot be animated, and the tab says why");
            var meshId = Guid.NewGuid(); editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 1, x: 4, operation: "create")); editor.Selection.Set([meshId]); await Frames(2);
            Require(!tab.AnimationEnabledForTesting && tab.AnimationNoteForTesting.Contains("carregando"), $"while the model loads there is nothing to animate yet ({tab.AnimationNoteForTesting})");
            editor.Synchronizer.Views[meshId].AttachModelForTesting(new Node3D { Name = "Plain" }); await Frames(2); editor.Window.RefreshPages();
            Require(!tab.AnimationEnabledForTesting && tab.AnimationNoteForTesting.Contains("não tem esqueleto"), $"a model with no skeleton cannot be animated ({tab.AnimationNoteForTesting})");
            Require(tab.ShapeChoicesForTesting.Contains("triangle"), "a model can use the exact triangles");
            var view = editor.Synchronizer.Views[meshId]; var model = RiggedModel(); view.AttachModelForTesting(model); await Frames(2); editor.Window.RefreshPages();
            Require(view.SkeletonBones == 2 && view.AnimationClips.SequenceEqual(["Andar", "Pular"]) && tab.AnimationEnabledForTesting && tab.AnimationNoteForTesting.Contains("2 ossos") && tab.ClipsForTesting.SequenceEqual(["Andar", "Pular"]), $"the skeleton and the clips of the model are found ({tab.AnimationNoteForTesting})");
            commands.Clear(); tab.ToggleAnimationForTesting(true);
            var animation = Material(commands).GetProperty("animation");
            Require(animation.GetProperty("clip").GetString() == "Andar" && !animation.GetProperty("playing").GetBoolean(), "turning it on chooses the first clip, paused");
            editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 2, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!, x: 4)); await Frames(2);
            view = editor.Synchronizer.Views[meshId]; model = RiggedModel(); view.AttachModelForTesting(model); await Frames(2); editor.Window.RefreshPages();
            commands.Clear(); tab.PressPlayForTesting();
            Require(Material(commands).GetProperty("animation").GetProperty("playing").GetBoolean(), "Reproduzir sends the clip as playing");
            var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 500; // the server stamps when it started
            var playing = $"{{\"animation\":{{\"clip\":\"Andar\",\"playing\":true,\"loop\":false,\"speed\":1,\"offset\":0,\"since\":{since}}}}}";
            editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 3, material: playing, x: 4)); await Frames(2);
            view = editor.Synchronizer.Views[meshId]; model = RiggedModel(); view.AttachModelForTesting(model); var player = model.GetNode<AnimationPlayer>("AnimationPlayer"); await Frames(2);
            Require(player.IsPlaying() && player.CurrentAnimation == "Andar" && Math.Abs(player.CurrentAnimationPosition - .5) < .35, $"everybody sees the clip at the same moment ({player.CurrentAnimationPosition})");
            var paused = $"{{\"animation\":{{\"clip\":\"Andar\",\"playing\":false,\"loop\":false,\"speed\":1,\"offset\":0.4,\"since\":{since}}}}}";
            editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 4, material: paused, x: 4)); await Frames(2);
            view = editor.Synchronizer.Views[meshId]; model = RiggedModel(); view.AttachModelForTesting(model); player = model.GetNode<AnimationPlayer>("AnimationPlayer"); await Frames(2);
            Require(!player.IsPlaying() && Math.Abs(player.CurrentAnimationPosition - .4) < .05, $"a paused clip stays where it was stopped ({player.CurrentAnimationPosition})");
            var looped = $"{{\"animation\":{{\"clip\":\"Andar\",\"playing\":true,\"loop\":true,\"speed\":2,\"offset\":0,\"since\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2250}}}}}"; // 4.5 s of clip at double speed: half way through the fifth turn
            editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 5, material: looped, x: 4)); await Frames(2);
            view = editor.Synchronizer.Views[meshId]; model = RiggedModel(); view.AttachModelForTesting(model); player = model.GetNode<AnimationPlayer>("AnimationPlayer"); await Frames(2);
            Require(player.IsPlaying() && player.GetAnimation("Andar").LoopMode == Animation.LoopModeEnum.Linear && Math.Abs(player.SpeedScale - 2f) < .01f && player.CurrentAnimationPosition < 1.0, $"a loop wraps around and the speed is used ({player.CurrentAnimationPosition})");
            var unknown = "{\"animation\":{\"clip\":\"Voar\",\"playing\":true,\"loop\":false,\"speed\":1,\"since\":1}}";
            editor.Synchronizer.OnOperation(StateJson(meshId, "mesh", 6, material: unknown, x: 4)); await Frames(2);
            view = editor.Synchronizer.Views[meshId]; model = RiggedModel(); view.AttachModelForTesting(model); player = model.GetNode<AnimationPlayer>("AnimationPlayer"); await Frames(2);
            Require(!player.IsPlaying(), "a clip the model does not have plays nothing");
            commands.Clear(); tab.ToggleAnimationForTesting(false);
            Require(!Material(commands).TryGetProperty("animation", out _), "turning it off removes the animation");

            // --- several objects ----------------------------------------------------------------------------------------
            // --- projector and Flexi ------------------------------------------------------------------------------------
            var pid = Guid.NewGuid(); editor.Synchronizer.OnOperation(StateJson(pid, "cube", 1, x: 6, operation: "create")); editor.Selection.Set([pid]); await Frames(2);
            commands.Clear(); tab.ToggleProjectorForTesting(true); tab.ChooseProjectorPatternForTesting(BuiltInTextures.All[0].Id); tab.FlushForTesting();
            Require(commands.Count == 0, "a projector without a light sends nothing (the server needs the light)");
            commands.Clear(); tab.ToggleLightForTesting(true);
            editor.Synchronizer.OnOperation(StateJson(pid, "cube", 2, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2);
            commands.Clear(); tab.ToggleProjectorForTesting(true); tab.ChooseProjectorPatternForTesting(BuiltInTextures.All[0].Id);
            editor.Synchronizer.OnOperation(StateJson(pid, "cube", 3, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2); tab.SetProjectorForTesting(60, 12, .3); tab.FlushForTesting();
            var projected = Material(commands);
            Require(projected.GetProperty("projectorBuiltin").GetString() == BuiltInTextures.All[0].Id && projected.GetProperty("projector").GetProperty("fov").GetDouble() == 60 && projected.GetProperty("projector").GetProperty("focus").GetDouble() == 12, "the projector sends its picture, cone and focus");
            editor.Synchronizer.OnOperation(StateJson(pid, "cube", 4, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2);
            Require(tab.ProjectorOnForTesting && tab.ProjectorRowsVisibleForTesting, "the stored projector comes back to the controls");
            commands.Clear(); tab.ToggleFlexForTesting(true); editor.Synchronizer.OnOperation(StateJson(pid, "cube", 5, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2); tab.SetFlexForTesting("wind", 4); tab.SetFlexForTesting("softness", 2); tab.FlushForTesting();
            var flexJson = Material(commands).GetProperty("flex");
            Require(flexJson.GetProperty("wind").GetDouble() == 4 && flexJson.GetProperty("softness").GetDouble() == 2, "Flexi sends its settings");
            editor.Synchronizer.OnOperation(StateJson(pid, "cube", 6, material: Last(commands).GetProperty("object").GetProperty("materialJson").GetString()!)); await Frames(2);
            Require(tab.FlexOnForTesting, "the stored Flexi comes back to the controls");
            commands.Clear(); tab.ToggleFlexForTesting(false); tab.FlushForTesting();
            Require(!Material(commands).TryGetProperty("flex", out _) && Material(commands).TryGetProperty("projector", out _), "turning Flexi off removes only it");
            commands.Clear(); tab.ToggleLightForTesting(false);
            Require(!Material(commands).TryGetProperty("projector", out _) && !Material(commands).TryGetProperty("projectorBuiltin", out _), "the light off takes the projector with it");

            var other = Guid.NewGuid(); editor.Synchronizer.OnOperation(StateJson(other, "sphere", 1, x: -4, operation: "create")); editor.Selection.Set([id, other]); await Frames(2);
            commands.Clear(); tab.ToggleLightForTesting(true);
            Require(commands.Count == 2, "a light goes to every selected object");
            GD.Print("event=build_resources_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_resources_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
