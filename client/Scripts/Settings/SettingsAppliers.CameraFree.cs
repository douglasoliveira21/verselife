using LifeVerse.Client.Scripts.Camera;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>What the settings of the free camera do: they become the one <see cref="CameraSettings.Current"/> that the player, the editors and the camera controller read.</summary>
public static partial class SettingsAppliers
{
    /// <summary>The camera options the store holds (the multipliers the person sees times the base of the game).</summary>
    public static CameraOptions CameraOptionsFrom(SettingsStore store)
    {
        var limit = Godot.Mathf.DegToRad(store.Get<int>("camera.pitch_limit"));
        return new CameraOptions
        {
            SensitivityH = .003f * store.Get<float>("camera.sens_h"), SensitivityV = .003f * store.Get<float>("camera.sens_v"), InvertH = store.Get<bool>("camera.invert_h"), InvertV = store.Get<bool>("camera.invert_v"),
            ZoomSpeed = store.Get<float>("camera.zoom_speed"), PrecisionZoomSpeed = store.Get<float>("camera.precision_zoom_speed"), Smoothing = store.Get<float>("camera.free_smoothing"),
            MinDistance = store.Get<float>("camera.min_distance"), MaxDistance = store.Get<float>("camera.max_distance"), MinPitch = -limit, MaxPitch = limit,
            Collision = store.Get<bool>("camera.collision"), KeepAfterCtrl = store.Get<bool>("camera.keep_after_ctrl"), RestoreAutomatically = store.Get<bool>("camera.restore_auto"), RestoreAfterEdit = store.Get<bool>("camera.restore_after_edit"),
            AllowDetached = store.Get<bool>("camera.allow_detached"), Classic = store.Get<bool>("camera.classic_controls"), CtrlSimplified = store.Get<bool>("camera.ctrl_simple"),
            FocusOnDoubleClick = store.Get<bool>("camera.focus_double_click"), DetachedRange = store.Get<int>("camera.detached_range"), DetachedSpeed = store.Get<float>("camera.detached_speed")
        }.Clamped();
    }

    private static void RegisterCameraFree(SettingsStore store)
    {
        foreach (var id in SettingsCatalog.FreeCameraIds) store.RegisterApplier(id, _ => CameraSettings.Current = CameraOptionsFrom(store));
        store.RegisterApplier("camera.collision", _ => { CameraSettings.Current = CameraOptionsFrom(store); ApplyCamera(store); }); // the spring arm of the player too
        SettingsActions.Register("camera.restore_defaults", window => { foreach (var id in SettingsCatalog.FreeCameraIds) window.Store.Set(id, store.Definition(id)!.Default); window.ApplyChanges(); });
    }
}
