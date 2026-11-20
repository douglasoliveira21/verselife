using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace LifeVerse.Unity.Models;

public sealed class ColladaException(string message) : Exception(message);

/// <summary>
/// Turns a COLLADA (.dae) model into one self-contained GLB, which is what the game stores and the server checks. Everything the model carries goes over:
/// meshes (triangles, polylists and polygons, with normals and texture coordinates), materials and their textures, the node hierarchy with its transforms,
/// the up axis and the unit of measure, skeletons with skin weights, and animations (of matrices as well as of single components).
/// It does not read any file by itself: the textures come from <c>readImage</c>, which the caller restricts to the folder the person chose.
/// </summary>
public static class ColladaConverter
{
    public static bool TryConvert(byte[] source, Func<string, byte[]?> readImage, out byte[] glb, out string? error, out IReadOnlyList<string> warnings)
    {
        glb = []; error = null;
        var notes = new List<string>(); warnings = notes;
        try
        {
            // a model file never needs a DTD or an external entity: refusing both closes the usual XML tricks
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
            XDocument document;
            using (var stream = new MemoryStream(source))
            using (var reader = XmlReader.Create(stream, settings)) document = XDocument.Load(reader);
            glb = new ColladaDocumentConverter(document, readImage, notes).Run();
            return true;
        }
        catch (ColladaException problem) { error = problem.Message; return false; }
        catch (XmlException) { error = "O arquivo não é um COLLADA (.dae) válido."; return false; }
        catch (Exception exception) when (exception is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException or InvalidCastException or NullReferenceException or IOException)
        { error = "O arquivo COLLADA está incompleto ou fora do padrão: " + exception.Message; return false; }
    }
}

internal sealed class ColladaDocumentConverter
{
    private const int MaxBytes = 100 * 1024 * 1024;
    private const int MaxKeysPerNode = 4000;

    // ---- what the file holds -------------------------------------------------------------------------------------------------------------
    private sealed class Source { public double[] Floats = []; public string[] Names = []; public int Stride = 1; }
    private sealed record PrimInput(string Semantic, int Offset, int Set, string Source);
    private sealed class Transform { public string Kind = ""; public string? Sid; public double[] Values = []; }
    private sealed class Instance { public string Kind = ""; public string TargetId = ""; public Dictionary<string, string> Bindings = []; }
    private sealed class PrimitiveData
    {
        public string? MaterialSymbol;
        public readonly List<float> Positions = [];
        public readonly List<float> Normals = [];
        public readonly List<float> Uvs = [];
        public readonly List<int> PositionIndex = [];
        public readonly List<int> Indices = [];
        public bool HasUvs;
    }
    private sealed class SkinData { public int Gltf; public int[] JointIds = []; public float[] Weights = []; }
    private sealed class SceneNode
    {
        public string Name = "Node"; public string? Id; public string? Sid;
        public readonly List<Transform> Transforms = [];
        public readonly List<SceneNode> Children = [];
        public readonly List<Instance> Instances = [];
        public int Index = -1; public int Mesh = -1; public int Skin = -1;
        public double[]? FixedLocal;                                   // the wrapper that turns the up axis and applies the unit
    }
    private sealed class Track { public SceneNode Node = new(); public Transform Element = new(); public int[] Slots = []; public double[] Times = []; public double[] Values = []; public int Stride = 1; }

    private readonly XDocument _document;
    private readonly Func<string, byte[]?> _readImage;
    private readonly List<string> _notes;
    private readonly Dictionary<string, XElement> _byId = [];
    private readonly Dictionary<string, SceneNode> _nodesById = [], _nodesBySid = [], _nodesByName = [];
    private readonly Dictionary<string, Source> _sources = [];
    private readonly Dictionary<string, List<PrimitiveData>> _geometries = [];
    private readonly Dictionary<string, int> _materials = [], _textures = [], _meshes = [];
    private readonly Dictionary<string, SkinData?> _skins = [];
    private readonly List<byte> _binary = [];
    private readonly JsonArray _bufferViews = new(), _accessors = new(), _gltfMaterials = new(), _gltfImages = new(), _gltfTextures = new(), _gltfMeshes = new(), _gltfSkins = new();

    public ColladaDocumentConverter(XDocument document, Func<string, byte[]?> readImage, List<string> notes)
    {
        _document = document; _readImage = readImage; _notes = notes;
    }

