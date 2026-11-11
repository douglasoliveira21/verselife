using System.Numerics;
using System.Text.Json;

namespace LifeVerse.Shared.Building;

/// <summary>Shape parameters of a procedural primitive, as stored in an object's geometry JSON.</summary>
/// <param name="Hollow">0 to 0.95: size of the hole through the shape, as a fraction of its outline.</param>
/// <param name="TaperX">-1 to 1: how much the top shrinks (positive) or grows (negative) along X.</param>
/// <param name="TaperY">-1 to 1: the same along the depth axis (Z).</param>
/// <param name="Twist">-180 to 180 degrees of rotation between bottom and top.</param>
/// <param name="CutStart">0 to 0.95: where the outline starts, as a fraction of its perimeter (a wedge is cut away).</param>
/// <param name="CutEnd">0.05 to 1: where the outline ends.</param>
/// <param name="PathBegin">0 to 0.95: where the path starts (a box or cylinder is cut from its base; a sphere from its bottom, which is its dimple; a torus from its first end).</param>
/// <param name="PathEnd">0.05 to 1: where the path ends.</param>
/// <param name="ShearX">-0.5 to 0.5: how far the top slides sideways along X relative to the base (top shear).</param>
/// <param name="ShearZ">-0.5 to 0.5: the same along Z.</param>
/// <param name="HoleX">-1 or 0 to 0.95: size of the hole along X (-1 means the same as Hollow); for a torus or ring, the thickness of the profile (0.05 to 1).</param>
/// <param name="HoleY">-1 or 0 to 0.95: size of the hole along the depth (Z), or the height of a torus profile.</param>
/// <param name="RadiusOffset">-0.9 to 0.9: how much the radius of a torus grows (positive) or shrinks along its path.</param>
/// <param name="Revolutions">1 to 3: turns of a torus path.</param>
/// <param name="TwistBegin">-180 to 180 degrees of rotation at the start of the path (the base); <c>Twist</c> is the rotation at its end, and the turn is spread evenly between them.</param>
/// <param name="Skew">-0.95 to 0.95: how far the end of a torus path is lifted above its start (a spiral when there is more than one turn).</param>
public readonly record struct PrimParams(float Hollow = 0, float TaperX = 0, float TaperY = 0, float Twist = 0, float CutStart = 0, float CutEnd = 1,
    float PathBegin = 0, float PathEnd = 1, float ShearX = 0, float ShearZ = 0, float HoleX = -1, float HoleY = -1, float RadiusOffset = 0, float Revolutions = 1, float Skew = 0, float TwistBegin = 0)
{
    /// <summary>A struct's own default would leave the cut end at 0, which means nothing is left; the shape starts whole.</summary>
    public PrimParams() : this(0, 0, 0, 0, 0, 1, 0, 1, 0, 0, -1, -1, 0, 1, 0) { }

    public static PrimParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PrimParams();
        try
        {
            using var document = JsonDocument.Parse(json);
            float Read(string key, float fallback) => document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number) && float.IsFinite(number) ? number : fallback;
            return new PrimParams(Read("hollow", 0), Read("taperX", 0), Read("taperY", 0), Read("twist", 0), Read("cutStart", 0), Read("cutEnd", 1),
                Read("pathBegin", 0), Read("pathEnd", 1), Read("shearX", 0), Read("shearZ", 0), Read("holeX", -1), Read("holeY", -1), Read("radiusOffset", 0), Read("revolutions", 1), Read("skew", 0), Read("twistBegin", 0)).Clamped();
        }
        catch (JsonException) { return new PrimParams(); }
    }

    /// <summary>Forces every value into the range the generator and the server accept, so no setting can produce a broken mesh.</summary>
    public PrimParams Clamped()
    {
        static (float, float) Range(float a, float b) { var s = Math.Clamp(a, 0f, .95f); var e = Math.Clamp(b, .05f, 1f); if (e - s < .05f) { if (s > .9f) s = e - .05f; else e = s + .05f; } return (s, e); }
        var (start, end) = Range(CutStart, CutEnd); var (pathBegin, pathEnd) = Range(PathBegin, PathEnd);
        float Hole(float v) => v < 0 ? -1f : Math.Clamp(v, 0f, .95f);
        return new PrimParams(Math.Clamp(Hollow, 0f, .95f), Math.Clamp(TaperX, -1f, 1f), Math.Clamp(TaperY, -1f, 1f), Math.Clamp(Twist, -180f, 180f), start, end,
            pathBegin, pathEnd, Math.Clamp(ShearX, -.5f, .5f), Math.Clamp(ShearZ, -.5f, .5f), Hole(HoleX), Hole(HoleY), Math.Clamp(RadiusOffset, -.9f, .9f), Math.Clamp(Revolutions, 1f, 3f), Math.Clamp(Skew, -.95f, .95f), Math.Clamp(TwistBegin, -180f, 180f));
    }
}

