using System.Linq;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class IconTests
    {
        [Test]
        public void EveryIconOfTheGodotClientBecomesADrawing()
        {
            Assert.AreEqual(111, UiIconData.Icons.Count);
            foreach (var name in UiIconData.Icons.Keys) Assert.Greater(IconShapes.Of(name).Count, 0, name);
            Assert.AreEqual(IconShapes.Of("dots").Count, IconShapes.Of("nao-existe").Count, "an unknown name draws the dots");
        }

        [Test]
        public void PathsAndArcsLandWhereSvgSays()
        {
            var search = IconShapes.Path("M3 10a7 7 0 1 0 14 0a7 7 0 1 0 -14 0");
            var last = search[0].Last(s => s.Op == IconShapes.Op.Cubic); Assert.AreEqual(3f, last.C.x, 1e-3f); Assert.AreEqual(10f, last.C.y, 1e-3f);
            var lines = IconShapes.Path("M4 7l16 0h-2v5H1z"); Assert.AreEqual(1, lines.Count); Assert.AreEqual(new Vector2(20, 7), lines[0][1].A); Assert.AreEqual(new Vector2(18, 7), lines[0][2].A); Assert.AreEqual(new Vector2(18, 12), lines[0][3].A); Assert.AreEqual(IconShapes.Op.Close, lines[0].Last().Op);
            var arc = IconShapes.Arc(new Vector2(0, 0), new Vector2(10, 0), 5, 5, 0, false, true); Assert.AreEqual(2, arc.Count, "half a circle is two quarter curves"); Assert.AreEqual(new Vector2(10, 0), arc.Last().C);
            var shapes = IconShapes.Parse("<circle cx=\"12\" cy=\"12\" r=\"9\" /><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\" /><line x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\" />"); Assert.AreEqual(3, shapes.Count);
            Assert.IsTrue(IconShapes.Filled("pin-filled")); Assert.IsFalse(IconShapes.Filled("pin"));
        }
    }
}