    // ---- small helpers -------------------------------------------------------------------------------------------------------------------
    private static IEnumerable<XElement> Kids(XElement? parent, string name) => parent is null ? Enumerable.Empty<XElement>() : parent.Elements().Where(e => e.Name.LocalName == name);
    private static XElement? Kid(XElement? parent, string name) => Kids(parent, name).FirstOrDefault();
    private static string? Attr(XElement? element, string name) => (string?)element?.Attribute(name);
    private static string Ref(string? url) => url is { Length: > 0 } && url[0] == '#' ? url[1..] : url ?? string.Empty;
    private static int ParseInt(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
    private static PrimInput ParseInput(XElement input) => new(Attr(input, "semantic") ?? string.Empty, ParseInt(Attr(input, "offset")), ParseInt(Attr(input, "set")), Ref(Attr(input, "source")));

    private static double[] Doubles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++) result[i] = double.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
        return result;
    }

    private static int[] Ints(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++) result[i] = int.Parse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture);
        return result;
    }

    private static JsonArray Numbers(IEnumerable<double> values) { var array = new JsonArray(); foreach (var value in values) array.Add(Finite(value)); return array; }

    // ---- matrices: 16 numbers, row by row, applied to column vectors (how COLLADA writes them) --------------------------------------------
    private static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Mul(double[] a, double[] b)
    {
        var result = new double[16];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++) sum += a[r * 4 + k] * b[k * 4 + c];
                result[r * 4 + c] = sum;
            }
        return result;
    }

    private static double[] TranslateMatrix(double[] v) { var m = Identity(); m[3] = v[0]; m[7] = v[1]; m[11] = v[2]; return m; }
    private static double[] ScaleMatrix(double[] v) { var m = Identity(); m[0] = v[0]; m[5] = v[1]; m[10] = v[2]; return m; }

    private static double[] RotateMatrix(double[] v)
    {
        var length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (length < 1e-12) return Identity();
        double x = v[0] / length, y = v[1] / length, z = v[2] / length;
        var angle = v[3] * Math.PI / 180.0; var c = Math.Cos(angle); var s = Math.Sin(angle); var k = 1 - c;
        return [c + x * x * k, x * y * k - z * s, x * z * k + y * s, 0, x * y * k + z * s, c + y * y * k, y * z * k - x * s, 0, x * z * k - y * s, y * z * k + x * s, c + z * z * k, 0, 0, 0, 0, 1];
    }

    private static double[] MatrixOf(Transform transform, double[] values) => transform.Kind switch { "matrix" => values, "translate" => TranslateMatrix(values), "rotate" => RotateMatrix(values), _ => ScaleMatrix(values) };

    private static double[] LocalMatrix(SceneNode node, Dictionary<Transform, double[]>? overrides)
    {
        if (node.FixedLocal is { } fixedLocal) return fixedLocal;
        var result = Identity();
        foreach (var transform in node.Transforms)
        {
            var values = overrides is not null && overrides.TryGetValue(transform, out var changed) ? changed : transform.Values;
            result = Mul(result, MatrixOf(transform, values));
        }
        return result;
    }

    private static (double[] T, double[] Q, double[] S) Decompose(double[] m)
    {
        var t = new[] { m[3], m[7], m[11] };
        var sx = Math.Sqrt(m[0] * m[0] + m[4] * m[4] + m[8] * m[8]);
        var sy = Math.Sqrt(m[1] * m[1] + m[5] * m[5] + m[9] * m[9]);
        var sz = Math.Sqrt(m[2] * m[2] + m[6] * m[6] + m[10] * m[10]);
        var determinant = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
        if (determinant < 0) sx = -sx;
        var ix = Math.Abs(sx) < 1e-12 ? 0 : 1 / sx; var iy = Math.Abs(sy) < 1e-12 ? 0 : 1 / sy; var iz = Math.Abs(sz) < 1e-12 ? 0 : 1 / sz;
        double r00 = m[0] * ix, r10 = m[4] * ix, r20 = m[8] * ix, r01 = m[1] * iy, r11 = m[5] * iy, r21 = m[9] * iy, r02 = m[2] * iz, r12 = m[6] * iz, r22 = m[10] * iz;
        double x, y, z, w; var trace = r00 + r11 + r22;
        if (trace > 0) { var s = Math.Sqrt(trace + 1) * 2; w = .25 * s; x = (r21 - r12) / s; y = (r02 - r20) / s; z = (r10 - r01) / s; }
        else if (r00 > r11 && r00 > r22) { var s = Math.Sqrt(1 + r00 - r11 - r22) * 2; w = (r21 - r12) / s; x = .25 * s; y = (r01 + r10) / s; z = (r02 + r20) / s; }
        else if (r11 > r22) { var s = Math.Sqrt(1 + r11 - r00 - r22) * 2; w = (r02 - r20) / s; x = (r01 + r10) / s; y = .25 * s; z = (r12 + r21) / s; }
        else { var s = Math.Sqrt(1 + r22 - r00 - r11) * 2; w = (r10 - r01) / s; x = (r02 + r20) / s; y = (r12 + r21) / s; z = .25 * s; }
        var norm = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (norm < 1e-12) { x = 0; y = 0; z = 0; w = 1; norm = 1; }
        return (t, [x / norm, y / norm, z / norm, w / norm], [sx, sy, sz]);
    }

    // ---- the whole job -------------------------------------------------------------------------------------------------------------------
    public byte[] Run()
    {
        var root = _document.Root;
        if (root is null || root.Name.LocalName != "COLLADA") throw new ColladaException("O arquivo não é um COLLADA (.dae) válido.");
        foreach (var element in root.Descendants()) if (Attr(element, "id") is { Length: > 0 } id) _byId.TryAdd(id, element);

        var scene = FindVisualScene(root) ?? throw new ColladaException("O COLLADA não tem uma cena visual para importar.");
        var roots = Kids(scene, "node").Select(node => ParseNode(node, 0)).ToList();

        // the up axis and the unit of measure of the file turn into one node above everything
        var asset = Kid(root, "asset");
        var upAxis = Kid(asset, "up_axis")?.Value.Trim().ToUpperInvariant() ?? "Y_UP";
        var meter = Doubles(Attr(Kid(asset, "unit"), "meter")); var unit = meter.Length > 0 && meter[0] > 0 && double.IsFinite(meter[0]) ? meter[0] : 1.0;
        var turn = upAxis switch
        {
            "Z_UP" => new[] { 1.0, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1 },
            "X_UP" => new[] { 0.0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 },
            _ => Identity()
        };
        var topLevel = roots;
        if (upAxis is "Z_UP" or "X_UP" || Math.Abs(unit - 1.0) > 1e-9)
        {
            var wrapper = new SceneNode { Name = "COLLADA_root", FixedLocal = Mul(ScaleMatrix([unit, unit, unit]), turn) };
            wrapper.Children.AddRange(roots); topLevel = [wrapper];
        }

        var ordered = new List<SceneNode>();
        void Number(SceneNode node) { node.Index = ordered.Count; ordered.Add(node); foreach (var child in node.Children) Number(child); }
        foreach (var top in topLevel) Number(top);

        foreach (var node in ordered) if (node.Instances.Count == 1) AttachInstance(node, node.Instances[0]);
        if (!ordered.Any(node => node.Mesh >= 0)) throw new ColladaException("O COLLADA não tem nenhuma malha na cena (só são aceitas malhas de polígonos).");

        var animations = BuildAnimations(root, ordered);
        return WriteGlb(ordered, topLevel, animations);
    }

    private XElement? FindVisualScene(XElement root)
    {
        var instance = Kid(Kid(root, "scene"), "instance_visual_scene");
        if (instance is not null && _byId.TryGetValue(Ref(Attr(instance, "url")), out var chosen)) return chosen;
        return Kid(Kid(root, "library_visual_scenes"), "visual_scene");
    }

    // ---- nodes ---------------------------------------------------------------------------------------------------------------------------
    private SceneNode ParseNode(XElement element, int depth)
    {
        if (depth > 64) throw new ColladaException("A hierarquia de nós do COLLADA é profunda demais.");
        var node = new SceneNode { Id = Attr(element, "id"), Sid = Attr(element, "sid") };
        node.Name = Attr(element, "name") ?? node.Id ?? node.Sid ?? "Node";
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "matrix": node.Transforms.Add(Numeric(child, "matrix", 16, Identity())); break;
                case "translate": node.Transforms.Add(Numeric(child, "translate", 3, [0, 0, 0])); break;
                case "rotate": node.Transforms.Add(Numeric(child, "rotate", 4, [0, 0, 1, 0])); break;
                case "scale": node.Transforms.Add(Numeric(child, "scale", 3, [1, 1, 1])); break;
                case "node": node.Children.Add(ParseNode(child, depth + 1)); break;
                case "instance_geometry": node.Instances.Add(ParseInstance(child, "geometry")); break;
                case "instance_controller": node.Instances.Add(ParseInstance(child, "controller")); break;
                case "instance_node":
                    if (_byId.TryGetValue(Ref(Attr(child, "url")), out var target)) node.Children.Add(ParseNode(target, depth + 1));
                    break;
            }
        }
        if (node.Id is { } id) _nodesById.TryAdd(id, node);
        if (node.Sid is { } sid) _nodesBySid.TryAdd(sid, node);
        _nodesByName.TryAdd(node.Name, node);
        // one mesh per node in glTF: a node that shows several gets a child for each
        if (node.Instances.Count > 1)
        {
            foreach (var instance in node.Instances) { var holder = new SceneNode { Name = node.Name + "_" + instance.TargetId }; holder.Instances.Add(instance); node.Children.Add(holder); }
            node.Instances.Clear();
        }
        return node;
    }

    private static Transform Numeric(XElement element, string kind, int length, double[] fallback)
    {
        var values = Doubles(element.Value);
        return new Transform { Kind = kind, Sid = Attr(element, "sid"), Values = values.Length >= length ? values[..length] : fallback };
    }

    private static Instance ParseInstance(XElement element, string kind)
    {
        var instance = new Instance { Kind = kind, TargetId = Ref(Attr(element, "url")) };
        var technique = Kid(Kid(element, "bind_material"), "technique_common");
        foreach (var bound in Kids(technique, "instance_material"))
            if (Attr(bound, "symbol") is { } symbol) instance.Bindings[symbol] = Ref(Attr(bound, "target"));
        return instance;
    }

    private void AttachInstance(SceneNode node, Instance instance)
    {
        var bindings = string.Join(";", instance.Bindings.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
        var key = instance.Kind + ":" + instance.TargetId + ":" + bindings;
        if (instance.Kind == "controller")
        {
            if (!_byId.TryGetValue(instance.TargetId, out var controller)) { _notes.Add("Um controlador do COLLADA não foi encontrado: " + instance.TargetId); return; }
            var geometryId = Ref(Attr(Kid(controller, "skin"), "source"));
            if (geometryId.Length == 0) { _notes.Add("Controlador sem pele (morph targets não são importados): " + instance.TargetId); return; }
            var skin = SkinFor(instance.TargetId, controller);
            if (!_meshes.TryGetValue(key, out var meshIndex)) { meshIndex = BuildMesh(node.Name, geometryId, instance.Bindings, skin); _meshes[key] = meshIndex; }
            if (meshIndex >= 0) { node.Mesh = meshIndex; if (skin is not null) node.Skin = skin.Gltf; }
            return;
        }
        if (!_meshes.TryGetValue(key, out var plain)) { plain = BuildMesh(node.Name, instance.TargetId, instance.Bindings, null); _meshes[key] = plain; }
        if (plain >= 0) node.Mesh = plain;
    }

    // ---- geometry ------------------------------------------------------------------------------------------------------------------------
    private Source? SourceOf(string id)
    {
        if (_sources.TryGetValue(id, out var known)) return known;
        if (!_byId.TryGetValue(id, out var element) || element.Name.LocalName != "source") return null;
        var source = new Source();
        var floats = Kid(element, "float_array"); var names = Kid(element, "Name_array") ?? Kid(element, "IDREF_array");
        if (floats is not null) source.Floats = Doubles(floats.Value);
        if (names is not null) source.Names = names.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var stride = ParseInt(Attr(Kid(Kid(element, "technique_common"), "accessor"), "stride"));
        if (stride > 0) source.Stride = stride;
        _sources[id] = source; return source;
    }

    private static float Value(Source source, int element, int component)
    {
        var at = element * source.Stride + component;
        if (element < 0 || component >= source.Stride || at < 0 || at >= source.Floats.Length) throw new ColladaException("O COLLADA aponta para dados que não existem (índice fora do intervalo).");
        return (float)source.Floats[at];
    }

    private List<PrimitiveData> Geometry(string geometryId)
    {
        if (_geometries.TryGetValue(geometryId, out var known)) return known;
        var result = new List<PrimitiveData>(); _geometries[geometryId] = result;
        if (!_byId.TryGetValue(geometryId, out var geometry)) { _notes.Add("Geometria não encontrada: " + geometryId); return result; }
        var mesh = Kid(geometry, "mesh");
        if (mesh is null) { _notes.Add("A geometria \"" + geometryId + "\" não é uma malha de polígonos (curvas não são importadas)."); return result; }
        foreach (var primitive in mesh.Elements())
        {
            var kind = primitive.Name.LocalName;
            if (kind is "triangles" or "polylist" or "polygons") { var data = ParsePrimitive(mesh, primitive); if (data is not null && data.Indices.Count > 0) result.Add(data); }   // (the mesh is where the "vertices" element is)
            else if (kind is "trifans" or "tristrips" or "lines" or "linestrips") _notes.Add("Primitiva \"" + kind + "\" ignorada (só triângulos, polylist e polygons).");
        }
        return result;
    }

    private PrimitiveData? ParsePrimitive(XElement mesh, XElement primitive)
    {
        var inputs = Kids(primitive, "input").Select(ParseInput).ToList();
        if (inputs.Count == 0) return null;
        var stride = inputs.Max(input => input.Offset) + 1;
        var vertexInput = inputs.FirstOrDefault(input => input.Semantic == "VERTEX");
        if (vertexInput is null || !_byId.TryGetValue(vertexInput.Source, out var vertices)) return null;

        Source? positions = null, normals = null, uvs = null; int normalOffset = -1, uvOffset = -1;
        foreach (var input in Kids(vertices, "input"))
        {
            var source = SourceOf(Ref(Attr(input, "source")));
            switch (Attr(input, "semantic"))
            {
                case "POSITION": positions = source; break;
                case "NORMAL": normals = source; normalOffset = vertexInput.Offset; break;
                case "TEXCOORD": if (uvs is null) { uvs = source; uvOffset = vertexInput.Offset; } break;
            }
        }
        foreach (var input in inputs.Where(candidate => candidate.Semantic == "NORMAL")) { normals = SourceOf(input.Source); normalOffset = input.Offset; }
        var uvInput = inputs.Where(candidate => candidate.Semantic == "TEXCOORD").OrderBy(candidate => candidate.Set).FirstOrDefault();
        if (uvInput is not null) { uvs = SourceOf(uvInput.Source); uvOffset = uvInput.Offset; }
        if (positions is null || positions.Stride < 3) return null;
        if (normals is not null && normals.Stride < 3) { normals = null; normalOffset = -1; }
        if (uvs is not null && uvs.Stride < 2) { uvs = null; uvOffset = -1; }
        Source positionSource = positions; var vertexOffset = vertexInput.Offset; var normalSource = normals; var uvSource = uvs;   // plain copies for the local function below

        var polygons = new List<(int[] Data, int Start, int Corners)>();
        switch (primitive.Name.LocalName)
        {
            case "triangles":
            {
                var data = Ints(Kid(primitive, "p")?.Value);
                for (var corner = 0; (corner + 3) * stride <= data.Length; corner += 3) polygons.Add((data, corner * stride, 3));
                break;
            }
            case "polylist":
            {
                var data = Ints(Kid(primitive, "p")?.Value); var counts = Ints(Kid(primitive, "vcount")?.Value); var corner = 0;
                foreach (var count in counts)
                {
                    if ((corner + count) * stride > data.Length) break;
                    if (count >= 3) polygons.Add((data, corner * stride, count));
                    corner += count;
                }
                break;
            }
            default:
                foreach (var element in Kids(primitive, "p"))
                {
                    var data = Ints(element.Value); var count = data.Length / stride;
                    if (count >= 3) polygons.Add((data, 0, count));
                }
                break;
        }

        var result = new PrimitiveData { MaterialSymbol = Attr(primitive, "material"), HasUvs = uvs is not null };
        var known = new Dictionary<(int, int, int), int>();
        int Emit(int[] corners, int at)
        {
            var position = corners[at + vertexOffset];
            var normal = normalSource is null ? -1 : corners[at + normalOffset];
            var uv = uvSource is null ? -1 : corners[at + uvOffset];
            if (known.TryGetValue((position, normal, uv), out var found)) return found;
            var index = result.PositionIndex.Count;
            for (var axis = 0; axis < 3; axis++) result.Positions.Add(Value(positionSource, position, axis));
            if (normalSource is not null) for (var axis = 0; axis < 3; axis++) result.Normals.Add(Value(normalSource, normal, axis));
            if (uvSource is not null) { result.Uvs.Add(Value(uvSource, uv, 0)); result.Uvs.Add(1f - Value(uvSource, uv, 1)); }   // COLLADA counts V from the bottom, glTF from the top
            result.PositionIndex.Add(position); known[(position, normal, uv)] = index; return index;
        }
        foreach (var (indices, start, cornerCount) in polygons)
            for (var i = 1; i + 1 < cornerCount; i++)       // a polygon is cut into a fan of triangles
            {
                result.Indices.Add(Emit(indices, start));
                result.Indices.Add(Emit(indices, start + i * stride));
                result.Indices.Add(Emit(indices, start + (i + 1) * stride));
            }
        if (normalSource is null) SmoothNormals(result);
        return result;
    }

    /// <summary>A mesh without normals gets them from its faces (each vertex takes the average of the faces that touch it).</summary>
    private static void SmoothNormals(PrimitiveData data)
    {
        var sums = new double[data.PositionIndex.Count * 3];
        for (var i = 0; i + 2 < data.Indices.Count; i += 3)
        {
            int a = data.Indices[i], b = data.Indices[i + 1], c = data.Indices[i + 2];
            double ux = data.Positions[b * 3] - data.Positions[a * 3], uy = data.Positions[b * 3 + 1] - data.Positions[a * 3 + 1], uz = data.Positions[b * 3 + 2] - data.Positions[a * 3 + 2];
            double vx = data.Positions[c * 3] - data.Positions[a * 3], vy = data.Positions[c * 3 + 1] - data.Positions[a * 3 + 1], vz = data.Positions[c * 3 + 2] - data.Positions[a * 3 + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            foreach (var vertex in new[] { a, b, c }) { sums[vertex * 3] += nx; sums[vertex * 3 + 1] += ny; sums[vertex * 3 + 2] += nz; }
        }
        for (var v = 0; v < data.PositionIndex.Count; v++)
        {
            var length = Math.Sqrt(sums[v * 3] * sums[v * 3] + sums[v * 3 + 1] * sums[v * 3 + 1] + sums[v * 3 + 2] * sums[v * 3 + 2]);
            if (length < 1e-12) { data.Normals.Add(0); data.Normals.Add(1); data.Normals.Add(0); }
            else { data.Normals.Add((float)(sums[v * 3] / length)); data.Normals.Add((float)(sums[v * 3 + 1] / length)); data.Normals.Add((float)(sums[v * 3 + 2] / length)); }
        }
    }

    // ---- skins ---------------------------------------------------------------------------------------------------------------------------
    private SkinData? SkinFor(string controllerId, XElement controller)
    {
        if (_skins.TryGetValue(controllerId, out var known)) return known;
        _skins[controllerId] = null;
        var skin = Kid(controller, "skin");
        var jointsElement = Kid(skin, "joints");
        var jointNames = SourceOf(Ref(Attr(Kids(jointsElement, "input").FirstOrDefault(input => Attr(input, "semantic") == "JOINT"), "source")));
        var inverse = SourceOf(Ref(Attr(Kids(jointsElement, "input").FirstOrDefault(input => Attr(input, "semantic") == "INV_BIND_MATRIX"), "source")));
        if (skin is null || jointNames is null || inverse is null || jointNames.Names.Length == 0 || inverse.Floats.Length < jointNames.Names.Length * 16) { _notes.Add("Esqueleto incompleto no controlador " + controllerId + ": a malha entra sem pele."); return null; }

        var joints = new SceneNode[jointNames.Names.Length];
        for (var i = 0; i < joints.Length; i++)
        {
            var name = jointNames.Names[i];
            var found = _nodesBySid.GetValueOrDefault(name) ?? _nodesById.GetValueOrDefault(name) ?? _nodesByName.GetValueOrDefault(name);
            if (found is null) { _notes.Add("O osso \"" + name + "\" do esqueleto não existe na cena: a malha entra sem pele."); return null; }
            joints[i] = found;
        }
        var bind = Doubles(Kid(skin, "bind_shape_matrix")?.Value); if (bind.Length < 16) bind = Identity();

        var weightsElement = Kid(skin, "vertex_weights");
        var weightInputs = Kids(weightsElement, "input").Select(ParseInput).ToList();
        var jointInput = weightInputs.FirstOrDefault(input => input.Semantic == "JOINT"); var weightInput = weightInputs.FirstOrDefault(input => input.Semantic == "WEIGHT");
        var weightSource = weightInput is null ? null : SourceOf(weightInput.Source);
        if (weightsElement is null || jointInput is null || weightInput is null || weightSource is null) { _notes.Add("Pesos do esqueleto ausentes no controlador " + controllerId + ": a malha entra sem pele."); return null; }
        var vcount = Ints(Kid(weightsElement, "vcount")?.Value); var v = Ints(Kid(weightsElement, "v")?.Value);
        var stride = weightInputs.Max(input => input.Offset) + 1;
        var vertexCount = Math.Min(ParseInt(Attr(weightsElement, "count")), vcount.Length);
        var jointIds = new int[vertexCount * 4]; var weights = new float[vertexCount * 4]; var cursor = 0;
        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var pairs = new List<(int Joint, double Weight)>();
            for (var k = 0; k < vcount[vertex] && cursor + stride <= v.Length; k++, cursor += stride)
            {
                var joint = v[cursor + jointInput.Offset]; var weight = v[cursor + weightInput.Offset];
                if (joint >= 0 && joint < joints.Length && weight >= 0 && weight < weightSource.Floats.Length) pairs.Add((joint, weightSource.Floats[weight]));
            }
            var top = pairs.OrderByDescending(pair => pair.Weight).Take(4).ToList();    // glTF carries four influences per vertex
            var sum = top.Sum(pair => pair.Weight);
            if (top.Count == 0 || sum <= 1e-9) { weights[vertex * 4] = 1; continue; }
            for (var k = 0; k < top.Count; k++) { jointIds[vertex * 4 + k] = top[k].Joint; weights[vertex * 4 + k] = (float)(top[k].Weight / sum); }
        }

        // glTF has no bind shape matrix: it goes into the inverse bind matrices (column by column, which is how glTF writes them)
        var matrices = new float[joints.Length * 16];
        for (var i = 0; i < joints.Length; i++)
        {
            var inv = new double[16]; Array.Copy(inverse.Floats, i * 16, inv, 0, 16);
            var combined = Mul(inv, bind);
            for (var r = 0; r < 4; r++) for (var c = 0; c < 4; c++) matrices[i * 16 + c * 4 + r] = (float)Finite(combined[r * 4 + c]);
        }
        var jointArray = new JsonArray(); foreach (var joint in joints) jointArray.Add(joint.Index);
        _gltfSkins.Add(new JsonObject { ["joints"] = jointArray, ["inverseBindMatrices"] = AddAccessor(ToBytes(matrices), 5126, joints.Length, "MAT4", null) });
        var data = new SkinData { Gltf = _gltfSkins.Count - 1, JointIds = jointIds, Weights = weights };
        _skins[controllerId] = data; return data;
    }

    // ---- materials -----------------------------------------------------------------------------------------------------------------------
    private int? MaterialIndex(string materialId)
    {
        if (_materials.TryGetValue(materialId, out var known)) return known;
        _gltfMaterials.Add(BuildMaterial(materialId));
        var index = _gltfMaterials.Count - 1; _materials[materialId] = index; return index;
    }

    private (double[]? Color, int? Texture) Property(XElement effect, XElement? property)
    {
        if (property is null) return (null, null);
        var colorElement = Kid(property, "color");
        if (colorElement is not null) { var c = Doubles(colorElement.Value); if (c.Length >= 3) return (new double[] { c[0], c[1], c[2], c.Length >= 4 ? c[3] : 1 }, null); }
        var textureElement = Kid(property, "texture");
        if (textureElement is not null && Attr(textureElement, "texture") is { } textureName) return (null, TextureIndex(effect, textureName));
        var scalar = Kid(property, "float");
        if (scalar is not null) { var f = Doubles(scalar.Value); if (f.Length >= 1) return (new double[] { f[0], f[0], f[0], 1 }, null); }
        return (null, null);
    }

    private JsonObject BuildMaterial(string materialId)
    {
        double[] color = [.8, .8, .8, 1]; double[] emissive = [0, 0, 0]; var roughness = .8; var alpha = 1.0; int? texture = null; int? emissiveTexture = null;
        var name = materialId;
        if (_byId.TryGetValue(materialId, out var material))
        {
            name = Attr(material, "name") ?? materialId;
            if (_byId.TryGetValue(Ref(Attr(Kid(material, "instance_effect"), "url")), out var effect))
            {
                var technique = Kid(Kid(effect, "profile_COMMON"), "technique");
                var shader = technique?.Elements().FirstOrDefault(element => element.Name.LocalName is "phong" or "lambert" or "blinn" or "constant");
                if (shader is not null)
                {
                    var constant = shader.Name.LocalName == "constant";
                    var (diffuseColor, diffuseTexture) = Property(effect, Kid(shader, constant ? "emission" : "diffuse"));
                    if (diffuseColor is not null) color = diffuseColor;
                    if (diffuseTexture is { } picture) { texture = picture; color = [1, 1, 1, color[3]]; }
                    if (!constant)
                    {
                        var (emissionColor, emissionTexture) = Property(effect, Kid(shader, "emission"));
                        if (emissionColor is not null && (emissionColor[0] > .001 || emissionColor[1] > .001 || emissionColor[2] > .001)) emissive = [emissionColor[0], emissionColor[1], emissionColor[2]];
                        if (emissionTexture is { } glow) { emissiveTexture = glow; emissive = [1, 1, 1]; }
                    }
                    var shininess = Doubles(Kid(Kid(shader, "shininess"), "float")?.Value);
                    if (shininess.Length > 0 && shininess[0] > 0) roughness = Math.Clamp(1 - shininess[0] / 128.0, .15, 1);
                    var transparency = Doubles(Kid(Kid(shader, "transparency"), "float")?.Value);
                    if (transparency.Length > 0)
                    {
                        var opaque = Attr(Kid(shader, "transparent"), "opaque") ?? "A_ONE";
                        var a = opaque is "RGB_ZERO" or "RGB_ONE" ? 1 - transparency[0] : transparency[0];
                        if (a is > 0.01 and < 0.999) alpha = a;           // a value of 0 or 1 is how exporters say "opaque"
                    }
                    if (alpha >= .999 && color[3] < .999 && color[3] > .01) alpha = color[3];
                }
            }
        }
        var pbr = new JsonObject { ["baseColorFactor"] = Numbers([color[0], color[1], color[2], alpha]), ["metallicFactor"] = 0, ["roughnessFactor"] = roughness };
        if (texture is { } baseTexture) pbr["baseColorTexture"] = new JsonObject { ["index"] = baseTexture };
        var result = new JsonObject { ["name"] = name, ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true };
        if (emissive.Any(channel => channel > .001)) result["emissiveFactor"] = Numbers(emissive);
        if (emissiveTexture is { } glowTexture) result["emissiveTexture"] = new JsonObject { ["index"] = glowTexture };
        if (alpha < .999) result["alphaMode"] = "BLEND";
        return result;
    }

    /// <summary>Follows a texture name of the effect (texture, then sampler, then surface, then image) to the file and loads it.</summary>
    private int? TextureIndex(XElement effect, string textureName)
    {
        string? imageId = textureName;
        var parameter = effect.Descendants().FirstOrDefault(element => element.Name.LocalName == "newparam" && Attr(element, "sid") == textureName);
        if (parameter is not null)
        {
            imageId = null;
            var sampler = Kid(parameter, "sampler2D"); var surface = Kid(parameter, "surface");
            if (sampler is not null)
            {
                var instance = Kid(sampler, "instance_image");
                if (instance is not null) imageId = Ref(Attr(instance, "url"));
                else
                {
                    var surfaceSid = Kid(sampler, "source")?.Value.Trim();
                    var surfaceParameter = effect.Descendants().FirstOrDefault(element => element.Name.LocalName == "newparam" && Attr(element, "sid") == surfaceSid);
                    surface = Kid(surfaceParameter, "surface");
                }
            }
            if (imageId is null && surface is not null) imageId = Kid(surface, "init_from")?.Value.Trim();
        }
        if (imageId is null || !_byId.TryGetValue(imageId, out var image)) return null;
        var initFrom = Kid(image, "init_from");
        var path = (Kid(initFrom, "ref")?.Value ?? initFrom?.Value)?.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        return AddTexture(path);
    }

    private static string? MimeOf(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' ? "image/webp" : null;

    private int? AddTexture(string path)
    {
        if (_textures.TryGetValue(path, out var known)) return known < 0 ? null : known;
        byte[]? bytes = null;
        try { bytes = _readImage(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { bytes = null; }
        var mime = bytes is null ? null : MimeOf(bytes);
        if (bytes is null || mime is null)
        {
            _notes.Add(bytes is null ? "Textura não encontrada ao lado do .dae: " + Path.GetFileName(path.Replace('\\', '/')) : "Textura em formato que o jogo não aceita (use PNG, JPG ou WebP): " + Path.GetFileName(path.Replace('\\', '/')));
            _textures[path] = -1; return null;
        }
        _gltfImages.Add(new JsonObject { ["bufferView"] = AddView(bytes, null), ["mimeType"] = mime });
        _gltfTextures.Add(new JsonObject { ["source"] = _gltfImages.Count - 1 });
        var index = _gltfTextures.Count - 1; _textures[path] = index; return index;
    }

    // ---- meshes in glTF ------------------------------------------------------------------------------------------------------------------
    private int BuildMesh(string name, string geometryId, Dictionary<string, string> bindings, SkinData? skin)
    {
        var primitives = new JsonArray();
        foreach (var data in Geometry(geometryId))
        {
            var attributes = new JsonObject(); var count = data.PositionIndex.Count;
            double[] min = [double.MaxValue, double.MaxValue, double.MaxValue], max = [double.MinValue, double.MinValue, double.MinValue];
            for (var i = 0; i < data.Positions.Count; i++) { min[i % 3] = Math.Min(min[i % 3], data.Positions[i]); max[i % 3] = Math.Max(max[i % 3], data.Positions[i]); }
            attributes["POSITION"] = AddAccessor(ToBytes(data.Positions), 5126, count, "VEC3", 34962, min, max);
            attributes["NORMAL"] = AddAccessor(ToBytes(data.Normals), 5126, count, "VEC3", 34962);
            if (data.HasUvs) attributes["TEXCOORD_0"] = AddAccessor(ToBytes(data.Uvs), 5126, count, "VEC2", 34962);
            if (skin is not null)
            {
                var joints = new byte[count * 8]; var weights = new float[count * 4];
                for (var v = 0; v < count; v++)
                {
                    var source = data.PositionIndex[v];
                    for (var k = 0; k < 4; k++)
                    {
                        var at = source * 4 + k; var ok = at >= 0 && at < skin.JointIds.Length;
                        BinaryPrimitives.WriteUInt16LittleEndian(joints.AsSpan((v * 4 + k) * 2), (ushort)(ok ? skin.JointIds[at] : 0));
                        weights[v * 4 + k] = ok ? skin.Weights[at] : (k == 0 ? 1f : 0f);
                    }
                }
                attributes["JOINTS_0"] = AddAccessor(joints, 5123, count, "VEC4", 34962);
                attributes["WEIGHTS_0"] = AddAccessor(ToBytes(weights), 5126, count, "VEC4", 34962);
            }
            byte[] indexBytes; int indexType;
            if (count <= 65535) { indexType = 5123; indexBytes = new byte[data.Indices.Count * 2]; for (var i = 0; i < data.Indices.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(indexBytes.AsSpan(i * 2), (ushort)data.Indices[i]); }
            else { indexType = 5125; indexBytes = new byte[data.Indices.Count * 4]; for (var i = 0; i < data.Indices.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(indexBytes.AsSpan(i * 4), (uint)data.Indices[i]); }
            var primitive = new JsonObject { ["attributes"] = attributes, ["indices"] = AddAccessor(indexBytes, indexType, data.Indices.Count, "SCALAR", 34963) };
            if (data.MaterialSymbol is { } symbol && bindings.TryGetValue(symbol, out var materialId) && MaterialIndex(materialId) is { } material) primitive["material"] = material;
            primitives.Add(primitive);
        }
        if (primitives.Count == 0) return -1;
        _gltfMeshes.Add(new JsonObject { ["name"] = name, ["primitives"] = primitives });
        return _gltfMeshes.Count - 1;
    }

    // ---- animations ----------------------------------------------------------------------------------------------------------------------
    private JsonObject? BuildAnimations(XElement root, List<SceneNode> ordered)
    {
        var tracks = new List<Track>(); string? name = null;
        void Collect(XElement animation)
        {
            name ??= Attr(animation, "name") ?? Attr(animation, "id");
            foreach (var channel in Kids(animation, "channel"))
            {
                var sampler = Kids(animation, "sampler").FirstOrDefault(candidate => Attr(candidate, "id") == Ref(Attr(channel, "source")));
                var track = sampler is null ? null : TrackOf(Attr(channel, "target") ?? string.Empty, sampler);
                if (track is not null) tracks.Add(track);
            }
            foreach (var nested in Kids(animation, "animation")) Collect(nested);
        }
        foreach (var animation in Kids(Kid(root, "library_animations"), "animation")) Collect(animation);
        if (tracks.Count == 0) return null;

        var samplers = new JsonArray(); var channels = new JsonArray();
        foreach (var group in tracks.GroupBy(track => track.Node))
        {
            var times = group.SelectMany(track => track.Times.Take(Math.Min(track.Times.Length, track.Values.Length / track.Stride))).Where(double.IsFinite).Distinct().OrderBy(time => time).ToList();
            if (times.Count == 0) continue;
            if (times.Count > MaxKeysPerNode) { _notes.Add("A animação de \"" + group.Key.Name + "\" tem chaves demais: só as primeiras " + MaxKeysPerNode + " foram importadas."); times = times.Take(MaxKeysPerNode).ToList(); }
            var translations = new List<float>(); var rotations = new List<float>(); var scales = new List<float>(); double[]? previous = null;
            foreach (var time in times)
            {
                var overrides = new Dictionary<Transform, double[]>();
                foreach (var track in group)
                {
                    if (!overrides.TryGetValue(track.Element, out var values)) { values = (double[])track.Element.Values.Clone(); overrides[track.Element] = values; }
                    for (var s = 0; s < track.Slots.Length; s++) values[track.Slots[s]] = Interpolate(track, s, time);
                }
                var (t, q, sc) = Decompose(LocalMatrix(group.Key, overrides));
                if (previous is not null && previous[0] * q[0] + previous[1] * q[1] + previous[2] * q[2] + previous[3] * q[3] < 0) q = [-q[0], -q[1], -q[2], -q[3]];   // the shorter way round between two keys
                previous = q;
                foreach (var value in t) translations.Add((float)Finite(value));
                foreach (var value in q) rotations.Add((float)Finite(value));
                foreach (var value in sc) scales.Add((float)Finite(value));
            }
            var timeBytes = ToBytes(times.Select(time => (float)time).ToList());
            var input = AddAccessor(timeBytes, 5126, times.Count, "SCALAR", null, [times[0]], [times[^1]]);
            foreach (var (path, type, data) in new[] { ("translation", "VEC3", translations), ("rotation", "VEC4", rotations), ("scale", "VEC3", scales) })
            {
                samplers.Add(new JsonObject { ["input"] = input, ["output"] = AddAccessor(ToBytes(data), 5126, times.Count, type, null), ["interpolation"] = "LINEAR" });
                channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = group.Key.Index, ["path"] = path } });
            }
        }
        if (channels.Count == 0) return null;
        return new JsonObject { ["name"] = name ?? "Animation", ["samplers"] = samplers, ["channels"] = channels };
    }

    private Track? TrackOf(string target, XElement sampler)
    {
        var parts = target.Split('/');
        if (parts.Length < 2) return null;
        var nodeName = parts[^2];
        var node = _nodesById.GetValueOrDefault(nodeName) ?? _nodesBySid.GetValueOrDefault(nodeName) ?? _nodesByName.GetValueOrDefault(nodeName);
        if (node is null) { _notes.Add("Animação para um nó que não existe: " + nodeName); return null; }

        var part = parts[^1]; var sid = part; string? component = null;
        var dot = part.IndexOf('.'); var paren = part.IndexOf('(');
        if (dot >= 0) { sid = part[..dot]; component = part[(dot + 1)..]; }
        else if (paren >= 0) { sid = part[..paren]; component = part[paren..]; }
        var element = node.Transforms.FirstOrDefault(transform => transform.Sid == sid) ?? (node.Transforms.Count == 1 ? node.Transforms[0] : null);
        if (element is null) { _notes.Add("Animação para uma transformação que não existe: " + part); return null; }

        var inputs = Kids(sampler, "input").Select(ParseInput).ToList();
        var timeSource = SourceOf(inputs.FirstOrDefault(input => input.Semantic == "INPUT")?.Source ?? string.Empty);
        var outputSource = SourceOf(inputs.FirstOrDefault(input => input.Semantic == "OUTPUT")?.Source ?? string.Empty);
        if (timeSource is null || outputSource is null || timeSource.Floats.Length == 0) return null;

        int[] slots;
        if (component is null) slots = Enumerable.Range(0, element.Values.Length).ToArray();
        else if (component.StartsWith('('))
        {
            var numbers = component.Split(new[] { '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseInt).ToArray();
            if (numbers.Length >= 2) slots = new[] { numbers[0] * 4 + numbers[1] };
            else if (numbers.Length == 1) slots = new[] { numbers[0] };
            else slots = Array.Empty<int>();
        }
        else slots = new[] { component.ToUpperInvariant() switch { "X" => 0, "Y" => 1, "Z" => 2, "ANGLE" => 3, "W" => 3, _ => -1 } };
        if (slots.Length == 0 || slots.Any(slot => slot < 0 || slot >= element.Values.Length)) { _notes.Add("Animação de uma parte da transformação que não é suportada: " + part); return null; }
        if (outputSource.Stride != slots.Length) { _notes.Add("Animação com dados que não casam com a transformação: " + part); return null; }
        return new Track { Node = node, Element = element, Slots = slots, Times = timeSource.Floats, Values = outputSource.Floats, Stride = outputSource.Stride };
    }

    private static double Interpolate(Track track, int component, double time)
    {
        var count = Math.Min(track.Times.Length, track.Values.Length / track.Stride);
        if (count == 0) return 0;
        if (time <= track.Times[0] || count == 1) return track.Values[component];
        if (time >= track.Times[count - 1]) return track.Values[(count - 1) * track.Stride + component];
        var hi = 1; while (hi < count - 1 && track.Times[hi] < time) hi++;
        var lo = hi - 1; var span = track.Times[hi] - track.Times[lo];
        var fraction = span <= 1e-12 ? 0 : (time - track.Times[lo]) / span;
        var a = track.Values[lo * track.Stride + component]; var b = track.Values[hi * track.Stride + component];
        return a + (b - a) * fraction;
    }

    // ---- the file ------------------------------------------------------------------------------------------------------------------------
    private static byte[] ToBytes(IReadOnlyList<float> values)
    {
        var bytes = new byte[values.Count * 4];
        for (var i = 0; i < values.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), BitConverter.SingleToInt32Bits(values[i]));
        return bytes;
    }

    private int AddView(byte[] data, int? target)
    {
        while (_binary.Count % 4 != 0) _binary.Add(0);
        var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = _binary.Count, ["byteLength"] = data.Length };
        if (target is { } kind) view["target"] = kind;
        _binary.AddRange(data);
        if (_binary.Count > MaxBytes) throw new ColladaException("O modelo convertido ultrapassa o limite de 100 MB.");
        _bufferViews.Add(view);
        return _bufferViews.Count - 1;
    }

    private int AddAccessor(byte[] data, int componentType, int count, string type, int? target, double[]? min = null, double[]? max = null)
    {
        var accessor = new JsonObject { ["bufferView"] = AddView(data, target), ["componentType"] = componentType, ["count"] = count, ["type"] = type };
        if (min is not null && max is not null) { accessor["min"] = Numbers(min); accessor["max"] = Numbers(max); }
        _accessors.Add(accessor);
        return _accessors.Count - 1;
    }

    private byte[] WriteGlb(List<SceneNode> ordered, List<SceneNode> topLevel, JsonObject? animation)
    {
        var nodes = new JsonArray();
        foreach (var node in ordered)
        {
            var json = new JsonObject { ["name"] = node.Name };
            if (node.Children.Count > 0) { var children = new JsonArray(); foreach (var child in node.Children) children.Add(child.Index); json["children"] = children; }
            var (t, q, s) = Decompose(LocalMatrix(node, null));
            if (Math.Abs(t[0]) > 1e-9 || Math.Abs(t[1]) > 1e-9 || Math.Abs(t[2]) > 1e-9) json["translation"] = Numbers(t);
            if (Math.Abs(q[3]) < 1 - 1e-9) json["rotation"] = Numbers(q);
            if (Math.Abs(s[0] - 1) > 1e-9 || Math.Abs(s[1] - 1) > 1e-9 || Math.Abs(s[2] - 1) > 1e-9) json["scale"] = Numbers(s);
            if (node.Mesh >= 0) json["mesh"] = node.Mesh;
            if (node.Skin >= 0) json["skin"] = node.Skin;
            nodes.Add(json);
        }
        var sceneNodes = new JsonArray(); foreach (var top in topLevel) sceneNodes.Add(top.Index);
        while (_binary.Count % 4 != 0) _binary.Add(0);
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "LifeVerse COLLADA importer" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = sceneNodes }),
            ["nodes"] = nodes,
            ["meshes"] = _gltfMeshes,
            ["accessors"] = _accessors,
            ["bufferViews"] = _bufferViews,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = _binary.Count })
        };
        if (_gltfMaterials.Count > 0) root["materials"] = _gltfMaterials;
        if (_gltfTextures.Count > 0) { root["textures"] = _gltfTextures; root["images"] = _gltfImages; }
        if (_gltfSkins.Count > 0) root["skins"] = _gltfSkins;
        if (animation is not null) root["animations"] = new JsonArray(animation);

        var json2 = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }));
        var jsonLength = (json2.Length + 3) & ~3; var binLength = _binary.Count;
        var total = 12L + 8 + jsonLength + 8L + binLength;
        if (total > MaxBytes) throw new ColladaException("O modelo convertido ultrapassa o limite de 100 MB.");
        var glb = new byte[(int)total];
        "glTF"u8.CopyTo(glb); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)jsonLength); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        json2.CopyTo(glb, 20); Array.Fill(glb, (byte)' ', 20 + json2.Length, jsonLength - json2.Length);
        var start = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(start), (uint)binLength); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(start + 4), 0x004E4942);
        _binary.CopyTo(glb, start + 8);
        return glb;
    }
}
