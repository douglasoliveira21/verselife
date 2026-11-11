using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// "Zoom no objeto": a camera that flies to the object and orbits it (right button turns, the wheel zooms). Esc, or any key that walks the avatar, gives the view back to the
/// player's own camera. It belongs to the click action, not to the editor, so it works for anybody who clicks an object.
/// </summary>
public partial class ObjectZoomCamera : Camera3D
{
    private Camera3D? _previous;
    private Vector3 _target;
    private float _distance = 4, _goalDistance = 4, _yaw, _pitch = .35f;
    private bool _orbiting;

    public bool Active { get; private set; }
    public Vector3 Target => _target;
    public float DistanceForTesting => _goalDistance;

    /// <summary>Starts looking at an object of the given size from the side the player's camera was on.</summary>
    public void Enter(Camera3D previous, Vector3 target, float radius)
    {
        _previous = previous; _target = target; Fov = 60;
        _goalDistance = _distance = OrbitMath.FocusDistance(Math.Max(.3f, radius), Fov);
        var away = previous.GlobalPosition - target; away.Y = 0; _yaw = away.LengthSquared() < 1e-4f ? 0 : Mathf.Atan2(away.X, away.Z);
        Active = true; Current = true; Place();
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false; _orbiting = false;
        if (_previous is not null && IsInstanceValid(_previous)) _previous.Current = true;
    }

    private void Place()
    {
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        GlobalPosition = _target + offset; LookAt(_target, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        _distance = Mathf.Lerp(_distance, _goalDistance, 1f - Mathf.Exp(-10f * (float)delta)); Place();
        if (Input.IsActionPressed("move_forward") || Input.IsActionPressed("move_back") || Input.IsActionPressed("move_left") || Input.IsActionPressed("move_right")) Exit();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!Active) return;
        if (input.IsActionPressed("ui_cancel")) { Exit(); GetViewport().SetInputAsHandled(); return; }
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } button: _orbiting = button.Pressed; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _goalDistance = OrbitMath.Zoom(_goalDistance, 1); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _goalDistance = OrbitMath.Zoom(_goalDistance, -1); break;
            case InputEventMouseMotion motion when _orbiting: _yaw -= motion.Relative.X * .006f; _pitch = OrbitMath.ClampPitch(_pitch + motion.Relative.Y * .006f); break;
        }
    }

    public void OrbitForTesting(float yaw, float pitch) { _yaw += yaw; _pitch = OrbitMath.ClampPitch(_pitch + pitch); Place(); }
}
