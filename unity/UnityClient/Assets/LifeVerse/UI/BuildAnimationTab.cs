using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Animação do modelo", at the top of the Recursos tab: for an imported model that carries animations, whether it is animated, which clip, play and pause, loop and speed.
    /// It lives in the material JSON (<c>animation</c>), like the light: the server stamps when it started, so everybody sees the same point of the clip.
    /// </summary>
    public sealed partial class BuildWindow
    {
        private Toggle _animationOn, _animationLoop; private DropdownField _clip; private Button _animationPlay; private Slider _animationSpeed; private Label _animationNote; private VisualElement _animationRows;
        private string _clipsShown = "";
        public Toggle AnimationOn => _animationOn;
        public Toggle AnimationLoop => _animationLoop;
        public DropdownField Clip => _clip;
        public Button AnimationPlay => _animationPlay;
        public Slider AnimationSpeed => _animationSpeed;
        public string AnimationNote => _animationNote.text;

        private void BuildAnimation(VisualElement tab)
        {
            tab.Add(UiKit.Text("Animação do modelo", "lv-bold"));
            _animationNote = UiKit.Text("", "lv-small", "lv-muted"); _animationNote.style.whiteSpace = WhiteSpace.Normal; tab.Add(_animationNote);
            _animationOn = UiKit.Check("Animar este modelo", false, SetAnimated); _animationOn.name = "animation-on"; tab.Add(_animationOn);
            _animationRows = new VisualElement { name = "animation-rows" }; tab.Add(_animationRows);
            _clip = new DropdownField("Animação", new List<string> { "—" }, 0) { name = "animation-clip" };
            _clip.RegisterValueChangedCallback(e => { if (!_filling) EditAnimation(a => { a["clip"] = e.newValue; a["offset"] = 0.0; }, false); });
            _animationPlay = UiKit.Button("Reproduzir", ToggleAnimationPlay, "lv-btn-primary"); _animationPlay.name = "animation-play";
            _animationLoop = UiKit.Check("Repetir (loop)", true, v => EditAnimation(a => a["loop"] = v, true)); _animationLoop.name = "animation-loop";
            _animationSpeed = UiKit.Slide("Velocidade", .1f, 4f, 1f, v => EditAnimation(a => a["speed"] = Math.Round(v, 2), true)); _animationSpeed.name = "animation-speed";
            foreach (var e in new VisualElement[] { _clip, UiKit.Row(_animationPlay), _animationLoop, _animationSpeed }) _animationRows.Add(e);
            tab.Add(UiKit.Divider());
            // a model takes a moment to arrive: its clips show up without another click
            tab.schedule.Execute(() => { if (CurrentTab != "Recursos") return; var first = First(); if (first != null && ClipsKey(first) != _clipsShown) Refresh(); }).Every(400);
        }

        private string ClipsKey(WorldObjectState first) => first.Id + "|" + _b.ModelLoaded(first.Id) + "|" + string.Join("|", _b.ClipsOf(first.Id));

        private void RefreshAnimation(WorldObjectState first)
        {
            if (_animationOn == null || first == null) return;
            var mesh = first.PrimitiveType == "mesh"; var clips = _b.ClipsOf(first.Id); _clipsShown = ClipsKey(first);
            var animation = ModelAnimations.Settings(_b.Latest(first.Id)?.MaterialJson ?? first.MaterialJson);
            var able = mesh && clips.Count > 0;
            _animationNote.text = !mesh ? "Só um modelo importado que traga animações pode ser animado." : !_b.ModelLoaded(first.Id) ? "O modelo ainda está carregando…" : clips.Count == 0 ? "Este modelo não traz animações." : clips.Count == 1 ? "O modelo traz uma animação." : "O modelo traz " + clips.Count + " animações.";
            _animationOn.SetEnabled(able || animation != null); _animationOn.SetValueWithoutNotify(animation != null);
            _animationRows.style.display = able && animation != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!able) return;
            if (!clips.SequenceEqual(_clip.choices)) _clip.choices = clips.ToList();
            var chosen = Text(animation, "clip"); _clip.SetValueWithoutNotify(clips.Contains(chosen) ? chosen : clips[0]);
            var playing = Flag(animation, "playing", false); _animationPlay.text = playing ? "Pausar" : "Reproduzir";
            _animationLoop.SetValueWithoutNotify(Flag(animation, "loop", true)); _animationSpeed.SetValueWithoutNotify((float)Number(animation, "speed", 1));
        }

        private static string Text(JsonObject a, string key) => a?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        private static bool Flag(JsonObject a, string key, bool fallback) => a?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
        private static double Number(JsonObject a, string key, double fallback) => a?[key] is JsonValue v && v.TryGetValue<double>(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;

        /// <summary>The selected models (only imported ones can be animated).</summary>
        private List<Guid> SelectedModels() => _b.Selection.Where(id => _b.StateOf(id)?.PrimitiveType == "mesh").ToList();

        private void SetAnimated(bool on)
        {
            if (_filling) return; var models = SelectedModels(); if (models.Count == 0) { Say("Selecione um modelo importado.", true); return; }
            _b.Edit(models, s =>
            {
                var material = ParseMaterial(s.MaterialJson); var clips = _b.ClipsOf(s.Id);
                if (!on) material.Remove("animation");
                else if (clips.Count > 0) material["animation"] = new JsonObject { ["clip"] = clips[0], ["playing"] = true, ["loop"] = true, ["speed"] = 1.0, ["offset"] = 0.0 };
                s.MaterialJson = material.ToJsonString();
            }, "animação");
        }

        /// <summary>The button "Reproduzir" / "Pausar".</summary>
        public void ToggleAnimationPlay()
        {
            var first = First(); if (first == null) return;
            var animation = ModelAnimations.Settings(_b.Latest(first.Id)?.MaterialJson ?? first.MaterialJson); if (animation == null) return;
            var playing = Flag(animation, "playing", false); var (position, length) = _b.ClipPoint(first.Id);
            // pausing freezes where it is; playing goes on from there (from the start when a single play had finished)
            var from = !playing && !Flag(animation, "loop", true) && length > 0f && position >= length - .01f ? 0f : position;
            EditAnimation(a => { a["playing"] = !playing; a["offset"] = Math.Round(from, 3); }, false);
        }

        /// <summary>Changes the animation of the selected models that are animated. With <paramref name="keepPoint"/>, what is playing goes on from the point it reached.</summary>
        private void EditAnimation(Action<JsonObject> change, bool keepPoint)
        {
            if (_filling) return; var models = SelectedModels(); if (models.Count == 0) return;
            _b.Edit(models, s =>
            {
                var material = ParseMaterial(s.MaterialJson); if (material["animation"] is not JsonObject animation) return;
                if (keepPoint && Flag(animation, "playing", false)) { var (position, length) = _b.ClipPoint(s.Id); if (length > 0f) animation["offset"] = Math.Round(position, 3); }
                change(animation); animation.Remove("since");   // the server writes when this started
                s.MaterialJson = material.ToJsonString();
            }, "animação");
        }
    }
}
