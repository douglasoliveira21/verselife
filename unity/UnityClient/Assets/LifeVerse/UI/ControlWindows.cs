using System;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A button that acts while it is held down (the arrows of the movement and camera windows): <see cref="Held"/> is true between the press and the release, also when the mouse leaves it
    /// with the button down.
    /// </summary>
    public sealed class HoldButton : VisualElement
    {
        public bool Held { get; private set; }
        public event Action<bool> Changed;

        public HoldButton(string name, string icon, string tip, float width = 46f, float height = 38f, string words = null)
        {
            this.name = name; tooltip = tip; AddToClassList("lv-btn"); style.width = width; style.height = height; style.alignItems = Align.Center; style.justifyContent = Justify.Center; style.flexDirection = FlexDirection.Row; pickingMode = PickingMode.Position;
            Add(new Icon(icon, 18));
            if (words != null) { var label = UiKit.Text(words, "lv-small"); label.style.marginLeft = 6; Add(label); }
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 || !enabledInHierarchy) return; this.CapturePointer(e.pointerId); Set(true); e.StopPropagation(); });
            RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); Set(false); });
            RegisterCallback<PointerCaptureOutEvent>(e => Set(false));
            RegisterCallback<DetachFromPanelEvent>(e => Set(false));
        }

        /// <summary>Presses or releases the button.</summary>
        public void Set(bool held)
        {
            if (Held == held) return; Held = held; EnableInClassList("lv-btn-selected", held);
            if (held) LifeVerse.Unity.Audio.UiSounds.Click(); Changed?.Invoke(held);
        }

        /// <summary>Three rows of three places, as a directional pad: up, left, right and down.</summary>
        internal static VisualElement Pad(VisualElement up, VisualElement left, VisualElement middle, VisualElement right, VisualElement down)
        {
            VisualElement Gap() { var gap = new VisualElement(); gap.style.width = 50; gap.style.height = 42; return gap; }
            var pad = new VisualElement(); pad.Add(UiKit.Row(Gap(), up, Gap())); pad.Add(UiKit.Row(left, middle ?? Gap(), right)); pad.Add(UiKit.Row(Gap(), down, Gap())); return pad;
        }
    }

    /// <summary>
    /// "Mover", the movement controls of the viewer: walk forward and back and turn with the mouse, run, jump or climb, come down, fly and sit. It adds to the keyboard, never replaces it;
    /// the arrows turn the view, and the avatar walks where the camera looks, as with the keys.
    /// </summary>
    public sealed class MoveWindow : FloatingWindow
    {
        public const float TurnDegreesPerSecond = 90f;
        public readonly HoldButton Forward, Back, TurnLeft, TurnRight, Jump, Descend;
        public readonly Button Run, Fly, Sit;
        public bool Running { get; private set; }
        private readonly Func<LocalPlayer> _player; private readonly Func<FollowCamera> _camera; private LocalPlayer _attached; private float _last;

        public MoveWindow(UiRoot ui, Func<LocalPlayer> player, Func<FollowCamera> camera) : base(ui, "move", "Mover", 316f, -1f, Corner(ui, 440f))
        {
            _player = player ?? (() => null); _camera = camera ?? (() => null);
            Forward = new HoldButton("move-forward", "caret-up", "Andar para a frente"); Back = new HoldButton("move-back", "caret-down", "Andar para trás");
            TurnLeft = new HoldButton("move-left", "caret-left", "Virar para a esquerda"); TurnRight = new HoldButton("move-right", "caret-right", "Virar para a direita");
            Jump = new HoldButton("move-jump", "chevron-up", "Pular; voando, subir", 132f, 30f, "Pular / subir"); Descend = new HoldButton("move-descend", "chevron-down", "Descer (voando ou nadando)", 132f, 30f, "Descer");
            Run = UiKit.WithIcon(UiKit.Button("Correr", () => SetRunning(!Running)), "run-fast"); Fly = UiKit.WithIcon(UiKit.Button("Voar", () => _player()?.RequestFlight()), "wing"); Sit = UiKit.WithIcon(UiKit.Button("Sentar", () => _player()?.RequestSit()), "ground");
            Run.name = "move-run"; Fly.name = "move-fly"; Sit.name = "move-sit";
            var side = new VisualElement(); side.style.marginLeft = 10; side.style.flexGrow = 1; foreach (var b in new VisualElement[] { Run, Fly, Sit }) { b.style.width = 132; side.Add(b); } side.Add(Jump); side.Add(Descend);
            var row = UiKit.Row(HoldButton.Pad(Forward, TurnLeft, null, TurnRight, Back), side); row.style.alignItems = Align.Center; Body.Add(row);
            _last = Time.realtimeSinceStartup; schedule.Execute(Step).Every(16);
            Closed += Release;
        }

        /// <summary>Over the toolbar, to the right of the chat.</summary>
        internal static Vector2 Corner(UiRoot ui, float left) { var height = ui.WindowLayer.layout.height; return new Vector2(left, height > 400f ? height - BottomInset - 290f : 340f); }

        public void SetRunning(bool running) { Running = running; Run.EnableInClassList("lv-btn-secondary", running); }

        /// <summary>What the buttons held ask for, added to the keyboard by <see cref="LocalPlayer.Pad"/>.</summary>
        public PlayerInputSample Sample() => new PlayerInputSample { Forward = (Forward.Held ? 1f : 0f) - (Back.Held ? 1f : 0f), Run = Running && (Forward.Held || Back.Held), Jump = Jump.Held, Descend = Descend.Held };

        /// <summary>Turns the view while a side arrow is held.</summary>
        public void Turn(float seconds)
        {
            var camera = _camera(); if (camera == null) return;
            var side = (TurnRight.Held ? 1f : 0f) - (TurnLeft.Held ? 1f : 0f); if (side != 0f) camera.Yaw += side * TurnDegreesPerSecond * seconds;
        }

        private void Step()
        {
            var now = Time.realtimeSinceStartup; var dt = Mathf.Clamp(now - _last, 0f, .1f); _last = now;
            var player = _player(); if (player != _attached) { Release(); _attached = player; }
            if (player != null) { player.Pad = Sample; Fly.text = player.State.Flying ? "Pousar" : "Voar"; Fly.EnableInClassList("lv-btn-primary", player.State.Flying); Sit.text = player.State.Seated ? "Levantar" : "Sentar"; }
            Turn(dt);
        }

        private void Release() { if (_attached != null) _attached.Pad = null; _attached = null; }
    }

    /// <summary>"Câmera", the camera controls of the viewer: turn around the person, look up and down, come closer, and the ready views (behind, in front, from the side, through their eyes).</summary>
    public sealed class CameraWindow : FloatingWindow
    {
        public const float TurnDegreesPerSecond = 80f, ZoomPerSecond = 5f, DefaultPitch = 14f, DefaultDistance = 4.6f;
        public readonly HoldButton Left, Right, Up, Down, Closer, Farther;
        private readonly Func<FollowCamera> _camera; private readonly Func<float> _heading; private float _last;

        public CameraWindow(UiRoot ui, Func<FollowCamera> camera, Func<float> heading) : base(ui, "camera", "Câmera", 316f, -1f, MoveWindow.Corner(ui, 770f))
        {
            _camera = camera ?? (() => null); _heading = heading ?? (() => 0f);
            Left = new HoldButton("camera-left", "caret-left", "Girar para a esquerda"); Right = new HoldButton("camera-right", "caret-right", "Girar para a direita");
            Up = new HoldButton("camera-up", "caret-up", "Olhar de mais alto"); Down = new HoldButton("camera-down", "caret-down", "Olhar de mais baixo");
            Closer = new HoldButton("camera-closer", "zoom-in", "Aproximar", 110f, 38f, "Perto"); Farther = new HoldButton("camera-farther", "zoom-out", "Afastar", 110f, 38f, "Longe");
            var side = new VisualElement(); side.style.marginLeft = 16; side.Add(Closer); side.Add(Farther);
            var row = UiKit.Row(HoldButton.Pad(Up, Left, null, Right, Down), side); Body.Add(row); Body.Add(UiKit.Divider());
            var views = new VisualElement(); views.style.flexDirection = FlexDirection.Row; views.style.flexWrap = Wrap.Wrap; views.style.justifyContent = Justify.Center;
            foreach (var (id, text, offset) in new[] { ("behind", "Atrás", 0f), ("front", "Frente", 180f), ("side", "Lado", 90f) }) { var turn = offset; var b = UiKit.Button(text, () => View(turn)); b.name = "camera-" + id; views.Add(b); }
            var eyes = UiKit.Button("1ª pessoa", () => _camera()?.Toggle()); eyes.name = "camera-eyes"; views.Add(eyes);
            var reset = UiKit.Button("Padrão", Reset, "lv-btn-primary"); reset.name = "camera-reset"; views.Add(reset); Body.Add(views);
            _last = Time.realtimeSinceStartup; schedule.Execute(Step).Every(16);
        }

        /// <summary>A ready view: the camera behind the avatar, turned by <paramref name="offset"/> degrees.</summary>
        public void View(float offset) { var camera = _camera(); if (camera == null) return; camera.SetFirstPerson(false); camera.Yaw = _heading() + offset; camera.Pitch = DefaultPitch; }

        /// <summary>Back to the camera as it starts: behind, at the usual height and distance.</summary>
        public void Reset() { View(0f); var camera = _camera(); if (camera == null) return; camera.Distance = DefaultDistance; camera.ZoomTarget = null; }

        /// <summary>Moves the camera by what is held, for a number of seconds.</summary>
        public void Move(float seconds)
        {
            var camera = _camera(); if (camera == null) return;
            var turn = (Right.Held ? 1f : 0f) - (Left.Held ? 1f : 0f); if (turn != 0f) camera.Yaw += turn * TurnDegreesPerSecond * seconds;
            var tilt = (Up.Held ? 1f : 0f) - (Down.Held ? 1f : 0f); if (tilt != 0f) camera.Pitch = Mathf.Clamp(camera.Pitch + tilt * TurnDegreesPerSecond * seconds, -60f, 80f);
            var zoom = (Farther.Held ? 1f : 0f) - (Closer.Held ? 1f : 0f); if (zoom == 0f) return;
            if (camera.ZoomTarget.HasValue) camera.ZoomDistance = Mathf.Clamp(camera.ZoomDistance + zoom * ZoomPerSecond * seconds, camera.FreeOptions.MinDistance + .3f, camera.FreeOptions.MaxDistance);
            else camera.Distance = Mathf.Clamp(camera.Distance + zoom * ZoomPerSecond * seconds, FollowCamera.MinDistance, FollowCamera.MaxDistance);
        }

        private void Step() { var now = Time.realtimeSinceStartup; var dt = Mathf.Clamp(now - _last, 0f, .1f); _last = now; Move(dt); }
    }
}
