using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>A placement: where an object sits relative to its attachment point (metres), how it is turned (degrees) and how big it is (per axis).</summary>
public readonly record struct Placement(Vector3 Position, Vector3 Rotation, Vector3 Scale)
{
    public static readonly Placement Default = new(Vector3.Zero, Vector3.Zero, Vector3.One);
    public bool Same(Placement other) => Position.IsEqualApprox(other.Position) && Rotation.IsEqualApprox(other.Rotation) && Scale.IsEqualApprox(other.Scale);
}

/// <summary>
/// The state of one placement being edited: the values, the limits the server enforces, undo and redo, the copy that can be pasted onto another object. Nothing here touches the screen or the
/// network, so every rule can be tried without a scene. A gesture (one drag) is a single step of the history.
/// </summary>
public sealed class AttachmentEditModel
{
    public const float Epsilon = 1e-5f;
    /// <summary>What copy put aside; shared by every editor so a placement can be taken to another object.</summary>
    public static Placement? Clipboard { get; set; }

    private readonly List<Placement> _undo = [], _redo = [];
    private Placement _now;
    private bool _gesture;

    public AttachmentEditModel(Placement saved, AttachmentPoints.Limits? limits = null) { Saved = _now = Clamp(saved, limits ?? AttachmentPoints.DefaultLimits); Limits = limits ?? AttachmentPoints.DefaultLimits; }

    public AttachmentPoints.Limits Limits { get; }
    /// <summary>What the server holds: what "cancel" goes back to.</summary>
    public Placement Saved { get; private set; }
    public Placement Now => _now;
    public Vector3 Position => _now.Position;
    public Vector3 Rotation => _now.Rotation;
    public Vector3 Scale => _now.Scale;
    public bool KeepProportions { get; set; } = true;
    public bool Dirty => !_now.Same(Saved);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    private Placement Clamp(Placement p, AttachmentPoints.Limits limits) => new(
        new Vector3(Mathf.Clamp(p.Position.X, -limits.MaxOffset, limits.MaxOffset), Mathf.Clamp(p.Position.Y, -limits.MaxOffset, limits.MaxOffset), Mathf.Clamp(p.Position.Z, -limits.MaxOffset, limits.MaxOffset)),
        new Vector3(Wrap(p.Rotation.X), Wrap(p.Rotation.Y), Wrap(p.Rotation.Z)),
        new Vector3(Mathf.Clamp(p.Scale.X, limits.MinScale, limits.MaxScale), Mathf.Clamp(p.Scale.Y, limits.MinScale, limits.MaxScale), Mathf.Clamp(p.Scale.Z, limits.MinScale, limits.MaxScale)));

    /// <summary>An angle in (-180, 180].</summary>
    public static float Wrap(float degrees) { var w = Mathf.PosMod(degrees + 180f, 360f) - 180f; return Mathf.Abs(w + 180f) < 1e-4f ? 180f : w; }

    // ---- changing ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Sets new values (what is limited is brought within the limits). One step of the history, unless a gesture is in progress, in which case the gesture's first step covers it.</summary>
    public bool Set(Placement value)
    {
        var next = Clamp(value, Limits); if (next.Same(_now)) return false;
        if (!_gesture) { _undo.Add(_now); _redo.Clear(); Trim(); }
        _now = next; Changed?.Invoke(); return true;
    }

    public bool SetPosition(Vector3 value) => Set(_now with { Position = value });
    public bool SetRotation(Vector3 value) => Set(_now with { Rotation = value });
    public bool SetScale(Vector3 value) => Set(_now with { Scale = value });

    /// <summary>Changes one axis of the scale; with the proportions kept the other axes follow by the same ratio.</summary>
    public bool SetScaleAxis(int axis, float value)
    {
        var s = _now.Scale; var old = s[axis]; value = Mathf.Clamp(value, Limits.MinScale, Limits.MaxScale);
        if (KeepProportions && Mathf.Abs(old) > Epsilon)
        {
            var ratio = value / old; var scaled = s * ratio;
            // never let the other axes leave the limits: the whole thing stops where the first one would
            var worst = Mathf.Max(scaled.X, Mathf.Max(scaled.Y, scaled.Z)); var least = Mathf.Min(scaled.X, Mathf.Min(scaled.Y, scaled.Z));
            if (worst > Limits.MaxScale) scaled *= Limits.MaxScale / worst; if (least < Limits.MinScale) scaled *= Limits.MinScale / least;
            return SetScale(scaled);
        }
        s[axis] = value; return SetScale(s);
    }

    public bool ResetPosition() => Set(_now with { Position = Vector3.Zero });
    public bool ResetRotation() => Set(_now with { Rotation = Vector3.Zero });
    public bool ResetScale() => Set(_now with { Scale = Vector3.One });
    /// <summary>Puts the object on its point: no offset (the rotation and the scale stay).</summary>
    public bool CenterOnPoint() => ResetPosition();

    /// <summary>The start of a drag: the history takes one step for the whole gesture, however many times the values change.</summary>
    public void BeginGesture() { if (_gesture) return; _gesture = true; _gestureStart = _now; }
    private Placement _gestureStart;
    public void EndGesture() { if (!_gesture) return; _gesture = false; if (!_gestureStart.Same(_now)) { _undo.Add(_gestureStart); _redo.Clear(); Trim(); Changed?.Invoke(); } }
    public bool Gesturing => _gesture;

    private void Trim() { if (_undo.Count > 100) _undo.RemoveAt(0); }

    public bool Undo() { if (_undo.Count == 0) return false; _redo.Add(_now); _now = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Changed?.Invoke(); return true; }
    public bool Redo() { if (_redo.Count == 0) return false; _undo.Add(_now); _now = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Changed?.Invoke(); return true; }

    /// <summary>Puts the current placement aside so it can be pasted onto another object.</summary>
    public void Copy() => Clipboard = _now;
    public bool Paste() => Clipboard is { } copied && Set(copied);

    /// <summary>The server took the current values: they are now what "cancel" goes back to, and the history starts over.</summary>
    public void MarkSaved() { Saved = _now; _undo.Clear(); _redo.Clear(); Changed?.Invoke(); }

    /// <summary>Back to what the server holds (cancel).</summary>
    public void Revert() { _undo.Clear(); _redo.Clear(); _gesture = false; _now = Saved; Changed?.Invoke(); }

    // ---- snapping ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Rounds to a step (0 or less: free).</summary>
    public static float Snap(float value, float step) => step <= 1e-6f ? value : Mathf.Round(value / step) * step;
}
