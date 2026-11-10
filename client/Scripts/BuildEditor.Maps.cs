using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// The material models and maps of the Textura tab. A material is Blinn-Phong (diffuse, normal and specular maps, specular colour, glossiness, environment) or PBR (base
/// colour, normal, metallic-roughness-occlusion and emissive maps). "Escolher…" puts the chosen image on the map picked here; each map can have its own repeat, offset and
/// rotation, or share the object's. "Limpar" takes every texture and the colour away from the target, leaving it plain white.
public partial class BuildEditor
{
    private OptionButton _shadingPick = null!, _mapPick = null!;
    private CheckBox _uvSync = null!;
    private ColorPickerButton _specColor = null!;
    private HSlider _gloss = null!, _env = null!;

    public const string DefaultMaterialJson = "{\"shading\":\"blinnphong\",\"builtin\":\"plywood\",\"color\":\"#FFFFFF\",\"glossiness\":0.4}";
    private static readonly string[] MapNames = ["Cor base / difusa", "Normal", "Especular (Blinn-Phong)", "Metal-rugosidade-oclusão (PBR)", "Emissivo"];

    private string CurrentMap => MaterialBuilder.MapKeys[Mathf.Clamp(_mapPick?.Selected ?? 0, 0, MaterialBuilder.MapKeys.Length - 1)];

