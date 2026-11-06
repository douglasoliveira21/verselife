using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Vehicles;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

public partial class PlayerController : CharacterBody3D
{
    private enum CameraMode { ThirdPerson, FirstPerson, FreeLook, Build, Photo }
    [Export] public float WalkSpeed { get; set; } = 3.2f;
    [Export] public float RunSpeed { get; set; } = 6.4f;
    [Export] public float FlySpeed { get; set; } = 7.5f;
    [Export] public float SwimSpeed { get; set; } = 3.5f;
    [Export] public float JumpVelocity { get; set; } = 5.2f;
    [Export] public float Acceleration { get; set; } = AuthoritativeMovement.Acceleration;
    [Export] public float Deceleration { get; set; } = AuthoritativeMovement.Deceleration;
    // A/D turn the avatar in place (radians per second); the camera swings behind it while moving.
    [Export] public float TurnRate { get; set; } = 2.6f;
    [Export] public float CameraFollowRate { get; set; } = 3.5f;
    private AvatarRig _rig = null!;
    private Node3D _cameraPivot = null!;
    private SpringArm3D _springArm = null!;
    private Camera3D _camera = null!;
    private Control _contextMenu = null!;
    private Label _status = null!;
    private InteractableSeat? _selectedSeat;
    private InteractableSeat? _seat;
    private bool _inVehicle;
    private bool _flying;
    private bool _volumeSwimming;
    private bool _flightRequestPending;
    private bool _flightRequestedState;
    private ulong _flightRequestAt;
    private bool _alwaysRun;
    private bool _doubleTapRun;
    private ulong _lastForwardPress;
    private float _spaceHeld;
    private bool _spaceFlightTriggered;
    private const ulong DoubleTapWindowMs = 320;
    private const float HoldToFlySeconds = .45f;
    private bool _oceanSwimming;
    private IslandTerrain? _terrainNode;
    private bool _swimming => _volumeSwimming || _oceanSwimming;
    private bool _firstPerson;
    private bool _cursorFree;
    private bool _rightDragging;
    private bool _rightDragMoved;
    private bool _wasGrounded;
    private float _landingTime;
    private float _emoteTime;
    private AvatarMotionState _emoteState;
    private float _cameraSensitivity = .003f;
    private bool _invertY;
    private float _cameraDistance = 5.2f;
    private float _targetYaw;
    private float _cameraYaw;
    private float _targetBodyYaw;
    private float _targetPitch;
    private CameraMode _cameraMode = CameraMode.ThirdPerson;
    private Vector3? _walkTarget;
    private ulong _lastWorldClick;
    private NetworkManager _network = null!;
    private uint _inputSequence;
    private InputFlags _pendingGesture;

    public override void _Ready()
    {
        FloorMaxAngle = Mathf.DegToRad(50);
        FloorSnapLength = .35f;
        FloorStopOnSlope = true;
        _rig = GetNode<AvatarRig>("AvatarRig");
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _network.RegisterLocalPlayer(this);
        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
        _camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
        _targetYaw = Rotation.Y; _cameraYaw = Rotation.Y; _targetBodyYaw = Rotation.Y; _targetPitch = _cameraPivot.Rotation.X;
        _contextMenu = GetNode<Control>("HUD/ContextMenu");
        _status = GetNode<Label>("HUD/Status");
        GetNode<Button>("HUD/ContextMenu/Sit").Pressed += ContextSit;
        GetNode<Button>("HUD/ContextMenu/Cancel").Pressed += () => _contextMenu.Visible = false;
        GetNode<Button>("HUD/EditAvatar").Pressed += () => GetNode<UIManager>("/root/UIManager").ShowCharacterCreator();
        BuildGesturePanel();
        // Keep the pointer available for the world UI. Camera orbit is opt-in
        // while dragging with the right mouse button, like a desktop viewer.
        _cursorFree = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var avatars = GetNode<AvatarManager>("/root/AvatarManager");
        avatars.AvatarLoaded += OnAvatarLoaded;
        var error = avatars.Load();
        if (error != Error.Ok) GD.PushWarning($"event=avatar_load_start_failed error={error}");
    }

