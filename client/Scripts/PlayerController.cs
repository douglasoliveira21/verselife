using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Scripts;

public partial class PlayerController : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 3.2f;
    [Export] public float RunSpeed { get; set; } = 6.4f;
    [Export] public float FlySpeed { get; set; } = 7.5f;
    [Export] public float SwimSpeed { get; set; } = 3.5f;
    [Export] public float JumpVelocity { get; set; } = 5.2f;
    private AvatarRig _rig = null!;
    private Node3D _cameraPivot = null!;
    private SpringArm3D _springArm = null!;
    private Camera3D _camera = null!;
    private Control _contextMenu = null!;
    private Label _status = null!;
    private InteractableSeat? _selectedSeat;
    private InteractableSeat? _seat;
    private bool _flying;
    private bool _swimming;
    private bool _firstPerson;
    private bool _cursorFree;
    private bool _wasGrounded;
    private float _landingTime;
    private float _emoteTime;
    private AvatarMotionState _emoteState;
    private float _cameraSensitivity = .003f;
    private bool _invertY;
    private float _cameraDistance = 5.2f;
    private NetworkManager _network = null!;
    private uint _inputSequence;

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
        _contextMenu = GetNode<Control>("HUD/ContextMenu");
        _status = GetNode<Label>("HUD/Status");
        GetNode<Button>("HUD/ContextMenu/Sit").Pressed += ContextSit;
        GetNode<Button>("HUD/ContextMenu/Cancel").Pressed += () => _contextMenu.Visible = false;
        GetNode<Button>("HUD/EditAvatar").Pressed += () => GetNode<UIManager>("/root/UIManager").ShowCharacterCreator();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        var avatars = GetNode<AvatarManager>("/root/AvatarManager");
        avatars.AvatarLoaded += OnAvatarLoaded;
        var error = avatars.Load();
        if (error != Error.Ok) GD.PushWarning($"event=avatar_load_start_failed error={error}");
    }

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var avatars = GetNodeOrNull<AvatarManager>("/root/AvatarManager");
        if (avatars is not null) avatars.AvatarLoaded -= OnAvatarLoaded;
        _network?.UnregisterLocalPlayer(this);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed("cursor_toggle")) SetCursorFree(!_cursorFree);
        if (input.IsActionPressed("first_person")) ToggleFirstPerson();
        if (input.IsActionPressed("fly_toggle") && _seat is null && !_swimming)
        {
            _flying = !_flying;
            if (_flying) Velocity = new Vector3(Velocity.X, 2f, Velocity.Z);
        }
        if (input.IsActionPressed("interact")) InteractOrStand();
        if (input.IsActionPressed("emote_wave")) StartEmote(AvatarMotionState.Wave, 2.5f);
        if (input.IsActionPressed("emote_dance")) StartEmote(AvatarMotionState.Dance, 5f);
        if (input is InputEventMouseMotion motion && !_cursorFree)
        {
            RotateY(-motion.Relative.X * _cameraSensitivity);
            var direction = _invertY ? 1f : -1f;
            _cameraPivot.Rotation = new Vector3(Mathf.Clamp(_cameraPivot.Rotation.X + motion.Relative.Y * _cameraSensitivity * direction, -1.25f, .85f), 0, 0);
        }
        if (input is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) SetZoom(_cameraDistance - .5f);
            if (button.ButtonIndex == MouseButton.WheelDown) SetZoom(_cameraDistance + .5f);
            if (button.ButtonIndex == MouseButton.Right && _cursorFree) OpenContextMenu(button.Position);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (_seat is not null) { UpdateSeated(); return; }
        if (_emoteTime > 0)
        {
            _emoteTime -= dt;
            Velocity = Vector3.Zero;
            _rig.SetMotionState(_emoteState);
            MoveAndSlide();
            return;
        }
        if (_landingTime > 0) _landingTime -= dt;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var forward = -GlobalTransform.Basis.Z; forward.Y = 0; forward = forward.Normalized();
        var right = GlobalTransform.Basis.X; right.Y = 0; right = right.Normalized();
        var direction = (right * input.X + forward * -input.Y).Normalized();
        var flags = InputFlags.None;
        if (Input.IsActionPressed("run")) flags |= InputFlags.Run;
        if (Input.IsActionJustPressed("jump")) flags |= InputFlags.Jump;
        if (Input.IsActionJustPressed("fly_toggle")) flags |= InputFlags.Fly;
        if (Input.IsActionPressed("descend")) flags |= InputFlags.Descend;
        _network.SendInput(++_inputSequence, new Vector2(direction.X, direction.Z), flags);
        var velocity = Velocity;

        if (_swimming)
        {
            _flying = false; velocity = direction * SwimSpeed;
            velocity.Y = Input.IsActionPressed("jump") ? SwimSpeed : Input.IsActionPressed("descend") ? -SwimSpeed : 0;
        }
        else if (_flying)
        {
            velocity = direction * FlySpeed;
            velocity.Y = Input.IsActionPressed("jump") ? FlySpeed : Input.IsActionPressed("descend") ? -FlySpeed : 0;
        }
        else
        {
            if (!IsOnFloor()) velocity.Y -= 9.8f * dt;
            if (Input.IsActionJustPressed("jump") && IsOnFloor()) velocity.Y = JumpVelocity;
            var speed = (Input.IsActionPressed("run") ? RunSpeed : WalkSpeed) * SurfaceSpeedMultiplier();
            velocity.X = direction.X * speed; velocity.Z = direction.Z * speed;
        }

        Velocity = velocity;
        MoveAndSlide();
        if (_flying && IsOnFloor() && Input.IsActionPressed("descend")) _flying = false;
        var grounded = IsOnFloor();
        if (grounded && !_wasGrounded) _landingTime = .22f;
        _wasGrounded = grounded;
        UpdateAnimation(direction, input.Length());
        _status.Text = _swimming ? "Swimming" : _flying ? "Flying" : Input.IsActionPressed("run") ? "Running" : "Walking";
    }

    public void SetSwimming(bool value) => _swimming = value;
    public void Reconcile(Vector3 authoritativePosition, Vector3 authoritativeVelocity, uint acknowledgedInput)
    {
        var error = GlobalPosition.DistanceTo(authoritativePosition);
        if (error > 3f) GlobalPosition = authoritativePosition;
        else if (error > .08f) GlobalPosition = GlobalPosition.Lerp(authoritativePosition, .18f);
        if (error > .5f) Velocity = Velocity.Lerp(authoritativeVelocity, .35f);
    }
    public void Sit(InteractableSeat seat)
    {
        if (_flying || _swimming || _seat is not null) return;
        _seat = seat; Velocity = Vector3.Zero; GlobalTransform = seat.SeatTransform;
        _rig.SetMotionState(AvatarMotionState.Sit, true); _contextMenu.Visible = false;
    }

    private void Stand()
    {
        if (_seat is null) return;
        var exit = _seat.ExitPosition; _seat = null; GlobalPosition = exit;
        _emoteState = AvatarMotionState.StandUp; _emoteTime = .5f;
    }

    private void UpdateSeated()
    {
        if (_seat is null) return;
        GlobalTransform = _seat.SeatTransform; Velocity = Vector3.Zero; _rig.SetMotionState(AvatarMotionState.Sit);
        if (Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("interact")) Stand();
    }

    private void UpdateAnimation(Vector3 direction, float inputAmount)
    {
        var snapshot = new MovementSnapshot(false, _swimming, _flying, IsOnFloor(), Velocity.Y > 0,
            _landingTime > 0, direction.LengthSquared() > .01f && inputAmount >= .05f, Input.IsActionPressed("run"));
        _rig.SetMotionState(MovementStateResolver.Resolve(snapshot));
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

    private void OpenContextMenu(Vector2 screenPosition)
    {
        _selectedSeat = RaycastSeat();
        if (_selectedSeat is null) return;
        _contextMenu.Position = screenPosition; _contextMenu.Visible = true;
    }

    private void InteractOrStand() { if (_seat is not null) { Stand(); return; } RaycastSeat()?.Interact(this); }
    private InteractableSeat? RaycastSeat()
    {
        var center = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(center); var to = from + _camera.ProjectRayNormal(center) * 5f;
        var query = PhysicsRayQueryParameters3D.Create(from, to); query.Exclude = [GetRid()];
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("collider", out var collider) ? collider.AsGodotObject() as InteractableSeat : null;
    }

    private void ContextSit() { _selectedSeat?.Interact(this); _selectedSeat = null; }
    private void StartEmote(AvatarMotionState state, float duration) { if (_seat is not null || _flying || _swimming) return; _emoteState = state; _emoteTime = duration; }
    private void SetCursorFree(bool value) { _cursorFree = value; Input.MouseMode = value ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured; }
    private void ToggleFirstPerson() { _firstPerson = !_firstPerson; _springArm.SpringLength = _firstPerson ? .05f : _cameraDistance; _rig.Visible = !_firstPerson; }
    private void SetZoom(float value) { _cameraDistance = Mathf.Clamp(value, 2f, 8f); if (!_firstPerson) _springArm.SpringLength = _cameraDistance; }
    private void OnAvatarLoaded(bool success, string message)
    {
        if (!success) { _status.Text = message; return; }
        var value = GetNode<AvatarManager>("/root/AvatarManager").Current;
        _cameraSensitivity = value.CameraSensitivity; _invertY = value.InvertCameraY; _rig.ApplyAppearance(value);
    }
}
