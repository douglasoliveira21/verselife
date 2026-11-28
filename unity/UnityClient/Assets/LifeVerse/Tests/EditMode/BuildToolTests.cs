using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class BuildToolTests
    {
        private static void Near(Vector3 expected, Vector3 actual, float tolerance = .001f) => Assert.Less((expected - actual).magnitude, tolerance, expected + " / " + actual);

        [Test]
        public void TheViewerReadsHeightAsZAndGoesBack()
        {
            Near(new Vector3(1f, -3f, 2f), BuildAxes.ToViewerPosition(new Vector3(1f, 2f, 3f)));
            Near(new Vector3(1f, 2f, 3f), BuildAxes.ToGamePosition(BuildAxes.ToViewerPosition(new Vector3(1f, 2f, 3f))));
            Near(new Vector3(4f, 6f, 5f), BuildAxes.ToViewerSize(new Vector3(4f, 5f, 6f)));
            Near(new Vector3(4f, 5f, 6f), BuildAxes.ToGameSize(BuildAxes.ToViewerSize(new Vector3(4f, 5f, 6f))));
            // a turn about the vertical is a turn about the viewer's Z
            Near(new Vector3(0f, 0f, 90f), BuildAxes.ToViewerRotation(new Vector3(0f, 90f, 0f)), .01f);
            Near(new Vector3(0f, 0f, 270f), BuildAxes.ToViewerRotation(new Vector3(0f, -90f, 0f)), .01f);
        }

        [Test]
        public void AnyTurnSurvivesTheRoundTrip()
        {
            foreach (var turn in new[] { new Vector3(0, 0, 0), new Vector3(10, 20, 30), new Vector3(-45, 135, 80), new Vector3(90, 0, 0), new Vector3(0, 0, 90), new Vector3(89.9f, 10, 20), new Vector3(30, -170, -120) })
            {
                var back = BuildAxes.ToGameRotation(BuildAxes.ToViewerRotation(turn));
                Assert.Less(Quaternion.Angle(Quaternion.Euler(turn), Quaternion.Euler(back)), .1f, turn + " -> " + back);
                Assert.That(Mathf.Abs(back.x) <= 180.001f && Mathf.Abs(back.y) <= 180.001f && Mathf.Abs(back.z) <= 180.001f, back.ToString());
            }
        }

        private static BuildShape.Field Field(string name) => BuildShape.Fields.Single(f => f.Name == name);
        private static float Written(string field, ShapeKind kind, float value, string key) { var g = new JsonObject(); Field(field).Write(kind, g, value); return (float)g[key].GetValue<double>(); }

        [Test]
        public void ShapeFieldsWriteTheIslandsGeometry()
        {
            Assert.AreEqual(ShapeKind.Straight, BuildShape.KindOf("tube")); Assert.AreEqual(ShapeKind.Sphere, BuildShape.KindOf("hemisphere")); Assert.AreEqual(ShapeKind.Torus, BuildShape.KindOf("ring")); Assert.AreEqual(ShapeKind.None, BuildShape.KindOf("tree")); Assert.AreEqual(ShapeKind.None, BuildShape.KindOf(null));
            Assert.AreEqual(.5f, Written("Scale 1", ShapeKind.Straight, 50f, "hollow"), 1e-5f);
            Assert.AreEqual(-.2f, Written("Shear Y", ShapeKind.Straight, .2f, "shearZ"), 1e-5f);
            // the same pair of fields is the wedge of a box and the turn of a torus
            Assert.AreEqual(.25f, Written("cut begin", ShapeKind.Straight, .25f, "cutStart"), 1e-5f); Assert.AreEqual(.25f, Written("cut begin", ShapeKind.Torus, .25f, "pathBegin"), 1e-5f);
            Assert.AreEqual(.75f, Written("Path Limit End", ShapeKind.Sphere, .75f, "pathEnd"), 1e-5f); Assert.AreEqual(.75f, Written("Path Limit End", ShapeKind.Torus, .75f, "cutEnd"), 1e-5f);
            // a torus's "hole size" is the size of its profile, half as large along Y
            Assert.AreEqual(.4f, Written("Taper Scale Y", ShapeKind.Torus, .2f, "holeX"), 1e-5f); Assert.AreEqual(.3f, Written("Taper Scale X", ShapeKind.Torus, .3f, "holeY"), 1e-5f);
            Assert.AreEqual(.3f, Written("Taper Scale X", ShapeKind.Straight, .3f, "taperX"), 1e-5f);
            Assert.AreEqual((.05f, .5f), BuildShape.Limits(Field("Taper Scale Y"), ShapeKind.Torus)); Assert.AreEqual((-1f, 1f), BuildShape.Limits(Field("Taper Scale Y"), ShapeKind.Straight));
            Assert.IsTrue(BuildShape.Shows("advanced_dimple", ShapeKind.Sphere)); Assert.IsFalse(BuildShape.Shows("text revolutions", ShapeKind.Straight)); Assert.IsFalse(BuildShape.Shows("text cut", ShapeKind.None));
        }

        [Test]
        public void WhatAFieldReadsIsWhatItWrote()
        {
            foreach (var kind in new[] { ShapeKind.Straight, ShapeKind.Sphere, ShapeKind.Torus })
                foreach (var field in BuildShape.Fields.Where(f => f.On.Contains(kind)))
                {
                    var (min, max) = BuildShape.Limits(field, kind); var value = Mathf.Round((min + (max - min) * .4f) * 100f) / 100f;
                    var g = new JsonObject(); field.Write(kind, g, value);
                    var read = field.Read(kind, kind == ShapeKind.Torus ? "torus" : kind == ShapeKind.Sphere ? "sphere" : "cube", PrimParams.Parse(g.ToJsonString()));
                    Assert.AreEqual(value, read, .002f * Mathf.Max(1f, Mathf.Abs(value)), kind + " " + field.Name + " " + g.ToJsonString());
                }
        }

        [Test]
        public void TheLayoutHasEveryComponentTheCodeAsksFor()
        {
            foreach (var key in new[] { "tools", "grid", "texture-picker", "color-picker", "media" }) { var sheet = BuildLayout.Sheet(key); Assert.Greater(sheet.Slots.Count, 3, key); foreach (var slot in sheet.Slots) { var area = BuildLayout.Place(slot, sheet.Size.x); Assert.LessOrEqual(area.xMax, sheet.Size.x + .5f, key + " " + slot.Name); } }
            var tools = BuildLayout.Sheet("tools"); Assert.AreEqual(428f, tools.Size.x, 1f); Assert.AreEqual(954f, tools.Size.y, 1f);
            foreach (var field in BuildShape.Fields) Assert.IsNotNull(tools.Of(field.Name), field.Name);
            foreach (var (button, type) in BuildWindow.Shapes) { Assert.IsNotNull(tools.Of(button), button); Assert.IsTrue(BuildCommands.Entries.Any(e => e.Type == type), type); }
            Assert.AreEqual(BuildCommands.Entries.Length, BuildWindow.Shapes.Length, "toda forma que a ilha faz tem o seu botão");
            foreach (var name in new[] { "button focus", "button move", "button edit", "button create", "button land", "Pos X", "Pos Y", "Pos Z", "Scale X", "Rot Z", "text status", "selection_count", "Object Name", "clickaction", "sale type", "Edit Cost", "Light Intensity", "combobox matmedia", "texture control", "contents_inventory", "slider brush size", "slider force" }) Assert.IsNotNull(tools.Of(name), name);
        }

        [Test]
        public void ANumberFieldKeepsToItsLimits()
        {
            var spinner = new BuildSpinner("X", 40f, "Posição X") { Min = 0f, Max = 10f, Step = 4f };
            Assert.IsNull(spinner.ProblemOf("2,5", out var value)); Assert.AreEqual(2.5f, value, 1e-5f); Assert.IsNull(spinner.ProblemOf(" 10 ", out _));
            StringAssert.Contains("número", spinner.ProblemOf("abc", out _)); StringAssert.Contains("número", spinner.ProblemOf("NaN", out _)); StringAssert.Contains("vai de", spinner.ProblemOf("11", out _)); StringAssert.Contains("Posição X", spinner.ProblemOf("-1", out _));
            var sent = -1f; spinner.Committed += v => sent = v; spinner.Show(5f, true); Assert.AreEqual(-1f, sent, "mostrar o que a ilha tem não envia nada");
            spinner.Nudge(+1); Assert.AreEqual(9f, sent, 1e-4f); spinner.Nudge(+1); Assert.AreEqual(10f, sent, 1e-4f); spinner.Nudge(-1); Assert.AreEqual(6f, sent, 1e-4f);
            spinner.Show(null, true); Assert.AreEqual("—", spinner.Input.value); Assert.IsFalse(spinner.Value.HasValue); spinner.Blank(); Assert.AreEqual("", spinner.Input.value);
        }

        [Test]
        public void ColoursGoThroughHueSaturationAndLight()
        {
            foreach (var colour in new[] { Color.red, Color.green, Color.blue, Color.white, Color.black, new Color(.71f, .28f, .18f), new Color(.2f, .9f, .6f), new Color(.5f, .5f, .5f) })
            {
                var (h, s, l) = BuildColourWindow.ToHsl(colour); var back = BuildColourWindow.FromHsl(h, s, l);
                Assert.Less(Mathf.Abs(back.r - colour.r) + Mathf.Abs(back.g - colour.g) + Mathf.Abs(back.b - colour.b), .01f, colour + " -> " + back);
            }
            Assert.AreEqual(1f / 3f, BuildColourWindow.ToHsl(Color.green).H, 1e-4f); Assert.AreEqual(.5f, BuildColourWindow.ToHsl(Color.green).L, 1e-4f);
        }

        [Test]
        public void TheTutorialSpeaksOfTheNewWindow()
        {
            StringAssert.Contains("Criar", BuildWindow.TutorialStepText(TutorialStep.Create)); StringAssert.Contains("Objeto", BuildWindow.TutorialStepText(TutorialStep.Resize)); StringAssert.Contains("Ctrl+D", BuildWindow.TutorialStepText(TutorialStep.Duplicate));
        }
    }
}