/// <summary>One selectable face of a primitive, with its own vertices so a different material can be given to each.</summary>
public sealed class PrimFace
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public Vector3[] Positions { get; init; } = [];
    public Vector3[] Normals { get; init; } = [];
    public Vector2[] Uvs { get; init; } = [];
    public int[] Indices { get; init; } = [];
}

public sealed class PrimMeshData
{
    public List<PrimFace> Faces { get; } = [];
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }
    public int TriangleCount => Faces.Sum(x => x.Indices.Length / 3);
}

/// <summary>
/// Builds the mesh of a box, prism, cylinder, tube, cone or pyramid from a profile (the outline) swept along a straight path
/// from the bottom (y = -0.5) to the top (y = +0.5): the top can be tapered and twisted, a wedge can be cut out of the outline
/// and a hole can be bored through it. The result has real geometry for every setting, one <see cref="PrimFace"/> per wall.
/// Face ids are stable: 0 top, 1 bottom, 2.. the walls (one per edge, or one round wall), then the inner wall and the two cut faces.
/// Triangles wind clockwise seen from outside, which is what Godot treats as the front.
/// </summary>
public static partial class PrimMesh
{
    public const int TopFace = 0, BottomFace = 1, FirstWallFace = 2;
    private const int RoundSegments = 24;
    private const float MinArea = 1e-9f;

    private static readonly Vector2[] SquareCorners = [new(.5f, .5f), new(-.5f, .5f), new(-.5f, -.5f), new(.5f, -.5f)];
    private static readonly Vector2[] TriangleCorners = [new(0, .5f), new(-.5f, -.5f), new(.5f, -.5f)];

    public static bool Supports(string type) => type.ToLowerInvariant() is "cube" or "prism" or "cylinder" or "tube" or "cone" or "pyramid" || IsRound(type);

    /// <summary>Number of walls (and so face ids) the outline of this primitive has: 4 for a box, 3 for a prism, 1 when it is round.</summary>
    public static int WallCount(string type) => type.ToLowerInvariant() switch { "cube" or "pyramid" => 4, "prism" => 3, _ => 1 };

    public static int InnerFace(string type) => FirstWallFace + WallCount(type);
    public static int CutStartFace(string type) => InnerFace(type) + 1;
    public static int CutEndFace(string type) => InnerFace(type) + 2;

