using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>One component of the build tool: what it is, where it goes (reference pixels from the window's top-left corner) and to which mode or tab it belongs. The names are the viewer's.</summary>
    public sealed class BuildSlot
    {
        public string Name, Type, Tab, Mode;
        /// <summary>It belongs to the line of information about the selection.</summary>
        public bool Info;
        /// <summary>The viewer keeps it hidden until something asks for it.</summary>
        public bool Hidden;
        public Rect Area;
        /// <summary>The width of a number field's own label, in the viewer's pixels (40 when not said).</summary>
        public float LabelWidth = 40f;
    }

    public sealed class BuildSheet
    {
        public string Key; public Vector2 Size; public readonly List<BuildSlot> Slots = new List<BuildSlot>();
        private Dictionary<string, BuildSlot> _byName;
        public BuildSlot Of(string name) { if (_byName == null) { _byName = new Dictionary<string, BuildSlot>(); foreach (var s in Slots) if (!_byName.ContainsKey(s.Name)) _byName[s.Name] = s; } return _byName.TryGetValue(name, out var slot) ? slot : null; }
    }

    /// <summary>
    /// Where everything of the build tool goes, as in the official Second Life viewer (the file is written by unity/tools/sl-build-map from the coordinate map of docs/design).
    /// </summary>
    public static class BuildLayout
    {
        /// <summary>How many reference pixels one of the viewer's pixels takes.</summary>
        public const float Scale = 1.45f;
        private static Dictionary<string, BuildSheet> _sheets;

        public static BuildSheet Sheet(string key)
        {
            if (_sheets == null) _sheets = Parse(Resources.Load<TextAsset>("LifeVerse/UI/BuildLayout")?.text ?? throw new InvalidOperationException("BuildLayout.json não encontrado"));
            return _sheets[key];
        }

        public static Dictionary<string, BuildSheet> Parse(string json)
        {
            var result = new Dictionary<string, BuildSheet>(); using var document = JsonDocument.Parse(json);
            foreach (var w in document.RootElement.GetProperty("windows").EnumerateArray())
            {
                var sheet = new BuildSheet { Key = w.GetProperty("key").GetString(), Size = new Vector2(w.GetProperty("w").GetSingle(), w.GetProperty("h").GetSingle()) };
                foreach (var e in w.GetProperty("widgets").EnumerateArray())
                {
                    string Text(string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;
                    bool Flag(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
                    var slot = new BuildSlot { Name = Text("n"), Type = Text("t"), Tab = Text("tab"), Mode = Text("mode"), Info = Flag("info"), Hidden = Flag("hidden"), Area = new Rect(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("w").GetSingle(), e.GetProperty("h").GetSingle()) };
                    if (e.TryGetProperty("lw", out var lw)) slot.LabelWidth = lw.GetSingle();
                    sheet.Slots.Add(slot);
                }
                result[sheet.Key] = sheet;
            }
            return result;
        }

        /// <summary>
        /// The rectangle a component takes on screen. Heights too small to read are grown around the viewer's visual centre (it draws check boxes at the bottom of their rectangle,
        /// so those grow upwards), and the pickers keep only the square of their picture (the rest is the viewer's caption, where the next component may sit).
        /// </summary>
        public static Rect Place(BuildSlot slot, float windowWidth)
        {
            var r = slot.Area;
            switch (slot.Type)
            {
                case "text": { var h = Mathf.Max(r.height, 19f); if (slot.Name == "perm_modify" || slot.Name == "media_setting_note") h = Mathf.Max(r.height, 38f); else r.y = r.center.y - h / 2f; r.height = h; break; }
                case "check_box": r.y = r.yMax - 22f; r.height = 22f; break;
                case "radio_item": r.y = r.center.y - 10.5f; r.height = 21f; break;
                case "spinner": { var h = Mathf.Clamp(r.height, 23f, 28f); r.y = r.center.y - h / 2f; r.height = h; break; }
                case "combo_box": { if (slot.Name == "combobox texgen") { r.y -= 2f; r.height = 25f; break; } var h = Mathf.Clamp(r.height, 27f, 30f); r.y = r.center.y - h / 2f; r.height = h; break; }
                case "line_editor": case "filter_editor": case "name_box": { var h = Mathf.Max(r.height, 26f); r.y = r.center.y - h / 2f; r.height = h; break; }
                case "slider_bar": case "slider": r.height = Mathf.Max(r.height, 20f); break;
                case "avatar_icon": r.y += 7f; break;
                case "color_swatch": if (r.height > r.width) r.height = r.width; break;
                case "texture_picker": if (slot.Name == "texture control" || slot.Name == "light texture control" || slot.Name == "bumpytexture control") r.height = Mathf.Min(r.height, r.width - 3f); break;
            }
            if (r.xMax > windowWidth - 6f) r.width = Mathf.Max(10f, windowWidth - 6f - r.x);
            return r;
        }
    }
}
