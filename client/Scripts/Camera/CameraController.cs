using Godot;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Camera;

/// <summary>
/// Reads the mouse and the keys for the camera of the player: ALT, CTRL+ALT and CTRL+ALT+SHIFT with the left button, CTRL and the wheel, ESC, ALT+V. It decides which movement a gesture is
/// (<see cref="CameraInputRules"/>) and asks the player to do it; the camera itself is the one of the player, there is no second one. Nothing here goes to the network: the camera is only seen by the person.
/// It sits below the player in the tree, so it sees the mouse before the player does and takes what is the camera's; clicks on windows and menus never get here (the interface keeps them).
/// </summary>
public sealed partial class CameraController : Node
{
    /// <summary>A click that moves less than this (pixels) is a click, not a drag.</summary>
    public const float ClickSlop = 4f;

    public PlayerController? Player { get; set; }
    /// <summary>Tests: do not ask the keyboard whether CTRL is still down (it never is, there is no keyboard).</summary>
    public bool PollKeyboard { get; set; } = true;

    private CameraGesture _gesture = CameraGesture.None;
    private float _travel;
    private bool _ctrlHeld, _ctrlUsed;

    public CameraGesture Gesture => _gesture;
    public bool Gesturing => _gesture != CameraGesture.None;
    public bool CtrlHeld => _ctrlHeld;
    /// <summary>True when CTRL was used for the camera while it was held (orbit, zoom or pan) and has not been let go yet.</summary>
    public bool CtrlUsed => _ctrlUsed;

    public override void _UnhandledInput(InputEvent input)
    {
        if (Handle(input)) GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // CTRL let go while the window did not see it (another window took the focus): the same as letting it go
        if (PollKeyboard && _ctrlHeld && !Input.IsKeyPressed(Godot.Key.Ctrl)) CtrlReleased();
    }

    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && _ctrlHeld) CtrlReleased(); }

    private void CtrlReleased()
    {
        _ctrlHeld = false; var used = _ctrlUsed; _ctrlUsed = false;
        if (used && !CameraSettings.Current.KeepAfterCtrl && Player is { FreeViewActive: true }) Player.ExitFreeView();
    }

    /// <summary>The camera of the build editor, when it is the one the person sees (it takes over from the avatar's while building).</summary>
    private BuildCamera? EditCamera => Player?.GetParent() is { } world && LifeVerse.Client.Build.BuildHost.Editor(world) is { OwnsTheMouse: true } editor ? editor.EditCamera : null;

    /// <summary>Looks at what is under a place of the screen. False when there is nothing to look at there.</summary>
    public bool FocusAtScreen(Vector2 screen) => Player?.CameraTargetResolver?.Invoke(screen) is { } target && CameraFocus.FocusOn(Player, target);

    /// <summary>Handles an event; true when it was the camera's (the caller then keeps it from everybody else).</summary>
    public bool Handle(InputEvent input)
    {
        if (Player is null || Player.ControlsLocked) return false;
        var options = CameraSettings.Current;
        switch (input)
        {
            case InputEventKey { Keycode: Godot.Key.Ctrl } ctrl:
                if (ctrl.Pressed) _ctrlHeld = true; else if (_ctrlHeld) CtrlReleased();
                return false; // the key itself stays everybody's
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    var gesture = CameraInputRules.Classify(click.CtrlPressed, click.AltPressed, click.ShiftPressed, options);
                    if (gesture == CameraGesture.None)
                    {
                        // a double click on a person or an object (when asked for) puts the camera on it; on the ground it still walks
                        if (click.DoubleClick && options.FocusOnDoubleClick && !click.CtrlPressed && !click.AltPressed && !click.ShiftPressed && Player.CameraTargetResolver?.Invoke(click.Position) is { } target and not GroundTarget)
                            return CameraFocus.FocusOn(Player, target);
                        return false;
                    }
                    _gesture = gesture; _travel = 0; if (EditCamera is not null) return true; // the editing camera is moved by the motion below; nothing starts on the avatar's camera
                    if (click.CtrlPressed && !click.AltPressed) { _ctrlHeld = true; _ctrlUsed = true; }
                    Player.BeginCameraGesture(); return true;
                }
                if (_gesture == CameraGesture.None) return false;
                var ended = _gesture; _gesture = CameraGesture.None;
                // ALT and a click without a drag: look at what is there (the way Second Life does)
                if (ended == CameraGesture.OrbitDolly && _travel < ClickSlop) FocusAtScreen(click.Position);
                return true;
            case InputEventMouseMotion motion when _gesture != CameraGesture.None && EditCamera is { } build:
                _travel += motion.Relative.Length();
                var turnH = options.SensitivityH * 2f * (options.InvertH ? -1f : 1f); var turnV = options.SensitivityV * 2f * (options.InvertV ? -1f : 1f);
                switch (_gesture)
                {
                    case CameraGesture.OrbitDolly: build.OrbitBy(motion.Relative.X * turnH, 0); build.Dolly(motion.Relative.Y); break;
                    case CameraGesture.Orbit or CameraGesture.CtrlOrbit: build.OrbitBy(motion.Relative.X * turnH, motion.Relative.Y * turnV); break;
                    case CameraGesture.Pan: build.PanBy(motion.Relative.X, motion.Relative.Y); break;
                }
                return true;
            case InputEventMouseMotion motion when _gesture != CameraGesture.None:
                _travel += motion.Relative.Length();
                switch (_gesture)
                {
                    case CameraGesture.OrbitDolly: Player.OrbitCamera(motion.Relative.X, 0, horizontalOnly: true); Player.DollyCamera(motion.Relative.Y); break;
                    case CameraGesture.Orbit or CameraGesture.CtrlOrbit: Player.OrbitCamera(motion.Relative.X, motion.Relative.Y); break;
                    case CameraGesture.Pan: Player.PanCamera(motion.Relative.X, motion.Relative.Y); break;
                }
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } when EditCamera is not null: return false;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                var notches = wheel.ButtonIndex == MouseButton.WheelUp ? 1f : -1f;
                if (CameraInputRules.ClassifyWheel(wheel.CtrlPressed) == CameraWheel.Precision)
                {
                    if (Player.DetachedActive) return false;
                    Player.BeginCameraGesture(); Player.ZoomCamera(notches, precision: true); if (options.CtrlSimplified && !wheel.AltPressed) { _ctrlHeld = true; _ctrlUsed = true; } return true;
                }
                if (Player.FreeViewActive) { Player.ZoomCamera(notches, precision: false); return true; }
                return false; // the normal wheel of the avatar's camera is the player's own
            case InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.V, AltPressed: true } when options.AllowDetached:
                if (Player.DetachedActive) Player.ExitDetached(); else Player.EnterDetached();
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Escape } when Player.FreeViewActive || Player.DetachedActive:
                _gesture = CameraGesture.None; Player.ExitFreeView(); Player.ExitDetached(); return true;
        }
        return false;
    }
}
