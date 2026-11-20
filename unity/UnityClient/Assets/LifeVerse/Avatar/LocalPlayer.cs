using System;
using System.Collections.Generic;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>What the person asks for in one instant: a direction relative to the camera and the buttons.</summary>
    public struct PlayerInputSample
    {
        public float Forward, Right;                    // -1..1, relative to where the camera looks
        public bool Run, Jump, Descend, Fly, Sit, Typing; // Fly and Sit are one-shot toggles: true for a single step
    }

    /// <summary>
    /// The person playing: reads the keyboard, sends the movement to the island server 60 times a second and moves its own body with the same rules at once (the server's
    /// <see cref="AuthoritativeMovement"/>, from the shared code), so the avatar answers without waiting for the network. Every snapshot says where the server put the
    /// avatar after the last input it processed; the position the player had at that same input is compared with it, and the difference is closed (gently when small, at
    /// once when large), which is what keeps walls, other people's obstacles and a bad connection from drifting the two apart. The server decides; this only predicts.
    /// Not yet: obstacles of built objects in the prediction (the server's correction stops the avatar), vehicles, seats, first person, gestures and the head look.
    /// </summary>
    public sealed class LocalPlayer : MonoBehaviour
    {
        public const float Step = 1f / 60f;

        public WorldClient Client; public TerrainView Terrain; public PlayerViews Views; public FollowCamera Follow; public VehicleViews Vehicles;
        /// <summary>True while the person types in the chat: the avatar types on a keyboard for everybody to see (set by the chat window).</summary>
        public bool Typing;
        /// <summary>Running is the normal pace and Shift walks (settings).</summary>
        public bool AlwaysRun;
        /// <summary>The vehicle this person drives now, or null.</summary>
        public VehicleView Driving { get; private set; }
        public float LastHeadPitchSent { get; private set; }
        private float _headSentAt = -10f;
        /// <summary>When set it replaces the keyboard (tests and scripted walks).</summary>
        public Func<PlayerInputSample> Scripted;

        public AuthoritativePlayerState State { get; } = new AuthoritativePlayerState();
        public NetworkMotionState Motion { get; private set; }
        public bool Spawned { get; private set; }
        public uint Sequence { get; private set; }
        public int Corrections { get; private set; }
        public float LastError { get; private set; }
        public float HeadingDegrees { get; private set; }
        public Vector3 GamePosition => new Vector3(State.X, State.Y, State.Z);

        private float _accumulator, _flightAsked; private bool _flyEdge, _sitEdge, _flightWanted;
        private readonly Dictionary<uint, Vector3> _history = new Dictionary<uint, Vector3>();

        public void Attach(WorldClient client, TerrainView terrain, PlayerViews views, FollowCamera follow)
        {
            Client = client; Terrain = terrain; Views = views; Follow = follow; views.SelfIsExternal = true;
            client.PlayerSpawned += OnSpawn; client.SnapshotReceived += OnSnapshot;
            // attached after joining: start from where the server last put this body
            var self = client.SelfPeerId >= 0 ? views.ViewOf(client.SelfPeerId) : null;
            if (self != null) { State.X = self.TargetGame.x; State.Y = self.TargetGame.y; State.Z = self.TargetGame.z; Spawned = true; if (follow != null) follow.Snap(GamePosition); }
        }

        private void OnSpawn(SpawnPlayerMessage spawn)
        {
            if (spawn.PeerId != Client.SelfPeerId) return;
            State.X = spawn.X; State.Y = spawn.Y; State.Z = spawn.Z; State.VelocityX = State.VelocityY = State.VelocityZ = 0f; Spawned = true;
            if (Follow != null) Follow.Snap(GamePosition);
        }

        private PlayerInputSample ReadKeyboard()
        {
            var sample = new PlayerInputSample(); var keyboard = Keyboard.current; if (keyboard == null) return sample;
            sample.Forward = (KeyBindings.Held(GameAction.Forward) ? 1f : 0f) - (KeyBindings.Held(GameAction.Back) ? 1f : 0f);
            sample.Right = (KeyBindings.Held(GameAction.Right) ? 1f : 0f) - (KeyBindings.Held(GameAction.Left) ? 1f : 0f);
            sample.Run = KeyBindings.Held(GameAction.Run) ^ AlwaysRun; sample.Jump = KeyBindings.Held(GameAction.Jump); sample.Descend = KeyBindings.Held(GameAction.Descend);
            return sample;
        }

        private void Update()
        {
            if (!Typing) { if (KeyBindings.Pressed(GameAction.Fly)) _flyEdge = true; if (KeyBindings.Pressed(GameAction.Sit)) _sitEdge = true; if (KeyBindings.Pressed(GameAction.Vehicle)) ToggleVehicle(); if (ObjectSeat.HasValue && (ServerSeated || State.Seated) && (KeyBindings.Pressed(GameAction.Forward) || KeyBindings.Pressed(GameAction.Back) || KeyBindings.Pressed(GameAction.Left) || KeyBindings.Pressed(GameAction.Right) || KeyBindings.Pressed(GameAction.Jump))) StandUp(); }
            GestureKeys();
            Advance(Time.deltaTime);
        }

        /// <summary>Runs as many 1/60 s steps as the elapsed time holds (the game calls it every frame, tests with a chosen time).</summary>
        public void Advance(float delta)
        {
            if (!Spawned || Client == null || !Client.Authenticated) return;
            _accumulator = Mathf.Min(_accumulator + delta, .25f);
            var driving = Vehicles != null ? Vehicles.DrivenBy(Client.SelfPeerId) : null;
            if (driving != null && Driving == null) { _history.Clear(); State.Flying = false; State.VelocityX = State.VelocityY = State.VelocityZ = 0f; }
            Driving = driving;
            while (_accumulator >= Step) { _accumulator -= Step; if (Driving != null) DriveOnce(); else StepOnce(); }
            if (Driving != null) { State.X = Driving.GamePosition.x; State.Y = Driving.GamePosition.y + LifeVerse.Shared.Vehicles.VehicleFleet.DriverHeight; State.Z = Driving.GamePosition.z; State.Seated = true; Motion = NetworkMotionState.Sit; HeadingDegrees = -Driving.Yaw * Mathf.Rad2Deg; }
            SendHeadLook();
            KeepAskingForSeat();
            if (ObjectSeat.HasValue) { if (State.Seated || ServerSeated) { _seatSeen = true; _standing = 0f; } else if (_seatSeen) { _standing += delta; if (_standing > .6f) ObjectSeat = null; } }   // a reconcile can flicker the seat for a moment: only a lasting stand counts   // stood up (X, or the server said so): back to the ground where the person was
            var onSeat = ObjectSeat.HasValue && (State.Seated || ServerSeated && _seatSeen); var shown = onSeat ? ObjectSeat.Value : GamePosition; var shownHeading = onSeat ? ObjectSeatHeading : HeadingDegrees;
            var view = Views != null ? Views.ViewOf(Client.SelfPeerId) : null;
            if (view != null)
            {
                view.External = true; view.SetPose(shown, shownHeading, new Vector3(State.VelocityX, State.VelocityY, State.VelocityZ), Motion);
                if (Follow != null && view.Visible == Follow.FirstPerson) view.SetVisible(!Follow.FirstPerson);   // one's own body is not drawn from the inside
            }
            if (Follow != null)
            {
                var h = Follow.Yaw * Mathf.Deg2Rad; var strafe = State.VelocityX * Mathf.Cos(h) + State.VelocityZ * Mathf.Sin(h);   // the part of the velocity along the camera's right
                Follow.Follow(shown, Mathf.Sqrt(State.VelocityX * State.VelocityX + State.VelocityZ * State.VelocityZ), State.Grounded, strafe);
            }
        }

        // ---- sitting on an object of the world: the seat belongs to the object and is known only on this computer (the island just learns that the person sits, as on the ground) ----
        private bool _seatSeen; private float _standing;
        /// <summary>What the island last said about sitting (the prediction can flicker between a toggle and its confirmation; this does not).</summary>
        public bool ServerSeated { get; private set; }
        public int SeatRetries => _retries; public bool WantSeat => _wantSeat.HasValue;
        private bool? _wantSeat; private float _retryAt; private int _retries; private uint _askSeq, _lastProcessed; private bool _asking;
        /// <summary>A sit toggle can still be lost on the way (the island keeps one input per tick): while the island has not confirmed what was asked, ask again, a few times.</summary>
        private void KeepAskingForSeat()
        {
            if (!_wantSeat.HasValue) return; if (ServerSeated == _wantSeat.Value) { _wantSeat = null; return; }
            if (Time.realtimeSinceStartup < _retryAt || _askSeq == 0 || _lastProcessed < _askSeq) return;   // only when the island has used the input that carried the toggle and still disagrees
            if (_retries >= 4) { _wantSeat = null; return; }
            _retries++; _retryAt = Time.realtimeSinceStartup + 1.2f; _sitEdge = true; _askSeq = 0;
        }
        /// <summary>Where the person is drawn while sitting on an object (game space), or null.</summary>
        public Vector3? ObjectSeat { get; private set; }
        public float ObjectSeatHeading { get; private set; }

        /// <summary>Sits on top of an object: the person sits where they are for the island and is drawn on the object. False while flying, swimming, driving or already sitting.</summary>
        public bool SitOnObject(Vector3 topGamePosition, float headingDegrees)
        {
            if (!Spawned || Client == null || State.Seated || State.Flying || State.Swimming || Driving != null) return false;
            ObjectSeat = topGamePosition; ObjectSeatHeading = headingDegrees; _seatSeen = false; _standing = 0f; _sitEdge = true; _wantSeat = true; _retries = 0; _retryAt = Time.realtimeSinceStartup + 1.2f; return true;
        }

        /// <summary>Gets up from the seat (the same toggle as X).</summary>
        public void StandUp() { if (ServerSeated || State.Seated) { _sitEdge = true; _wantSeat = false; _retries = 0; _retryAt = Time.realtimeSinceStartup + 1.2f; } }

        /// <summary>Asks to start or stop flying (the F key, the radial menu).</summary>
        public void RequestFlight() => _flyEdge = true;

        // ---- vehicles: the server drives the car and pins the driver to it; the client sends the throttle, the steering and the brake ----

        /// <summary>E: get into the nearest vehicle (within 4 m), or out of the one being driven.</summary>
        public bool ToggleVehicle()
        {
            if (!Spawned || Client == null || Vehicles == null) return false;
            if (Driving != null) { Client.Send(new ExitVehicleMessage()); return true; }
            var car = Vehicles.Nearest(GamePosition, LifeVerse.Shared.Vehicles.VehicleFleet.EnterRange); if (car == null) return false;
            State.Flying = false; Client.Send(new EnterVehicleMessage(car.Id)); return true;
        }

        private void DriveOnce()
        {
            var sample = Scripted != null ? Scripted() : ReadKeyboard();
            Sequence++; Client.Send(new SubmitVehicleInputMessage(Mathf.Clamp(sample.Forward, -1f, 1f), Mathf.Clamp(sample.Right, -1f, 1f), sample.Jump));
        }

        /// <summary>Tells the others how far up or down the person looks (only through their own eyes), as the shared rules allow: when it changed enough, and again every second.</summary>
        private void SendHeadLook()
        {
            var pitch = Follow != null && Follow.FirstPerson ? Mathf.Clamp(-Follow.Pitch * Mathf.Deg2Rad, -HeadLook.AcceptedPitch, HeadLook.AcceptedPitch) : 0f;
            Views?.ViewOf(Client.SelfPeerId)?.ApplyHeadLook(pitch);
            var since = Time.realtimeSinceStartup - _headSentAt;
            if (!HeadLook.ShouldSend(LastHeadPitchSent, pitch, TimeSpan.FromSeconds(since))) return;
            _headSentAt = Time.realtimeSinceStartup; LastHeadPitchSent = pitch; Client.Send(new HeadLookMessage(pitch));
        }

        // ---- gestures: wave and dance are input flags the server turns into a motion state (sent every step while they last); the others are told once ----
        private float _waveLeft, _danceLeft;
        public const float WaveSeconds = 2.5f, DanceSeconds = 5f;
        public bool Gesturing => _waveLeft > 0f || _danceLeft > 0f || (Views != null && Views.ViewOf(Client.SelfPeerId) is { } own && own.ActiveGesture != AvatarView.Gesture.None);

        /// <summary>Starts a gesture by name: wave, dance, applaud, point, greet, laugh, sitfloor. Not while sitting, flying, swimming or moving.</summary>
        public bool StartGesture(string name)
        {
            if (!Spawned || State.Seated || State.Flying || State.Swimming) return false;
            switch (name)
            {
                case "wave": _waveLeft = WaveSeconds; _danceLeft = 0f; return true;
                case "dance": _danceLeft = DanceSeconds; _waveLeft = 0f; return true;
            }
            var id = System.Array.IndexOf(new[] { "applaud", "point", "greet", "laugh", "sitfloor" }, name); if (id < 0) return false;
            _waveLeft = _danceLeft = 0f; Client.Send(new GestureMessage(id)); Views?.ViewOf(Client.SelfPeerId)?.PlayGesture(id, ExtraGestures.Seconds); return true;
        }

        private void GestureKeys()
        {
            if (Typing) return;   // letters typed in the chat are not gestures
            if (KeyBindings.Pressed(GameAction.Wave)) StartGesture("wave"); if (KeyBindings.Pressed(GameAction.Dance)) StartGesture("dance");
            if (KeyBindings.Pressed(GameAction.Applaud)) StartGesture("applaud"); if (KeyBindings.Pressed(GameAction.Point)) StartGesture("point");
            if (KeyBindings.Pressed(GameAction.Greet)) StartGesture("greet"); if (KeyBindings.Pressed(GameAction.Laugh)) StartGesture("laugh");
        }

        private void StepOnce()
        {
            var sample = Scripted != null ? Scripted() : ReadKeyboard();
            sample.Typing |= Typing;
            if (_flyEdge) { sample.Fly = true; _flyEdge = false; }
            if (_sitEdge) { sample.Sit = true; _sitEdge = false; _asking = true; }
            // flying is a state the server is asked for (reliable, idempotent), not a per-step input: ask, and take it at once locally as the server will
            if (sample.Fly && !State.Seated)
            {
                var wanted = !State.Flying; State.Flying = wanted; if (wanted) { State.Swimming = false; State.VelocityY = Mathf.Max(State.VelocityY, 2f); }
                _flightWanted = wanted; _flightAsked = Time.realtimeSinceStartup; Client.Send(new SetFlightMessage(wanted));
            }
            sample.Fly = false;
            // the direction relative to the camera: the camera's heading h (Unity, degrees) looks along game (sin h, -cos h); right of it is (cos h, sin h)
            var h = (Follow != null ? Follow.Yaw : 0f) * Mathf.Deg2Rad; float fx = Mathf.Sin(h), fz = -Mathf.Cos(h), rx = Mathf.Cos(h), rz = Mathf.Sin(h);
            var x = fx * sample.Forward + rx * sample.Right; var z = fz * sample.Forward + rz * sample.Right;
            var length = Mathf.Sqrt(x * x + z * z); if (length > 1f) { x /= length; z /= length; }
            var flags = InputFlags.None;
            // moving, jumping, flying or sitting ends a gesture
            if (length > .05f || sample.Jump || sample.Fly || sample.Sit || State.Flying || State.Seated || State.Swimming) { _waveLeft = _danceLeft = 0f; Views?.ViewOf(Client.SelfPeerId)?.StopGesture(); }
            if (_waveLeft > 0f) { flags |= InputFlags.Wave; _waveLeft -= Step; } else if (_danceLeft > 0f) { flags |= InputFlags.Dance; _danceLeft -= Step; }
            if (sample.Run) flags |= InputFlags.Run; if (sample.Jump) flags |= InputFlags.Jump; if (sample.Descend) flags |= InputFlags.Descend;
            if (sample.Fly) flags |= InputFlags.Fly; if (sample.Sit) flags |= InputFlags.Sit; if (sample.Typing) flags |= InputFlags.Typing;
            Sequence++; if (_asking) { _askSeq = Sequence; _asking = false; } Client.SendInput(Sequence, x, z, flags);
            var map = Terrain != null ? Terrain.Map : null;
            Motion = AuthoritativeMovement.Step(State, new MovementInput(Sequence, x, z, flags), Step, map != null ? map.SampleHeight : (Func<float, float, float>)null,
                map != null ? map.SeaLevel : float.NegativeInfinity, map != null ? map.HalfExtent + TerrainMap.OceanMargin : float.PositiveInfinity, Client != null && Client.Objects != null ? Client.Objects.Obstacles : null);
            _history[Sequence] = GamePosition; if (Sequence > 240) _history.Remove(Sequence - 240);
            if (State.VelocityX * State.VelocityX + State.VelocityZ * State.VelocityZ > .09f) HeadingDegrees = Mathf.Atan2(State.VelocityX, -State.VelocityZ) * Mathf.Rad2Deg;
        }

        private void OnSnapshot(WorldSnapshot snapshot)
        {
            if (!Spawned) return;
            foreach (var player in snapshot.Players)
            {
                if (player.PeerId != Client.SelfPeerId) continue;
                Reconcile(player); return;
            }
        }

        /// <summary>Closes the gap between what the server decided and what was predicted at the same input.</summary>
        public void Reconcile(PlayerSnapshot server)
        {
            var served = new Vector3(server.X, server.Y, server.Z);
            var then = _history.TryGetValue(server.LastProcessedInput, out var past) ? past : GamePosition;
            var error = served - then; LastError = error.magnitude;
            // flight and sitting are toggles the server may have refused or that a lost packet missed: the server's word wins
            var serverFlying = server.Motion == NetworkMotionState.Fly;
            if (_flightAsked > 0f) { if (serverFlying == _flightWanted || Time.realtimeSinceStartup - _flightAsked > 1.5f) { _flightAsked = 0f; State.Flying = serverFlying; } }
            else State.Flying = serverFlying;
            State.Seated = server.Motion == NetworkMotionState.Sit; ServerSeated = State.Seated; _lastProcessed = server.LastProcessedInput;
            if (LastError > 3f) { State.X += error.x; State.Y += error.y; State.Z += error.z; State.VelocityX = server.VelocityX; State.VelocityY = server.VelocityY; State.VelocityZ = server.VelocityZ; Corrections++; }
            else if (LastError > .08f) { State.X += error.x * .3f; State.Y += error.y * .3f; State.Z += error.z * .3f; Corrections++; }
        }

        private void OnDestroy() { if (Client != null) { Client.PlayerSpawned -= OnSpawn; Client.SnapshotReceived -= OnSnapshot; } }
    }
}
