using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>Where a worn object sits on its point: position (metres), rotation (degrees) and scale, each x y z in the point's own space.</summary>
    public sealed class Placement
    {
        public readonly float[] P, R, S;
        public Placement(float[] p, float[] r, float[] s) { P = (float[])p.Clone(); R = (float[])r.Clone(); S = (float[])s.Clone(); }
        public Placement Copy() => new Placement(P, R, S);
        public bool Same(Placement o) => o != null && P.SequenceEqual(o.P) && R.SequenceEqual(o.R) && S.SequenceEqual(o.S);
    }

    /// <summary>The rules of editing a placement (the Godot client's <c>AttachmentEditModel</c>): a drag along an axis moves, turns or stretches by steps, keeping proportions when asked, within the limits of the platform; with undo and redo.</summary>
    public sealed class AttachmentEditRules
    {
        public enum Tool { Move, Rotate, Scale }
        public const float MaxOffset = 2f, MinScale = .01f, MaxScale = 10f;
        public static readonly (string Label, float Step)[] PositionSteps = { ("Livre", 0f), ("1 cm", .01f), ("5 cm", .05f), ("10 cm", .1f) };
        public static readonly (string Label, float Step)[] RotationSteps = { ("Livre", 0f), ("1°", 1f), ("5°", 5f), ("15°", 15f), ("45°", 45f) };
        private readonly List<Placement> _undo = new List<Placement>(), _redo = new List<Placement>();
        public Placement Saved { get; private set; }
        public Placement Now { get; private set; }
        public bool KeepProportions = true;
        public float PositionStep, RotationStep = 5f;
        public bool Dirty => !Now.Same(Saved);
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public AttachmentEditRules(Placement saved) { Saved = saved.Copy(); Now = saved.Copy(); }

        public static float Snap(float value, float step) => step <= 0f ? value : Mathf.Round(value / step) * step;
        public static float Wrap(float degrees) => Mathf.Repeat(degrees + 180f, 360f) - 180f;

        /// <summary>What a drag of <paramref name="amount"/> (metres for move and scale, degrees for turn) along an axis makes of a starting placement.</summary>
        public Placement Dragged(Placement from, Tool tool, int axis, float amount)
        {
            var next = from.Copy();
            switch (tool)
            {
                case Tool.Move: next.P[axis] = Mathf.Clamp(Snap(from.P[axis] + amount, PositionStep), -MaxOffset, MaxOffset); break;
                case Tool.Rotate: next.R[axis] = Wrap(Snap(from.R[axis] + amount, RotationStep)); break;
                case Tool.Scale:
                    if (KeepProportions) { var factor = Mathf.Max(.05f, 1f + amount / Mathf.Max(.05f, from.S[axis])); for (var i = 0; i < 3; i++) next.S[i] = Mathf.Clamp(from.S[i] * factor, MinScale, MaxScale); }
                    else next.S[axis] = Mathf.Clamp(from.S[axis] + amount, MinScale, MaxScale);
                    break;
            }
            return next;
        }

        /// <summary>A change becomes the placement now (and can be undone).</summary>
        public void Commit(Placement next) { if (next.Same(Now)) return; _undo.Add(Now.Copy()); _redo.Clear(); Now = next.Copy(); }
        public void Undo() { if (!CanUndo) return; _redo.Add(Now.Copy()); Now = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1); }
        public void Redo() { if (!CanRedo) return; _undo.Add(Now.Copy()); Now = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1); }
        public void Accept() { Saved = Now.Copy(); }
    }

    /// <summary>
    /// The editor of where an object sits on the body, as the Godot client's <c>AttachmentEditor</c>: the camera goes close to the object, the object gets the handles of the build editor (arrows to move, rings
    /// to turn, boxes to stretch) in the space of its point, and a panel shows and takes the numbers. What is changed is shown at once on the person's own body and kept only by "Salvar", which asks the
    /// platform; "Cancelar" (or closing) puts everything back.
    /// </summary>
    public sealed class AttachmentEditor : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Func<AvatarAttachments> _body; private readonly Func<Camera> _camera; private readonly Func<FollowCamera> _follow; private readonly Action _saved;
        private readonly AttachmentData _target; private readonly Gizmo _gizmo; private readonly Label _status, _where; private readonly TextField[] _fields = new TextField[9]; private readonly Toggle _proportions;
        private readonly Dictionary<AttachmentEditRules.Tool, Button> _tools = new Dictionary<AttachmentEditRules.Tool, Button>(); private bool _filling, _closing, _saving;
        private int _axis = -1; private Vector2 _from; private Placement _start;
        public AttachmentEditRules Model { get; }
        public AttachmentEditRules.Tool Tool { get; private set; } = AttachmentEditRules.Tool.Move;
        public string Status => _status.text;
        public IReadOnlyList<Vector2> HandleTips => _gizmo.Tips;
        public bool Saved { get; private set; }

        public AttachmentEditor(ApiContext context, AttachmentData target, Func<AvatarAttachments> body, Func<Camera> camera, Func<FollowCamera> follow, Action saved)
            : base(context.Ui, "attachment-editor", "Editar no corpo: " + (string.IsNullOrWhiteSpace(target.Name) ? "objeto" : target.Name), 360f, -1f, new Vector2(20, 120))
        {
            _c = context; _target = target; _body = body; _camera = camera; _follow = follow; _saved = saved;
            Model = new AttachmentEditRules(new Placement(target.Position, target.Rotation, target.Scale));
            _where = UiKit.Text("Ponto: " + target.PointName, "lv-small", "lv-muted"); Body.Add(_where);
            var tools = UiKit.Row(); foreach (var (tool, text) in new[] { (AttachmentEditRules.Tool.Move, "Mover"), (AttachmentEditRules.Tool.Rotate, "Girar"), (AttachmentEditRules.Tool.Scale, "Escalar") }) { var t = tool; var b = UiKit.Button(text, () => SetTool(t)); _tools[t] = b; tools.Add(b); } Body.Add(tools);
            string[] names = { "Posição", "Rotação", "Escala" };
            for (var row = 0; row < 3; row++)
            {
                var line = UiKit.Row(UiKit.Text(names[row], "lv-small")); line.Children().First().style.width = 64;
                for (var axis = 0; axis < 3; axis++) { var index = row * 3 + axis; var f = UiKit.Field("", false, 12); f.style.width = 78; f.RegisterValueChangedCallback(_ => Typed()); _fields[index] = f; line.Add(f); }
                Body.Add(line);
            }
            _proportions = UiKit.Check("Manter as proporções", true, on => Model.KeepProportions = on); Body.Add(_proportions);
            var position = new DropdownField("Passo da posição", AttachmentEditRules.PositionSteps.Select(s => s.Label).ToList(), 0); position.RegisterValueChangedCallback(_ => Model.PositionStep = AttachmentEditRules.PositionSteps[Math.Max(0, position.index)].Step); Body.Add(position);
            var rotation = new DropdownField("Passo da rotação", AttachmentEditRules.RotationSteps.Select(s => s.Label).ToList(), 2); rotation.RegisterValueChangedCallback(_ => Model.RotationStep = AttachmentEditRules.RotationSteps[Math.Max(0, rotation.index)].Step); Body.Add(rotation);
            Body.Add(UiKit.Row(UiKit.Button("Desfazer", () => { Model.Undo(); Show(); }), UiKit.Button("Refazer", () => { Model.Redo(); Show(); }), UiKit.Spacer(), UiKit.Button("Cancelar", Cancel), UiKit.Button("Salvar", Save, "lv-btn-primary")));
            _status = UiKit.Text("Arraste as setas, os anéis ou as caixas no objeto. Alt arrasta sem passo.", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            _gizmo = new Gizmo(this); context.Ui.HudLayer.Add(_gizmo);
            Body.schedule.Execute(Tick).Every(16);
            Closed += () => { if (!_closing) { _closing = true; Revert(); } _gizmo.RemoveFromHierarchy(); var f = _follow(); if (f != null) f.ZoomTarget = null; };
            SetTool(AttachmentEditRules.Tool.Move); Show(); Frame();
        }

        /// <summary>The camera comes close to the object.</summary>
        private void Frame()
        {
            var holder = _body()?.HolderOf(_target.Id); var follow = _follow(); if (holder == null || follow == null) return;
            var size = 0f; foreach (var r in holder.GetComponentsInChildren<Renderer>()) size = Mathf.Max(size, r.bounds.size.magnitude); follow.ZoomTarget = holder.position; follow.ZoomDistance = Mathf.Clamp(size * 3.2f, .45f, 2.6f);
        }

        public void SetTool(AttachmentEditRules.Tool tool) { Tool = tool; foreach (var p in _tools) p.Value.EnableInClassList("lv-btn-primary", p.Key == tool); }

        private void Show()
        {
            _filling = true; var now = Model.Now; for (var i = 0; i < 3; i++) { _fields[i].value = F(now.P[i]); _fields[3 + i].value = F(now.R[i]); _fields[6 + i].value = F(now.S[i]); } _filling = false;
            _body()?.Preview(_target.Id, now.P, now.R, now.S);
        }

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private void Typed()
        {
            if (_filling) return; var values = new float[9];
            for (var i = 0; i < 9; i++) if (!float.TryParse(_fields[i].value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return;
            var next = new Placement(values.Take(3).Select(v => Mathf.Clamp(v, -AttachmentEditRules.MaxOffset, AttachmentEditRules.MaxOffset)).ToArray(), values.Skip(3).Take(3).Select(AttachmentEditRules.Wrap).ToArray(), values.Skip(6).Select(v => Mathf.Clamp(v, AttachmentEditRules.MinScale, AttachmentEditRules.MaxScale)).ToArray());
            Model.Commit(next); _body()?.Preview(_target.Id, next.P, next.R, next.S);
        }

        /// <summary>A drag along a handle as the checks do it: an amount along an axis.</summary>
        public void DragBy(int axis, float amount) { Model.Commit(Model.Dragged(Model.Now, Tool, axis, amount)); Show(); }

        // ---- the handles on the object ----

        /// <summary>The axes of the point in the scene (game x, y, z; game z is Unity's −z).</summary>
        private Vector3 Axis(Transform holder, int axis) { var parent = holder.parent != null ? holder.parent : holder; return axis == 0 ? parent.right : axis == 1 ? parent.up : -parent.forward; }

        private void Tick()
        {
            if (Body.panel == null) return; var holder = _body()?.HolderOf(_target.Id); var camera = _camera(); _gizmo.Build(holder, camera); var mouse = Mouse.current; if (mouse == null || holder == null || camera == null) return;
            var screen = mouse.position.ReadValue(); var panel = new Vector2(screen.x, Screen.height - screen.y) / Mathf.Max(.01f, _c.Ui.Panel.scale);
            if (mouse.leftButton.wasPressedThisFrame && !UiKit.IsOver(_c.Ui.Root, screen)) { _axis = _gizmo.HandleAt(panel); if (_axis >= 0) { _from = panel; _start = Model.Now.Copy(); } }
            else if (_axis >= 0 && mouse.leftButton.isPressed)
            {
                var (a, b) = _gizmo.Segment(_axis); var along = b - a; var length = along.magnitude; if (length < 1f) return; var pixels = Vector2.Dot(panel - _from, along / length);
                var amount = Tool == AttachmentEditRules.Tool.Rotate ? pixels * .5f : pixels / length * _gizmo.Length;
                var free = Keyboard.current != null && Keyboard.current.altKey.isPressed; var p = Model.PositionStep; var r = Model.RotationStep; if (free) { Model.PositionStep = 0f; Model.RotationStep = 0f; }
                var next = Model.Dragged(_start, Tool, _axis, amount); Model.PositionStep = p; Model.RotationStep = r; _body()?.Preview(_target.Id, next.P, next.R, next.S); _dragging = next;
            }
            else if (_axis >= 0) { _axis = -1; if (_dragging != null) { Model.Commit(_dragging); _dragging = null; Show(); } }
        }
        private Placement _dragging;

        // ---- saving and cancelling ----

        public void Save()
        {
            if (_saving) return; if (!Model.Dirty) { _closing = true; Close(); return; }
            _saving = true; _status.text = "Salvando…"; var now = Model.Now;
            this.Then(_c.Api.ApiAsync("PATCH", AttachmentsWindow.Route + "/" + _target.Id, JsonSerializer.Serialize(new { position = now.P, rotation = now.R, scale = now.S, sway = _target.Sway })), r =>
            {
                _saving = false;
                if (r.Status >= 200 && r.Status < 300) { Model.Accept(); Saved = true; _target.Position = (float[])now.P.Clone(); _target.Rotation = (float[])now.R.Clone(); _target.Scale = (float[])now.S.Clone(); _c.Notices.Show("Posição do objeto salva.", NoticeKind.Good); _saved?.Invoke(); _closing = true; Close(); return; }
                _status.text = AttachmentsWindow.Explain(r.Body) ?? UiTasks.Explain(r.Status, r.Body); _status.AddToClassList("lv-bad");
            });
        }

        public void Cancel() { if (_saving) return; _closing = true; Revert(); Close(); }
        private void Revert() { var saved = Model.Saved; _body()?.Preview(_target.Id, saved.P, saved.R, saved.S); }

        /// <summary>The handles drawn over the object: an arrow, a ring or a box per axis, in the colours of the build editor.</summary>
        private sealed class Gizmo : VisualElement
        {
            private static readonly Color[] Colours = { new Color(.95f, .3f, .3f), new Color(.35f, .9f, .4f), new Color(.35f, .55f, 1f) };
            private readonly AttachmentEditor _editor; private readonly Vector2[] _a = new Vector2[3], _b = new Vector2[3]; private bool _shown;
            public float Length { get; private set; } = .2f;
            public IReadOnlyList<Vector2> Tips => _shown ? _b.ToList() : new List<Vector2>();
            public Gizmo(AttachmentEditor editor) { _editor = editor; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; generateVisualContent += Draw; }

            public void Build(Transform holder, Camera camera)
            {
                _shown = holder != null && camera != null && Vector3.Dot(camera.transform.forward, holder.position - camera.transform.position) > 0f; if (!_shown) { MarkDirtyRepaint(); return; }
                Length = Mathf.Clamp(Vector3.Distance(camera.transform.position, holder.position) * .22f, .05f, 1.5f); var scale = Mathf.Max(.01f, _editor._c.Ui.Panel.scale);
                Vector2 P(Vector3 w) { var s = camera.WorldToScreenPoint(w); return new Vector2(s.x, Screen.height - s.y) / scale; }
                for (var axis = 0; axis < 3; axis++) { _a[axis] = P(holder.position); _b[axis] = P(holder.position + _editor.Axis(holder, axis) * Length); }
                MarkDirtyRepaint();
            }

            public (Vector2, Vector2) Segment(int axis) => (_a[axis], _b[axis]);
            public int HandleAt(Vector2 panel) { if (!_shown) return -1; for (var axis = 0; axis < 3; axis++) if (BuildEditor.DistanceToSegment(panel, _a[axis], _b[axis]) < 12f && (panel - _a[axis]).magnitude > 6f) return axis; return -1; }

            private void Draw(MeshGenerationContext context)
            {
                if (!_shown) return; var painter = context.painter2D; painter.lineWidth = 3.2f;
                for (var axis = 0; axis < 3; axis++)
                {
                    var a = _a[axis]; var b = _b[axis]; painter.strokeColor = Colours[axis]; painter.fillColor = Colours[axis];
                    painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
                    var dir = (b - a).sqrMagnitude > .01f ? (b - a).normalized : Vector2.right; var side = new Vector2(-dir.y, dir.x);
                    painter.BeginPath();
                    switch (_editor.Tool)
                    {
                        case AttachmentEditRules.Tool.Move: painter.MoveTo(b + dir * 12f); painter.LineTo(b + side * 6f); painter.LineTo(b - side * 6f); painter.ClosePath(); painter.Fill(); break;
                        case AttachmentEditRules.Tool.Rotate: painter.Arc(b, 8f, 0f, 360f); painter.Stroke(); break;
                        default: painter.MoveTo(b + new Vector2(-6f, -6f)); painter.LineTo(b + new Vector2(6f, -6f)); painter.LineTo(b + new Vector2(6f, 6f)); painter.LineTo(b + new Vector2(-6f, 6f)); painter.ClosePath(); painter.Fill(); break;
                    }
                }
            }
        }
    }
}