    private void AddMapsSection(VBoxContainer page)
    {
        page.AddChild(Heading("Modelo de material"));
        _shadingPick = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "Blinn-Phong: luz difusa e brilho especular. PBR: metal, rugosidade, oclusão e emissivo." };
        _shadingPick.AddItem("Blinn-Phong"); _shadingPick.AddItem("PBR (metal e rugosidade)"); page.AddChild(FormRow("Modelo:", _shadingPick, 140));
        _mapPick = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "O mapa que recebe a imagem escolhida em “Escolher…” e que o mapeamento abaixo edita" };
        foreach (var name in MapNames) _mapPick.AddItem(name);
        _mapPick.ItemSelected += _ => { _filling = true; FillMapTransform(); _filling = false; };
        page.AddChild(FormRow("Mapa:", _mapPick, 140));
        _specColor = ColorButton(Colors.White, 100); _gloss = NewSlider(0, 1, .01, .4); _env = NewSlider(0, 1, .01, 0);
        var specRow = Row(10); specRow.AddChild(Txt("Cor do brilho (especular)", 19)); specRow.AddChild(Filler()); specRow.AddChild(_specColor); page.AddChild(specRow);
        page.AddChild(PairRow("Brilho (glossiness)", ("", SpinFor(_gloss)))); page.AddChild(PairRow("Ambiente", ("", SpinFor(_env))));
        _uvSync = Check("Mesmo mapeamento em todos os mapas", "Desligado: cada mapa tem a sua própria repetição, deslocamento e rotação (os campos abaixo editam o mapa escolhido)"); _uvSync.ButtonPressed = true;
        _uvSync.Toggled += _ => { if (!_filling) { _filling = true; FillMapTransform(); _filling = false; } };
        page.AddChild(_uvSync);
        var apply = IconButton("check", "Aplicar modelo e brilho", "Grava o modelo, a cor do brilho, o brilho e o ambiente no alvo", ApplyShading, UiStyle.ButtonKind.Secondary, 40); page.AddChild(apply);
    }

    private void ApplyShading()
    {
        EditTarget(layer =>
        {
            layer["shading"] = _shadingPick.Selected == 1 ? "pbr" : "blinnphong";
            layer["specularColor"] = "#" + _specColor.Color.ToHtml(false); layer["glossiness"] = Math.Round(_gloss.Value, 2); layer["environment"] = Math.Round(_env.Value, 2);
        }, "Modelo de material aplicado.");
    }

    /// Puts the chosen image on the map picked in "Mapa" (the base colour by default). System textures only make sense as a base colour.
    private void ApplyTexture(bool normal)
    {
        if (Chosen() is not { } entry) { _status.Text = "Escolha uma textura na biblioteca (Escolher…)."; return; }
        var key = normal ? "normalMap" : CurrentMap;
        if (key != "texture" && entry.Builtin is not null) { _status.Text = "Texturas do sistema só servem de cor base; para esse mapa use uma imagem sua."; return; }
        var ok = EditTarget(layer =>
        {
            layer.Remove("noTexture");
            if (key == "texture") { layer.Remove("texture"); layer.Remove("builtin"); if (entry.Builtin is { } builtin) layer["builtin"] = builtin; else layer["texture"] = entry.Asset!.Value.ToString(); }
            else layer[key] = entry.Asset!.Value.ToString();
        }, key == "texture" ? "Textura aplicada." : $"Mapa “{MapNames[Array.IndexOf(MaterialBuilder.MapKeys, key)]}” aplicado.");
        if (ok && key == "texture") RememberRecent(entry);
    }

    /// "Limpar": no texture at all and no colour. On the whole object the faces' own textures and colours go too.
    private void RemoveTexture()
    {
        var wholeObject = _faceSet.Count == 0;
        EditTarget(layer =>
        {
            foreach (var key in MaterialBuilder.MapKeys) layer.Remove(key);
            layer.Remove("builtin"); layer.Remove("mapUv"); layer["noTexture"] = true; layer["color"] = "#FFFFFF";
            if (wholeObject) layer.Remove("faces");
        }, wholeObject ? "Textura e cor retiradas: o objeto ficou liso e branco." : "Textura e cor retiradas das faces.");
    }

    /// Every face of the object gets the chosen image on the chosen map, replacing what the faces had.
    private void ApplyTextureToAll()
    {
        if (Chosen() is not { } entry) { _status.Text = "Escolha uma textura na biblioteca (Escolher…)."; return; }
        var key = CurrentMap;
        if (key != "texture" && entry.Builtin is not null) { _status.Text = "Texturas do sistema só servem de cor base; para esse mapa use uma imagem sua."; return; }
        ClearFace();
        var ok = EditTarget(layer =>
        {
            layer.Remove("noTexture");
            if (key == "texture") { layer.Remove("texture"); layer.Remove("builtin"); if (entry.Builtin is { } builtin) layer["builtin"] = builtin; else layer["texture"] = entry.Asset!.Value.ToString(); }
            else layer[key] = entry.Asset!.Value.ToString();
            if (layer["faces"] is JsonArray faces) foreach (var node in faces.OfType<JsonObject>()) { node.Remove(key); if (key == "texture") node.Remove("builtin"); node.Remove("noTexture"); }
        }, "Aplicado a todas as faces.");
        if (ok && key == "texture") RememberRecent(entry);
    }

    /// Repetition, offset, rotation and mirroring: for every map, or for the one chosen in "Mapa" when the mapping is not shared.
    private void ApplyUv()
    {
        EditTarget(layer =>
        {
            var repeatX = Mathf.Clamp(NumOr(_repeatX, 1), .01f, 100f); var repeatY = Mathf.Clamp(NumOr(_repeatY, 1), .01f, 100f);
            var offsetX = Mathf.Clamp(NumOr(_offsetX, 0), -100f, 100f); var offsetY = Mathf.Clamp(NumOr(_offsetY, 0), -100f, 100f); var rotation = Mathf.Clamp(NumOr(_uvRotation, 0), -360f, 360f);
            if (_uvSync is null || _uvSync.ButtonPressed)
            {
                layer["uvRepeatX"] = repeatX; layer["uvRepeatY"] = repeatY; layer["uvOffsetX"] = offsetX; layer["uvOffsetY"] = offsetY; layer["uvRotation"] = rotation; layer.Remove("mapUv");
            }
            else
            {
                var maps = layer["mapUv"] as JsonObject ?? []; layer["mapUv"] = maps;
                maps[CurrentMap] = new JsonObject { ["repeatX"] = repeatX, ["repeatY"] = repeatY, ["offsetX"] = offsetX, ["offsetY"] = offsetY, ["rotation"] = rotation };
            }
            if (_flipU.ButtonPressed) layer["flipU"] = true; else layer.Remove("flipU");
            if (_flipV.ButtonPressed) layer["flipV"] = true; else layer.Remove("flipV");
        }, "Mapeamento aplicado.");
    }

    /// Shows the model, the shine settings and the mapping of the target in the controls.
    private void FillMapControls(JsonObject layer, JsonObject material)
    {
        if (_shadingPick is null) return;
        JsonNode? Get(string key) => layer[key] ?? material[key];
        _shadingPick.Select(Get("shading") is JsonValue s && s.TryGetValue<string>(out var model) && model == "blinnphong" ? 0 : Get("shading") is null ? 1 : 1);
        try { _specColor.Color = Get("specularColor") is JsonValue c && c.TryGetValue<string>(out var hex) ? Color.FromHtml(hex) : Colors.White; } catch { _specColor.Color = Colors.White; }
        _gloss.SetValueNoSignal(Get("glossiness") is JsonValue g && g.TryGetValue<double>(out var gloss) ? gloss : .4); _env.SetValueNoSignal(Get("environment") is JsonValue e && e.TryGetValue<double>(out var env) ? env : 0);
        _uvSync.SetPressedNoSignal(layer["mapUv"] is not JsonObject { Count: > 0 });
        FillMapTransform();
    }

    /// The five mapping fields for the map chosen in "Mapa" (when the mapping is shared they show the general values).
    private void FillMapTransform()
    {
        if (_mapPick is null || _repeatX is null) return;
        var material = _selection.Count == 1 ? ParseObject(_selection[0].State.MaterialJson) : new JsonObject(); var layer = material;
        if (_faceSet.Count > 0 && material["faces"] is JsonArray faces) foreach (var node in faces.OfType<JsonObject>()) if (node["index"]?.GetValue<int>() == _selectedFace) layer = node;
        var entry = !_uvSync.ButtonPressed ? (layer["mapUv"] as JsonObject)?[CurrentMap] as JsonObject : null;
        float Pick(string field, string general, float fallback) => entry?[field] is JsonValue v && v.TryGetValue<double>(out var n) ? (float)n : Read(layer, general, fallback);
        _repeatX.Text = Disp(Pick("repeatX", "uvRepeatX", 1), 2); _repeatY.Text = Disp(Pick("repeatY", "uvRepeatY", 1), 2);
        _offsetX.Text = Disp(Pick("offsetX", "uvOffsetX", 0), 2); _offsetY.Text = Disp(Pick("offsetY", "uvOffsetY", 0), 2); _uvRotation.Text = Disp(Pick("rotation", "uvRotation", 0), 2);
    }

    public void SetShadingForTesting(bool blinn, double gloss, double env, Color specular) { _shadingPick.Select(blinn ? 0 : 1); _gloss.Value = gloss; _env.Value = env; _specColor.Color = specular; ApplyShading(); }
    public void SelectMapForTesting(int index) { _mapPick.Select(index); _filling = true; FillMapTransform(); _filling = false; }
    public void SetUvSyncForTesting(bool on) { _uvSync.ButtonPressed = on; }
    public void ApplyTextureToAllForTesting() => ApplyTextureToAll();
}
