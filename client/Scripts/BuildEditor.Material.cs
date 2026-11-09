using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Advanced material settings of the Textura tab, applied to the target (the whole object, or every selected face): mapping alignment, full bright, alpha mode
/// and cutoff, double sided, plus the roughness, metallic, opacity and glow set above. Faces can be several: Shift + click adds faces.
public partial class BuildEditor
{
    private OptionButton _mapping = null!, _alphaMode = null!;
    private CheckBox _fullBright = null!, _doubleSided = null!;
    private HSlider _alphaCutoff = null!;
    private static readonly string[] AlphaModes = ["none", "blend", "mask"];

    private PanelContainer BuildAdvancedMaterialCard()
    {
        var card = Card("Material avançado", "layers", out var body, out _, 10);
        body.AddChild(Txt("Vale para o alvo: o objeto inteiro ou as faces escolhidas (Shift + clique soma faces). Usa também rugosidade, metalicidade, opacidade e brilho acima.", 17, false, UiStyle.TextMuted));
        var faceRow = Row(8); body.AddChild(faceRow);
        faceRow.AddChild(IconButton("cube", "Todas as faces", "Seleciona todas as faces do objeto", SelectAllFaces, UiStyle.ButtonKind.Tool, 44));
        faceRow.AddChild(IconButton("reset", "Sem faces", "Volta a editar o objeto inteiro", ClearFace, UiStyle.ButtonKind.Tool, 44));
        var mapRow = Row(8); body.AddChild(mapRow);
        mapRow.AddChild(RowLabel("Alinhamento:")); _mapping = Pick("Padrão (UV da forma)", "Planar (em metros)"); _mapping.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _mapping.TooltipText = "Planar projeta a textura em metros, sem esticar quando o objeto é redimensionado"; mapRow.AddChild(_mapping);
        var alphaRow = Row(8); body.AddChild(alphaRow);
        alphaRow.AddChild(RowLabel("Modo alfa:")); _alphaMode = Pick("Opaco", "Misturar (translúcido)", "Cortar (recorte)"); _alphaMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; alphaRow.AddChild(_alphaMode);
        _alphaCutoff = NewSlider(0, 1, .01, .5); body.AddChild(SliderLine(null, "Corte alfa", _alphaCutoff, 2, false, 190));
        _fullBright = new CheckBox { Text = " Brilho total (sem sombras)", TooltipText = "O material não recebe luz nem sombra: aparece sempre com a cor cheia" }; _fullBright.AddThemeFontSizeOverride("font_size", 19); body.AddChild(_fullBright);
        _doubleSided = new CheckBox { Text = " Dupla face", TooltipText = "Desenha também o lado de dentro das faces" }; _doubleSided.AddThemeFontSizeOverride("font_size", 19); body.AddChild(_doubleSided);
        body.AddChild(IconButton("check", "Aplicar material avançado", "Envia ao alvo: alinhamento, modo alfa, brilho total, dupla face, rugosidade, metalicidade, opacidade e brilho", ApplyAdvancedMaterial, UiStyle.ButtonKind.Primary, 48));
        return card;
    }

    private void ApplyAdvancedMaterial()
    {
        EditTarget(layer =>
        {
            layer["roughness"] = Math.Round(_roughness.Value, 2); layer["metallic"] = Math.Round(_metallic.Value, 2);
            if (_opacity.Value < .999) layer["transparency"] = Math.Round(_opacity.Value, 2); else layer.Remove("transparency");
            if (_glow.ButtonPressed) { layer["emission"] = "#" + _emissionPicker.Color.ToHtml(false); layer["emissionStrength"] = Math.Round(_glowStrength.Value * 8, 2); } else { layer.Remove("emission"); layer.Remove("emissionStrength"); }
            if (_mapping.Selected == 1) layer["mapping"] = "planar"; else layer.Remove("mapping");
            var mode = AlphaModes[Mathf.Clamp(_alphaMode.Selected, 0, 2)];
            if (mode == "none") layer.Remove("alphaMode"); else layer["alphaMode"] = mode;
            if (mode == "mask") layer["alphaCutoff"] = Math.Round(_alphaCutoff.Value, 2); else layer.Remove("alphaCutoff");
            if (_fullBright.ButtonPressed) layer["fullBright"] = true; else layer.Remove("fullBright");
            if (_doubleSided.ButtonPressed) layer["doubleSided"] = true; else layer.Remove("doubleSided");
        }, "Material avançado aplicado.");
    }

    /// Shows the advanced settings of the target layer (the primary selected face, or the object).
    private void FillAdvancedMaterial(JsonObject layer, JsonObject material)
    {
        if (_mapping is null) return;
        JsonNode? Pick(string key) => layer[key] ?? material[key];
        bool Flag(string key) => Pick(key) is JsonValue v && v.TryGetValue<bool>(out var on) && on;
        _mapping.Select(Pick("mapping") is JsonValue m && m.TryGetValue<string>(out var map) && map == "planar" ? 1 : 0);
        var mode = Pick("alphaMode") is JsonValue a && a.TryGetValue<string>(out var text) ? Array.IndexOf(AlphaModes, text) : 0;
        _alphaMode.Select(Math.Max(0, mode));
        _alphaCutoff.SetValueNoSignal(Pick("alphaCutoff") is JsonValue c && c.TryGetValue<double>(out var cut) ? cut : .5);
        _fullBright.SetPressedNoSignal(Flag("fullBright")); _doubleSided.SetPressedNoSignal(Flag("doubleSided"));
    }

    public void SetAdvancedMaterialForTesting(bool planar, int alphaMode, bool fullBright, bool doubleSided, double cutoff)
    {
        _mapping.Select(planar ? 1 : 0); _alphaMode.Select(alphaMode); _fullBright.ButtonPressed = fullBright; _doubleSided.ButtonPressed = doubleSided; _alphaCutoff.Value = cutoff; ApplyAdvancedMaterial();
    }
}
