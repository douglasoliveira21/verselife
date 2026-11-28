using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>What a machine can say about a drawn prototype: which functions have a place, what leaves the screen, which blocks cover each other and which words are cut.</summary>
    public static class Checks
    {
        private static bool Shown(VisualElement e)
        {
            for (var at = e; at != null; at = at.parent) if (at.resolvedStyle.display == DisplayStyle.None) return false;
            return e.layout.width > .5f && e.layout.height > .5f;
        }

        private static bool Visible(VisualElement e) { for (var at = e; at != null; at = at.parent) if (at.resolvedStyle.display == DisplayStyle.None) return false; return true; }

        public static HashSet<string> Functions(VisualElement hud)
        {
            var found = new HashSet<string>();
            hud.Query<VisualElement>().ForEach(e => { if (e.userData is string tag && tag.StartsWith("fn:") && Shown(e)) foreach (var id in tag.Substring(3).Split('|')) found.Add(id); });
            return found;
        }

        public static List<string> Layout(VisualElement hud, bool blocks)
        {
            var problems = new List<string>(); var screen = hud.worldBound;
            hud.Query<Label>().ForEach(l => { if (l.text.Length > 0 && l.resolvedStyle.display != DisplayStyle.None && !Shown(l) && Visible(l)) problems.Add("texto sem tamanho: " + l.text); });
            hud.Query<VisualElement>().ForEach(e =>
            {
                if (!Shown(e)) return; var r = e.worldBound; var tag = e.userData as string;
                var plate = tag != null && tag.Contains("name-plates");
                if (!plate && (tag != null && tag.StartsWith("fn:") || e.ClassListContains("region")) && (r.xMin < screen.xMin - 1f || r.yMin < screen.yMin - 1f || r.xMax > screen.xMax + 1f || r.yMax > screen.yMax + 1f))
                    problems.Add("fora da tela: " + (tag ?? e.name) + " " + Box(r));
                if (e is Label label && label.resolvedStyle.whiteSpace == WhiteSpace.NoWrap && label.text.Length > 0 && !label.text.Contains("<"))
                {
                    var need = label.MeasureTextSize(label.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
                    if (need > label.contentRect.width + 1.5f) problems.Add("texto cortado: " + label.text);
                }
            });
            if (!blocks) return problems;
            var regions = hud.Query<VisualElement>(null, "region").ToList().Where(Shown).ToList();
            for (var i = 0; i < regions.Count; i++)
                for (var j = i + 1; j < regions.Count; j++)
                {
                    var a = regions[i].worldBound; var b = regions[j].worldBound; var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin); var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (w > 1.5f && h > 1.5f) problems.Add($"sobreposição: {regions[i].name} × {regions[j].name} ({w:0}×{h:0})");
                }
            // words of one list that run into the list beside it, or under the strip below
            var items = hud.Query<VisualElement>().ToList().Where(e => Shown(e) && e.userData is string t && t.StartsWith("fn:item:")).ToList();
            for (var i = 0; i < items.Count; i++)
                for (var j = i + 1; j < items.Count; j++)
                {
                    var a = items[i].worldBound; var b = items[j].worldBound; if (Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 2f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 2f) problems.Add("itens do menu sobrepostos: " + items[i].userData + " × " + items[j].userData);
                }
            var strip = hud.Query<VisualElement>().ToList().Where(e => Shown(e) && e.userData is string t && t == "fn:toolbar-customize").Select(e => e.worldBound).ToList();
            foreach (var item in items) foreach (var s in strip) { var a = item.worldBound; if (Mathf.Min(a.xMax, s.xMax) - Mathf.Max(a.xMin, s.xMin) > 2f && Mathf.Min(a.yMax, s.yMax) - Mathf.Max(a.yMin, s.yMin) > 2f) problems.Add("item do menu sobre a personalização: " + item.userData); }
            return problems;
        }

        private static string Box(Rect r) => $"[{r.xMin:0},{r.yMin:0} {r.width:0}×{r.height:0}]";

        /// <summary>The game's own lists (read, not changed): every button of its bar and every entry of its menus must be in <see cref="Model"/>.</summary>
        public static List<string> AgainstTheGame()
        {
            var missing = new List<string>();
            foreach (var id in LifeVerse.Unity.UI.Hud.Icons.Keys) if (!Model.Tools.Any(t => t.Id == id)) missing.Add("botão da barra do jogo sem lugar no modelo: " + id);
            var source = File.ReadAllText(Path.Combine(Application.dataPath, "LifeVerse/UI/GameApp.cs"));
            var starts = Regex.Matches(source, "Hud\\.AddMenu\\(\"([^\"]+)\"").Cast<Match>().ToList(); var count = 0;
            for (var i = 0; i < starts.Count; i++)
            {
                var title = starts[i].Groups[1].Value; var from = starts[i].Index; var end = source.IndexOf("\n            Hud.", from + 10); var block = source.Substring(from, (end < 0 ? source.Length : end) - from);
                foreach (Match m in Regex.Matches(block, "\\(\"([^\"]+)\", \"[a-z-]*\", "))
                {
                    var label = Regex.Split(m.Groups[1].Value, "   ")[0]; count++;
                    if (!Model.Menus.Any(menu => menu.Title == title && menu.Items.Any(item => item.Label == label))) missing.Add("entrada do menu do jogo sem lugar no modelo: " + title + " / " + label);
                }
            }
            var modelled = Model.Menus.Sum(m => m.Items.Length); if (count != modelled) missing.Add($"o jogo tem {count} entradas de menu e o modelo {modelled}");
            return missing;
        }
    }
}
