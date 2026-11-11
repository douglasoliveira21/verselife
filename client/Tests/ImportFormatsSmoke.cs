using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Everything people can import must load in the game: pictures (PNG, JPEG, lossy and lossless WebP, odd sizes), sounds (WAV in every kind the server accepts, plus any real OGG / MP3 / WAV
/// files named in LIFEVERSE_IMPORT_SAMPLES), and models (static, rigged, rigged with animation, animation only) exported by Godot itself and read back the way the world reads them.
/// </summary>
public partial class ImportFormatsSmoke : Node3D
{
    private readonly List<string> _report = [];

    public override async void _Ready()
    {
        try
        {
            Pictures(); Sounds(); await Models(); RealFiles();
            foreach (var line in _report) GD.Print("event=import_formats " + line);
            GD.Print("event=import_formats_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=import_formats_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static Image Picture(int width, int height, bool alpha)
    {
        var image = Image.CreateEmpty(width, height, false, alpha ? Image.Format.Rgba8 : Image.Format.Rgb8);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) image.SetPixel(x, y, new Color(x / (float)width, y / (float)height, .5f, alpha ? (x % 7 == 0 ? .4f : 1f) : 1f));
        return image;
    }

    // ---- pictures ----------------------------------------------------------------------------------------------------------

    private void Pictures()
    {
        foreach (var (width, height) in new[] { (64, 32), (1, 1), (100, 37), (257, 255), (1024, 1024) })
            foreach (var alpha in new[] { false, true })
            {
                var image = Picture(width, height, alpha);
                var files = new List<(string Name, byte[] Bytes)> { ("png", image.SavePngToBuffer()), ("webp-lossless", image.SaveWebpToBuffer(false, .75f)), ("webp-lossy", image.SaveWebpToBuffer(true, .8f)) };
                if (!alpha) files.Add(("jpg", image.SaveJpgToBuffer(.85f)));
                foreach (var (name, bytes) in files)
                {
                    var texture = TextureCache.Decode(bytes);
                    Require(texture is not null, $"{name} {width}x{height} alpha={alpha} must load");
                    Require(texture!.GetWidth() == width && texture.GetHeight() == height, $"{name} {width}x{height} came back as {texture.GetWidth()}x{texture.GetHeight()}");
                }
            }
        _report.Add("pictures=png,jpg,webp-lossy,webp-lossless sizes=1x1,64x32,100x37,257x255,1024x1024 alpha=yes");
        var big = Picture(5000, 40, false).SavePngToBuffer(); var shrunk = TextureCache.Decode(big);
        Require(shrunk is not null && shrunk.GetWidth() <= TextureCache.MaxSize, "a picture wider than the limit is brought down to it");
        Require(TextureCache.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]) is null && TextureCache.Decode(new byte[3]) is null, "garbage is not a picture");
        var png = Picture(32, 32, false).SavePngToBuffer(); Require(TextureCache.Decode(png[..(png.Length / 2)]) is null, "a PNG that is cut in half is refused, not shown as noise");
        foreach (var path in new[] { "res://Assets/ui/construir/construir_7_importar_modelo.webp", "res://Assets/ui/construir/construir_9_textura.webp" })
        {
            var bytes = Godot.FileAccess.GetFileAsBytes(path); var texture = TextureCache.Decode(bytes);
            Require(texture is not null && texture.GetWidth() > 100, "the real WebP file " + path + " must load"); _report.Add($"real-webp={System.IO.Path.GetFileName(path)} {texture!.GetWidth()}x{texture.GetHeight()}");
        }
    }

    // ---- sounds ------------------------------------------------------------------------------------------------------------

    private static byte[] Wav(int format, int channels, int bits, int rate, int frames)
    {
        var bytesPerFrame = channels * bits / 8; var data = new byte[frames * bytesPerFrame];
        for (var i = 0; i < frames; i++) for (var c = 0; c < channels; c++)
        {
            var sample = Math.Sin(i * 2 * Math.PI * 440 / rate) * .5; var at = i * bytesPerFrame + c * bits / 8;
            switch (bits)
            {
                case 8: data[at] = (byte)(128 + sample * 100); break;
                case 16: BitConverter.TryWriteBytes(data.AsSpan(at), (short)(sample * short.MaxValue)); break;
                case 24: { var v = (int)(sample * 8388607); data[at] = (byte)v; data[at + 1] = (byte)(v >> 8); data[at + 2] = (byte)(v >> 16); break; }
                case 32 when format == 3: BitConverter.TryWriteBytes(data.AsSpan(at), (float)sample); break;
                default: BitConverter.TryWriteBytes(data.AsSpan(at), (int)(sample * int.MaxValue)); break;
            }
        }
        var extensible = format == 0xFFFE; var fmtSize = extensible ? 40 : 16;
        using var output = new System.IO.MemoryStream(); using var w = new System.IO.BinaryWriter(output);
        w.Write("RIFF"u8.ToArray()); w.Write(4 + 8 + fmtSize + 8 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(fmtSize); w.Write((short)format); w.Write((short)channels); w.Write(rate); w.Write(rate * bytesPerFrame); w.Write((short)bytesPerFrame); w.Write((short)bits);
        if (extensible) { w.Write((short)22); w.Write((short)bits); w.Write(3); w.Write((short)1); w.Write(new byte[] { 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 }); }
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        return output.ToArray();
    }

    private void Sounds()
    {
        var kinds = new (int Format, int Channels, int Bits)[] { (1, 1, 8), (1, 1, 16), (1, 2, 16), (1, 1, 24), (1, 2, 24), (1, 2, 32), (3, 1, 32), (3, 2, 32), (0xFFFE, 2, 24) };
        foreach (var (format, channels, bits) in kinds)
            foreach (var rate in new[] { 8000, 22050, 44100, 48000 })
            {
                const int frames = 4410; var stream = ObjectEffects.Decode(Wav(format, channels, bits, rate, frames)) as AudioStreamWav;
                Require(stream is not null, $"WAV format {format} {channels}ch {bits}bit {rate}Hz must play");
                Require(stream!.MixRate == rate && stream.Stereo == (channels == 2), $"WAV {format}/{channels}/{bits}/{rate} keeps its rate and channels");
                Require(Math.Abs(stream.GetLength() - frames / (double)rate) < .01, $"WAV {format}/{channels}/{bits}/{rate} keeps its length ({stream.GetLength():0.000}s)");
                Require(stream.Format is AudioStreamWav.FormatEnum.Format16Bits or AudioStreamWav.FormatEnum.Format8Bits, "and plays as 8 or 16 bits");
            }
        _report.Add("wav=8,16,24,32-bit pcm + 32-bit float + extensible, mono/stereo, 8000-48000Hz");
        Require(ObjectEffects.Decode(Wav(2, 1, 16, 22050, 100)) is null, "ADPCM WAV is refused, not played as noise");
        Require(ObjectEffects.Decode(Wav(1, 6, 16, 22050, 100)) is null, "surround WAV is refused");
        Require(ObjectEffects.Decode(new byte[100]) is null && ObjectEffects.Decode([1, 2, 3]) is null, "garbage is not a sound");
        // a loud 24 bit sound must not come out silent or distorted after the conversion to 16 bits
        var loud = (AudioStreamWav)ObjectEffects.Decode(Wav(1, 1, 24, 22050, 2205))!; var peak = 0; for (var i = 0; i + 1 < loud.Data.Length; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(loud.Data, i)));
        Require(peak > 12000 && peak < 20000, $"the 24 bit sound keeps its volume (peak {peak} of 32767; half scale is expected)");
        var floatPeak = 0; var floats = (AudioStreamWav)ObjectEffects.Decode(Wav(3, 1, 32, 22050, 2205))!; for (var i = 0; i + 1 < floats.Data.Length; i += 2) floatPeak = Math.Max(floatPeak, Math.Abs(BitConverter.ToInt16(floats.Data, i)));
        Require(floatPeak > 12000 && floatPeak < 20000, $"the float sound keeps its volume (peak {floatPeak})");
    }

    // ---- models ------------------------------------------------------------------------------------------------------------

    private static ArrayMesh Triangle(bool skinned, int bones)
    {
        var mesh = new ArrayMesh(); var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 2, 0), new(1, 2, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 2, 1, 3 };
        if (skinned)
        {
            var joints = new int[16]; var weights = new float[16];
            for (var v = 0; v < 4; v++) { joints[v * 4] = v < 2 ? 0 : Math.Min(1, bones - 1); weights[v * 4] = 1; }
            arrays[(int)Mesh.ArrayType.Bones] = joints; arrays[(int)Mesh.ArrayType.Weights] = weights;
        }
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); return mesh;
    }

    private static Node3D Scene(bool mesh, bool rigged, bool animated)
    {
        var root = new Node3D { Name = "Root" };
        Node3D parent = root; Skeleton3D? skeleton = null;
        if (rigged || animated)
        {
            skeleton = new Skeleton3D { Name = "Skeleton" }; root.AddChild(skeleton); parent = skeleton;
            skeleton.AddBone("Osso0"); skeleton.AddBone("Osso1"); skeleton.SetBoneParent(1, 0);
            skeleton.SetBoneRest(0, Transform3D.Identity); skeleton.SetBoneRest(1, new Transform3D(Basis.Identity, new Vector3(0, 1, 0)));
            skeleton.ResetBonePoses();
        }
        if (mesh)
        {
            var instance = new MeshInstance3D { Name = "Corpo", Mesh = Triangle(rigged, 2) };
            parent.AddChild(instance);
            if (rigged && skeleton is not null)
            {
                var skin = new Skin(); for (var bone = 0; bone < skeleton.GetBoneCount(); bone++) { skin.AddBind(bone, skeleton.GetBoneGlobalRest(bone).AffineInverse()); skin.SetBindName(bone, skeleton.GetBoneName(bone)); }
                instance.Skin = skin; instance.Skeleton = new NodePath("..");
            }
        }
        if (animated)
        {
            var clip = new Animation { Length = 1f, LoopMode = Animation.LoopModeEnum.Linear };
            var track = clip.AddTrack(Animation.TrackType.Rotation3D); clip.TrackSetPath(track, "Skeleton:Osso0");
            clip.RotationTrackInsertKey(track, 0, Quaternion.Identity); clip.RotationTrackInsertKey(track, .5, new Quaternion(Vector3.Up, Mathf.Pi / 2)); clip.RotationTrackInsertKey(track, 1, Quaternion.Identity);
            var library = new AnimationLibrary(); library.AddAnimation("Girar", clip);
            var player = new AnimationPlayer { Name = "AnimationPlayer" }; root.AddChild(player); player.AddAnimationLibrary("", library);
        }
        SetOwners(root, root); return root;
    }

    private static void SetOwners(Node node, Node owner) { foreach (var child in node.GetChildren()) { child.Owner = owner; SetOwners(child, owner); } }

    private static byte[] Export(Node3D scene)
    {
        var document = new GltfDocument(); var state = new GltfState();
        Require(document.AppendFromScene(scene, state) == Error.Ok, "Godot exports the scene"); return document.GenerateBuffer(state);
    }

    private async Task Models()
    {
        var write = !string.IsNullOrEmpty(OS.GetEnvironment("LIFEVERSE_WRITE_FIXTURES")); var folder = ProjectSettings.GlobalizePath("res://") + "../tests/fixtures/";
        if (write) System.IO.Directory.CreateDirectory(folder);
        var cases = new (string Name, bool Mesh, bool Rigged, bool Animated)[] { ("static", true, false, false), ("rigged", true, true, false), ("rigged-animated", true, true, true), ("static-animated", true, false, true), ("animation-only", false, true, true) };
        foreach (var (name, mesh, rigged, animated) in cases)
        {
            var scene = Scene(mesh, rigged, animated); var bytes = Export(scene); scene.Free();
            Require(bytes.Length > 200 && bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F', name + ": a real GLB");
            if (write) System.IO.File.WriteAllBytes(folder + name + ".glb", bytes);

            // read back the way the world reads a model
            var document = new GltfDocument(); var state = new GltfState();
            Require(document.AppendFromBuffer(bytes, string.Empty, state) == Error.Ok, name + ": Godot reads it back");
            var read = (Node3D)document.GenerateScene(state)!;
            var skeletons = read.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().ToList(); var players = read.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().ToList();
            if (mesh) Require(rigged == skeletons.Count > 0, $"{name}: skeleton present = {rigged}"); // without a mesh Godot keeps the bones as plain nodes (a skeleton exists only for a skin)
            if (mesh && rigged) Require(skeletons[0].GetBoneCount() >= 2, $"{name}: the bones survive ({skeletons[0].GetBoneCount()})");
            if (animated) Require(players.Count == 1 && players[0].GetAnimationList().Contains("Girar") && players[0].GetAnimation("Girar").Length > .9f, $"{name}: the animation survives");
            else Require(players.Count == 0, $"{name}: no animation");
            if (mesh && rigged) Require(read.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Any(m => m.Skin is not null || m.Skeleton != null), $"{name}: the mesh stays bound to the skeleton");
            read.Free();

            var (node, size) = ModelCache.Parse(bytes);
            if (!mesh) { Require(node is null, $"{name}: a model with no mesh is not placed in the world"); _report.Add($"model={name} bytes={bytes.Length} placed=no (animation only)"); continue; }
            Require(node is not null, $"{name}: the world can place it");
            Require(Mathf.Abs(Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) - 1f) < .02f, $"{name}: normalised to one metre ({size})");

            // inside an object: the animation clips and the bones are what the Recursos tab offers
            var view = new BuildObjectView(); AddChild(view);
            view.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "mesh", Name = name, PhysicsJson = "{\"collision\":true}", MaterialJson = "{}", GeometryJson = "{}" });
            view.AttachModelForTesting(node!); await Frames(2);
            Require(view.ModelLoaded, name + ": loaded in the object");
            Require(rigged == (view.SkeletonBones >= 2), $"{name}: bones shown = {rigged}");
            Require(animated == view.AnimationClips.Contains("Girar"), $"{name}: clips offered = {animated} ({string.Join(",", view.AnimationClips)})");
            if (animated)
            {
                var driver = view.AnimationDriver!; var now = Time.GetUnixTimeFromSystem();
                driver.Apply(new System.Text.Json.Nodes.JsonObject { ["clip"] = "Girar", ["playing"] = true, ["loop"] = true, ["speed"] = 1.0, ["offset"] = 0.0, ["since"] = now * 1000 }, now);
                Require(driver.Playing && driver.Clip == "Girar", name + ": the animation plays");
                await Frames(10); Require(driver.Position > 0, $"{name}: and moves forward in time ({driver.Position:0.000}s)");
                if (rigged)
                {
                    var skeleton = view.AnimationDriver!.Skeleton!; var before = skeleton.GetBonePose(0).Basis; driver.Apply(new System.Text.Json.Nodes.JsonObject { ["clip"] = "Girar", ["playing"] = false, ["offset"] = 0.25 }, now);
                    await Frames(3); var after = skeleton.GetBonePose(0).Basis; Require(!before.IsEqualApprox(after) || skeleton.GetBonePose(0).Basis.GetRotationQuaternion().AngleTo(Quaternion.Identity) > .1f, $"{name}: the bones really turn");
                }
            }
            view.QueueFree();
            _report.Add($"model={name} bytes={bytes.Length} bones={(rigged ? 2 : 0)} clips={(animated ? 1 : 0)} placed=yes");
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }

    // ---- real files, when the machine has some ----------------------------------------------------------------------------

    private void RealFiles()
    {
        var list = OS.GetEnvironment("LIFEVERSE_IMPORT_SAMPLES"); if (string.IsNullOrWhiteSpace(list)) return;
        foreach (var path in list.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Godot.FileAccess.GetFileAsBytes(path); Require(bytes.Length > 0, "cannot read " + path);
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".ogg": case ".wav": case ".mp3":
                {
                    var stream = ObjectEffects.Decode(bytes); Require(stream is not null, "real sound must load: " + path);
                    Require(stream!.GetLength() > .05, $"real sound has a length: {path}"); _report.Add($"real-sound={System.IO.Path.GetFileName(path)} type={stream.GetClass()} seconds={stream.GetLength():0.00}"); break;
                }
                case ".png": case ".jpg": case ".jpeg": case ".webp":
                { var texture = TextureCache.Decode(bytes); Require(texture is not null, "real picture must load: " + path); _report.Add($"real-picture={System.IO.Path.GetFileName(path)} {texture!.GetWidth()}x{texture.GetHeight()}"); break; }
                case ".gltf":
                {
                    Require(GltfUploadPackager.TryPack(path, bytes, out var packed, out var packError), "the glTF must be packed into one GLB: " + path + " " + packError);
                    var (node, size) = ModelCache.Parse(packed); Require(node is not null, "real packed glTF must load: " + path);
                    var players = node!.FindChildren("*", "AnimationPlayer", true, false).Count; var skeletons = node.FindChildren("*", "Skeleton3D", true, false).Count;
                    _report.Add($"real-gltf={System.IO.Path.GetFileName(path)} packedKB={packed.Length / 1024} animations={players} skeletons={skeletons}");
                    if (OS.GetEnvironment("LIFEVERSE_PACKED_OUT") is { Length: > 0 } outDir) System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + "_packed.glb"), packed);
                    break;
                }
                case ".glb":
                { var (node, size) = ModelCache.Parse(bytes); Require(node is not null, "real model must load: " + path); _report.Add($"real-model={System.IO.Path.GetFileName(path)} size={size} animations={node!.FindChildren("*", "AnimationPlayer", true, false).Count} skeletons={node.FindChildren("*", "Skeleton3D", true, false).Count}"); break; }
            }
        }
    }
}
