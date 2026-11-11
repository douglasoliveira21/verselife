using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// The shape of an object: which parametric controls each primitive has (a control that would do nothing is not offered), how the values are stored in the object's geometry
/// JSON, changing the kind of primitive without losing what still fits, and the radius. Changing a value asks the server for an ordinary update; every client then rebuilds
/// the real mesh (with normals, UVs, tangents, bounds and collision) from the stored values, so nothing here is a picture of the change.
/// </summary>
public partial class BuildGeometryManager : Node
{
    /// <summary>One shape control. <c>Display</c> is what the number box shows per unit stored (the hollow is stored 0 to 0.95 and shown as a percentage).</summary>
    public sealed record Param(string Key, string Group, string Label, double Min, double Max, double Step, double Default, double Display, string Tip, Func<string, bool> Applies);

    private static readonly string[] Profiled = ["cube", "prism", "cylinder", "tube", "cone", "pyramid"];
    private static readonly string[] Rounded = ["sphere", "hemisphere"];
    private static readonly string[] Rings = ["torus", "ring"];
    private static bool Any(string type) => Profiled.Contains(type) || Rounded.Contains(type) || Rings.Contains(type);
    private static bool Not(string type, params string[][] sets) => !sets.Any(s => s.Contains(type));

    public static readonly IReadOnlyList<Param> Params =
    [
        new("pathBegin", "Corte do caminho", "Início (B)", 0, .95, .01, 0, 1, "Onde o caminho começa: corta a forma a partir da base", Any),
        new("pathEnd", "Corte do caminho", "Fim (E)", .05, 1, .01, 1, 1, "Onde o caminho termina: corta a forma a partir do topo", Any),
        new("hollow", "Oco", "Oco (%)", 0, .95, .01, 0, 100, "Tamanho do vazio por dentro, em % do contorno", Any),
        new("holeX", "Oco", "Furo X", 0, .95, .01, 0, 1, "Tamanho do furo no eixo X (sozinho, deixa o furo assimétrico)", t => Profiled.Contains(t)),
        new("holeY", "Oco", "Furo Y", 0, .95, .01, 0, 1, "Tamanho do furo no eixo Z (sozinho, deixa o furo assimétrico)", t => Profiled.Contains(t)),
        new("holeX", "Perfil do anel", "Espessura do perfil", .05, 1, .01, .3, 1, "A largura da seção do toroide ou do anel", t => Rings.Contains(t)),
        new("holeY", "Perfil do anel", "Altura do perfil", .05, 1, .01, .3, 1, "A altura da seção do toroide ou do anel", t => Rings.Contains(t)),
        new("twistBegin", "Torção", "Início", -180, 180, 1, 0, 1, "Giro da base, em graus", Any),
        new("twist", "Torção", "Fim", -180, 180, 1, 0, 1, "Giro do topo, em graus; o giro se reparte entre a base e o topo", Any),
        new("taperX", "Afunilamento", "Eixo X", -1, 1, .01, 0, 1, "Quanto o topo encolhe (positivo) ou cresce (negativo) em X", Any),
        new("taperY", "Afunilamento", "Eixo Y", -1, 1, .01, 0, 1, "O mesmo no outro eixo horizontal (Z)", Any),
        new("shearX", "Assimetria", "Eixo X", -.5, .5, .01, 0, 1, "Quanto o topo desliza para o lado em X", t => Not(t, Rings)),
        new("shearZ", "Assimetria", "Eixo Z", -.5, .5, .01, 0, 1, "Quanto o topo desliza para o lado em Z", t => Not(t, Rings)),
        new("cutStart", "Corte do perfil", "Início", 0, .95, .01, 0, 1, "Onde o contorno começa: abre uma fatia", Any),
        new("cutEnd", "Corte do perfil", "Fim", .05, 1, .01, 1, 1, "Onde o contorno termina", Any),
        new("radiusOffset", "Anel", "Variação do raio", -.9, .9, .01, 0, 1, "Quanto o raio cresce ou encolhe ao longo do caminho", t => Rings.Contains(t)),
        new("revolutions", "Anel", "Voltas", 1, 3, .1, 1, 1, "Voltas do caminho", t => Rings.Contains(t)),
        new("skew", "Anel", "Inclinação", -.95, .95, .01, 0, 1, "Quanto o fim do caminho sobe em relação ao começo (uma espiral, com mais de uma volta)", t => Rings.Contains(t))
    ];

    /// <summary>Round shapes have a radius: half of their width. Everything else is sized by its sides.</summary>
    public static bool HasRadius(string type) => type is "sphere" or "hemisphere" or "cylinder" or "cone" or "tube" or "torus" or "ring";
    public static bool IsParametric(string type) => Any(type);

