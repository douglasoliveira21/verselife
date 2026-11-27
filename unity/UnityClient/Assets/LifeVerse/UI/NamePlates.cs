using System.Collections.Generic;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The name plates over the avatars, as the Godot client draws them (docs/design/09_placas_nome.png): a dark glass pill with a light outline, the status dot (green online, grey away,
    /// red busy), the name, and the waves of the voice while the person speaks. The plate follows the head on screen; when and whether a name shows is the avatar's own rule (distance, facing,
    /// the "show names" options), so this only draws.
    /// </summary>
    public sealed class NamePlates
    {
        private sealed class Plate { public VisualElement Root; public VisualElement Dot; public Label Name, Title; public Icon Waves; }
        /// <summary>"Mostrar títulos": the group title under the name.</summary>
        public static bool ShowTitles = true;
        public int TitlesShown { get; private set; }
        private readonly UiRoot _ui; private readonly Dictionary<int, Plate> _plates = new Dictionary<int, Plate>();
        public static readonly Color Online = new Color(.13f, .9f, .3f), Away = new Color(.66f, .7f, .74f), Busy = new Color(.94f, .27f, .27f);
        public int Shown { get; private set; }

        public NamePlates(UiRoot ui) { _ui = ui; AvatarView.PlatesDrawn = true; }

        public static Color StatusColour(string presence) => presence switch { "away" => Away, "busy" => Busy, _ => Online };

        private Plate Make()
        {
            var p = new Plate { Root = new VisualElement { pickingMode = PickingMode.Ignore, name = "name-plate" } }; p.Root.AddToClassList("lv-plate");
            p.Root.style.position = Position.Absolute; p.Root.style.flexDirection = FlexDirection.Row; p.Root.style.alignItems = Align.Center;
            p.Dot = new VisualElement { pickingMode = PickingMode.Ignore }; p.Dot.style.width = p.Dot.style.height = 9; p.Dot.style.borderTopLeftRadius = p.Dot.style.borderTopRightRadius = p.Dot.style.borderBottomLeftRadius = p.Dot.style.borderBottomRightRadius = 5; p.Dot.style.marginRight = 7; p.Root.Add(p.Dot);
            p.Name = new Label { pickingMode = PickingMode.Ignore }; p.Name.AddToClassList("lv-plate-name"); p.Root.Add(p.Name);
            p.Waves = new Icon("waves", 16) { name = "waves" }; p.Waves.style.marginLeft = 6; p.Root.Add(p.Waves);
            p.Title = new Label { pickingMode = PickingMode.Ignore, name = "plate-title" }; p.Title.AddToClassList("lv-small"); p.Title.style.position = Position.Absolute; p.Title.style.top = Length.Percent(100); p.Title.style.left = 0; p.Title.style.right = 0;
            p.Title.style.unityTextAlign = TextAnchor.UpperCenter; p.Title.style.color = new Color(.85f, .9f, .95f); p.Title.style.unityTextOutlineColor = Color.black; p.Title.style.unityTextOutlineWidth = .5f; p.Title.style.whiteSpace = WhiteSpace.NoWrap; p.Root.Add(p.Title);
            _ui.HudLayer.Add(p.Root); p.Root.SendToBack(); return p;
        }

        /// <summary>Places every plate for this frame. <paramref name="ownPresence"/> colours one's own dot; the others show online.</summary>
        public void Tick(PlayerViews views, Camera camera, int selfPeer, string ownPresence)
        {
            Shown = 0; TitlesShown = 0; var seen = new HashSet<int>(); var scale = Mathf.Max(.01f, _ui.Panel.scale);
            if (views != null && camera != null)
                foreach (var peer in views.Peers)
                {
                    var view = views.ViewOf(peer); if (view == null || !view.NameShown || view.NameTag == null) continue;
                    var screen = camera.WorldToScreenPoint(view.NameTag.position); if (screen.z <= 0f) continue;
                    if (!_plates.TryGetValue(peer, out var plate)) _plates[peer] = plate = Make(); seen.Add(peer); Shown++;
                    plate.Name.text = view.DisplayName; plate.Dot.style.backgroundColor = peer == selfPeer ? StatusColour(ownPresence) : Online;
                    plate.Waves.style.display = view.Speaking ? DisplayStyle.Flex : DisplayStyle.None; plate.Waves.Colour = Online;
                    var title = view.Appearance != null ? view.Appearance.GroupLabel ?? "" : ""; var showTitle = ShowTitles && title.Length > 0; plate.Title.text = title; plate.Title.style.display = showTitle ? DisplayStyle.Flex : DisplayStyle.None; if (showTitle) TitlesShown++;
                    var size = plate.Root.layout; var x = screen.x / scale - (float.IsNaN(size.width) ? 0f : size.width / 2f); var y = (Screen.height - screen.y) / scale - (float.IsNaN(size.height) ? 0f : size.height);
                    plate.Root.style.left = x; plate.Root.style.top = y; plate.Root.style.display = DisplayStyle.Flex;
                }
            foreach (var pair in _plates) if (!seen.Contains(pair.Key)) pair.Value.Root.style.display = DisplayStyle.None;
        }

        public void Clear() { foreach (var p in _plates.Values) p.Root.RemoveFromHierarchy(); _plates.Clear(); }
    }
}
