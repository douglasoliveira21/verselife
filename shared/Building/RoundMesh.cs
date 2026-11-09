using System.Numerics;

namespace LifeVerse.Shared.Building;

/// <summary>
/// Spheres, hemispheres, tori and rings, built as a profile (a circle or ellipse) swept along a path: for a sphere the path is a line from pole to pole and the
/// profile grows and shrinks; for a torus it is a circle in the ground plane (more than one turn makes a spiral). Every setting changes the mesh: hole size
/// (profile thickness), hollow, taper, twist, top shear, profile cut, path cut (the dimple of a sphere), radius offset, revolutions and skew.
/// Face ids: 0 top and 1 bottom (spheres), 2 outer wall, 3 inner wall, 4/5 the two profile-cut faces, 6/7 the two ends of a torus path.
/// </summary>
public static partial class PrimMesh
{
    public const int OuterRoundFace = 2, InnerRoundFace = 3, ProfileCutStartFace = 4, ProfileCutEndFace = 5, PathStartFace = 6, PathEndFace = 7;

    public static bool IsRound(string type) => type.ToLowerInvariant() is "sphere" or "hemisphere" or "torus" or "ring";

    private static PrimMeshData BuildRound(string type, PrimParams raw)
    {
        var p = raw.Clamped();
        var sphere = type is "sphere" or "hemisphere";
        float s0 = type == "hemisphere" ? .5f + .5f * p.PathBegin : p.PathBegin, s1 = type == "hemisphere" ? .5f + .5f * p.PathEnd : p.PathEnd;
        float t0 = p.CutStart, t1 = p.CutEnd;
        var hollow = p.Hollow > 0; var h = p.Hollow;
        float hx = p.HoleX >= 0 ? Math.Clamp(p.HoleX, .05f, 1f) : (type == "ring" ? .5f : .3f), hy = p.HoleY >= 0 ? Math.Clamp(p.HoleY, .05f, 1f) : (type == "ring" ? .2f : .3f);
        float a0 = .5f * hx, b0 = .5f * hy, r0 = .5f - a0;
        var turns = sphere ? 0f : p.Revolutions;
        var nS = sphere ? Math.Max(4, (int)MathF.Ceiling(24 * (s1 - s0))) : Math.Max(8, (int)MathF.Ceiling(36 * turns * (s1 - s0)));
        var nT = Math.Max(6, (int)MathF.Ceiling(24 * (t1 - t0)));
        var cut = t0 > 0 || t1 < 1;

        Vector3 Center(float s)
        {
            if (sphere) return new Vector3(p.ShearX * s, -.5f * MathF.Cos(MathF.PI * s) - (type == "hemisphere" ? .25f : 0f), p.ShearZ * s);
            var angle = 2f * MathF.PI * turns * s; var radius = MathF.Max(r0 * (1f + p.RadiusOffset * (2f * s - 1f)), a0 * .2f);
            return new Vector3(radius * MathF.Cos(angle), p.Skew * (s - .5f), radius * MathF.Sin(angle));
        }
        Vector3 Radial(float s) { var angle = 2f * MathF.PI * turns * s; return new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)); }
        // the profile point at path position s and profile angle theta, before it is placed on the path
        Vector2 Profile(float s, float theta)
        {
            var ax = sphere ? .5f * MathF.Sin(MathF.PI * s) : a0; var ay = sphere ? ax : b0;
            var x = MathF.Cos(theta) * ax * MathF.Max(0f, 1f - p.TaperX * s); var y = MathF.Sin(theta) * ay * MathF.Max(0f, 1f - p.TaperY * s);
            var twist = MathF.PI / 180f * p.Twist * s; var cos = MathF.Cos(twist); var sin = MathF.Sin(twist);
            return new Vector2(x * cos - y * sin, x * sin + y * cos);
        }
        Vector3 Pos(float s, float theta, float scale = 1f)
        {
            var o = Profile(s, theta) * scale; var c = Center(s);
            return sphere ? c + new Vector3(o.X, 0, o.Y) : c + Radial(s) * o.X + new Vector3(0, o.Y, 0);
        }
        float S(int i) => s0 + (s1 - s0) * i / nS;
        float Th(int k) => 2f * MathF.PI * (t0 + (t1 - t0) * k / nT);
        static Vector3 Dir(Vector3 value, Vector3 fallback) => value.LengthSquared() < 1e-10f ? Vector3.Normalize(fallback) : Vector3.Normalize(value);

        var builders = new Dictionary<int, FaceBuilder>();
        FaceBuilder Face(int id, string name) { if (!builders.TryGetValue(id, out var face)) builders[id] = face = new FaceBuilder(id, name); return face; }

        // walls
        for (var i = 0; i < nS; i++)
        {
            float sa = S(i), sb = S(i + 1);
            for (var k = 0; k < nT; k++)
            {
                float ta = Th(k), tb = Th(k + 1);
                var uv = new[] { new Vector2((float)k / nT, sa), new Vector2((float)(k + 1) / nT, sa), new Vector2((float)(k + 1) / nT, sb), new Vector2((float)k / nT, sb) };
                var a = Pos(sa, ta); var b = Pos(sa, tb); var c = Pos(sb, tb); var d = Pos(sb, ta);
                var ca = Center(sa); var cb = Center(sb);
                var outward = (a + b + c + d) / 4f - (ca + cb) / 2f; if (outward.LengthSquared() < 1e-10f) outward = Vector3.UnitY;
                Vector3[] smooth = [Dir(a - ca, outward), Dir(b - ca, outward), Dir(c - cb, outward), Dir(d - cb, outward)];
                Face(OuterRoundFace, "Superfície").Quad(a, b, c, d, uv, outward, smooth);
                if (!hollow) continue;
                Face(InnerRoundFace, "Interior").Quad(Pos(sa, ta, h), Pos(sa, tb, h), Pos(sb, tb, h), Pos(sb, ta, h), uv, -outward, [-smooth[0], -smooth[1], -smooth[2], -smooth[3]]);
            }
        }

        // the two ends of the path: flat discs (or rings when hollow)
        foreach (var (s, id, name, sign) in new[] { (s0, sphere ? BottomFace : PathStartFace, sphere ? "Base" : "Início do caminho", -1f), (s1, sphere ? TopFace : PathEndFace, sphere ? "Cima" : "Fim do caminho", 1f) })
        {
            var tangent = Center(MathF.Min(1f, s + .002f)) - Center(MathF.Max(0f, s - .002f)); if (tangent.LengthSquared() < 1e-12f) tangent = Vector3.UnitY;
            var outward = Vector3.Normalize(tangent) * sign; var center = Center(s); var cap = Face(id, name);
            for (var k = 0; k < nT; k++)
            {
                var a = Pos(s, Th(k)); var b = Pos(s, Th(k + 1));
                if (hollow) { var ia = Pos(s, Th(k), h); var ib = Pos(s, Th(k + 1), h); cap.Quad(a, b, ib, ia, [Planar(a), Planar(b), Planar(ib), Planar(ia)], outward, null); }
                else cap.Triangle(center, a, b, [Planar(center), Planar(a), Planar(b)], outward);
            }
        }

        // the two flat faces left by a profile cut
        if (cut)
        {
            foreach (var (theta, id, name, sign) in new[] { (Th(0), ProfileCutStartFace, "Corte inicial", -1f), (Th(nT), ProfileCutEndFace, "Corte final", 1f) })
            {
                var face = Face(id, name);
                for (var i = 0; i < nS; i++)
                {
                    float sa = S(i), sb = S(i + 1);
                    var around = (Pos((sa + sb) / 2f, theta + .01f) - Pos((sa + sb) / 2f, theta - .01f)) * sign;
                    var inner = hollow ? h : 0f;
                    var a = Pos(sa, theta, inner); var b = Pos(sa, theta); var c = Pos(sb, theta); var d = Pos(sb, theta, inner);
                    face.Quad(a, b, c, d, [new(0, sa), new(1, sa), new(1, sb), new(0, sb)], around, null);
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
}
