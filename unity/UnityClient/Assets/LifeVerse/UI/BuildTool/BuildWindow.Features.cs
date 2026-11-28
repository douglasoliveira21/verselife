using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Items;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The Features tab: the animation of an imported model (where a flexible path would be), the light of the object and its projector, the reflection of the surroundings and its
    /// physics. Light, projector, reflection and animation live in the material JSON the server keeps, so everybody sees the same; the server stamps when an animation started.
    /// </summary>
    public sealed partial class BuildWindow
    {
        public static readonly string[] LightColours = { "#FFE0A8", "#FFFFFF", "#FFB46B", "#9FD3FF", "#FF6B6B", "#7CFF9E", "#C59BFF" };
        public static readonly (string Id, string Label)[] CollisionShapes = { ("box", "Primitiva (caixa)"), ("convex", "Convexa"), ("triangle", "Malha (parada)"), ("sphere", "Esfera"), ("cylinder", "Cilindro"), ("none", "Nenhuma") };

        private Toggle _animationLoop; private DropdownField _clip; private Button _animationPlay, _lightMore; private BuildSpinner _animationSpeed, _mass; private Label _animationNote; private Button _lightColour;
        private string _clipsShown = "";
        public Toggle LightOn => _ui.Check("Light Checkbox Ctrl");
        public Toggle ReflectOn => _ui.Check("Reflection Probe");
        public BuildSpinner LightEnergy => _ui.Spinner("Light Intensity");
        public Toggle AnimationOn => _ui.Check("Animated Mesh Checkbox Ctrl");
        public Toggle AnimationLoop => _animationLoop;
        public DropdownField Clip => _clip;
        public Button AnimationPlay => _animationPlay;
        public BuildSpinner AnimationSpeed => _animationSpeed;
        public string AnimationNote => _animationNote.text;

        private Button Swatch(string tab, string name, string tip, Action run)
        {
            var slot = _ui.Sheet.Slots.First(s => s.Name == "colorswatch" && s.Tab == tab); var area = BuildLayout.Place(slot, _ui.Sheet.Size.x); area.y -= ScrollTop;
            var button = new Button(() => { LifeVerse.Unity.Audio.UiSounds.Click(); run(); }) { tooltip = tip }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-picture"); _ui.Put(name, button, area, _pages[tab]); return button;
        }

        private void BuildFeatures()
        {
            var page = _pages["Recursos"]; Rect Under(string slot, float dy, float width, float height) { var a = _ui.AreaOf(slot); return new Rect(a.x, a.y - ScrollTop + dy, width, height); }
            foreach (var name in new[] { "Flexible1D Checkbox Ctrl", "FlexNumSections", "FlexGravity", "FlexFriction", "FlexWind", "FlexTension", "FlexForceX", "FlexForceY", "FlexForceZ" }) _ui.Unavailable(name, "o cliente do jogo ainda não simula objetos flexíveis.");
            _ui.Unavailable("clipboard_features_params_btn", "copiar e colar propriedades chega com o editor 3D."); _ui.Unavailable("clipboard_light_params_btn", "copiar e colar propriedades chega com o editor 3D.");

            // the animation of an imported model: where the flexible path is for the other shapes
            OnCheck("Animated Mesh Checkbox Ctrl", SetAnimated);
            _animationNote = new Label { name = "build-animation-note" }; _animationNote.AddToClassList("bt-text"); _animationNote.AddToClassList("bt-wrap"); _animationNote.AddToClassList("bt-soft"); _ui.Put("build-animation-note", _animationNote, Under("FlexNumSections", -26f, 190f, 40f), page);
            _clip = new DropdownField(new List<string> { "—" }, 0); _clip.AddToClassList("bt-combo"); _ui.Put("animation-clip", _clip, Under("FlexNumSections", 20f, 186f, 29f), page);
            _clip.RegisterValueChangedCallback(e => { if (!_filling) EditAnimation(a => { a["clip"] = e.newValue; a["offset"] = 0.0; }, false); });
            _animationPlay = UiKit.Button("Reproduzir", ToggleAnimationPlay, "lv-btn-primary"); _animationPlay.AddToClassList("bt-btn"); _ui.Put("animation-play", _animationPlay, Under("FlexNumSections", 58f, 130f, 29f), page);
            _animationLoop = new Toggle { text = "Repetir" }; _animationLoop.AddToClassList("bt-check"); _ui.Put("animation-loop", _animationLoop, Under("FlexNumSections", 96f, 186f, 22f), page);
            _animationLoop.RegisterValueChangedCallback(e => { if (!_filling) EditAnimation(a => a["loop"] = e.newValue, true); });
            _animationSpeed = new BuildSpinner("Velocidade", 101f, "Velocidade") { Min = .1f, Max = 4f, Step = .1f, Format = "0.00" }; _ui.Put("animation-speed", _animationSpeed, Under("FlexNumSections", 128f, 186f, 27f), page);
            _animationSpeed.Committed += v => { if (!_filling) EditAnimation(a => a["speed"] = Math.Round(v, 2), true); }; _animationSpeed.Refused += why => Say(why, true);

            // light and projector
            OnCheck("Light Checkbox Ctrl", v => { if (v) EditMaterial(m => { if (m["light"] is not JsonObject) m["light"] = new JsonObject { ["color"] = "#FFE0A8", ["energy"] = 1.5, ["range"] = 8.0, ["falloff"] = 1.0, ["shadow"] = false }; }); else EditMaterial(m => { m.Remove("light"); m.Remove("projector"); m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); }); });
            _lightColour = Swatch("Recursos", "build-light-colour", "Cor da luz", () => PickColour(Text(Parse(First()?.MaterialJson)["light"] as JsonObject, "color"), "#FFE0A8", hex => EditLight(l => l["color"] = hex)));
            OnPress("light texture control", () => PickTexture("Imagem do projetor", Current("projectorBuiltin", "projectorTexture"), (builtin, id) => SetProjector(builtin, id), () => EditMaterial(m => { m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); })));
            var projector = _ui.AreaOf("light texture control");
            _lightMore = UiKit.Button("Mais…", OpenLight); _lightMore.AddToClassList("bt-btn"); _lightMore.tooltip = "Sombra, de que face a luz sai e a abertura do cone"; _ui.Put("build-light-more", _lightMore, new Rect(projector.xMax + 14f, projector.y - ScrollTop + 8f, 80f, 27f), page);
            OnNumber("Light Intensity", 0f, 8f, .1f, "0.000", v => EditLight(l => l["energy"] = Math.Round(v, 2))); OnNumber("Light Radius", .5f, 30f, .5f, "0.000", v => EditLight(l => l["range"] = Math.Round(v, 1))); OnNumber("Light Falloff", .1f, 4f, .1f, "0.000", v => EditLight(l => l["falloff"] = Math.Round(v, 2)));
            OnNumber("Light FOV", 10f, 120f, 5f, "0", v => EditProjector(p => p["fov"] = Math.Round(v))); OnNumber("Light Focus", 0f, 30f, .5f, "0.000", v => EditProjector(p => p["focus"] = Math.Round(v, 1))); OnNumber("Light Ambiance", 0f, 1f, .05f, "0.000", v => EditProjector(p => p["ambiance"] = Math.Round(v, 2)));

            // reflection
            OnCheck("Reflection Probe", v => EditMaterial(m => { if (v) { if (m["reflection"] is not JsonObject) m["reflection"] = new JsonObject { ["intensity"] = .6, ["range"] = 8.0 }; } else m.Remove("reflection"); }));
            OnNumber("Probe Ambiance", 0f, 1f, .05f, "0.000", v => EditReflection(r => r["intensity"] = Math.Round(v, 2))); OnNumber("Probe Near Clip", 2f, 30f, 1f, "0.000", v => EditReflection(r => r["range"] = Math.Round(v, 1)));
            _ui.Unavailable("Probe Volume Type", "o reflexo do jogo tem uma forma só."); _ui.Unavailable("Probe Dynamic", "o reflexo do jogo não tem esta escolha.");
            _ui.Visible("Probe Update Label", false); _ui.Visible("Probe Update Type", false);   // the viewer shows them only for a kind of probe the game does not have

            // physics
            Choices("Physics Shape Type Combo Ctrl", CollisionShapes.Select(s => s.Label));
            OnPick("Physics Shape Type Combo Ctrl", i => EditPhysics(p => p with { CollisionShape = CollisionShapes[i].Id }));
            if (_ui.Combo("material") is { } surfaceKind) surfaceKind.tooltip = "De que é feito: muda o atrito, o quique e o som"; Choices("material", new[] { "(sem material)" }.Concat(PhysicsRules.Materials.Select(m => m.Value.Label)));
            OnPick("material", i => { var id = i == 0 ? "" : PhysicsRules.Materials.ElementAt(i - 1).Key; EditPhysics(p => p with { Material = id }); });
            OnNumber("Physics Friction", 0f, 1f, .05f, "0.00", v => EditPhysics(p => p with { Friction = v })); OnNumber("Physics Restitution", 0f, 1f, .05f, "0.00", v => EditPhysics(p => p with { Bounce = v }));
            _ui.Unavailable("Physics Gravity", "o multiplicador de gravidade precisa de mudança no servidor de ilha."); _ui.Unavailable("Physics Density", "o jogo usa a massa (abaixo); densidade precisa de mudança no servidor de ilha.");
            var bounce = _ui.AreaOf("Physics Restitution");
            _mass = new BuildSpinner("Massa (kg)", _ui.Sheet.Of("Physics Restitution").LabelWidth * BuildLayout.Scale, "Massa") { Min = PhysicsRules.MinMass, Max = PhysicsRules.MaxMass, Step = 1f, Format = "0.##" };
            _ui.Put("build-mass", _mass, new Rect(bounce.x, bounce.y - ScrollTop + 38f, bounce.width, bounce.height), page); _mass.Committed += v => { if (!_filling) EditPhysics(p => p with { Mass = v }); }; _mass.Refused += why => Say(why, true);
        }

        public BuildSpinner MassField => _mass;

        private void EditLight(Action<JsonObject> change) => EditMaterial(m => { var l = m["light"] as JsonObject; if (l == null) { l = new JsonObject { ["color"] = "#FFE0A8", ["energy"] = 1.5, ["range"] = 8.0 }; m["light"] = l; } change(l); });
        private void EditProjector(Action<JsonObject> change) => EditMaterial(m => { var p = m["projector"] as JsonObject; if (p == null) { p = new JsonObject(); m["projector"] = p; } change(p); });
        private void EditReflection(Action<JsonObject> change) => EditMaterial(m => { var r = m["reflection"] as JsonObject; if (r == null) { r = new JsonObject { ["intensity"] = .6, ["range"] = 8.0 }; m["reflection"] = r; } change(r); });

        /// <summary>Puts a picture in the projector (a pattern of the system or an image of the inventory); turns the light on when it was off.</summary>
        public void SetProjector(bool builtin, string id)
        {
            EditMaterial(m =>
            {
                m.Remove("projectorBuiltin"); m.Remove("projectorTexture"); m[builtin ? "projectorBuiltin" : "projectorTexture"] = id;
                if (m["light"] is not JsonObject) m["light"] = new JsonObject { ["color"] = "#FFFFFF", ["energy"] = 2.0, ["range"] = 10.0 };
            });
        }

        private void OpenLight()
        {
            if (First() is not { } first || WorldObjects.ReadLight(first.MaterialJson) == null) { Say("Ligue a luz primeiro.", true); return; }
            Child("build-light", () => new BuildLightWindow(_c, _b, change => EditLight(change)));
        }

        private void RefreshFeatures(List<WorldObjectState> all, WorldObjectState first)
        {
            var has = first != null; var one = all.Count == 1;
            _ui.Visible("select_single", all.Count > 1); _ui.Visible("edit_object", !(all.Count > 1));
            // animation
            var mesh = has && first.PrimitiveType == "mesh"; var clips = has ? _b.ClipsOf(first.Id) : (IReadOnlyList<string>)Array.Empty<string>(); if (has) _clipsShown = ClipsKey(first);
            var animation = has ? ModelAnimations.Settings(_b.Latest(first.Id)?.MaterialJson ?? first.MaterialJson) : null; var able = mesh && clips.Count > 0;
            foreach (var name in new[] { "Flexible1D Checkbox Ctrl", "FlexNumSections", "FlexGravity", "FlexFriction", "FlexWind", "FlexTension", "FlexForceX", "FlexForceY", "FlexForceZ" }) _ui.Visible(name, !mesh);
            if (!mesh) foreach (var name in new[] { "FlexNumSections", "FlexGravity", "FlexFriction", "FlexWind", "FlexTension", "FlexForceX", "FlexForceY", "FlexForceZ" }) _ui.Spinner(name)?.Blank();
            _animationNote.text = !mesh ? "" : !_b.ModelLoaded(first.Id) ? "O modelo ainda está carregando…" : clips.Count == 0 ? "Este modelo não traz animações." : clips.Count == 1 ? "O modelo traz uma animação." : "O modelo traz " + clips.Count + " animações.";
            _ui.Enable("Animated Mesh Checkbox Ctrl", able || animation != null); AnimationOn.SetValueWithoutNotify(animation != null);
            AnimationOn.tooltip = mesh ? "" : "Só um modelo importado que traga animações pode ser animado.";
            _animationNote.style.display = mesh ? DisplayStyle.Flex : DisplayStyle.None;
            var rows = able && animation != null ? DisplayStyle.Flex : DisplayStyle.None; _clip.style.display = _animationPlay.style.display = _animationLoop.style.display = _animationSpeed.style.display = rows;
            if (able)
            {
                if (!clips.SequenceEqual(_clip.choices)) _clip.choices = clips.ToList();
                var chosen = Text(animation, "clip"); _clip.SetValueWithoutNotify(clips.Contains(chosen) ? chosen : clips[0]);
                _animationPlay.text = Flag(animation, "playing", false) ? "Pausar" : "Reproduzir"; _animationLoop.SetValueWithoutNotify(Flag(animation, "loop", true)); _animationSpeed.Show(Number(animation, "speed", 1f));
            }
            // light
            var light = has ? WorldObjects.ReadLight(first.MaterialJson) : null;
            Check("Light Checkbox Ctrl", all, s => WorldObjects.ReadLight(s.MaterialJson) != null, has);
            _lightColour.SetEnabled(light != null); _lightColour.style.backgroundColor = ColourOf(light != null ? Text(Parse(first.MaterialJson)["light"] as JsonObject, "color") : null, new Color(1f, .88f, .66f));
            _ui.Enable("light texture control", light != null); Picture(_ui.Button("light texture control"), light?.ProjectorBuiltin, light?.ProjectorTexture != null);
            if (_ui.Button("light texture control") is { } tile) tile.tooltip = light == null ? "Imagem do projetor: ligue a luz" : light.ProjectorBuiltin != null ? "Projetor: " + BuiltInTextures.All.First(t => t.Id == light.ProjectorBuiltin).Label : light.ProjectorTexture != null ? "Projetor: imagem do inventário" : "Imagem do projetor (sem imagem, a luz é redonda)";
            _lightMore.SetEnabled(light != null);
            Number("Light Intensity", light?.Energy, light != null); Number("Light Radius", light?.Range, light != null); Number("Light Falloff", light?.Falloff, light != null);
            Number("Light FOV", light?.ProjectorFov, light != null); Number("Light Focus", light?.ProjectorFocus, light != null); Number("Light Ambiance", light?.Ambiance, light != null);
            // reflection
            var reflection = has ? WorldObjects.ReadReflection(first.MaterialJson) : null;
            Check("Reflection Probe", all, s => WorldObjects.ReadReflection(s.MaterialJson) != null, has);
            Number("Probe Ambiance", reflection?.Item1, reflection != null); Number("Probe Near Clip", reflection?.Item2, reflection != null);
            Choice("Probe Volume Type", new[] { "Esfera" }, 0, false);
            // physics
            var physics = all.Select(s => PhysicsRules.Read(s.PhysicsJson)).ToList();
            int Shared(Func<PhysicsSettings, int> read) => physics.Count == 0 ? -1 : physics.Select(read).Distinct().Count() > 1 ? -2 : read(physics[0]);
            Choice("Physics Shape Type Combo Ctrl", CollisionShapes.Select(s => s.Label).ToList(), Shared(p => Mathf.Max(0, Array.FindIndex(CollisionShapes, s => s.Id == p.CollisionShape))), has);
            Choice("material", new[] { "(sem material)" }.Concat(PhysicsRules.Materials.Select(m => m.Value.Label)).ToList(), Shared(p => PhysicsRules.Materials.Keys.ToList().IndexOf(p.Material ?? "") + 1), has);
            if (!has) { _ui.Combo("Physics Shape Type Combo Ctrl")?.SetValueWithoutNotify("—"); _ui.Combo("material")?.SetValueWithoutNotify("—"); }
            Number("Physics Friction", Same(all, s => PhysicsRules.Read(s.PhysicsJson).Friction), has); Number("Physics Restitution", Same(all, s => PhysicsRules.Read(s.PhysicsJson).Bounce), has);
            _ui.Spinner("Physics Gravity")?.Blank(); _ui.Spinner("Physics Density")?.Blank();
            _mass.SetEnabled(has); var mass = Same(all, s => PhysicsRules.Read(s.PhysicsJson).Mass); if (has) _mass.Show(mass); else _mass.Blank();
        }

        private static Color ColourOf(string hex, Color fallback) => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var colour) ? colour : fallback;

        /// <summary>Draws on a tile the pattern of the system it holds, or a mark when it holds a picture of the inventory.</summary>
        private static void Picture(Button tile, string builtin, bool inventory)
        {
            if (tile == null) return; tile.Clear(); tile.style.backgroundImage = builtin != null && BuiltInTextures.Get(builtin) is { } pattern ? new StyleBackground(pattern) : new StyleBackground(StyleKeyword.None);
            if (builtin == null) { var mark = new Icon(inventory ? "image" : "plus", 22); mark.style.alignSelf = UnityEngine.UIElements.Align.Center; mark.style.marginTop = StyleKeyword.Auto; mark.style.marginBottom = StyleKeyword.Auto; tile.Add(mark); }
        }

        private string ClipsKey(WorldObjectState first) => first.Id + "|" + _b.ModelLoaded(first.Id) + "|" + string.Join("|", _b.ClipsOf(first.Id));

        /// <summary>The selected models (only imported ones can be animated).</summary>
        private List<Guid> SelectedModels() => _b.Selection.Where(id => _b.StateOf(id)?.PrimitiveType == "mesh").ToList();

        private void SetAnimated(bool on)
        {
            if (_filling) return; var models = SelectedModels(); if (models.Count == 0) { Say("Selecione um modelo importado.", true); return; }
            _b.Edit(models, s =>
            {
                var material = Parse(s.MaterialJson); var clips = _b.ClipsOf(s.Id);
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
                var material = Parse(s.MaterialJson); if (material["animation"] is not JsonObject animation) return;
                if (keepPoint && Flag(animation, "playing", false)) { var (position, length) = _b.ClipPoint(s.Id); if (length > 0f) animation["offset"] = Math.Round(position, 3); }
                change(animation); animation.Remove("since");   // the server writes when this started
                s.MaterialJson = material.ToJsonString();
            }, "animação");
        }
    }
}
