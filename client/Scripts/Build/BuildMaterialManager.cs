using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The surface of objects: which material model they use (PBR or Blinn-Phong), the colour, the texture of each map, the numbers (roughness, metallic, glossiness, strengths,
/// glow, transparency) and the scale, offset and rotation of each map. A change goes to the faces chosen with the Face tool, or to the whole object (which also takes the same setting
/// off the faces, so the whole object really changes). The result is the material JSON stored with the object: the server checks it and every player draws it with the real renderer.
/// </summary>
public partial class BuildMaterialManager : Node
{
    /// <summary>The maps a material model has, in the order the tab shows them: the key stored, and the name each model gives it.</summary>
    public static readonly (string Key, string Pbr, string Blinn)[] Channels = [("texture", "BASE COLOR", "DIFFUSE"), ("normalMap", "NORMAL", "NORMAL"), ("specularMap", "", "SPECULAR"), ("ormMap", "(O)RM", ""), ("emissiveMap", "EMISSIVE", "")];

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private BuildFaceSelectionManager _faces = null!;
    private BuildUndoRedoManager _history = null!;

    public event Action<string>? Said;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection, BuildFaceSelectionManager faces, BuildUndoRedoManager history) { _sync = sync; _selection = selection; _faces = faces; _history = history; }

    // ---- reading -------------------------------------------------------------------------------------------------------------

    private static JsonObject Parse(string json) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; } catch (JsonException) { return []; } }

    /// <summary>The faces being edited (empty means the whole object).</summary>
    public IReadOnlyCollection<int> TargetFaces => _selection.Count == 1 && _faces.Count > 0 && _faces.ObjectId == _selection.Ids[0] ? _faces.Faces : [];

    public string TargetName
    {
        get
        {
            if (_selection.Count == 0) return "Nada selecionado";
            var faces = TargetFaces; if (faces.Count == 0) return _selection.Count == 1 ? "Objeto inteiro" : $"{_selection.Count} objetos inteiros";
            return _faces.Describe().Replace("Face escolhida: ", "Face: ").Replace(" faces escolhidas", " faces");
        }
    }

    private static JsonObject? FaceEntry(JsonObject material, int index) => (material["faces"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(f => f["index"] is JsonValue v && v.TryGetValue<int>(out var i) && i == index);

    /// <summary>The value a key has for what is being edited: the first chosen face if it sets it, otherwise the object.</summary>
    public JsonNode? Value(string key)
    {
        if (_selection.Views().LastOrDefault()?.State is not { } state) return null;
        var material = Parse(state.MaterialJson); var faces = TargetFaces;
        if (faces.Count > 0 && FaceEntry(material, faces.First()) is { } entry && entry[key] is { } own) return own.DeepClone();
        return material[key]?.DeepClone();
    }

    public string? Text(string key) => Value(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public double Number(string key, double fallback) => Value(key) is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? n : fallback;
    public bool Flag(string key) => Value(key) is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public bool IsBlinn => Text("shading") == "blinnphong";
    public bool Blank => Flag("noTexture");

    /// <summary>The asset (or built-in pattern) on a map, or null.</summary>
    public (string? Asset, string? Builtin) MapOf(string key)
    {
        if (Blank) return (null, null);
        if (key == "texture" && Text("builtin") is { } builtin && Text("texture") is null) return (null, builtin);
        return (Text(key), null);
    }

    /// <summary>The scale, offset and rotation of a map (or the shared ones for "all").</summary>
    public (double RepeatX, double RepeatY, double OffsetX, double OffsetY, double Rotation) Uv(string channel)
    {
        double Shared(string key, double fallback) => Number(key, fallback);
        if (channel != "all" && Value("mapUv") is JsonObject maps && maps[channel] is JsonObject entry)
        {
            double Pick(string field, string general, double fallback) => entry[field] is JsonValue v && v.TryGetValue<double>(out var n) ? n : Shared(general, fallback);
            return (Pick("repeatX", "uvRepeatX", 1), Pick("repeatY", "uvRepeatY", 1), Pick("offsetX", "uvOffsetX", 0), Pick("offsetY", "uvOffsetY", 0), Pick("rotation", "uvRotation", 0));
        }
        return (Shared("uvRepeatX", 1), Shared("uvRepeatY", 1), Shared("uvOffsetX", 0), Shared("uvOffsetY", 0), Shared("uvRotation", 0));
    }

    // ---- changing ------------------------------------------------------------------------------------------------------------

    /// <summary>Applies a change to the chosen faces or to the whole selection and sends one update per object. Returns how many were sent.</summary>
    public int Edit(Action<JsonObject> change, string label)
    {
        var sent = 0; var faces = TargetFaces; var changes = new List<BuildUndoRedoManager.Change>();
        foreach (var view in _selection.Views().ToList())
        {
            var before = BuildPersistenceManager.Clone(view.State); var after = BuildPersistenceManager.Clone(before); var material = Parse(after.MaterialJson);
            if (faces.Count > 0 && view.State.Id == _faces.ObjectId)
            {
                var array = material["faces"] as JsonArray ?? []; material["faces"] = array;
                foreach (var face in faces) { var entry = FaceEntry(material, face); if (entry is null) { entry = new JsonObject { ["index"] = face }; array.Add(entry); } change(entry); }
            }
            else
            {
                var snapshot = material.Where(p => p.Key != "faces").ToDictionary(p => p.Key, p => p.Value?.ToJsonString());
                change(material);
                // the whole object changed: a face that said something different about the same thing no longer does
                var touched = material.Where(p => p.Key != "faces" && (!snapshot.TryGetValue(p.Key, out var old) || old != p.Value?.ToJsonString())).Select(p => p.Key).Concat(snapshot.Keys.Where(k => !material.ContainsKey(k))).ToList();
                if (material["faces"] is JsonArray entries) foreach (var entry in entries.OfType<JsonObject>()) foreach (var key in touched) entry.Remove(key);
            }
            after.MaterialJson = material.ToJsonString(); if (after.MaterialJson == before.MaterialJson) continue;
            if (!_sync.Update(after)) continue;
            sent++; changes.Add(new BuildUndoRedoManager.Change { Id = before.Id, Before = before, After = after, Known = before.Revision + 1 });
        }
        if (sent > 0) { _history.Record(label, changes); }
        return sent;
    }

    public int SetShading(bool pbr) => Edit(layer => layer["shading"] = pbr ? "pbr" : "blinnphong", "modelo de material");

    /// <summary>Puts a picture on a map, or takes it off (null). The base colour also accepts the system patterns.</summary>
    public int SetMap(string key, string? asset, bool builtin = false) => Edit(layer =>
    {
        layer.Remove("noTexture");
        if (key == "texture") { layer.Remove("texture"); layer.Remove("builtin"); if (asset is not null) layer[builtin ? "builtin" : "texture"] = asset; else if (TargetFaces.Count > 0) layer["noTexture"] = true; /* a face with no base picture says so, or it would show the picture of the object */ }
        else if (asset is null) layer.Remove(key); else layer[key] = asset;
    }, asset is null ? "tirar textura" : "textura");


    public static void ColorTo(JsonObject layer, string hex) => layer["color"] = hex;
    public int SetColor(string hex) => Edit(layer => ColorTo(layer, hex), "cor");

    /// <summary>The opacity (1 opaque) and how the alpha is used: "none", "blend" or "mask".</summary>
    public static void OpacityTo(JsonObject layer, double opacity, string alphaMode, double cutoff = .5)
    {
        opacity = Math.Clamp(opacity, 0, 1); if (opacity < .999) layer["transparency"] = Math.Round(opacity, 2); else layer.Remove("transparency");
        if (alphaMode == "none" && opacity < .999) alphaMode = "blend"; // asking for see-through is asking to blend
        if (alphaMode is "blend" or "mask") layer["alphaMode"] = alphaMode; else layer.Remove("alphaMode");
        if (alphaMode == "mask") layer["alphaCutoff"] = Math.Round(Math.Clamp(cutoff, 0, 1), 2); else layer.Remove("alphaCutoff");
    }
    public int SetOpacity(double opacity, string alphaMode, double cutoff = .5) => Edit(layer => OpacityTo(layer, opacity, alphaMode, cutoff), "transparência");

    public static void NumberTo(JsonObject layer, string key, double value, double min, double max) => layer[key] = Math.Round(Math.Clamp(value, min, max), 3);
    public int SetNumber(string key, double value, double min, double max) => Edit(layer => NumberTo(layer, key, value, min, max), key);
    public static void FlagTo(JsonObject layer, string key, bool on) { if (on) layer[key] = true; else layer.Remove(key); }
    public int SetFlag(string key, bool on) => Edit(layer => FlagTo(layer, key, on), key);

    /// <summary>The glow: a colour and how strongly it shines; off removes it.</summary>
    public static void GlowTo(JsonObject layer, bool on, string hex, double strength)
    {
        if (!on) { layer.Remove("emission"); layer.Remove("emissionStrength"); return; }
        layer["emission"] = hex; layer["emissionStrength"] = Math.Round(Math.Clamp(strength, 0, 16), 2);
    }
    public int SetGlow(bool on, string hex, double strength) => Edit(layer => GlowTo(layer, on, hex, strength), "brilho");

    /// <summary>Scale, offset and rotation of one map, or of all maps at once ("all": the shared values, and no map keeps its own).</summary>
    public static void UvTo(JsonObject layer, string channel, double repeatX, double repeatY, double offsetX, double offsetY, double rotation)
    {
        repeatX = Math.Clamp(repeatX, .01, 100); repeatY = Math.Clamp(repeatY, .01, 100); offsetX = Math.Clamp(offsetX, -100, 100); offsetY = Math.Clamp(offsetY, -100, 100); rotation = Math.Clamp(rotation, -360, 360);
        if (channel == "all")
        {
            layer["uvRepeatX"] = Math.Round(repeatX, 3); layer["uvRepeatY"] = Math.Round(repeatY, 3); layer["uvOffsetX"] = Math.Round(offsetX, 3); layer["uvOffsetY"] = Math.Round(offsetY, 3); layer["uvRotation"] = Math.Round(rotation, 2); layer.Remove("mapUv");
            return;
        }
        var maps = layer["mapUv"] as JsonObject ?? []; layer["mapUv"] = maps;
        maps[channel] = new JsonObject { ["repeatX"] = Math.Round(repeatX, 3), ["repeatY"] = Math.Round(repeatY, 3), ["offsetX"] = Math.Round(offsetX, 3), ["offsetY"] = Math.Round(offsetY, 3), ["rotation"] = Math.Round(rotation, 2) };
    }
    public int SetUv(string channel, double repeatX, double repeatY, double offsetX, double offsetY, double rotation) => Edit(layer => UvTo(layer, channel, repeatX, repeatY, offsetX, offsetY, rotation), "mapeamento");

    /// <summary>Takes every texture and the colour away: the surface becomes plain white. On the whole object the faces are cleared too.</summary>
    public int Clear() => Edit(layer =>
    {
        foreach (var key in MaterialBuilder.MapKeys) layer.Remove(key);
        foreach (var key in new[] { "builtin", "mapUv", "uvRepeatX", "uvRepeatY", "uvOffsetX", "uvOffsetY", "uvRotation", "emission", "emissionStrength", "transparency", "alphaMode", "alphaCutoff", "media", "mediaSlot" }) layer.Remove(key);
        layer["noTexture"] = true; layer["color"] = "#FFFFFF";
    }, "limpar material");


    // ---- media ------------------------------------------------------------------------------------------------------------------

    public JsonObject? Media => Value("media") as JsonObject;

    /// <summary>Shows a picture or a video from a public https address on the target. With glow it also shines (the media is its own emissive map).</summary>
    public int SetMedia(string type, string url, bool playing, bool loop, bool glow, double glowStrength) => Edit(layer =>
    {
        layer["media"] = new JsonObject { ["type"] = type, ["url"] = url, ["playing"] = playing, ["loop"] = loop, ["glow"] = glow };
        layer.Remove("noTexture");
        if (glow) { layer["emission"] = "#FFFFFF"; layer["emissionStrength"] = Math.Round(Math.Clamp(glowStrength, 0, 16), 2); } else if (layer["emission"] is JsonValue e && e.TryGetValue<string>(out var c) && c == "#FFFFFF") { layer.Remove("emission"); layer.Remove("emissionStrength"); }
    }, "mídia");

    public int RemoveMedia() => Edit(layer =>
    {
        if (layer["media"] is JsonObject media && media["glow"] is JsonValue g && g.TryGetValue<bool>(out var glow) && glow) { layer.Remove("emission"); layer.Remove("emissionStrength"); }
        layer.Remove("media");
    }, "tirar mídia");
    // ---- material presets and files ----------------------------------------------------------------------------------------

    /// <summary>The keys of a surface that make up a "material" (everything but where the object is, its light, its animation and its faces).</summary>
    private static readonly string[] MaterialKeys = ["shading", "color", "texture", "builtin", "normalMap", "specularMap", "ormMap", "emissiveMap", "noTexture", "roughness", "metallic", "glossiness", "specularColor", "environment", "normalStrength", "specularStrength", "aoStrength", "emission", "emissionStrength", "fullBright", "transparency", "alphaMode", "alphaCutoff", "doubleSided", "uvRepeatX", "uvRepeatY", "uvOffsetX", "uvOffsetY", "uvRotation", "mapUv", "flipU", "flipV", "mapping"];

    public static readonly IReadOnlyList<(string Name, string Json)> Presets =
    [
        ("Madeira", "{\"shading\":\"pbr\",\"builtin\":\"planks\",\"color\":\"#FFFFFF\",\"roughness\":0.75,\"metallic\":0}"),
        ("Tijolo", "{\"shading\":\"pbr\",\"builtin\":\"bricks\",\"color\":\"#FFFFFF\",\"roughness\":0.9,\"metallic\":0}"),
        ("Metal escovado", "{\"shading\":\"pbr\",\"builtin\":\"noise\",\"color\":\"#C8CDD2\",\"roughness\":0.35,\"metallic\":1}"),
        ("Plástico brilhante", "{\"shading\":\"blinnphong\",\"color\":\"#E04040\",\"glossiness\":0.9,\"specularColor\":\"#FFFFFF\",\"specularStrength\":1.2}"),
        ("Ladrilho", "{\"shading\":\"blinnphong\",\"builtin\":\"tiles\",\"color\":\"#FFFFFF\",\"glossiness\":0.7,\"uvRepeatX\":2,\"uvRepeatY\":2}"),
        ("Neon", "{\"shading\":\"pbr\",\"color\":\"#101820\",\"emission\":\"#12F2E0\",\"emissionStrength\":4,\"roughness\":0.4}")
    ];

    /// <summary>Gives the target a material (a preset or an imported one). Only known keys are taken, each inside its range.</summary>
    public int ApplyMaterial(JsonObject material, string label = "material")
    {
        var clean = Sanitize(material); if (clean.Count == 0) { Said?.Invoke("Esse material não tem nada que eu reconheça."); return 0; }
        return Edit(layer => { foreach (var key in MaterialKeys) layer.Remove(key); foreach (var (key, value) in clean) layer[key] = value?.DeepClone(); }, label);
    }

    public static JsonObject Sanitize(JsonObject material)
    {
        var clean = new JsonObject(); var ranges = new Dictionary<string, (double Min, double Max)> { ["roughness"] = (0, 1), ["metallic"] = (0, 1), ["glossiness"] = (0, 1), ["environment"] = (0, 1), ["normalStrength"] = (0, 2), ["specularStrength"] = (0, 2), ["aoStrength"] = (0, 1), ["emissionStrength"] = (0, 16), ["transparency"] = (0, 1), ["alphaCutoff"] = (0, 1), ["uvRepeatX"] = (.01, 100), ["uvRepeatY"] = (.01, 100), ["uvOffsetX"] = (-100, 100), ["uvOffsetY"] = (-100, 100), ["uvRotation"] = (-360, 360) };
        foreach (var (key, value) in material)
        {
            if (!MaterialKeys.Contains(key) || value is null || key is "texture" or "normalMap" or "specularMap" or "ormMap" or "emissiveMap") continue; // pictures belong to the person who chose them, never to a file
            if (ranges.TryGetValue(key, out var range)) { if (value is JsonValue n && n.TryGetValue<double>(out var number) && double.IsFinite(number)) clean[key] = Math.Round(Math.Clamp(number, range.Min, range.Max), 3); }
            else if (key is "color" or "specularColor" or "emission") { if (value is JsonValue t && t.TryGetValue<string>(out var hex) && hex.Length == 7 && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit)) clean[key] = hex; }
            else if (key == "shading") { if (value is JsonValue t && t.TryGetValue<string>(out var s) && s is "pbr" or "blinnphong") clean[key] = s; }
            else if (key == "alphaMode") { if (value is JsonValue t && t.TryGetValue<string>(out var s) && s is "none" or "blend" or "mask") clean[key] = s; }
            else if (key == "builtin") { if (value is JsonValue t && t.TryGetValue<string>(out var s) && BuiltInTextures.All.Any(b => b.Id == s)) clean[key] = s; }
            else if (key == "mapping") { if (value is JsonValue t && t.TryGetValue<string>(out var s) && s is "default" or "planar") clean[key] = s; }
            else if (key is "fullBright" or "doubleSided" or "flipU" or "flipV" or "noTexture") { if (value is JsonValue t && t.TryGetValue<bool>(out var b) && b) clean[key] = true; }
            else if (key == "mapUv" && value is JsonObject maps) clean[key] = maps.DeepClone();
        }
        return clean;
    }

    /// <summary>The material of what is being edited, as a file the person can keep and share (the pictures are left out: they stay with whoever owns them).</summary>
    public string? Export()
    {
        if (_selection.Views().LastOrDefault()?.State is not { } state) return null;
        var material = Parse(state.MaterialJson); var faces = TargetFaces; var layer = faces.Count > 0 && FaceEntry(material, faces.First()) is { } entry ? entry : material;
        var result = new JsonObject { ["format"] = "lifeverse.material", ["version"] = 1 }; var body = new JsonObject();
        foreach (var key in MaterialKeys) if ((layer[key] ?? material[key]) is { } value) body[key] = value.DeepClone();
        result["material"] = Sanitize(body); return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public int Import(string json)
    {
        JsonObject? root; try { root = JsonNode.Parse(json) as JsonObject; } catch (JsonException) { root = null; }
        if (root?["format"]?.GetValue<string>() != "lifeverse.material" || root["material"] is not JsonObject material) { Said?.Invoke("Esse arquivo não é um material do LifeVerse."); return 0; }
        return ApplyMaterial(material, "importar material");
    }
}
