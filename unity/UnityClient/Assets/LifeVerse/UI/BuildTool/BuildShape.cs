using System;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>Which of the viewer's shape fields a primitive uses.</summary>
    public enum ShapeKind { None, Straight, Sphere, Torus }

    /// <summary>
    /// The viewer's shape fields over the island's geometry (<see cref="PrimParams"/>). The same number means something else from one shape to another, as in Second Life: "path cut" is the
    /// wedge of a box but the turn of a torus; the pair under it is the slice, the dimple or the profile cut; "taper" of a torus is the size of its hole. Values are in the viewer's units.
    /// </summary>
    public static class BuildShape
    {
        public static ShapeKind KindOf(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "sphere": case "hemisphere": return ShapeKind.Sphere;
                case "torus": case "ring": return ShapeKind.Torus;
                case "cube": case "prism": case "cylinder": case "tube": case "cone": case "pyramid": return ShapeKind.Straight;
                default: return ShapeKind.None;
            }
        }

        public sealed class Field
        {
            public string Name; public float Min, Max, Step; public string Format = "0.00";
            /// <summary>The kinds that have it.</summary>
            public ShapeKind[] On;
            public Func<ShapeKind, string, PrimParams, float> Read;
            public Action<ShapeKind, JsonObject, float> Write;
        }

        private static readonly ShapeKind[] All = { ShapeKind.Straight, ShapeKind.Sphere, ShapeKind.Torus }, Edged = { ShapeKind.Straight, ShapeKind.Torus }, Round = { ShapeKind.Torus };

        /// <summary>The thickness of a torus's profile when the object says nothing about it.</summary>
        private static float HoleAlong(string type, PrimParams p) => p.HoleX >= 0 ? p.HoleX : type == "ring" ? .5f : .3f;
        private static float HoleAcross(string type, PrimParams p) => p.HoleY >= 0 ? p.HoleY : type == "ring" ? .2f : .3f;

        private static Field Pair(string name, string straightKey, string torusKey, float min, float max, float step, string format, Func<PrimParams, float> straight, Func<PrimParams, float> torus) => new Field
        {
            Name = name, Min = min, Max = max, Step = step, Format = format, On = All,
            Read = (k, t, p) => k == ShapeKind.Torus ? torus(p) : straight(p), Write = (k, g, v) => g[k == ShapeKind.Torus ? torusKey : straightKey] = Math.Round(v, 3),
        };

        private static Field Plain(string name, string key, float min, float max, float step, ShapeKind[] on, Func<PrimParams, float> read, string format = "0.00", float factor = 1f) => new Field
        {
            Name = name, Min = min, Max = max, Step = step, Format = format, On = on, Read = (k, t, p) => read(p) * factor, Write = (k, g, v) => g[key] = Math.Round(v / factor, 3),
        };

        public static readonly Field[] Fields =
        {
            Pair("cut begin", "cutStart", "pathBegin", 0f, .95f, .025f, "0.000", p => p.CutStart, p => p.PathBegin),
            Pair("cut end", "cutEnd", "pathEnd", .05f, 1f, .025f, "0.000", p => p.CutEnd, p => p.PathEnd),
            Plain("Scale 1", "hollow", 0f, 95f, 5f, All, p => p.Hollow, "0.0", 100f),
            Plain("Skew", "skew", -.95f, .95f, .05f, Round, p => p.Skew),
            Plain("Twist Begin", "twistBegin", -180f, 180f, 9f, All, p => p.TwistBegin, "0"),
            Plain("Twist End", "twist", -180f, 180f, 9f, All, p => p.Twist, "0"),
            // straight shapes: how much the top narrows; torus and ring: the size of the profile (X along the axis, Y across, up to half)
            new Field { Name = "Taper Scale X", Min = -1f, Max = 1f, Step = .05f, On = Edged, Read = (k, t, p) => k == ShapeKind.Torus ? HoleAcross(t, p) : p.TaperX, Write = (k, g, v) => g[k == ShapeKind.Torus ? "holeY" : "taperX"] = Math.Round(v, 3) },
            new Field { Name = "Taper Scale Y", Min = -1f, Max = 1f, Step = .05f, On = Edged, Read = (k, t, p) => k == ShapeKind.Torus ? HoleAlong(t, p) / 2f : p.TaperY, Write = (k, g, v) => { if (k == ShapeKind.Torus) g["holeX"] = Math.Round(v * 2f, 3); else g["taperY"] = Math.Round(v, 3); } },
            Plain("Shear X", "shearX", -.5f, .5f, .05f, Edged, p => p.ShearX),
            Plain("Shear Y", "shearZ", -.5f, .5f, .05f, Edged, p => p.ShearZ, "0.00", -1f),   // the viewer's Y is the island's -Z
            Pair("Path Limit Begin", "pathBegin", "cutStart", 0f, .95f, .02f, "0.000", p => p.PathBegin, p => p.CutStart),
            Pair("Path Limit End", "pathEnd", "cutEnd", .05f, 1f, .02f, "0.000", p => p.PathEnd, p => p.CutEnd),
            Plain("Taper X", "taperX", -1f, 1f, .05f, Round, p => p.TaperX),
            Plain("Taper Y", "taperY", -1f, 1f, .05f, Round, p => p.TaperY),
            Plain("Radius Offset", "radiusOffset", -.9f, .9f, .05f, Round, p => p.RadiusOffset),
            Plain("Revolutions", "revolutions", 1f, 3f, .1f, Round, p => p.Revolutions),
        };

        /// <summary>The limits of a field for a shape (the hole of a torus has its own).</summary>
        public static (float Min, float Max) Limits(Field field, ShapeKind kind) => kind == ShapeKind.Torus && field.Name == "Taper Scale X" ? (.05f, 1f) : kind == ShapeKind.Torus && field.Name == "Taper Scale Y" ? (.05f, .5f) : (field.Min, field.Max);

        /// <summary>The labels over the fields that a shape shows.</summary>
        public static bool Shows(string label, ShapeKind kind)
        {
            switch (label)
            {
                case "text cut": case "text hollow": case "Hollow Shape": case "text twist": return kind != ShapeKind.None;
                case "text skew": case "scale_hole": case "advanced_cut": case "text taper2": case "text radius delta": case "text revolutions": return kind == ShapeKind.Torus;
                case "scale_taper": case "advanced_slice": return kind == ShapeKind.Straight;
                case "advanced_dimple": return kind == ShapeKind.Sphere;
                case "text topshear": return kind == ShapeKind.Straight || kind == ShapeKind.Torus;
                default: return false;
            }
        }
    }
}
