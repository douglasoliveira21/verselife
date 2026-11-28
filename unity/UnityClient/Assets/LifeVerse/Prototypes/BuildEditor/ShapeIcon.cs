using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>The fifteen shapes of the Create tool as line drawings of the LifeVerse set (24 × 24 box), in the colour of the button's text.</summary>
    public sealed class ShapeIcon : VisualElement
    {
        private readonly string _shape;

        public ShapeIcon(string shape, float size = 20f)
        {
            _shape = shape; pickingMode = PickingMode.Ignore; style.width = size; style.height = size; generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; var k = contentRect.width / 24f; p.strokeColor = resolvedStyle.color; p.lineWidth = 1.5f; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            void Line(params float[] v) { p.BeginPath(); p.MoveTo(new Vector2(v[0], v[1]) * k); for (var i = 2; i < v.Length; i += 2) p.LineTo(new Vector2(v[i], v[i + 1]) * k); p.Stroke(); }
            // an ellipse, whole or only from one angle to another (degrees, 90 is the lower half's middle)
            void Oval(float cx, float cy, float rx, float ry, float from = 0f, float to = 360f)
            {
                p.BeginPath();
                for (var i = 0; i <= 28; i++) { var a = Mathf.Lerp(from, to, i / 28f) * Mathf.Deg2Rad; var at = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry) * k; if (i == 0) p.MoveTo(at); else p.LineTo(at); }
                p.Stroke();
            }
            switch (_shape)
            {
                case "Cube": Line(4, 8, 12, 4, 20, 8, 20, 17, 12, 21, 4, 17, 4, 8, 12, 12, 20, 8); Line(12, 12, 12, 21); break;
                case "Prism": Line(7, 19, 3, 15, 10, 5, 14, 9, 7, 19, 21, 19, 14, 9); Line(10, 5, 17, 15, 21, 19); break;
                case "Pyramid": Line(12, 4, 3, 17, 12, 21, 21, 17, 12, 4, 12, 21); break;
                case "Tetrahedron": Line(12, 4, 4, 19, 20, 19, 12, 4); Line(4, 19, 13, 15, 20, 19); Line(12, 4, 13, 15); break;
                case "Cylinder": Oval(12, 6, 7, 2.6f); Line(5, 6, 5, 18); Line(19, 6, 19, 18); Oval(12, 18, 7, 2.6f, 0, 180); break;
                case "HemiCylinder": Oval(12, 6, 7, 2.6f, 0, 180); Line(5, 6, 19, 6); Line(5, 6, 5, 18); Line(19, 6, 19, 18); Oval(12, 18, 7, 2.6f, 0, 180); break;
                case "Cone": Oval(12, 18, 7, 2.6f); Line(5, 18, 12, 3, 19, 18); break;
                case "HemiCone": Oval(12, 18, 7, 2.6f, 0, 180); Line(5, 18, 19, 18); Line(5, 18, 12, 3, 19, 18); break;
                case "Sphere": Oval(12, 12, 8.5f, 8.5f); Oval(12, 12, 8.5f, 3.2f, 0, 180); break;
                case "HemiSphere": Oval(12, 16, 8.5f, 8.5f, 180, 360); Oval(12, 16, 8.5f, 3.2f); break;
                case "Torus": Oval(12, 12, 9.5f, 6.5f); Oval(12, 11, 3.8f, 2f); break;
                case "Tube": Oval(12, 7, 8, 3.2f); Oval(12, 7, 4, 1.5f); Line(4, 7, 4, 17); Line(20, 7, 20, 17); Oval(12, 17, 8, 3.2f, 0, 180); break;
                case "Ring": Line(12, 3, 21, 19, 3, 19, 12, 3); Line(12, 9, 16.5f, 16.5f, 7.5f, 16.5f, 12, 9); break;
                case "Tree": Oval(12, 9, 6.5f, 6f); Line(12, 12, 12, 21); Line(8, 21, 16, 21); break;
                default: Line(4, 21, 20, 21); Line(8, 21, 6, 11); Line(12, 21, 12, 7); Line(16, 21, 18, 12); break;
            }
        }
    }
}
