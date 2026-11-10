using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The rest of the Recursos tab: light falloff, the light projector (image, field of view, focus, ambiance) and the flexible path (softness, gravity, drag, wind,
/// tension and a force on each axis). Everything is stored in the material JSON and changes what the object does in the world; "Aplicar recursos" sends it all.
public partial class BuildEditor
{
    private HSlider _lightFalloff = null!, _projFov = null!, _projFocus = null!, _projAmbiance = null!;
    private OptionButton _projTexture = null!;
    private Guid? _projAsset;
    private Label _projAssetLabel = null!;
    private CheckBox _flexOn = null!;
    private readonly Dictionary<string, HSlider> _flexSliders = [];

    private HSlider LightFalloffSlider() => _lightFalloff = NewSlider(.1, 4, .05, 1);

    private void UseLibraryForProjector()
    {
        if (Chosen() is not { } entry) { _status.Text = "Escolha uma imagem na biblioteca da aba Textura."; return; }
        if (entry.Builtin is { } builtin)
        {
            for (var i = 1; i < _projTexture.ItemCount; i++) if (_projTexture.GetItemMetadata(i).AsString() == builtin) _projTexture.Select(i);
            _projAsset = null; _projAssetLabel.Text = string.Empty;
        }
        else if (entry.Asset is { } asset) { _projTexture.Select(0); _projAsset = asset; _projAssetLabel.Text = entry.Name; }
    }

    /// Shows the resources of the selected object in the controls.
    private void FillResources()
    {
        if (_flexOn is null) return;
        var material = _selection.Count == 1 ? ParseObject(_selection[0].State.MaterialJson) : new JsonObject();
        var light = material["light"] as JsonObject;
        _lightOn.SetPressedNoSignal(light is not null);
        if (light is not null)
        {
            if (light["color"] is JsonValue c && c.TryGetValue<string>(out var text)) { try { _lightColor.Color = Color.FromHtml(text); } catch { } }
            _lightEnergy.SetValueNoSignal(Read(light, "energy", 1.5f)); _lightRange.SetValueNoSignal(Read(light, "range", 8f)); _lightFalloff.SetValueNoSignal(Read(light, "falloff", 1f));
        }
        var projector = material["projector"] as JsonObject ?? new JsonObject();
        _projFov.SetValueNoSignal(Read(projector, "fov", 45)); _projFocus.SetValueNoSignal(Read(projector, "focus", 10)); _projAmbiance.SetValueNoSignal(Read(projector, "ambiance", .1f));
        _projAsset = null; _projAssetLabel.Text = string.Empty; _projTexture.Select(0);
        if (material["projectorBuiltin"] is JsonValue b && b.TryGetValue<string>(out var builtin))
            for (var i = 1; i < _projTexture.ItemCount; i++) if (_projTexture.GetItemMetadata(i).AsString() == builtin) _projTexture.Select(i);
        if (material["projectorTexture"] is JsonValue a && a.TryGetValue<string>(out var assetText) && Guid.TryParse(assetText, out var asset)) { _projAsset = asset; _projAssetLabel.Text = "Imagem da biblioteca"; }
        var flex = FlexSettings.FromMaterial(_selection.Count == 1 ? _selection[0].State.MaterialJson : null);
        _flexOn.SetPressedNoSignal(flex is not null);
        var shown = flex ?? new FlexSettings(1, 0, 2, 0, 1, 0, 0, 0);
        foreach (var (key, value) in new (string, float)[] { ("softness", shown.Softness), ("gravity", shown.Gravity), ("drag", shown.Drag), ("wind", shown.Wind), ("tension", shown.Tension), ("forceX", shown.ForceX), ("forceY", shown.ForceY), ("forceZ", shown.ForceZ) })
            _flexSliders[key].SetValueNoSignal(value);
    }

    /// Writes light, projector and flex into a material.
    private void WriteResources(JsonObject material)
    {
        material.Remove("projector"); material.Remove("projectorTexture"); material.Remove("projectorBuiltin");
        if (_lightOn.ButtonPressed)
        {
            material["light"] = new JsonObject { ["color"] = "#" + _lightColor.Color.ToHtml(false), ["energy"] = Math.Round(_lightEnergy.Value, 1), ["range"] = Math.Round(_lightRange.Value, 1), ["falloff"] = Math.Round(_lightFalloff.Value, 2) };
            if (_projAsset is { } asset) material["projectorTexture"] = asset.ToString();
            else if (_projTexture.Selected > 0) material["projectorBuiltin"] = _projTexture.GetItemMetadata(_projTexture.Selected).AsString();
            if (_projAsset is not null || _projTexture.Selected > 0) material["projector"] = new JsonObject { ["fov"] = Math.Round(_projFov.Value, 1), ["focus"] = Math.Round(_projFocus.Value, 1), ["ambiance"] = Math.Round(_projAmbiance.Value, 2) };
        }
        else material.Remove("light");
        if (_flexOn.ButtonPressed)
        {
            float V(string key) => (float)_flexSliders[key].Value;
            material["flex"] = new FlexSettings(V("softness"), V("gravity"), V("drag"), V("wind"), V("tension"), V("forceX"), V("forceY"), V("forceZ")).Clamped().ToJson();
        }
        else material.Remove("flex");
    }

    public void SetResourcesForTesting(bool flex, double wind, double softness, bool light, string? projectorBuiltin)
    {
        _flexOn.ButtonPressed = flex; _flexSliders["wind"].Value = wind; _flexSliders["softness"].Value = softness; _lightOn.ButtonPressed = light;
        _projTexture.Select(0);
        if (projectorBuiltin is not null) for (var i = 1; i < _projTexture.ItemCount; i++) if (_projTexture.GetItemMetadata(i).AsString() == projectorBuiltin) _projTexture.Select(i);
        ApplyLight();
    }
    public bool FlexOnForTesting => _flexOn.ButtonPressed;
}
