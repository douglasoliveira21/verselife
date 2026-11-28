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
    /// <summary>The Object tab: locked, physical, temporary and phantom; position, size and rotation (the viewer's axes, Z up); the kind of shape and its geometry.</summary>
    public sealed partial class BuildWindow
    {
        private static readonly string[] PositionNames = { "Pos X", "Pos Y", "Pos Z" }, RotationNames = { "Rot X", "Rot Y", "Rot Z" }, SizeNames = { "Scale X", "Scale Y", "Scale Z" };
        private Toggle _trigger;

        /// <summary>The nine fields in the order position, rotation, size (X, Y, Z of the viewer).</summary>
        public TextField NumberField(int index) => _ui.Spinner(index < 3 ? PositionNames[index] : index < 6 ? RotationNames[index - 3] : SizeNames[index - 6]).Input;
        public Toggle PhysicalToggle => _ui.Check("Physical Checkbox Ctrl");

        private void BuildObject()
        {
            OnCheck("checkbox locked", v => _b.Protect(v));
            OnCheck("Physical Checkbox Ctrl", v => EditPhysics(p => p with { Physical = v })); OnCheck("Temporary Checkbox Ctrl", v => EditPhysics(p => p with { Temporary = v }));
            OnCheck("Phantom Checkbox Ctrl", v => EditPhysics(p => p with { Collision = !v }));
            // a LifeVerse extra, under Phantom
            _trigger = new Toggle { text = "Gatilho", tooltip = "Atravessável, e avisa os scripts de quem passa" }; _trigger.AddToClassList("bt-check"); var last = _ui.AreaOf("Phantom Checkbox Ctrl");
            _ui.Put("build-trigger", _trigger, new Rect(last.x, last.y + 26f - ScrollTop, last.width, 22f), _pages["Objeto"]);
            _trigger.RegisterValueChangedCallback(e => { if (!_filling) EditPhysics(p => p with { Trigger = e.newValue }); });

            for (var axis = 0; axis < 3; axis++)
            {
                var a = axis;
                OnNumber(PositionNames[a], -5000f, 5000f, .1f, "0.000", v => Transform(s => { var p = BuildAxes.ToViewerPosition(new Vector3(s.PositionX, s.PositionY, s.PositionZ)); p[a] = v; var g = BuildAxes.ToGamePosition(p); s.PositionX = g.x; s.PositionY = g.y; s.PositionZ = g.z; }, "posição"));
                OnNumber(SizeNames[a], .01f, 64f, .1f, "0.000", v => Transform(s => { var p = BuildAxes.ToViewerSize(new Vector3(s.ScaleX, s.ScaleY, s.ScaleZ)); p[a] = v; var g = BuildAxes.ToGameSize(p); s.ScaleX = g.x; s.ScaleY = g.y; s.ScaleZ = g.z; }, "tamanho"));
                OnNumber(RotationNames[a], -360f, 360f, 1f, "0.00", v => Transform(s => { var r = BuildAxes.ToViewerRotation(new Vector3(s.RotationX, s.RotationY, s.RotationZ)); r[a] = v; var g = BuildAxes.ToGameRotation(r); s.RotationX = g.x; s.RotationY = g.y; s.RotationZ = g.z; }, "rotação"));
            }
            foreach (var name in new[] { "clipboard_pos_btn", "clipboard_size_btn", "clipboard_rot_btn", "clipboard_obj_params_btn" }) _ui.Unavailable(name, "copiar e colar propriedades chega com o editor 3D.");
            _ui.Unavailable("hole", "o formato do oco precisa mudar o gerador de formas e as regras da API.");
            _ui.Check("checkbox locked").tooltip = "Protege contra edição por engano";

            OnPick("comboBaseType", i => { var types = ShapeChoices(); if (i < types.Count) _b.Edit(_b.Selection.ToList(), s => s.PrimitiveType = types[i], "forma"); });
            foreach (var field in BuildShape.Fields)
            {
                var captured = field; var spinner = _ui.Spinner(field.Name); if (spinner == null) continue; spinner.Step = field.Step; spinner.Format = field.Format;
                spinner.Committed += v => { if (_filling || First() is not { } first) return; var kind = BuildShape.KindOf(first.PrimitiveType); EditGeometry(g => captured.Write(kind, g, v)); };
                spinner.Refused += why => Say(why, true);
            }
        }

        /// <summary>The number field of a shape value, by the viewer's name ("Scale 1" is the hollow, in percent).</summary>
        public BuildSpinner ShapeField(string name) => _ui.Spinner(name);

        private void Transform(Action<WorldObjectState> change, string label)
        {
            if (_b.Selection.Count != 1) { Say("Valores exatos valem para um objeto de cada vez; use as setas para vários.", true); return; }
            _b.Edit(_b.Selection.ToList(), change, label);
        }

        private static List<string> ShapeChoices() => BuildCommands.Entries.Select(e => e.Type).ToList();

        private void RefreshObject(List<WorldObjectState> all, WorldObjectState first)
        {
            var has = first != null; var one = all.Count == 1; var modify = has && all.All(s => (s.OwnerRights & ItemRights.Modify) != 0);
            Check("checkbox locked", all, s => s.EditLocked, has);
            var physics = all.Select(s => PhysicsRules.Read(s.PhysicsJson)).ToList();
            Check("Physical Checkbox Ctrl", all, s => PhysicsRules.Read(s.PhysicsJson).Physical, has); Check("Temporary Checkbox Ctrl", all, s => PhysicsRules.Read(s.PhysicsJson).Temporary, has); Check("Phantom Checkbox Ctrl", all, s => !PhysicsRules.Read(s.PhysicsJson).Collision, has);
            _trigger.SetEnabled(has); _trigger.showMixedValue = physics.Select(p => p.Trigger).Distinct().Count() > 1; if (!_trigger.showMixedValue) _trigger.SetValueWithoutNotify(has && physics[0].Trigger);

            var position = has ? BuildAxes.ToViewerPosition(new Vector3(first.PositionX, first.PositionY, first.PositionZ)) : Vector3.zero;
            var size = has ? BuildAxes.ToViewerSize(new Vector3(first.ScaleX, first.ScaleY, first.ScaleZ)) : Vector3.zero;
            var turn = has ? BuildAxes.ToViewerRotation(new Vector3(first.RotationX, first.RotationY, first.RotationZ)) : Vector3.zero;
            for (var i = 0; i < 3; i++)
            {
                Number(PositionNames[i], one ? position[i] : (float?)null, one); Number(SizeNames[i], one ? size[i] : (float?)null, one); Number(RotationNames[i], one ? turn[i] : (float?)null, one);
            }

            var kind = one ? BuildShape.KindOf(first.PrimitiveType) : ShapeKind.None; var model = has && first.PrimitiveType == "mesh";
            var types = ShapeChoices();
            Choice("comboBaseType", model ? new List<string> { "Modelo 3D" } : types.Select(BuildCommands.LabelOf).ToList(), !has ? -1 : model ? 0 : all.Select(s => s.PrimitiveType).Distinct().Count() > 1 ? -2 : types.IndexOf(first.PrimitiveType), one && !model && modify);
            if (!has) _ui.Combo("comboBaseType")?.SetValueWithoutNotify("—");
            foreach (var label in new[] { "text cut", "text hollow", "Hollow Shape", "text twist", "text skew", "scale_hole", "scale_taper", "advanced_cut", "advanced_dimple", "advanced_slice", "text taper2", "text radius delta", "text revolutions", "text topshear" })
                _ui.Visible(label, BuildShape.Shows(label, kind) || !has && (label == "text cut" || label == "text hollow" || label == "Hollow Shape" || label == "text twist" || label == "scale_taper" || label == "text topshear" || label == "advanced_slice"));
            _ui.Visible("hole", kind != ShapeKind.None || !has); if (_ui.Combo("hole") is { } hole) { hole.choices = new List<string> { "Padrão" }; hole.SetValueWithoutNotify("Padrão"); }
            var shape = one ? PrimParams.Parse(first.GeometryJson) : new PrimParams();
            foreach (var field in BuildShape.Fields)
            {
                var spinner = _ui.Spinner(field.Name); if (spinner == null) continue;
                var shown = has ? field.On.Contains(kind) : field.On.Contains(ShapeKind.Straight);
                _ui.Visible(field.Name, shown); var (min, max) = BuildShape.Limits(field, kind); spinner.Min = min; spinner.Max = max;
                Number(field.Name, one && kind != ShapeKind.None ? field.Read(kind, first.PrimitiveType, shape) : (float?)null, one && kind != ShapeKind.None && modify);
            }
            _ui.Visible("sculpt mirror control", false); _ui.Visible("sculpt invert control", false); _ui.Visible("label sculpt type", false); _ui.Visible("sculpt type control", false);
        }
    }
}
