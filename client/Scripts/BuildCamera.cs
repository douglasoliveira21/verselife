using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// The editing camera: it circles the selection instead of following the avatar, so tall and tiny objects can be looked at from any
/// side. Right mouse button drags to orbit, middle button (or Shift + right) to pan, the wheel zooms by steps; the editor can also focus
/// it on the selection. It takes over from the player's camera when switched on and gives it back when switched off.
public partial class BuildCamera : Camera3D
{
    private Camera3D? _previous;
    private Vector3 _target, _goalTarget;
    private float _yaw, _pitch = -.5f, _distance = 10f, _goalDistance = 10f;
    private bool _orbiting, _panning;

    public bool Active { get; private set; }
    public Vector3 Target => _goalTarget;
    public float Distance => _goalDistance;
    public event Action<bool>? ActiveChanged;

    /// Switches to this camera, keeping the view the player had as the starting point.
    public void Enter(Camera3D previous, Vector3 target, float distance)
    {
        _previous = previous;
        _goalTarget = _target = target;
        _goalDistance = _distance = Mathf.Clamp(distance, OrbitMath.MinDistance, OrbitMath.MaxDistance);
        var offset = previous.GlobalPosition - target;
        if (offset.LengthSquared() > .01f) { _yaw = Mathf.Atan2(offset.X, offset.Z); _pitch = OrbitMath.ClampPitch(-Mathf.Asin(Mathf.Clamp(offset.Y / offset.Length(), -1f, 1f))); }
        Active = true; Fov = previous.Fov; Place(); MakeCurrent();
        ActiveChanged?.Invoke(true);
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false; _orbiting = _panning = false;
        if (_previous is not null && IsInstanceValid(_previous)) _previous.MakeCurrent();
        ActiveChanged?.Invoke(false);
    }

    /// Looks at a point from where the camera is now, close enough to see something of that radius well.
    public void Focus(Vector3 target, float radius) { _goalTarget = target; _goalDistance = OrbitMath.FocusDistance(radius, Fov); }

    public void ZoomSteps(float steps) => _goalDistance = OrbitMath.Zoom(_goalDistance, steps);
    public void OrbitBy(float yaw, float pitch) { _yaw -= yaw; _pitch = OrbitMath.ClampPitch(_pitch - pitch); }
    public void PanBy(float dx, float dy) { var moved = OrbitMath.Pan(Convert(_goalTarget), Convert(GlobalBasis.X), Convert(GlobalBasis.Y), dx, dy, _goalDistance); _goalTarget = new Vector3(moved.X, moved.Y, moved.Z); }

    private static System.Numerics.Vector3 Convert(Vector3 v) => new(v.X, v.Y, v.Z);

    public override void _Process(double delta)
    {
        if (!Active) return;
        var weight = 1f - Mathf.Exp(-12f * (float)delta);
        _target = _target.Lerp(_goalTarget, weight); _distance = Mathf.Lerp(_distance, _goalDistance, weight);
        Place();
    }

    private void Place()
    {
        var position = OrbitMath.Position(Convert(_target), _yaw, _pitch, _distance);
        GlobalPosition = new Vector3(position.X, position.Y, position.Z);
        LookAt(_target, Vector3.Up);
    }

    public override void _Input(InputEvent input)
    {
        if (!Active) return;
        if (input is InputEventMouseButton button)
        {
            var overWindow = GetViewport().GuiGetHoveredControl() is not null;
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && button.Pressed && !overWindow) { ZoomSteps(button.ButtonIndex == MouseButton.WheelUp ? 1 : -1); GetViewport().SetInputAsHandled(); return; }
            if (button.ButtonIndex == MouseButton.Right) { if (!button.Pressed || !overWindow) { _orbiting = button.Pressed && !button.ShiftPressed; _panning = button.Pressed && button.ShiftPressed; } if (button.Pressed && !overWindow) GetViewport().SetInputAsHandled(); }
            if (button.ButtonIndex == MouseButton.Middle) { _panning = button.Pressed && !overWindow; if (_panning) GetViewport().SetInputAsHandled(); }
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (_orbiting) { OrbitBy(motion.Relative.X * .006f, motion.Relative.Y * .006f); GetViewport().SetInputAsHandled(); }
            else if (_panning) { PanBy(motion.Relative.X, motion.Relative.Y); GetViewport().SetInputAsHandled(); }
        }
    }
}