    public static PrimMeshData Build(string type, PrimParams parameters)
    {
        type = type.ToLowerInvariant();
        if (!Supports(type)) throw new ArgumentException($"'{type}' is not a procedural primitive.", nameof(type));
        if (IsRound(type)) return BuildRound(type, parameters);
        var p = parameters.Clamped();
        if (type == "tube") p = p with { Hollow = Math.Max(p.Hollow, .6f), HoleX = p.HoleX >= 0 ? Math.Max(p.HoleX, .6f) : -1, HoleY = p.HoleY >= 0 ? Math.Max(p.HoleY, .6f) : -1 };
        if (type is "cone" or "pyramid") p = p with { TaperX = 1, TaperY = 1 };

        var corners = type switch { "cube" or "pyramid" => SquareCorners, "prism" => TriangleCorners, _ => null };
        var round = corners is null;
        var outline = Outline(corners, p.CutStart, p.CutEnd);
        var cut = p.CutStart > 0 || p.CutEnd < 1;
        var holeX = p.HoleX >= 0 ? p.HoleX : p.Hollow; var holeZ = p.HoleY >= 0 ? p.HoleY : p.Hollow;
        var hollow = holeX > 0 || holeZ > 0; holeX = MathF.Max(holeX, .02f); holeZ = MathF.Max(holeZ, .02f);
        var levels = p.Twist == p.TwistBegin ? 1 : Math.Clamp((int)MathF.Ceiling(MathF.Abs(p.Twist - p.TwistBegin) / 10f), 2, 18);
        var walls = WallCount(type);
        var builders = new Dictionary<int, FaceBuilder>();
        FaceBuilder Face(int id, string name) { if (!builders.TryGetValue(id, out var face)) builders[id] = face = new FaceBuilder(id, name); return face; }

        Vector3 Place(Vector2 point, float t, bool inner = false)
        {
            var tp = p.PathBegin + t * (p.PathEnd - p.PathBegin); // a path cut keeps only this part of the height
            var sx = MathF.Max(0f, 1f - p.TaperX * tp); var sz = MathF.Max(0f, 1f - p.TaperY * tp);
            var x = point.X * (inner ? holeX : 1f) * sx; var z = point.Y * (inner ? holeZ : 1f) * sz;
            var angle = MathF.PI / 180f * (p.TwistBegin + (p.Twist - p.TwistBegin) * tp);
            var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
            return new Vector3(x * cos + z * sin + p.ShearX * tp, tp - .5f, -x * sin + z * cos + p.ShearZ * tp);
        }
        Vector3 Spin(Vector3 direction, float t)
        {
            var angle = MathF.PI / 180f * (p.TwistBegin + (p.Twist - p.TwistBegin) * (p.PathBegin + t * (p.PathEnd - p.PathBegin))); var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
            return new Vector3(direction.X * cos + direction.Z * sin, direction.Y, -direction.X * sin + direction.Z * cos);
        }

        // outer walls, and inner walls when hollow
        for (var i = 0; i < outline.Length - 1; i++)
        {
            var (p0, u0) = outline[i]; var (p1, u1) = outline[i + 1];
            var edge = round ? 0 : Math.Min(walls - 1, (int)MathF.Floor((u0 + u1) / 2f * walls));
            var outer = Face(FirstWallFace + edge, round ? "Lado" : $"Lado {edge + 1}");
            var inner = hollow ? Face(InnerFace(type), "Interior") : null;
            for (var level = 0; level < levels; level++)
            {
                float t0 = level / (float)levels, t1 = (level + 1) / (float)levels, tm = (t0 + t1) / 2f;
                var a = Place(p0, t0); var b = Place(p1, t0); var c = Place(p1, t1); var d = Place(p0, t1);
                var hint = Spin(new Vector3((p0.X + p1.X) / 2f, 0, (p0.Y + p1.Y) / 2f), tm);
                Vector3[]? smooth = round ? [Spin(Radial(p0), t0), Spin(Radial(p1), t0), Spin(Radial(p1), t1), Spin(Radial(p0), t1)] : null;
                outer.Quad(a, b, c, d, [new(u0, t0), new(u1, t0), new(u1, t1), new(u0, t1)], hint, smooth);
                if (inner is null) continue;
                var ia = Place(p0, t0, true); var ib = Place(p1, t0, true); var ic = Place(p1, t1, true); var id = Place(p0, t1, true);
                Vector3[]? innerSmooth = round ? [-smooth![0], -smooth[1], -smooth[2], -smooth[3]] : null;
                inner.Quad(ia, ib, ic, id, [new(u0, t0), new(u1, t0), new(u1, t1), new(u0, t1)], -hint, innerSmooth);
            }
        }

        // top and bottom
        foreach (var (id, name, t, normal) in new[] { (TopFace, "Cima", 1f, Vector3.UnitY), (BottomFace, "Base", 0f, -Vector3.UnitY) })
        {
            var cap = Face(id, name);
            var center = Place(Vector2.Zero, t);
            for (var i = 0; i < outline.Length - 1; i++)
            {
                var a = Place(outline[i].Point, t); var b = Place(outline[i + 1].Point, t);
                if (hollow)
                {
                    var ia = Place(outline[i].Point, t, true); var ib = Place(outline[i + 1].Point, t, true);
                    cap.Quad(a, b, ib, ia, [Planar(a), Planar(b), Planar(ib), Planar(ia)], normal, null);
                }
                else cap.Triangle(center, a, b, [Planar(center), Planar(a), Planar(b)], normal);
            }
        }

        // the two flat faces left by the wedge that was cut out
        if (cut)
        {
            foreach (var (start, name, id) in new[] { (true, "Corte inicial", CutStartFace(type)), (false, "Corte final", CutEndFace(type)) })
            {
                var index = start ? 0 : outline.Length - 1;
                var neighbour = start ? 1 : outline.Length - 2;
                var edgePoint = outline[index].Point;
                var direction = Vector2.Normalize(outline[neighbour].Point - edgePoint);
                var away = new Vector3(-direction.X, 0, -direction.Y); // points out of the solid, along the outline
                var face = Face(id, name);
                var inside = hollow;
                for (var level = 0; level < levels; level++)
                {
                    float t0 = level / (float)levels, t1 = (level + 1) / (float)levels;
                    var a = inside ? Place(edgePoint, t0, true) : Place(Vector2.Zero, t0); var b = Place(edgePoint, t0); var c = Place(edgePoint, t1); var d = inside ? Place(edgePoint, t1, true) : Place(Vector2.Zero, t1);
                    face.Quad(a, b, c, d, [new(0, t0), new(1, t0), new(1, t1), new(0, t1)], Spin(away, (t0 + t1) / 2f), null);
                }
            }
        }

        var data = new PrimMeshData();
        foreach (var face in builders.Values.OrderBy(x => x.Id)) { var built = face.Build(); if (built.Indices.Length > 0) data.Faces.Add(built); }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var face in data.Faces) foreach (var position in face.Positions) { min = Vector3.Min(min, position); max = Vector3.Max(max, position); }
        data.Min = data.Faces.Count == 0 ? Vector3.Zero : min; data.Max = data.Faces.Count == 0 ? Vector3.Zero : max;
        return data;
    }

    private static Vector3 Radial(Vector2 point) { var length = point.Length(); return length < 1e-6f ? Vector3.UnitX : new Vector3(point.X / length, 0, point.Y / length); }
    private static Vector2 Planar(Vector3 position) => new(position.X + .5f, position.Z + .5f);

    /// <summary>The outline points between the two cuts, with their position along the perimeter (0 to 1).</summary>
    private static (Vector2 Point, float U)[] Outline(Vector2[]? corners, float start, float end)
    {
        var values = new List<float> { start };
        if (corners is null)
        {
            var count = Math.Max(2, (int)MathF.Ceiling(RoundSegments * (end - start)));
            for (var i = 1; i < count; i++) values.Add(start + (end - start) * i / count);
        }
        else for (var i = 1; i < corners.Length; i++) { var u = i / (float)corners.Length; if (u > start + 1e-4f && u < end - 1e-4f) values.Add(u); }
        values.Add(end);
        return values.Select(u => (PointAt(corners, u), u)).ToArray();
    }

    private static Vector2 PointAt(Vector2[]? corners, float u)
    {
        if (corners is null) { var angle = 2f * MathF.PI * u; return new Vector2(.5f * MathF.Cos(angle), .5f * MathF.Sin(angle)); }
        var position = u * corners.Length; var edge = Math.Min(corners.Length - 1, (int)MathF.Floor(position));
        return Vector2.Lerp(corners[edge], corners[(edge + 1) % corners.Length], position - edge);
    }

    private sealed class FaceBuilder(int id, string name)
    {
        private readonly List<Vector3> _positions = [], _normals = [];
        private readonly List<Vector2> _uvs = [];
        private readonly List<int> _indices = [];
        public int Id { get; } = id;

        /// <summary>Adds a four-corner quad; <paramref name="outward"/> says which side is the front. Flat normals unless <paramref name="smooth"/> is given.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2[] uv, Vector3 outward, Vector3[]? smooth)
        {
            var first = Vector3.Cross(b - a, c - a); var second = Vector3.Cross(c - a, d - a);
            var flat = first.LengthSquared() >= second.LengthSquared() ? first : second;
            if (flat.LengthSquared() < MinArea) return;
            var normal = Vector3.Normalize(Vector3.Dot(flat, outward) > 0 ? flat : -flat);
            var corners = new[] { a, b, c, d };
            var start = _positions.Count;
            for (var i = 0; i < 4; i++) { _positions.Add(corners[i]); _normals.Add(smooth is null ? normal : Vector3.Normalize(smooth[i])); _uvs.Add(uv[i]); }
            // clockwise from outside: each triangle is turned around when its counter-clockwise side faces the front; a collapsed one (at an apex) is dropped
            AddTriangle(start, start + 1, start + 2, first, outward);
            AddTriangle(start, start + 2, start + 3, second, outward);
        }

        private void AddTriangle(int a, int b, int c, Vector3 cross, Vector3 outward)
        {
            if (cross.LengthSquared() < MinArea) return;
            if (Vector3.Dot(cross, outward) > 0) _indices.AddRange([a, c, b]); else _indices.AddRange([a, b, c]);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2[] uv, Vector3 outward)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < MinArea) return;
            var normal = Vector3.Normalize(outward);
            var start = _positions.Count;
            _positions.AddRange([a, b, c]); _normals.AddRange([normal, normal, normal]); _uvs.AddRange(uv);
            if (Vector3.Dot(cross, outward) > 0) _indices.AddRange([start, start + 2, start + 1]); else _indices.AddRange([start, start + 1, start + 2]);
        }

        public PrimFace Build() => new() { Id = Id, Name = name, Positions = [.. _positions], Normals = [.. _normals], Uvs = [.. _uvs], Indices = [.. _indices] };
    }
}
