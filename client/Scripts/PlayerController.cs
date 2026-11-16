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
    private float _landingTime, _flatImpactTime;
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
        _rig = GetNode<AvatarRig>("AvatarRig"); _rig.OwnAvatar = true;
        _cameraController = new Scripts.Camera.CameraController { Name = "CameraController", Player = this }; AddChild(_cameraController);
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _network.RegisterLocalPlayer(this);
        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
        _camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
        AddToGroup("lifeverse_player"); _pivotBaseY = _cameraPivot.Position.Y; _thirdPersonFov = _camera.Fov;
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
        avatars.Attachments.Changed += OnAttachmentsChanged;
        if (avatars.Attachments.Loaded) _rig.ApplyAttachments(avatars.Attachments.Items);
        var error = avatars.Load();
        if (error != Error.Ok) GD.PushWarning($"event=avatar_load_start_failed error={error}");
    }

    public override void _ExitTree()
    {
        _seat?.Release(this);
        _seat = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var avatars = GetNodeOrNull<AvatarManager>("/root/AvatarManager");
        if (avatars is not null) { avatars.AvatarLoaded -= OnAvatarLoaded; avatars.Attachments.Changed -= OnAttachmentsChanged; }
        _network?.UnregisterLocalPlayer(this);
    }

    public override void _Process(double delta)
    {
        var weight = 1f - Mathf.Exp(-Scripts.Camera.CameraSettings.Current.Smoothing * (float)delta);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _targetBodyYaw, weight), 0);
        // The pivot is a child of the body, so its local yaw is the camera yaw relative to the avatar.
        _cameraYaw = Mathf.LerpAngle(_cameraYaw, _targetYaw, weight);
        _cameraPivot.Rotation = new Vector3(Mathf.Lerp(_cameraPivot.Rotation.X, _targetPitch, weight), Mathf.Wrap(_cameraYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi), 0);
        // While swimming the camera rides higher so it stays above the waves instead of dipping under the surface.
        var pivotHeight = _mirror ? (_mirrorFace ? 1.62f : 1.05f) : (_oceanSwimming ? 2.7f : 1.55f) + _cameraHeightOffset;
        // in first person the camera is at the eyes of the body (its own head, wherever the animation takes it), a little in front of the neck, so it never sees the inside of the head and never cuts the chest
        Vector3? eyes = _firstPerson && !_oceanSwimming && !_mirror && !_editView && !_freeView && _rig.EyeWorldPosition(-GlobalBasis.Z) is { } world ? ToLocal(world) : null;
        if (_detached) { if (!Scripts.Camera.CameraSettings.Current.AllowDetached) ExitDetached(); else { UpdateDetached((float)delta); return; } }
        if (_focusNode is not null) { if (IsInstanceValid(_focusNode) && _focusNode.IsInsideTree()) _editFocus = ToLocal(_focusNode.ToGlobal(_focusOffset)); else _focusNode = null; }
        _cameraPivot.Position = _editView || _freeView ? _cameraPivot.Position.Lerp(_editFocus, weight * (_freeView ? 1f : .6f)) : eyes is { } at ? _cameraPivot.Position.Lerp(at + Vector3.Up * _cameraHeightOffset, Mathf.Min(1f, weight * 2f)) : new Vector3(0, Mathf.Lerp(_cameraPivot.Position.Y, pivotHeight, weight * (_mirror ? .6f : .35f)), 0);
        // the distance glides too: a notch of the wheel or a drag never jumps
        _springArm.SpringLength = LifeVerse.Shared.Camera.CameraZoomController.Glide(_springArm.SpringLength, _firstPerson ? FirstPersonArm : _cameraDistance, (float)delta, Scripts.Camera.CameraSettings.Current.Smoothing);
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
            var sensitivity = _cameraSensitivity * SensitivityScale;
            _targetYaw -= motion.Relative.X * sensitivity;
            var direction = _invertY ^ InvertYExtra ? 1f : -1f;
            _targetPitch = _firstPerson ? Mathf.Clamp(_targetPitch + motion.Relative.Y * sensitivity * direction, FirstPersonPitchDown, FirstPersonPitchUp) : Mathf.Clamp(_targetPitch + motion.Relative.Y * sensitivity * direction, -1.25f, .85f);
            if (_firstPerson) _targetBodyYaw = _targetYaw;
        }
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) SetZoom(_cameraDistance - ZoomStep);
            if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) SetZoom(_cameraDistance + ZoomStep);
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
                    if (!_rightDragMoved && !_editView) OpenContextMenu(button.Position);
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
        if (_flatImpactTime > 0) _flatImpactTime -= dt;      // lying on the ground after a fall from the sky: no walking, jumping or flying until the avatar is up
        var blocked = IsTyping() || _contextMenu.Visible || _mirror || _detached || _flatImpactTime > 0;
        UpdateDoubleTapRun(blocked);
        UpdateHoldToFly(blocked, dt);
        var input = blocked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var strafe = blocked ? 0f : Input.GetAxis("strafe_left", "strafe_right");
        var steering = Mathf.Abs(input.X) > .01f || Mathf.Abs(input.Y) > .01f || Mathf.Abs(strafe) > .01f;
        // Viewer-style controls in third person. First person follows the mouse like a
        // crosshair: W/S go along the view and A/D strafe without changing that heading.
        if (steering) _walkTarget = null;
        if (_freeView && steering && Scripts.Camera.CameraSettings.Current.RestoreAutomatically) ExitFreeView();
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
        if ((moving || Mathf.Abs(input.X) > .01f) && !_rightDragging && !_freeView && _cameraMode is CameraMode.ThirdPerson or CameraMode.FirstPerson)
            _targetYaw += Mathf.Wrap(_targetBodyYaw - _targetYaw, -Mathf.Pi, Mathf.Pi) * (1f - Mathf.Exp(-CameraFollowRate * dt));
        // Ocean: same rule as the server (water over the ground at least chest deep means swimming).
        _terrainNode ??= GetParent()?.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        _oceanSwimming = !_flying && _terrainNode is not null && _terrainNode.Map.WaterDepth(_terrainNode.Map.SampleHeight(GlobalPosition.X, GlobalPosition.Z)) >= AuthoritativeMovement.WadeDepth;
        var flags = InputFlags.None;
        if (_mirror || _detached) _walkTarget = null;
        var jumpPressed = !blocked && Input.IsActionPressed("jump");
        var jumpJustPressed = !blocked && Input.IsActionJustPressed("jump");
        var descendPressed = !blocked && Input.IsActionPressed("descend");
        if (!blocked && RunHeld) flags |= InputFlags.Run;
        if (_flying || _swimming ? jumpPressed : jumpJustPressed) flags |= InputFlags.Jump;
        if (_mirror || _detached) flags = InputFlags.None; // in front of the mirror the avatar stands still: no jumping, running or flying from the keys typed into the window
        if (descendPressed) flags |= InputFlags.Descend;
        if (ChatTypingActive) flags |= InputFlags.Typing;
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
        NoteBuiltSupport(grounded);
        if (grounded && !_wasGrounded) { _landingTime = .22f; if (_rig.FallingFromFlight && _rig.CanFlatImpact) _flatImpactTime = _rig.FlatImpactSeconds; }      // a fall that began when the flight stopped in the air ends flat on the ground
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
        if (!DoubleTapRunEnabled) { _doubleTapRun = false; return; }
        if (blocked) { _doubleTapRun = false; _lastForwardPress = 0; return; }
        if (Input.IsActionJustReleased("move_forward")) _doubleTapRun = false;
        if (!Input.IsActionJustPressed("move_forward")) return;
        var now = Time.GetTicksMsec();
        _doubleTapRun = _lastForwardPress > 0 && now - _lastForwardPress <= DoubleTapWindowMs;
        _lastForwardPress = now;
    }

    private void UpdateHoldToFly(bool blocked, float dt)
    {
        if (!HoldToFlyEnabled) { _spaceHeld = 0; return; }
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
    public void FaceYaw(float radians) { ExitFreeView(); ExitDetached(); _targetBodyYaw = radians; _targetYaw = radians; _cameraYaw = radians; Rotation = new Vector3(0, radians, 0); }

    private ulong _builtSupportUntil;
    /// <summary>How long after standing on built geometry a jump is still treated as being from it (longer than a jump lasts).</summary>
    public const ulong BuiltSupportMilliseconds = 1600;
    public bool OnBuiltSupportForTesting => Time.GetTicksMsec() < _builtSupportUntil;
    private void NoteBuiltSupport(bool grounded)
    {
        if (!grounded || _flying || _swimming) return;
        _terrainNode ??= GetParent()?.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        if (_terrainNode is null) return;
        if (GlobalPosition.Y > _terrainNode.Map.SampleHeight(GlobalPosition.X, GlobalPosition.Z) + .5f) _builtSupportUntil = Time.GetTicksMsec() + BuiltSupportMilliseconds;
    }

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
        // Standing (or jumping) on something built that the server does not model (an imported mesh, a pyramid...): the server thinks the avatar is lower.
        // Pulling it down would cancel every jump, so while that is the case the vertical difference is left alone and only the horizontal position is reconciled.
        var onBuilt = Time.GetTicksMsec() < _builtSupportUntil && authoritativePosition.Y < GlobalPosition.Y - .05f;
        if (onBuilt) authoritativePosition = new Vector3(authoritativePosition.X, GlobalPosition.Y, authoritativePosition.Z);
        var error = GlobalPosition.DistanceTo(authoritativePosition);
        if (error > 3f)
        {
            // A server snapshot can be far away when its simplified obstacle model
            // does not describe an imported triangular mesh. Never assign that position
            // blindly: doing so lets a snapshot carry the local capsule through a wall.
            // Real teleports still apply immediately when the direct path is clear.
            var displacement = authoritativePosition - GlobalPosition;
            if (!TestMove(GlobalTransform, displacement)) GlobalPosition = authoritativePosition;
            else
            {
                var safeCorrection = displacement.Length() > .75f ? displacement.Normalized() * .75f : displacement;
                MoveAndCollide(safeCorrection);
            }
        }
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
        ExitFreeView(); ExitDetached(); _seat = seat; Velocity = Vector3.Zero; GlobalTransform = seat.SeatTransform;
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
        // S or the back arrow with W not held: the body keeps facing forward while it moves back (walk or run), so it plays the backwards walk
        var backwards = !_flying && !_swimming && !IsTyping() && Input.IsActionPressed("move_back") && !Input.IsActionPressed("move_forward") && direction.LengthSquared() > .01f;
        var resolved = TypingState.Apply(WaterStill.Apply(BackwardsWalk.Apply(MovementStateResolver.Resolve(snapshot), backwards), snapshot.IsMoving), ChatTypingActive);
        _rig.SetMotionState(_flatImpactTime > 0 ? AvatarMotionState.FlatImpact : resolved);
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
        if (!_inVehicle) { _inVehicle = true; _flying = false; _walkTarget = null; _contextMenu.Visible = false; ExitFreeView(); ExitDetached(); }
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
        _springArm.SpringLength = _firstPerson ? FirstPersonArm : _cameraDistance;
        _camera.Fov = _firstPerson ? _firstPersonFov : _thirdPersonFov;
        _camera.Near = _firstPerson ? FirstPersonNear : NormalNear;   // from inside the head, whatever is closer than this is not drawn: the head, its hair, glasses and hats
        _rig.Visible = !_avatarHidden; _rig.SetFirstPersonView(_firstPerson);
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
    public void SetAvatarHidden(bool hidden) { _avatarHidden = hidden; _rig.Visible = !hidden; }
    private void SetZoom(float value) { var options = Scripts.Camera.CameraSettings.Current; _cameraDistance = Mathf.Clamp(value, _editView ? .2f : _mirror ? .7f : _freeView ? options.MinDistance : 1.5f, _mirror ? 5f : _freeView ? options.MaxDistance : 12f); if (!_firstPerson) _springArm.SpringLength = _cameraDistance; }
    /// <summary>True while a loading screen covers the world: the avatar ignores the keyboard and the mouse until the world is ready.</summary>
    // ---- the person's own settings (Configurações › Controles e Câmera) ----
    /// <summary>Multiplies the camera sensitivity of the avatar; and flips the vertical axis on top of what the avatar says.</summary>
    public static float SensitivityScale { get; set; } = 1f;
    public static bool InvertYExtra { get; set; }
    public static bool DoubleTapRunEnabled { get; set; } = true;
    public static bool HoldToFlyEnabled { get; set; } = true;
    /// <summary>How far one notch of the mouse wheel moves the camera, in meters.</summary>
    public static float ZoomStep { get; set; } = .5f;
    private float _pivotBaseY, _cameraHeightOffset;
    private const float NormalNear = .05f, FirstPersonNear = .06f, FirstPersonArm = .02f;
    /// <summary>How far the person can look down (at their own feet) and up in first person.</summary>
    public const float FirstPersonPitchDown = -1.5f, FirstPersonPitchUp = 1.35f;
    public const float FirstPersonFovMin = 60f, FirstPersonFovMax = 100f, FirstPersonFovDefault = 75f;
    private float _firstPersonFov = FirstPersonFovDefault, _thirdPersonFov = 75f;
    public float FirstPersonFov => _firstPersonFov;

    /// <summary>The field of view of first person (60 to 100 degrees).</summary>
    public void SetFirstPersonFov(float fov) { _firstPersonFov = Mathf.Clamp(fov, FirstPersonFovMin, FirstPersonFovMax); if (_firstPerson) _camera.Fov = _firstPersonFov; }
    public string CameraModeName => _cameraMode.ToString();
    public float FieldOfView => _camera.Fov;
    public float PivotOffset => _cameraHeightOffset;
    public float ShoulderOffset => _camera.HOffset;
    public bool CameraCollides => _springArm.CollisionMask != 0;

    /// <summary>The camera as the person likes it: field of view, how far behind, how high, a little to one side, how soon it follows a turn, whether walls stop it. Not while the mirror is in use.</summary>
    public void ApplyCameraSettings(float fov, float distance, float height, float shoulder, float follow, bool collision)
    {
        _thirdPersonFov = Mathf.Clamp(fov, 40f, 120f); if (!_firstPerson) _camera.Fov = _thirdPersonFov;
        _cameraHeightOffset = Mathf.Clamp(height, -1f, 1.5f);      // the pivot follows it smoothly (see _Process)
        _camera.HOffset = Mathf.Clamp(shoulder, -2f, 2f);
        CameraFollowRate = Mathf.Clamp(follow, .5f, 12f);
        Scripts.Camera.CameraCollision.Apply(_springArm, collision);
        if (!_mirror) SetZoom(distance);
    }

    // ---- typing in the chat: the avatar types on a keyboard, for everybody -------------------------------------------------------------------------------------
    /// <summary>How long after the last key the avatar goes on typing (a person pauses between words).</summary>
    public const ulong ChatTypingHoldMs = 1500;
    private ulong _chatTypingUntil;
    /// <summary>True while the person is typing a message in the chat: standing still, the avatar types (and tells the island, so everybody sees and hears it).</summary>
    public bool ChatTypingActive => Time.GetTicksMsec() < _chatTypingUntil;
    /// <summary>The chat field changed (a key was typed): typing goes on for a moment. False: it is over (sent, emptied, left).</summary>
    public void SetChatTyping(bool active) => _chatTypingUntil = active ? Time.GetTicksMsec() + ChatTypingHoldMs : 0;

    public bool ControlsLocked { get; set; }
    private bool IsTyping() => ControlsLocked || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
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
        _mirror = true; _walkTarget = null; _rightDragging = false; _freeView = false; _focusNode = null; _detached = false;
        SetCameraMode(CameraMode.ThirdPerson);
        SetMirrorView(face);
    }

    public void SetMirrorView(bool face)
    {
        if (!_mirror) return;
        _editView = false; _editViewStarted = false;
        _mirrorFace = face; _cameraDistance = face ? 1.15f : 3.1f; _springArm.SpringLength = _cameraDistance;
        _targetPitch = face ? -.02f : -.1f; _targetYaw = _targetBodyYaw + Mathf.Pi;
    }

    /// <summary>Turns the camera around the avatar (radians), so every side can be seen.</summary>
    public void TurnMirror(float radians) { if (_mirror) _targetYaw += radians; }

    // ---- the edit view: the camera close to a part of the body while an attachment is being placed -----------------------------------

    private bool _editView;
    private Vector3 _editFocus;
    /// <summary>True while the camera is on a part of the body for the attachment editor; the avatar stands still.</summary>
    public bool EditViewActive => _editView;
    public AvatarRig Rig => _rig;

    /// <summary>Puts the camera close to a point of the body (in the world), looking at it from the front, with the avatar still. The camera before is kept for <see cref="ExitMirror"/>.</summary>
    public void EnterEditView(Vector3 world, float distance = 1.2f)
    {
        if (!_mirror) _beforeMirror = (_cameraMode, _cameraDistance, _targetYaw, _targetPitch);
        _mirror = true; _walkTarget = null; _rightDragging = false; _freeView = false; _focusNode = null; _detached = false;
        SetCameraMode(CameraMode.ThirdPerson);
        _editView = true; _editFocus = ToLocal(world); _cameraDistance = Mathf.Clamp(distance, .2f, 5f); _springArm.SpringLength = _cameraDistance;
        if (!_editViewStarted) { _targetPitch = -.05f; _targetYaw = _targetBodyYaw + Mathf.Pi; _editViewStarted = true; }
    }
    private bool _editViewStarted;

    /// <summary>Keeps the edit view on a thing while it moves (an object worn on the body moves with every step of the animation).</summary>
    public void FollowInEditView(Node3D? thing, Vector3 localOffset = default) { if (!_editView) return; _focusNode = thing; _focusOffset = localOffset; }

    /// <summary>Leaves the edit view. With <paramref name="restore"/> the camera goes back to how it was before; otherwise it stays where the editing left it, as a free view.</summary>
    public void ExitEditView(bool restore)
    {
        if (!_editView) return;
        if (restore) { ExitMirror(); return; }
        _editView = false; _editViewStarted = false; _mirror = false; _freeView = true; SetCameraMode(CameraMode.ThirdPerson);
    }

    /// <summary>Looks at another point (the camera glides to it).</summary>
    public void FocusEditView(Vector3 world, float? distance = null) { if (!_editView) return; _editFocus = ToLocal(world); if (distance is { } d) { _cameraDistance = Mathf.Clamp(d, .2f, 5f); _springArm.SpringLength = _cameraDistance; } }

    /// <summary>Slides the point looked at sideways and up or down, along the screen (what a drag with the middle button does).</summary>
    public void PanEditView(Vector2 screen) { if (_editView) PanCamera(screen.X, screen.Y); }

    // ---- the free camera: orbit, zoom and pan around any point without moving the avatar (ALT, CTRL+ALT, CTRL+ALT+SHIFT, CTRL and the wheel) ----------

    private bool _freeView;
    private Node3D? _focusNode;
    private Vector3 _focusOffset;
    private Scripts.Camera.CameraController? _cameraController;

    /// <summary>True while the camera looks at a point of its own choosing instead of following the avatar.</summary>
    public bool FreeViewActive => _freeView;
    /// <summary>True when the camera can be turned, zoomed and moved freely (the free view, the mirror, the edit view).</summary>
    public bool CameraIsFree => _freeView || _editView;
    /// <summary>Where the camera looks (the world point it is going to).</summary>
    public Vector3 FocusWorld => ToGlobal(_freeView || _editView ? _editFocus : _cameraPivot.Position);
    public float CameraYaw => _targetYaw;
    public float CameraPitch => _targetPitch;
    public float CameraDistanceGoal => _cameraDistance;
    /// <summary>How far from the focus the camera really is right now (a wall in the way makes it shorter).</summary>
    public float CameraDistanceNow => Scripts.Camera.CameraCollision.ActualDistance(_springArm);
    public Camera3D ViewCamera => _camera;
    public Node3D? FocusedNode => _focusNode;

    /// <summary>Lets go of the avatar: the camera stays where it is and can be moved on its own.</summary>
    public void EnterFreeView()
    {
        if (_freeView || _mirror || _detached) return;
        if (_firstPerson) SetCameraMode(CameraMode.ThirdPerson);
        _freeView = true; _focusNode = null; _editFocus = _cameraPivot.Position;
    }

    /// <summary>Back behind the avatar (the way it was before the free view).</summary>
    public void ExitFreeView()
    {
        if (!_freeView) return;
        _freeView = false; _focusNode = null; _targetYaw = _targetBodyYaw; SetZoom(_cameraDistance);
    }

    /// <summary>A camera gesture starts: the avatar's camera becomes free (unless the mirror or the edit view already holds it).</summary>
    public void BeginCameraGesture() { if (!_mirror && !_editView && !_detached) EnterFreeView(); }

    public void OrbitCamera(float dx, float dy, bool horizontalOnly = false)
    {
        var orbit = new LifeVerse.Shared.Camera.CameraOrbitController(Scripts.Camera.CameraSettings.Current) { Yaw = _targetYaw, Pitch = _targetPitch };
        if (horizontalOnly) orbit.OrbitHorizontal(dx); else orbit.Orbit(dx, dy);
        _targetYaw = orbit.Yaw; _targetPitch = orbit.Pitch;
    }

    private LifeVerse.Shared.Camera.CameraZoomController ZoomState() { var zoom = new LifeVerse.Shared.Camera.CameraZoomController(Scripts.Camera.CameraSettings.Current); zoom.Set(_cameraDistance, true); return zoom; }
    /// <summary>A vertical drag with ALT: closer when dragging down.</summary>
    public void DollyCamera(float dy) { if (_mirror && !_editView) return; SetZoom(ZoomState().Dolly(dy)); }
    /// <summary>The wheel in the free camera: proportional, and a small part of a notch with CTRL (precision).</summary>
    public void ZoomCamera(float notches, bool precision) { if (_mirror && !_editView) return; SetZoom(ZoomState().Zoom(notches, precision)); }

    /// <summary>Slides the point looked at along the screen. Starts the free view when nothing else holds the camera.</summary>
    public void PanCamera(float dx, float dy)
    {
        if (_mirror && !_editView) return;
        if (!_freeView && !_editView) EnterFreeView();
        var basis = _camera.GlobalBasis; var from = ToGlobal(_editFocus);
        var panned = LifeVerse.Shared.Camera.CameraPanController.Pan(new System.Numerics.Vector3(from.X, from.Y, from.Z), new System.Numerics.Vector3(basis.X.X, basis.X.Y, basis.X.Z), new System.Numerics.Vector3(basis.Y.X, basis.Y.Y, basis.Y.Z), dx, dy, _cameraDistance);
        var to = new Vector3(panned.X, panned.Y, panned.Z);
        if (_focusNode is not null && IsInstanceValid(_focusNode)) _focusOffset = _focusNode.ToLocal(to); else _editFocus = ToLocal(to);
    }

    // ---- the detached camera: it flies on its own, the avatar stays where it is ---------------------------------------------------------------------------------

    private bool _detached;
    private Vector3 _detachedPos;
    /// <summary>The camera is somewhere of its own, not tied to the avatar (turned on by the person, and only when the options allow it).</summary>
    public bool DetachedActive => _detached;
    public Vector3 DetachedPosition => _detachedPos;
    /// <summary>Tests and the interface: what a right click would have found at a place of the screen (set by the interface).</summary>
    public Func<Vector2, ContextTarget?>? CameraTargetResolver { get; set; }

    /// <summary>Lets the camera go. False when the options do not allow it or something else holds the camera (the mirror, the editors).</summary>
    public bool EnterDetached()
    {
        if (_detached) return true;
        if (!Scripts.Camera.CameraSettings.Current.AllowDetached || _mirror || _editView) return false;
        if (_firstPerson) SetCameraMode(CameraMode.ThirdPerson);
        _freeView = false; _focusNode = null; _detachedPos = _camera.GlobalPosition; _walkTarget = null; _detached = true; return true;
    }

    /// <summary>The camera comes back behind the avatar.</summary>
    public void ExitDetached()
    {
        if (!_detached) return;
        _detached = false; _targetYaw = _targetBodyYaw; SetZoom(_cameraDistance);
    }

    private void UpdateDetached(float dt)
    {
        var options = Scripts.Camera.CameraSettings.Current;
        if (!IsTyping() && _contextMenu is { Visible: false })
        {
            var axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back"); var strafe = Input.GetAxis("strafe_left", "strafe_right");
            var up = (Input.IsActionPressed("jump") ? 1f : 0f) - (Input.IsActionPressed("descend") ? 1f : 0f);
            var basis = _cameraPivot.GlobalBasis; var forward = -basis.Z; var right = basis.X;
            var move = new System.Numerics.Vector3(Mathf.Clamp(axis.X + strafe, -1f, 1f), up, -axis.Y);
            var next = LifeVerse.Shared.Camera.CameraDetachedRules.Move(new System.Numerics.Vector3(_detachedPos.X, _detachedPos.Y, _detachedPos.Z), new System.Numerics.Vector3(forward.X, forward.Y, forward.Z), new System.Numerics.Vector3(right.X, right.Y, right.Z), move, options.DetachedSpeed * (RunHeld ? 3f : 1f), dt);
            _detachedPos = new Vector3(next.X, next.Y, next.Z);
        }
        _terrainNode ??= GetParent()?.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        var ground = _terrainNode?.Map is { } map ? map.SampleHeight(_detachedPos.X, _detachedPos.Z) : GlobalPosition.Y - 2f; var half = _terrainNode?.Map.HalfExtent ?? 0f;
        var here = GlobalPosition; var kept = LifeVerse.Shared.Camera.CameraDetachedRules.Constrain(new System.Numerics.Vector3(_detachedPos.X, _detachedPos.Y, _detachedPos.Z), new System.Numerics.Vector3(here.X, here.Y, here.Z), options.DetachedRange, half, ground);
        _detachedPos = new Vector3(kept.X, kept.Y, kept.Z);
        _cameraPivot.GlobalPosition = _detachedPos;
        _springArm.SpringLength = .02f;
    }

    /// <summary>Looks at a point of the world (the camera glides to it) and, when given, goes to that distance.</summary>
    public void FocusOnPoint(Vector3 world, float? distance = null)
    {
        if (!_freeView && !_editView) EnterFreeView();
        _focusNode = null; _editFocus = ToLocal(world); if (distance is { } d) SetZoom(d);
    }

    /// <summary>Looks at a thing and keeps looking at it while it moves (an object, another avatar, a watch on an arm); the offset is a point of the thing, in its own space.</summary>
    public void FocusOn(Node3D thing, Vector3 localOffset = default, float? distance = null)
    {
        if (!_freeView && !_editView) EnterFreeView();
        _focusNode = thing; _focusOffset = localOffset; _editFocus = ToLocal(thing.ToGlobal(localOffset)); if (distance is { } d) SetZoom(d);
    }

    public void ExitMirror()
    {
        if (!_mirror) return;
        _editView = false; _editViewStarted = false;
        _mirror = false;
        _cameraDistance = _beforeMirror.Distance; _targetYaw = _beforeMirror.Yaw; _targetPitch = _beforeMirror.Pitch;
        SetCameraMode(_beforeMirror.Mode);
    }

    public void ResetCamera() { SetCameraMode(CameraMode.ThirdPerson); _targetPitch = -.15f; _targetYaw = Rotation.Y; _targetBodyYaw = Rotation.Y; }
    /// Shows a new look on our own avatar (it came from the island after a refresh); the camera preferences stay as they are.
    public void ShowAppearance(AvatarAppearanceData value) { _rig.ApplyAppearance(value); var store = GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments; if (store is { Loaded: true }) _rig.ApplyAttachments(store.Items); } // our own list is the one we just read from the API: it is never older than what the island sent
    private void OnAttachmentsChanged() => _rig.ApplyAttachments(GetNode<AvatarManager>("/root/AvatarManager").Attachments.Items);

    private void OnAvatarLoaded(bool success, string message)
    {
        if (!success) { _status.Text = message; return; }
        var value = GetNode<AvatarManager>("/root/AvatarManager").Current;
        _cameraSensitivity = value.CameraSensitivity; _invertY = value.InvertCameraY; _rig.ApplyAppearance(value);
        GetNode<AvatarManager>("/root/AvatarManager").Attachments.Refresh();
    }
}
