using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Camera;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Tests;

/// <summary>The options of the free camera in Configurações › Câmera e visualização: they exist, have sensible defaults, reach the camera, are kept for the next time and can be put back.</summary>
public partial class CameraSettingsSmoke : Node
{
    private const string Root = "user://smoke_camera_settings";
    private static void Wipe() { var absolute = ProjectSettings.GlobalizePath(Root); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public override async void _Ready()
    {
        try
        {
            Wipe(); CameraSettings.Reset();
            // ---- the catalog: every option of the request is there, explained, in the camera category
            foreach (var id in SettingsCatalog.FreeCameraIds) Require(SettingsCatalog.Find(id) is { Category: SettingCategory.Camera, Tab: SettingsCatalog.FreeCameraTab } d && d.Description.Length > 40 && d.Label.Length > 5, $"{id} is a documented option of the free camera");
            foreach (var id in new[] { "camera.sens_h", "camera.sens_v", "camera.zoom_speed", "camera.precision_zoom_speed", "camera.free_smoothing", "camera.invert_h", "camera.invert_v", "camera.min_distance", "camera.max_distance", "camera.collision", "camera.keep_after_ctrl", "camera.restore_auto", "camera.allow_detached", "camera.classic_controls", "camera.ctrl_simple" })
                Require(SettingsCatalog.Find(id) is not null, $"the option {id} exists");
            Require(SettingsCatalog.Find("camera.restore_defaults") is { Kind: SettingKind.Action, Confirm: not null }, "there is a button to put the free camera back to the default, and it asks first");
            Require(SettingsCatalog.All.Where(d => d.Category == SettingCategory.Camera).Select(d => d.Tab).Distinct().SequenceEqual(["Câmera", "Foto", "Câmera livre"]), $"the tabs of the camera category: {string.Join(",", SettingsCatalog.All.Where(d => d.Category == SettingCategory.Camera).Select(d => d.Tab).Distinct())}");
            Require(SettingsCatalog.All.Select(d => d.Id).Distinct().Count() == SettingsCatalog.All.Count, "no id is used twice");

            // ---- the defaults make the same camera as the code's own defaults
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world); await Frames(20);
            var player = world.GetNode<PlayerController>("Player"); var cam = player.GetNode<CameraController>("CameraController"); cam.PollKeyboard = false; player.CameraTargetResolver = null;
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(store);
            var fresh = SettingsAppliers.CameraOptionsFrom(store); var code = new CameraOptions().Clamped();
            Require(Mathf.IsEqualApprox(fresh.SensitivityH, code.SensitivityH) && Mathf.IsEqualApprox(fresh.ZoomSpeed, code.ZoomSpeed) && Mathf.IsEqualApprox(fresh.PrecisionZoomSpeed, code.PrecisionZoomSpeed) && Mathf.IsEqualApprox(fresh.Smoothing, code.Smoothing) && Mathf.IsEqualApprox(fresh.MinDistance, code.MinDistance) && Mathf.IsEqualApprox(fresh.MaxDistance, code.MaxDistance)
                && Mathf.IsEqualApprox(fresh.MinPitch, code.MinPitch, .001f) && fresh.Classic && !fresh.CtrlSimplified && fresh.KeepAfterCtrl && fresh.RestoreAutomatically && !fresh.AllowDetached && !fresh.FocusOnDoubleClick && fresh.Collision && !fresh.InvertH && !fresh.InvertV, "the defaults of the window are the defaults of the camera");

            // ---- every option reaches the camera
            store.Set("camera.sens_h", 2.0); store.Set("camera.sens_v", .5); store.Set("camera.invert_h", true); store.Set("camera.invert_v", true); store.Set("camera.zoom_speed", 2.0); store.Set("camera.precision_zoom_speed", .1); store.Set("camera.free_smoothing", 6.0);
            store.Set("camera.min_distance", .5); store.Set("camera.max_distance", 25.0); store.Set("camera.pitch_limit", 60); store.Set("camera.collision", false); store.Set("camera.keep_after_ctrl", false); store.Set("camera.restore_auto", false);
            store.Set("camera.allow_detached", true); store.Set("camera.classic_controls", false); store.Set("camera.ctrl_simple", true); store.Set("camera.focus_double_click", true); store.Set("camera.detached_range", 90); store.Set("camera.detached_speed", 12.0);
            Require(CameraSettings.Current.Classic, "nothing changes before the changes are applied"); store.Apply();
            var now = CameraSettings.Current;
            Require(Mathf.IsEqualApprox(now.SensitivityH, .006f) && Mathf.IsEqualApprox(now.SensitivityV, .0015f) && now.InvertH && now.InvertV && Mathf.IsEqualApprox(now.ZoomSpeed, 2) && Mathf.IsEqualApprox(now.PrecisionZoomSpeed, .1f) && Mathf.IsEqualApprox(now.Smoothing, 6), "sensitivities, inversions, zoom speeds and smoothing");
            Require(Mathf.IsEqualApprox(now.MinDistance, .5f) && Mathf.IsEqualApprox(now.MaxDistance, 25) && Mathf.IsEqualApprox(now.MaxPitch, Mathf.DegToRad(60), .001f) && !now.Collision && !now.KeepAfterCtrl && !now.RestoreAutomatically, "limits, vertical angle, collision and what happens after CTRL");
            Require(now.AllowDetached && !now.Classic && now.CtrlSimplified && now.FocusOnDoubleClick && now.DetachedRange == 90 && Mathf.IsEqualApprox(now.DetachedSpeed, 12), "detached camera, the two control modes and the double click");
            Require(!player.CameraCollides, "the collision switch reaches the camera of the player");

            // ---- and the camera behaves accordingly
            var click = (bool ctrl, bool alt) => new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = ctrl, AltPressed = alt, Position = new Vector2(400, 300) };
            Require(!cam.Handle(click(false, true)), "with the classic controls off ALT is nothing");
            var yaw = player.CameraYaw; cam.Handle(click(true, false)); cam.Handle(new InputEventMouseMotion { Relative = new Vector2(100, 0), CtrlPressed = true }); cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, CtrlPressed = true });
            Require(Mathf.Abs(player.CameraYaw - (yaw + .6f)) < .001f, $"CTRL turns the camera, twice as fast and the other way (sensitivity 2×, inverted): {player.CameraYaw - yaw}");
            player.ExitFreeView(); var distance = player.CameraDistanceGoal; player.EnterFreeView(); player.ZoomCamera(1, false); Require(Mathf.Abs(player.CameraDistanceGoal - distance * Mathf.Pow(.88f, 2)) < .02f, "the zoom speed doubles the notch");
            for (var i = 0; i < 200; i++) player.ZoomCamera(1, false); Require(Mathf.Abs(player.CameraDistanceGoal - .5f) < .001f, "the closest limit is the one of the window"); for (var i = 0; i < 400; i++) player.ZoomCamera(-1, false); Require(Mathf.Abs(player.CameraDistanceGoal - 25) < .001f, "and the farthest"); player.ExitFreeView();

            // ---- kept for the next time
            var again = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(again);
            Require(SettingsAppliers.CameraOptionsFrom(again).Equals(now), "starting again finds the same camera options");
            CameraSettings.Reset(); again.ApplyAll(); Require(CameraSettings.Current.Equals(now), "and applying them at the start gives the same camera");

            // ---- out of range values are brought back, and a sample of what is saved in a profile survives
            store.Set("camera.sens_h", 99.0); store.Set("camera.max_distance", 1e9); store.Set("camera.pitch_limit", 400); store.Apply();
            Require(CameraSettings.Current.SensitivityH <= .02f && CameraSettings.Current.MaxDistance <= 200 && CameraSettings.Current.MaxPitch <= 1.55f, "values out of range never leave the camera unusable");

            // ---- the window: the tab, the options, the button and its question
            var holder = new SubViewport { Size = new Vector2I(2400, 1350), Disable3D = true, TransparentBg = true }; AddChild(holder);
            var windowStore = new SettingsStore(Root + "_window", SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(windowStore);
            var window = new SettingsWindow(windowStore); holder.AddChild(window); window.Open(SettingCategory.Camera, SettingsCatalog.FreeCameraTab); await Frames(8);
            Require(window.Tab == SettingsCatalog.FreeCameraTab && window.RowOf("camera.ctrl_simple") is not null && window.RowOf("camera.sens_h") is not null && window.RowOf("camera.allow_detached") is not null && window.RowOf("camera.restore_defaults") is not null, "the tab shows the options and the restore button");
            windowStore.Set("camera.sens_h", 2.5); windowStore.Set("camera.ctrl_simple", true); windowStore.Set("camera.collision", false); windowStore.Apply();
            window.RunAction(SettingsCatalog.Find("camera.restore_defaults")!); await Frames(2);
            Require(window.Dialog.IsOpen && windowStore.Get<float>("camera.sens_h") == 2.5f, "restoring asks first and nothing changes until it is confirmed");
            window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(4);
            Require(windowStore.Get<float>("camera.sens_h") == 1f && !windowStore.Get<bool>("camera.ctrl_simple") && !windowStore.HasPending, "confirmed, the free camera options are back at the default and applied");
            Require(!windowStore.Get<bool>("camera.collision") && Mathf.IsEqualApprox(CameraSettings.Current.SensitivityH, .003f), "the other tabs are left alone and the camera has the default sensitivity again");

            GD.Print("event=camera_settings_smoke result=passed");
            CameraSettings.Reset(); Wipe(); GetTree().Quit();
        }
        catch (Exception exception)
        {
            CameraSettings.Reset();
            GD.PushError($"event=camera_settings_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }
}
