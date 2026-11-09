using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The advanced shape settings of the Objeto tab: path cut, profile cut, top shear, hole size X/Y, radius offset, revolutions and skew.
/// Each slider writes its own key of the geometry JSON and changes the real mesh; a slider the selected shape does not use is disabled.
public partial class BuildEditor
{
    private sealed record ShapeSetting(string Key, string Label, string Tip, double Min, double Max, double Step, double Default, Func<string, bool> Applies);

    private static bool Linear(string t) => t is "cube" or "prism" or "cylinder" or "tube" or "cone" or "pyramid";
    private static bool Round(string t) => t is "sphere" or "hemisphere" or "torus" or "ring";
    private static bool Loop(string t) => t is "torus" or "ring";

    private static readonly ShapeSetting[] ShapeSettings =
    [
        new("pathBegin", "Caminho: início", "Corta a base do caminho (altura de uma caixa; polo de baixo de uma esfera, a concavidade)", 0, .95, .01, 0, t => Linear(t) || Round(t)),
        new("pathEnd", "Caminho: fim", "Corta o topo do caminho (altura; polo de cima da esfera; fim do toro)", .05, 1, .01, 1, t => Linear(t) || Round(t)),
        new("cutStart", "Perfil: início do corte", "Remove uma fatia do contorno, começando aqui", 0, .95, .01, 0, t => Linear(t) || Round(t)),
        new("cutEnd", "Perfil: fim do corte", "A fatia removida termina aqui", .05, 1, .01, 1, t => Linear(t) || Round(t)),
        new("shearX", "Cisalhar topo X", "Desliza o topo para o lado em X", -.5, .5, .01, 0, t => Linear(t) || t is "sphere" or "hemisphere"),
        new("shearZ", "Cisalhar topo Z", "Desliza o topo para a frente ou para trás", -.5, .5, .01, 0, t => Linear(t) || t is "sphere" or "hemisphere"),
        new("holeX", "Tamanho do furo X", "Tamanho do furo no eixo X (no toro e no anel, a espessura do perfil)", 0, .95, .01, 0, t => Linear(t) || Loop(t)),
        new("holeY", "Tamanho do furo Y", "Tamanho do furo na profundidade (no toro e no anel, a altura do perfil)", 0, .95, .01, 0, t => Linear(t) || Loop(t)),
        new("radiusOffset", "Deslocar raio", "O raio do toro cresce ou diminui ao longo do caminho", -.9, .9, .01, 0, Loop),
        new("revolutions", "Revoluções", "Voltas do caminho: mais de uma faz uma espiral", 1, 3, .1, 1, Loop),
        new("skew", "Inclinação", "Quanto o fim do caminho sobe em relação ao começo (espiral)", -.95, .95, .01, 0, Loop)
    ];

    private readonly Dictionary<string, HSlider> _shapeSliders = [];

    private PanelContainer BuildAdvancedShapeCard()
    {
        var card = Card("Forma avançada", "layers", out var body, out _, 10);
        body.AddChild(Txt("Cada controle muda a malha de verdade. Os que a forma escolhida não usa ficam desabilitados.", 17, false, UiStyle.TextMuted));
        foreach (var setting in ShapeSettings)
        {
            var slider = NewSlider(setting.Min, setting.Max, setting.Step, setting.Default); slider.TooltipText = setting.Tip; _shapeSliders[setting.Key] = slider;
            body.AddChild(SliderLine(null, setting.Label, slider, 2, false, 190));
        }
        return card;
    }

    private static string PrimitiveOf(IReadOnlyList<BuildObjectView> selection) => selection.Count > 0 && selection.All(v => v.State.PrimitiveType == selection[0].State.PrimitiveType) ? selection[0].State.PrimitiveType.ToLowerInvariant() : "";

    private double ShapeDefault(ShapeSetting setting, string type)
    {
        if (setting.Key is "holeX" or "holeY" && Loop(type)) return setting.Key == "holeX" ? (type == "ring" ? .5 : .3) : (type == "ring" ? .2 : .3);
        return setting.Default;
    }

    /// Shows the geometry of the selected object in the advanced sliders and disables the ones its shape does not use.
    private void FillShape(JsonObject geometry)
    {
        if (_shapeSliders.Count == 0) return;
        var type = PrimitiveOf(_selection);
        foreach (var setting in ShapeSettings)
        {
            var slider = _shapeSliders[setting.Key];
            slider.Editable = type.Length > 0 && setting.Applies(type) && !_readOnly;
            slider.MinValue = Loop(type) && setting.Key is "holeX" or "holeY" ? .05 : setting.Min;
            slider.SetValueNoSignal(Read(geometry, setting.Key, (float)ShapeDefault(setting, type)));
        }
    }

    /// Writes the sliders the shape uses into the geometry JSON; a value left at its default is not stored, so old objects stay as they were.
    private void WriteShape(JsonObject node, string type)
    {
        foreach (var setting in ShapeSettings)
        {
            if (!setting.Applies(type)) continue;
            var value = Math.Round(_shapeSliders[setting.Key].Value, 3);
            if (Math.Abs(value - ShapeDefault(setting, type)) < 1e-6 && !node.ContainsKey(setting.Key)) continue;
            if (Math.Abs(value - ShapeDefault(setting, type)) < 1e-6 && setting.Key is "holeX" or "holeY") { node.Remove(setting.Key); continue; }
            node[setting.Key] = value;
        }
    }

    public void SetShapeForTesting(string key, double value) { _shapeSliders[key].Value = value; }
    public bool ShapeEnabledForTesting(string key) => _shapeSliders[key].Editable;
    public void ApplyForTesting() => ApplySelected();
}
