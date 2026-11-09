using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Advanced edit options: "Esticar dos dois lados" (a stretch grows from the centre, or only towards the handle you drag) and "Esticar texturas"
/// (the textures stretch with the object, or keep their size in metres).
public partial class BuildEditor
{
    private CheckBox _stretchBoth = null!, _stretchTextures = null!;

    private void AddStretchOptions(VBoxContainer body)
    {
        _stretchBoth = new CheckBox { Text = " Esticar dos dois lados", ButtonPressed = true, TooltipText = "Ligado: o objeto cresce a partir do centro. Desligado: a face oposta ao puxador fica parada" }; _stretchBoth.AddThemeFontSizeOverride("font_size", 19);
        _stretchTextures = new CheckBox { Text = " Esticar texturas", ButtonPressed = true, TooltipText = "Ligado: a textura estica junto com o objeto. Desligado: ela mantém o tamanho em metros (mapeamento planar)" }; _stretchTextures.AddThemeFontSizeOverride("font_size", 19);
        body.AddChild(_stretchBoth); body.AddChild(_stretchTextures);
    }

    private static bool ScaleChanged(BuildObjectState before, BuildObjectState after) =>
        !Mathf.IsEqualApprox(before.ScaleX, after.ScaleX) || !Mathf.IsEqualApprox(before.ScaleY, after.ScaleY) || !Mathf.IsEqualApprox(before.ScaleZ, after.ScaleZ);

    /// With "Esticar texturas" off, resizing an object makes its textures planar, so they keep their size in metres instead of stretching.
    private void KeepTextureSize(BuildObjectState before, BuildObjectState after)
    {
        if (_stretchTextures is null || _stretchTextures.ButtonPressed || !ScaleChanged(before, after)) return;
        var material = ParseObject(after.MaterialJson);
        var textured = material.ContainsKey("texture") || material.ContainsKey("builtin") || (material["faces"] as JsonArray)?.OfType<JsonObject>().Any(f => f.ContainsKey("texture") || f.ContainsKey("builtin")) == true;
        if (!textured || material["mapping"] is JsonValue) return;
        material["mapping"] = "planar"; after.MaterialJson = material.ToJsonString();
    }

    public void SetStretchOptionsForTesting(bool both, bool textures) { _stretchBoth.ButtonPressed = both; _stretchTextures.ButtonPressed = textures; }
}