    public static IEnumerable<Param> For(string type) => Params.Where(p => p.Applies(type.ToLowerInvariant()));

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }

    // ---- reading and writing the stored values ---------------------------------------------------------------------------

    private static JsonObject Parse(string json) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; } catch (JsonException) { return []; } }

    /// <summary>The value of a control for an object, as stored (a control the object never touched has its default; a hole follows the hollow until it is set).</summary>
    public static double StoredValue(BuildObjectState state, Param param)
    {
        var geometry = Parse(state.GeometryJson);
        if (geometry[param.Key] is JsonValue v && v.TryGetValue<double>(out var value) && !(param.Key is "holeX" or "holeY" && value < 0)) return value; // a hole of -1 means "same as the hollow"
        if (param.Key is "holeX" or "holeY" && Profiled.Contains(state.PrimitiveType)) return geometry["hollow"] is JsonValue h && h.TryGetValue<double>(out var hollow) ? hollow : 0;
        if (param.Key == "holeX" && Rings.Contains(state.PrimitiveType)) return state.PrimitiveType == "ring" ? .5 : .3;
        if (param.Key == "holeY" && Rings.Contains(state.PrimitiveType)) return state.PrimitiveType == "ring" ? .2 : .3;
        return param.Default;
    }

    /// <summary>The geometry JSON with the changed values written (only controls that fit the shape, each inside its range, with a start always before its end).</summary>
    public static string Write(string geometryJson, string type, IReadOnlyDictionary<string, double> changes)
    {
        var geometry = Parse(geometryJson); var fit = For(type).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
        foreach (var (key, raw) in changes)
        {
            if (!fit.TryGetValue(key, out var param)) continue;
            var value = Math.Round(Math.Clamp(raw, param.Min, param.Max), 3);
            if (Math.Abs(value - param.Default) < 1e-9 && key is not ("holeX" or "holeY")) geometry.Remove(key); else geometry[key] = value;
        }
        // a path or a cut needs at least a twentieth of its length left
        foreach (var (begin, end) in new[] { ("pathBegin", "pathEnd"), ("cutStart", "cutEnd") })
        {
            var b = geometry[begin] is JsonValue bv && bv.TryGetValue<double>(out var bd) ? bd : 0; var e = geometry[end] is JsonValue ev && ev.TryGetValue<double>(out var ed) ? ed : 1;
            if (e - b >= .05 - 1e-9) continue;
            if (changes.ContainsKey(end) && !changes.ContainsKey(begin)) geometry[begin] = Math.Round(Math.Max(0, e - .05), 3); else geometry[end] = Math.Round(Math.Min(1, b + .05), 3);
        }
        return geometry.ToJsonString();
    }

    /// <summary>The names of the controls that have a value of the person's and would be lost on the new shape.</summary>
    public static IReadOnlyList<string> Discarded(string geometryJson, string oldType, string newType)
    {
        var geometry = Parse(geometryJson); var keep = For(newType).Select(p => p.Key).ToHashSet();
        return [.. For(oldType).Where(p => !keep.Contains(p.Key) && geometry[p.Key] is not null).Select(p => $"{p.Group}: {p.Label}").Distinct()];
    }

    /// <summary>The geometry kept when the shape changes: what still means something on the new shape.</summary>
    public static string Keep(string geometryJson, string newType)
    {
        var geometry = Parse(geometryJson); var keep = For(newType).Select(p => p.Key).ToHashSet(); var result = new JsonObject();
        foreach (var (key, value) in geometry) if (keep.Contains(key) && value is not null) result[key] = value.DeepClone();
        return result.ToJsonString();
    }

    // ---- sending the changes -----------------------------------------------------------------------------------------------

    /// <summary>Changes controls on the whole selection (a control that does not fit an object's shape is skipped for it). Returns how many objects were sent.</summary>
    public int Set(IReadOnlyDictionary<string, double> changes)
    {
        var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            if (!IsParametric(view.State.PrimitiveType)) continue;
            var after = BuildPersistenceManager.Clone(view.State); after.GeometryJson = Write(view.State.GeometryJson, view.State.PrimitiveType, changes);
            if (after.GeometryJson == view.State.GeometryJson || JsonNode.DeepEquals(Parse(after.GeometryJson), Parse(view.State.GeometryJson))) continue;
            if (_sync.Update(after)) sent++;
        }
        return sent;
    }

    /// <summary>Puts every control of the selected shapes back to its default.</summary>
    public int Reset()
    {
        var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            if (!IsParametric(view.State.PrimitiveType) || Parse(view.State.GeometryJson).Count == 0) continue;
            var after = BuildPersistenceManager.Clone(view.State); after.GeometryJson = "{}"; if (_sync.Update(after)) sent++;
        }
        return sent;
    }

    /// <summary>Changes the kind of primitive of the selection, keeping size, material, physics and the controls that still fit. Returns how many were sent.</summary>
    public int ChangeType(string newType)
    {
        var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            var old = view.State.PrimitiveType; if (!IsParametric(old) || old == newType || !IsParametric(newType)) continue;
            var after = BuildPersistenceManager.Clone(view.State); after.PrimitiveType = newType; after.GeometryJson = Keep(view.State.GeometryJson, newType);
            if (_sync.Update(after)) sent++;
        }
        return sent;
    }

    /// <summary>Sets the radius of the selected round shapes: half of the width (the height of a sphere follows).</summary>
    public int SetRadius(double radius)
    {
        var diameter = (float)Math.Clamp(radius * 2, BuildTransformManager.MinScale, BuildTransformManager.MaxScale); var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            if (!HasRadius(view.State.PrimitiveType)) continue;
            var after = BuildPersistenceManager.Clone(view.State); after.ScaleX = diameter; after.ScaleZ = diameter; if (view.State.PrimitiveType == "sphere") after.ScaleY = diameter;
            if (_sync.Update(after)) sent++;
        }
        return sent;
    }

    public static double RadiusOf(BuildObjectState state) => Math.Max(state.ScaleX, state.ScaleZ) / 2.0;
    public static string Format(double value, int decimals = 2) => value.ToString("0." + new string('0', decimals), CultureInfo.GetCultureInfo("pt-BR"));
}
