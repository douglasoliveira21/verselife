using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>One component of the original build tool, as measured in docs/design/build-editor-coordinate-map.json. Rectangles are relative to the window's top-left corner.</summary>
    public sealed class Widget
    {
        public string Id, Name, Type, Group, Label, Function, Component, Window;
        /// <summary>Where it goes in LifeVerse (reference pixels at 1920×1080) and where it is in the original window.</summary>
        public Rect Lv, Original;
        public bool VisibleAtLoad;
        /// <summary>The width of a spinner's own label, in original pixels (40 when the XML does not say).</summary>
        public float LabelWidth = 40f;
        /// <summary>The tab it belongs to ("Geral", "Objeto"…) or null.</summary>
        public string Tab => Group.StartsWith("Aba ", StringComparison.Ordinal) ? Group.Substring(4) : null;
        /// <summary>The mode it belongs to ("Foco", "Editar"…) or null.</summary>
        public string Mode => Group.StartsWith("Modo ", StringComparison.Ordinal) ? Group.Substring(5) : null;
    }

    public sealed class MapWindow
    {
        public string Key;
        public Vector2 Size, OriginalSize;
        public readonly List<Widget> Widgets = new List<Widget>();
        public Widget Find(string name) => Widgets.FirstOrDefault(w => w.Name == name);
    }

    /// <summary>The coordinate map, read from the documentation: the prototype is drawn from the same numbers the document lists, so the two cannot drift apart.</summary>
    public static class Map
    {
        public const float Scale = 1.45f;
        public static string DocsFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "design"));

        public static Dictionary<string, MapWindow> Load()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DocsFolder, "build-editor-coordinate-map.json")));
            var result = new Dictionary<string, MapWindow>();
            foreach (var w in document.RootElement.GetProperty("windows").EnumerateArray())
            {
                var window = new MapWindow { Key = w.GetProperty("key").GetString() };
                window.Size = new Vector2(Number(w.GetProperty("lifeverse"), "w"), Number(w.GetProperty("lifeverse"), "h"));
                window.OriginalSize = new Vector2(Number(w.GetProperty("original"), "w"), Number(w.GetProperty("original"), "h"));
                foreach (var r in w.GetProperty("widgets").EnumerateArray())
                {
                    var widget = new Widget
                    {
                        Window = window.Key, Id = Text(r, "id"), Name = Text(r, "name"), Type = Text(r, "type"), Group = Text(r, "group"), Label = Text(r, "originalLabel"), Function = Text(r, "function"), Component = Text(r, "component"),
                        Lv = Box(r.GetProperty("lifeverse")), Original = Box(r.GetProperty("original")), VisibleAtLoad = r.GetProperty("visibleAtLoad").GetBoolean(),
                    };
                    if (r.TryGetProperty("labelWidth", out var lw) && lw.ValueKind == JsonValueKind.String && float.TryParse(lw.GetString(), out var width)) widget.LabelWidth = width;
                    window.Widgets.Add(widget);
                }
                result[window.Key] = window;
            }
            return result;
        }

        private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        private static float Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;
        private static Rect Box(JsonElement e) => new Rect(Number(e, "x"), Number(e, "y"), Number(e, "w"), Number(e, "h"));
    }
}