    public override void _ExitTree()
    {
        _seat?.Release(this);
        _seat = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var avatars = GetNodeOrNull<AvatarManager>("/root/AvatarManager");
        if (avatars is not null) avatars.AvatarLoaded -= OnAvatarLoaded;
        _network?.UnregisterLocalPlayer(this);
    }

    public override void _Process(double delta)
    {
        var weight = 1f - Mathf.Exp(-14f * (float)delta);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _targetBodyYaw, weight), 0);
        // The pivot is a child of the body, so its local yaw is the camera yaw relative to the avatar.
        _cameraYaw = Mathf.LerpAngle(_cameraYaw, _targetYaw, weight);
        _cameraPivot.Rotation = new Vector3(Mathf.Lerp(_cameraPivot.Rotation.X, _targetPitch, weight), Mathf.Wrap(_cameraYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi), 0);
        // While swimming the camera rides higher so it stays above the waves instead of dipping under the surface.
        var pivotHeight = _mirror ? (_mirrorFace ? 1.62f : 1.05f) : _oceanSwimming ? 2.7f : 1.55f;
        _cameraPivot.Position = new Vector3(0, Mathf.Lerp(_cameraPivot.Position.Y, pivotHeight, weight * (_mirror ? .6f : .35f)), 0);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        // A text field owns the keyboard while it has focus. In particular, Space must
        // type a space instead of jumping and letter shortcuts must not open tools.
        if (IsTyping())
        {
            if (input.IsActionPressed("ui_cancel"))
            {
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (input.IsActionPressed("cursor_toggle")) SetCursorFree(true);
        if (input.IsActionPressed("first_person") && !_mirror) ToggleFirstPerson();
        if (input.IsActionPressed("camera_mode") && !_mirror) CycleCameraMode();
        if (input.IsActionPressed("camera_reset") && !_mirror) ResetCamera();
        if (input.IsActionPressed("ui_cancel"))
        {
            _contextMenu.Visible = false; _rightDragging = false; SetCursorFree(true);
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        }
        if (input.IsActionPressed("fly_toggle")) RequestFlightToggle();
        if (input.IsActionPressed("interact")) InteractOrStand();
        if (input.IsActionPressed("emote_wave")) StartGesture(AvatarMotionState.Wave, 2.5f, InputFlags.Wave);
        if (input.IsActionPressed("emote_dance")) StartGesture(AvatarMotionState.Dance, 5f, InputFlags.Dance);
        if (input is InputEventMouseMotion motion && (_rightDragging || _firstPerson))
        {
            if (motion.Relative.LengthSquared() > 0.01f) _rightDragMoved = true;
            _targetYaw -= motion.Relative.X * _cameraSensitivity;
            var direction = _invertY ? 1f : -1f;
            _targetPitch = Mathf.Clamp(_targetPitch + motion.Relative.Y * _cameraSensitivity * direction, -1.25f, .85f);
            if (_firstPerson) _targetBodyYaw = _targetYaw;
        }
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) SetZoom(_cameraDistance - .5f);
            if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) SetZoom(_cameraDistance + .5f);
            if (button.ButtonIndex == MouseButton.Right)
            {
                if (button.Pressed)
                {
                    _rightDragging = true;
                    _rightDragMoved = false;
                }
                else if (_rightDragging)
                {
                    _rightDragging = false;
                    if (!_rightDragMoved) OpenContextMenu(button.Position);
                }
            }
            if (button.ButtonIndex == MouseButton.Left && button.Pressed && button.DoubleClick && !IsTyping()) SetWalkTarget(button.Position);
            if (button.ButtonIndex == MouseButton.Middle && button.Pressed && !IsTyping()) FocusRaycast(button.Position);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        UpdateVoiceIndicator();
        if (UpdateVehicleDriving()) return;
        if (_seat is not null) { UpdateSeated(); return; }
        LiftOutOfTerrain();
        if (_emoteTime > 0)
        {
            _emoteTime -= dt;
            Velocity = Vector3.Zero;
            _rig.SetMotionState(_emoteState);
            if (_pendingGesture != InputFlags.None) { _network.SendInput(++_inputSequence, Vector2.Zero, _pendingGesture); _pendingGesture = InputFlags.None; }
            MoveAndSlide();
            return;
        }
        if (_landingTime > 0) _landingTime -= dt;
        var blocked = IsTyping() || _contextMenu.Visible || _mirror;
        UpdateDoubleTapRun(blocked);
        UpdateHoldToFly(blocked, dt);
        var input = blocked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var strafe = blocked ? 0f : Input.GetAxis("strafe_left", "strafe_right");
        var steering = Mathf.Abs(input.X) > .01f || Mathf.Abs(input.Y) > .01f || Mathf.Abs(strafe) > .01f;
        // Viewer-style controls in third person. First person follows the mouse like a
        // crosshair: W/S go along the view and A/D strafe without changing that heading.
        if (steering) _walkTarget = null;
        if (_firstPerson) _targetBodyYaw = _targetYaw;
        else _targetBodyYaw = Mathf.Wrap(_targetBodyYaw - input.X * TurnRate * dt, -Mathf.Pi, Mathf.Pi);
        var forward = new Vector3(-Mathf.Sin(_targetBodyYaw), 0, -Mathf.Cos(_targetBodyYaw));
        var right = new Vector3(Mathf.Cos(_targetBodyYaw), 0, -Mathf.Sin(_targetBodyYaw));
        var direction = forward * -input.Y + right * (strafe + (_firstPerson ? input.X : 0f));
        if (direction.LengthSquared() > 1f) direction = direction.Normalized();
        var moving = Mathf.Abs(input.Y) > .01f || Mathf.Abs(strafe) > .01f;
        if (_walkTarget is Vector3 target)
        {
            var toTarget = target - GlobalPosition; toTarget.Y = 0;
            if (toTarget.Length() < .35f) _walkTarget = null;
            else if (!blocked)
            {
                var wanted = Mathf.Atan2(-toTarget.X, -toTarget.Z);
                var offset = Mathf.Wrap(wanted - _targetBodyYaw, -Mathf.Pi, Mathf.Pi);
                _targetBodyYaw += Mathf.Clamp(offset, -TurnRate * 2.5f * dt, TurnRate * 2.5f * dt);
                direction = Mathf.Abs(offset) < 1f ? toTarget.Normalized() : Vector3.Zero;
                moving = true;
            }
        }
        // Swing the camera behind the avatar while it walks or turns, unless the user is orbiting.
        if ((moving || Mathf.Abs(input.X) > .01f) && !_rightDragging && _cameraMode is CameraMode.ThirdPerson or CameraMode.FirstPerson)
            _targetYaw += Mathf.Wrap(_targetBodyYaw - _targetYaw, -Mathf.Pi, Mathf.Pi) * (1f - Mathf.Exp(-CameraFollowRate * dt));
        // Ocean: same rule as the server (water over the ground at least chest deep means swimming).
        _terrainNode ??= GetParent()?.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        _oceanSwimming = !_flying && _terrainNode is not null && _terrainNode.Map.WaterDepth(_terrainNode.Map.SampleHeight(GlobalPosition.X, GlobalPosition.Z)) >= AuthoritativeMovement.WadeDepth;
        var flags = InputFlags.None;
        if (_mirror) _walkTarget = null;
        var jumpPressed = !blocked && Input.IsActionPressed("jump");
        var jumpJustPressed = !blocked && Input.IsActionJustPressed("jump");
        var descendPressed = !blocked && Input.IsActionPressed("descend");
        if (!blocked && RunHeld) flags |= InputFlags.Run;
        if (_flying || _swimming ? jumpPressed : jumpJustPressed) flags |= InputFlags.Jump;
        if (_mirror) flags = InputFlags.None; // in front of the mirror the avatar stands still: no jumping, running or flying from the keys typed into the window
        if (descendPressed) flags |= InputFlags.Descend;
        _network.SendInput(++_inputSequence, SentDirection(direction), flags);
        var velocity = Velocity;

        if (_oceanSwimming)
        {
            // Buoyancy keeps the avatar floating chest-deep; jump surfaces/rises, descend dives toward the sea floor.
            var swimSpeed = RunHeld ? AuthoritativeMovement.SwimRunSpeed : AuthoritativeMovement.SwimSpeed;
            var floatY = (_terrainNode?.Map.SeaLevel ?? TerrainMap.DefaultSeaLevel) - AuthoritativeMovement.SwimFloatDepth;
            velocity.X = SmoothHorizontal(velocity.X, direction.X * swimSpeed, dt); velocity.Z = SmoothHorizontal(velocity.Z, direction.Z * swimSpeed, dt);
            var verticalTarget = jumpPressed ? AuthoritativeMovement.SwimVerticalSpeed : descendPressed ? -AuthoritativeMovement.SwimVerticalSpeed : Mathf.Clamp((floatY - GlobalPosition.Y) * 3f, -2f, 2f);
            velocity.Y = Mathf.MoveToward(velocity.Y, verticalTarget, 14f * dt);
            if (GlobalPosition.Y + velocity.Y * dt > floatY + AuthoritativeMovement.SwimSurfaceMargin) velocity.Y = Mathf.Min(velocity.Y, (floatY + AuthoritativeMovement.SwimSurfaceMargin - GlobalPosition.Y) / dt);
        }
        else if (_swimming)
        {
            _flying = false; velocity.X = SmoothHorizontal(velocity.X, direction.X * SwimSpeed, dt); velocity.Z = SmoothHorizontal(velocity.Z, direction.Z * SwimSpeed, dt);
            velocity.Y = jumpPressed ? SwimSpeed : descendPressed ? -SwimSpeed : 0;
        }
        else if (_flying)
        {
            velocity.X = SmoothHorizontal(velocity.X, direction.X * FlySpeed, dt); velocity.Z = SmoothHorizontal(velocity.Z, direction.Z * FlySpeed, dt);
            velocity.Y = jumpPressed ? FlySpeed : descendPressed ? -FlySpeed : 0;
        }
        else
        {
            if (!IsOnFloor()) velocity.Y -= 9.8f * dt;
            if (jumpJustPressed && IsOnFloor()) velocity.Y = JumpVelocity;
            var speed = (RunHeld ? RunSpeed : WalkSpeed) * SurfaceSpeedMultiplier();
            velocity.X = SmoothHorizontal(velocity.X, direction.X * speed, dt); velocity.Z = SmoothHorizontal(velocity.Z, direction.Z * speed, dt);
        }

        Velocity = velocity;
        MoveAndSlide();
        _blockedVelocity = WallContact() ? new Vector2(Velocity.X, Velocity.Z) : null;
        if (_flying && IsOnFloor() && descendPressed) _flying = false;
        var grounded = IsOnFloor();
        if (grounded && !_wasGrounded) _landingTime = .22f;
        _wasGrounded = grounded;
        UpdateAnimation(direction, direction.Length());
        _status.Text = $"{_cameraMode} · " + (_swimming ? "Swimming" : _flying ? "Flying" : direction.LengthSquared() < .01f ? "Idle" : RunHeld ? "Running" : "Walking");
    }

    private Vector2? _blockedVelocity;

    /// True while the avatar walks against a wall (a surface that is not floor).
    private bool WallContact()
    {
        if (_flying || _swimming || _oceanSwimming) return false;
        for (var i = 0; i < GetSlideCollisionCount(); i++) if (GetSlideCollision(i).GetNormal().Y < .6f) return true;
        return false;
    }

    /// What the server is told about where the avatar is going. Normally the intended direction; while a wall stops the avatar it is what
    /// the avatar really did (the part that slides along the wall), so the server never runs ahead into or through the wall, even
    /// where it knows nothing about that wall.
    private Vector2 SentDirection(Vector3 direction)
    {
        var intended = new Vector2(direction.X, direction.Z);
        if (_blockedVelocity is not { } actual || intended.LengthSquared() < .0025f) return intended;
        var real = actual / (RunHeld ? RunSpeed : WalkSpeed);
        return real.Length() > intended.Length() ? real.Normalized() * intended.Length() : real;
    }

    /// A heightmap shape does not push bodies out when the ground rises (terrain edit, reconcile, teleport),
    /// so the avatar would end up buried to the waist. The server already lifts the avatar; mirror it here.
    private void LiftOutOfTerrain()
    {
        _terrainNode ??= GetParent()?.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        if (_terrainNode is null) return;
        var ground = _terrainNode.Map.SampleHeight(GlobalPosition.X, GlobalPosition.Z);
        if (GlobalPosition.Y >= ground - .25f) return; // tolerate the small bilinear-vs-triangle difference on slopes
        GlobalPosition = new Vector3(GlobalPosition.X, ground, GlobalPosition.Z);
        Velocity = new Vector3(Velocity.X, Mathf.Max(0f, Velocity.Y), Velocity.Z);
    }

    public bool IsFlying => _flying;
    public bool IsSwimming => _swimming;
    public bool IsRunning => RunHeld;
    public bool AlwaysRun { get => _alwaysRun; set => _alwaysRun = value; }
    /// Shift temporarily inverts the always-run setting. A quick second press of W/Up
    /// also runs until that second press is released.
    private bool RunHeld => !IsTyping() && ((Input.IsActionPressed("run") ^ _alwaysRun) || _doubleTapRun);

    private void UpdateDoubleTapRun(bool blocked)
    {
        if (blocked) { _doubleTapRun = false; _lastForwardPress = 0; return; }
        if (Input.IsActionJustReleased("move_forward")) _doubleTapRun = false;
        if (!Input.IsActionJustPressed("move_forward")) return;
        var now = Time.GetTicksMsec();
        _doubleTapRun = _lastForwardPress > 0 && now - _lastForwardPress <= DoubleTapWindowMs;
        _lastForwardPress = now;
    }

    private void UpdateHoldToFly(bool blocked, float dt)
    {
        if (blocked || !Input.IsActionPressed("jump"))
        {
            _spaceHeld = 0;
            _spaceFlightTriggered = false;
            return;
        }
        if (_flying || _swimming || _seat is not null || _spaceFlightTriggered) return;
        _spaceHeld += dt;
        if (_spaceHeld < HoldToFlySeconds) return;
        _spaceFlightTriggered = true;
        RequestFlightToggle();
    }

    /// Same logic as the F key, so the viewer's Fly button and the key behave identically (and the server is told).
    public void RequestFlightToggle()
    {
        if (!ToggleFlight()) return;
        if (!_network.IsAuthenticated) return;
        _flightRequestPending = true;
        _flightRequestedState = _flying;
        _flightRequestAt = Time.GetTicksMsec();
        _network.SetFlight(_flying);
    }

    private bool ToggleFlight()
    {
        if (_seat is not null || _volumeSwimming) return false;
        _flying = !_flying;
        if (_flying) Velocity = new Vector3(Velocity.X, 2f, Velocity.Z);
        return true;
    }

    public void SetSwimming(bool value) => _volumeSwimming = value;
    /// Turns the avatar and the camera to face a heading at once (after a teleport to an arrival point).
    public void FaceYaw(float radians) { _targetBodyYaw = radians; _targetYaw = radians; _cameraYaw = radians; Rotation = new Vector3(0, radians, 0); }

    public void Reconcile(Vector3 authoritativePosition, Vector3 authoritativeVelocity, uint acknowledgedInput, NetworkMotionState motion)
    {
        if (_inVehicle) return;
        var serverFlying = motion == NetworkMotionState.Fly;
        if (_flightRequestPending)
        {
            if (serverFlying == _flightRequestedState || Time.GetTicksMsec() - _flightRequestAt >= 1500)
            {
                _flightRequestPending = false;
                _flying = serverFlying;
            }
        }
        else _flying = serverFlying;
        var error = GlobalPosition.DistanceTo(authoritativePosition);
        if (error > 3f) GlobalPosition = authoritativePosition; // a real teleport
        else if (error > .3f)
        {
            // The server's shapes are simpler than the ones the engine collides with, so small differences are normal and are left alone.
            // A bigger one is closed gently, and with a collision-checked move: a correction must never carry the avatar through a wall.
            var correction = (authoritativePosition - GlobalPosition) * .12f;
            if (IsOnFloor() && Mathf.Abs(correction.Y) < .5f && new Vector2(correction.X, correction.Z).Length() < .05f) correction.Y = 0; // no tugging up and down while standing
            MoveAndCollide(correction);
        }
        if (error > 1f) Velocity = Velocity.Lerp(authoritativeVelocity, .2f);
    }
    public void Sit(InteractableSeat seat)
    {
        if (_flying || _swimming || _seat is not null || !seat.TryOccupy(this)) return;
        _seat = seat; Velocity = Vector3.Zero; GlobalTransform = seat.SeatTransform;
        _network.SendInput(++_inputSequence, Vector2.Zero, InputFlags.Sit);
        _rig.SetAnimationOverride(AvatarMotionState.Sit, .45f, 3); _contextMenu.Visible = false;
    }

    private void Stand()
    {
        if (_seat is null) return;
        var seat = _seat; _seat = null; seat.Release(this); var exit = seat.ExitPosition; GlobalPosition = exit;
        _network.SendInput(++_inputSequence, Vector2.Zero, InputFlags.Sit);
        _emoteState = AvatarMotionState.StandUp; _emoteTime = .5f; _rig.SetAnimationOverride(AvatarMotionState.StandUp, .5f, 3);
    }

    private void UpdateSeated()
    {
        if (_seat is null || !IsInstanceValid(_seat) || !IsInstanceValid(_seat.GetNodeOrNull<Marker3D>("SeatAnchor"))) { _seat = null; Velocity = Vector3.Zero; return; }
        GlobalTransform = _seat.SeatTransform; Velocity = Vector3.Zero; _rig.SetMotionState(AvatarMotionState.Sit);
        if (Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("interact")) Stand();
    }

    private void UpdateAnimation(Vector3 direction, float inputAmount)
    {
        var snapshot = new MovementSnapshot(false, _swimming, _flying, IsOnFloor(), Velocity.Y > 0,
            _landingTime > 0, direction.LengthSquared() > .01f && inputAmount >= .05f, RunHeld);
        _rig.SetMotionState(MovementStateResolver.Resolve(snapshot));
    }

    private float SmoothHorizontal(float current, float target, float dt)
    {
        var accelerating = Mathf.Abs(target) > Mathf.Abs(current) || Mathf.Sign(target) != Mathf.Sign(current);
        var rate = accelerating ? Acceleration : Deceleration;
        return Mathf.MoveToward(current, target, Mathf.Max(.01f, rate) * dt);
    }

    private float SurfaceSpeedMultiplier()
    {
        for (var i = 0; i < GetSlideCollisionCount(); i++)
        {
            var collider = GetSlideCollision(i).GetCollider();
            if (collider is GodotObject obj && obj.HasMeta("speed_multiplier")) return Mathf.Clamp(obj.GetMeta("speed_multiplier").AsSingle(), .25f, 1.5f);
        }
        return 1f;
    }

    /// A right click that was not an orbit drag; the radial menu listens. Without a listener the old seat menu is used.
    public event Action<Vector2>? ContextRequested;

    public void WalkTo(Vector3 point) { _walkTarget = point; }
    public void Gesture(string name) { if (name == "wave") StartGesture(AvatarMotionState.Wave, 2.5f, InputFlags.Wave); else if (name == "dance") StartGesture(AvatarMotionState.Dance, 5f, InputFlags.Dance); }
    public void SitOn(InteractableSeat seat) => seat.Interact(this);

    private void OpenContextMenu(Vector2 screenPosition)
    {
        if (ContextRequested is not null) { ContextRequested.Invoke(screenPosition); return; }
        _selectedSeat = RaycastSeat();
        if (_selectedSeat is null) return;
        _contextMenu.Position = screenPosition; _contextMenu.Visible = true;
    }

    private void InteractOrStand()
    {
        if (_inVehicle) { _network.ExitVehicle(); return; }
        if (_seat is not null) { Stand(); return; }
        if (RaycastSeat() is { } seat) { seat.Interact(this); return; }
        if (_network.IsAuthenticated && !_flying && !_swimming && _network.Vehicles.Nearest(GlobalPosition, VehicleFleet.EnterRange) is { } car) _network.EnterVehicle(car.VehicleId);
    }

    /// While the server says this player drives a car, the avatar rides at the seat and the keys steer the car instead of walking.
    /// The dot above your own name shows that voice is on, and its rings follow how loud you are (red when too loud).
    private void UpdateVoiceIndicator()
    {
        if (_network is null) return;
        _rig.SetVoice(_network.IsAuthenticated && _network.Voice.Enabled, _network.Voice.LocalLevel, _network.Voice.LocalPeak);
    }

    private bool UpdateVehicleDriving()
    {
        var car = _network.IsAuthenticated ? _network.Vehicles.DrivenBy(_network.OwnPeerId) : null;
        if (car is null) { if (_inVehicle) { _inVehicle = false; Velocity = Vector3.Zero; } return false; }
        if (!_inVehicle) { _inVehicle = true; _flying = false; _walkTarget = null; _contextMenu.Visible = false; }
        GlobalTransform = car.SeatTransform; Velocity = Vector3.Zero; _rig.SetMotionState(AvatarMotionState.Sit);
        var typing = IsTyping();
        _network.SendVehicleInput(typing ? 0f : Input.GetAxis("move_back", "move_forward"), typing ? 0f : Input.GetAxis("move_left", "move_right"), !typing && Input.IsActionPressed("jump"));
        return true;
    }
    private InteractableSeat? RaycastSeat()
    {
        var center = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(center); var to = from + _camera.ProjectRayNormal(center) * 5f;
        var query = PhysicsRayQueryParameters3D.Create(from, to); query.Exclude = [GetRid()];
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("collider", out var collider) ? collider.AsGodotObject() as InteractableSeat : null;
    }

    private void ContextSit() { _selectedSeat?.Interact(this); _selectedSeat = null; }
    private void StartGesture(AvatarMotionState state, float duration, InputFlags networkFlag = InputFlags.None) { if (_seat is not null || _flying || _swimming || IsTyping()) return; _emoteState = state; _emoteTime = duration; _pendingGesture = networkFlag; _rig.SetAnimationOverride(state, duration, 2); }
    private void BuildGesturePanel()
    {
        var panel = new PanelContainer { Name = "GesturePanel", Position = new Vector2(20, 135), CustomMinimumSize = new Vector2(210, 0) };
        var box = new VBoxContainer(); panel.AddChild(box); box.AddChild(new Label { Text = "Gestures" });
        foreach (var item in new[] { ("Wave", AvatarMotionState.Wave), ("Dance", AvatarMotionState.Dance), ("Applaud", AvatarMotionState.Applaud), ("Point", AvatarMotionState.Point), ("Greet", AvatarMotionState.Greet), ("Laugh", AvatarMotionState.Laugh), ("Sit floor", AvatarMotionState.SitFloor) })
        { var button = new Button { Text = item.Item1 }; box.AddChild(button); button.Pressed += () => StartGesture(item.Item2, item.Item2 == AvatarMotionState.Dance ? 5f : 2.5f, item.Item2 == AvatarMotionState.Wave ? InputFlags.Wave : item.Item2 == AvatarMotionState.Dance ? InputFlags.Dance : InputFlags.None); }
        GetNode<CanvasLayer>("HUD").AddChild(panel);
    }
    private void SetCursorFree(bool value)
    {
        _cursorFree = value;
        _rightDragging = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
    private void ToggleFirstPerson() => SetCameraMode(_cameraMode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson);
    private void CycleCameraMode() => SetCameraMode(_cameraMode switch { CameraMode.ThirdPerson => CameraMode.FirstPerson, CameraMode.FirstPerson => CameraMode.FreeLook, _ => CameraMode.ThirdPerson });
    public void SetCameraMode(string mode)
    {
        if (!Enum.TryParse<CameraMode>(mode, true, out var parsed)) return;
        SetCameraMode(parsed);
    }
    private void SetCameraMode(CameraMode mode)
    {
        _cameraMode = mode; _firstPerson = mode == CameraMode.FirstPerson;
        _springArm.SpringLength = _firstPerson ? .05f : _cameraDistance;
        _rig.Visible = !_firstPerson && !_avatarHidden;
        if (_firstPerson)
        {
            _targetYaw = _targetBodyYaw;
            _cameraYaw = _targetBodyYaw;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _cursorFree = false;
        }
        else if (Input.MouseMode == Input.MouseModeEnum.Captured) SetCursorFree(true);
    }
    private bool _avatarHidden;
    /// Hides the avatar while the build camera is in use (and shows it again afterwards).
    public void SetAvatarHidden(bool hidden) { _avatarHidden = hidden; _rig.Visible = !hidden && !_firstPerson; }
    private void SetZoom(float value) { _cameraDistance = Mathf.Clamp(value, _mirror ? .7f : 1.5f, _mirror ? 5f : 12f); if (!_firstPerson) _springArm.SpringLength = _cameraDistance; }
    private bool IsTyping() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
    public void SetTextInputActive(bool active)
    {
        if (active) Input.MouseMode = Input.MouseModeEnum.Visible;
        else if (_firstPerson) Input.MouseMode = Input.MouseModeEnum.Captured;
    }
    private void SetWalkTarget(Vector2 screenPosition)
    {
        var from = _camera.ProjectRayOrigin(screenPosition); var to = from + _camera.ProjectRayNormal(screenPosition) * 200f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.TryGetValue("position", out var value)) _walkTarget = value.AsVector3();
    }
    private void FocusRaycast(Vector2 screenPosition)
    {
        var from = _camera.ProjectRayOrigin(screenPosition); var to = from + _camera.ProjectRayNormal(screenPosition) * 200f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (!hit.TryGetValue("position", out var value)) return;
        var point = value.AsVector3(); var direction = point - GlobalPosition; direction.Y = 0;
        if (direction.LengthSquared() > .01f) _targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
    }
    // ---- the mirror: the camera in front of the avatar, close, while the appearance is being changed on the spot ----------------------------

    private bool _mirror, _mirrorFace = true;
    private (CameraMode Mode, float Distance, float Yaw, float Pitch) _beforeMirror;

    /// <summary>True while the camera is in front of the avatar for the appearance window; the avatar stands still.</summary>
    public bool MirrorActive => _mirror;
    public bool MirrorFace => _mirrorFace;
    public float CameraDistance => _cameraDistance;

    /// <summary>Puts the camera in front of the avatar (the face, or the whole body) and keeps the avatar still. The state of the camera before is kept for <see cref="ExitMirror"/>.</summary>
    public void EnterMirror(bool face = true)
    {
        if (!_mirror) _beforeMirror = (_cameraMode, _cameraDistance, _targetYaw, _targetPitch);
        _mirror = true; _walkTarget = null; _rightDragging = false;
        SetCameraMode(CameraMode.ThirdPerson);
        SetMirrorView(face);
    }

    public void SetMirrorView(bool face)
    {
        if (!_mirror) return;
        _mirrorFace = face; _cameraDistance = face ? 1.15f : 3.1f; _springArm.SpringLength = _cameraDistance;
        _targetPitch = face ? -.02f : -.1f; _targetYaw = _targetBodyYaw + Mathf.Pi;
    }

    /// <summary>Turns the camera around the avatar (radians), so every side can be seen.</summary>
    public void TurnMirror(float radians) { if (_mirror) _targetYaw += radians; }

    public void ExitMirror()
    {
        if (!_mirror) return;
        _mirror = false;
        _cameraDistance = _beforeMirror.Distance; _targetYaw = _beforeMirror.Yaw; _targetPitch = _beforeMirror.Pitch;
        SetCameraMode(_beforeMirror.Mode);
    }

    public void ResetCamera() { SetCameraMode(CameraMode.ThirdPerson); _targetPitch = -.15f; _targetYaw = Rotation.Y; _targetBodyYaw = Rotation.Y; }
    /// Shows a new look on our own avatar (it came from the island after a refresh); the camera preferences stay as they are.
    public void ShowAppearance(AvatarAppearanceData value) => _rig.ApplyAppearance(value);

    private void OnAvatarLoaded(bool success, string message)
    {
        if (!success) { _status.Text = message; return; }
        var value = GetNode<AvatarManager>("/root/AvatarManager").Current;
        _cameraSensitivity = value.CameraSensitivity; _invertY = value.InvertCameraY; _rig.ApplyAppearance(value);
    }
}
