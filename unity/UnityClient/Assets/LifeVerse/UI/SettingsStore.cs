using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The values of the settings catalog, kept in <see cref="UserSettings"/>: the ones the Unity client already had stay in their own properties (so the old window, the account sync and the world see the same thing),
    /// the rest live in <see cref="UserSettings.Values"/> by id. Also restores the defaults of a category or of all, and keeps named profiles (a snapshot of every value) to switch between.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly UserSettings _s;
        public event Action<string> Changed;
        public SettingsStore(UserSettings settings) { _s = settings ?? new UserSettings(); }
        public UserSettings Settings => _s;

        private static string B(bool v) => v ? "true" : "false";
        private static bool IsTrue(string v) => v == "true";
        private static string N(float v) => SettingsCatalog.S(v);
        private static int I(string v) => (int)Math.Round(SettingsCatalog.F(v));

        /// <summary>The godot keys of the quality presets and the Unity preset each one uses (Baixo, Médio, Alto, Ultra).</summary>
        public static int PresetIndexOf(string key) => key switch { "verylow" => 0, "low" => 0, "medium" => 1, "high" => 2, "ultra" => 3, "auto" => AutoPreset(SystemInfo.graphicsMemorySize), _ => -1 };
        public static string PresetKeyOf(int index) => index switch { 0 => "low", 1 => "medium", 3 => "ultra", _ => "high" };
        /// <summary>"Automático": the preset by the memory of the graphics card (MB).</summary>
        public static int AutoPreset(int videoMegabytes) => videoMegabytes >= 6000 ? 3 : videoMegabytes >= 3000 ? 2 : videoMegabytes >= 1500 ? 1 : 0;
        /// <summary>The voice level that opens the microphone (0.01 to 0.7) from the Godot sensitivity (0 to 100, higher opens with less).</summary>
        public static float ThresholdOf(int sensitivity) => Mathf.Clamp((100 - Mathf.Clamp(sensitivity, 0, 100)) / 100f * .7f, .01f, .7f);
        public static int SensitivityOf(float threshold) => Mathf.Clamp(Mathf.RoundToInt(100 - threshold / .7f * 100f), 0, 100);

        private static readonly Dictionary<string, (Func<UserSettings, string> Get, Action<UserSettings, string> Set)> Bound = new Dictionary<string, (Func<UserSettings, string>, Action<UserSettings, string>)>
        {
            ["graphics.preset"] = (s => s.Values.TryGetValue("graphics.preset", out var k) ? k : PresetKeyOf(s.GraphicsPreset), (s, v) => { s.Values["graphics.preset"] = v; var i = PresetIndexOf(v); if (i >= 0) s.GraphicsPreset = i; }),
            ["graphics.vsync"] = (s => B(s.VSync), (s, v) => s.VSync = IsTrue(v)),
            ["graphics.fps_limit"] = (s => s.FpsLimit <= 0 ? "360" : s.FpsLimit.ToString(CultureInfo.InvariantCulture), (s, v) => { var n = I(v); s.FpsLimit = n >= 360 ? 0 : n; }),
            ["audio.master"] = (s => N(s.MasterVolume), (s, v) => s.MasterVolume = SettingsCatalog.F(v, 1f)),
            ["audio.music"] = (s => N(s.MusicVolume), (s, v) => s.MusicVolume = SettingsCatalog.F(v, .8f)),
            ["audio.effects"] = (s => N(s.EffectsVolume), (s, v) => s.EffectsVolume = SettingsCatalog.F(v, 1f)),
            ["audio.ambient"] = (s => N(s.AmbientVolume), (s, v) => s.AmbientVolume = SettingsCatalog.F(v, .8f)),
            ["audio.media"] = (s => N(s.MediaVolume), (s, v) => s.MediaVolume = SettingsCatalog.F(v, 1f)),
            ["audio.parcel_music"] = (s => B(s.ParcelMusic), (s, v) => s.ParcelMusic = IsTrue(v)),
            ["audio.ui_sounds"] = (s => B(s.UiSounds), (s, v) => s.UiSounds = IsTrue(v)),
            ["voice.users_volume"] = (s => N(s.VoiceVolume), (s, v) => s.VoiceVolume = SettingsCatalog.F(v, 1f)),
            ["voice.input_device"] = (s => string.IsNullOrEmpty(s.InputDevice) ? "auto" : s.InputDevice, (s, v) => s.InputDevice = v),
            ["voice.sensitivity"] = (s => SensitivityOf(s.VoiceSensitivity).ToString(CultureInfo.InvariantCulture), (s, v) => s.VoiceSensitivity = ThresholdOf(I(v))),
            ["voice.echo_cancel"] = (s => B(s.EchoGuard), (s, v) => s.EchoGuard = IsTrue(v)),
            ["controls.mouse_sensitivity"] = (s => N(s.MouseSensitivity / .15f), (s, v) => s.MouseSensitivity = .15f * SettingsCatalog.F(v, 1f)),
            ["controls.invert_y"] = (s => B(s.InvertY), (s, v) => s.InvertY = IsTrue(v)),
            ["controls.always_run"] = (s => B(s.AlwaysRun), (s, v) => s.AlwaysRun = IsTrue(v)),
            ["chat.opacity"] = (s => Mathf.RoundToInt(s.ChatOpacity * 100f).ToString(CultureInfo.InvariantCulture), (s, v) => s.ChatOpacity = I(v) / 100f),
            ["chat.font_size"] = (s => s.ChatFontSize.ToString(CultureInfo.InvariantCulture), (s, v) => s.ChatFontSize = I(v)),
            ["interface.scale"] = (s => s.AutoScale ? "auto" : N(s.InterfaceScale), (s, v) => { s.AutoScale = v == "auto"; if (v != "auto") s.InterfaceScale = SettingsCatalog.F(v, 1f); }),
            ["accessibility.reduce_motion"] = (s => B(s.ReduceMotion), (s, v) => s.ReduceMotion = IsTrue(v)),
            ["accessibility.contrast"] = (s => B(s.HighContrast), (s, v) => s.HighContrast = IsTrue(v)),
            ["accessibility.large_text"] = (s => B(s.LargeText), (s, v) => s.LargeText = IsTrue(v)),
            ["notifications.toast_grupos"] = (s => B(s.ToastGroups), (s, v) => s.ToastGroups = IsTrue(v)),
            ["notifications.toast_convites"] = (s => B(s.ToastInvites), (s, v) => s.ToastInvites = IsTrue(v)),
            ["notifications.toast_sistema"] = (s => B(s.ToastSystem), (s, v) => s.ToastSystem = IsTrue(v)),
            ["privacy.search_history"] = (s => B(s.KeepSearchHistory), (s, v) => { s.KeepSearchHistory = IsTrue(v); if (!s.KeepSearchHistory) s.RecentSearches = new List<string>(); }),
            ["account.sync"] = (s => B(s.SyncAccount), (s, v) => s.SyncAccount = IsTrue(v)),
        };

        public static bool IsBound(string id) => Bound.ContainsKey(id);

        /// <summary>The value of a setting now (normalized), its default when never set.</summary>
        public string Get(string id)
        {
            var d = SettingsCatalog.Find(id); if (d == null) return "";
            var raw = Bound.TryGetValue(id, out var bind) ? bind.Get(_s) : _s.Values.TryGetValue(id, out var v) ? v : d.Default;
            return d.IsValue ? SettingsCatalog.Normalize(d, raw) : raw;
        }

        public bool Bool(string id) => Get(id) == "true";
        public float Float(string id) => SettingsCatalog.F(Get(id));
        public int Int(string id) => (int)Math.Round(SettingsCatalog.F(Get(id)));

        /// <summary>Sets a setting (normalized). Changing by hand one the quality preset controls turns the preset into "Personalizada". Returns the value kept.</summary>
        public string Set(string id, string value, bool byHand = true)
        {
            var d = SettingsCatalog.Find(id); if (d == null || !d.IsValue) return "";
            var normal = SettingsCatalog.Normalize(d, value); var before = Get(id);
            if (Bound.TryGetValue(id, out var bind)) bind.Set(_s, normal); else _s.Values[id] = normal;
            if (byHand && d.PresetControlled && normal != before) _s.Values["graphics.preset"] = "custom";
            if (normal != before) Changed?.Invoke(id); return normal;
        }

        /// <summary>Puts back the defaults of one category (null = all); bound values go back to the defaults of the catalog too.</summary>
        public int Restore(string category = null)
        {
            var n = 0; foreach (var d in SettingsCatalog.All.Where(d => d.IsValue && (category == null || d.Category == category)))
            { if (Get(d.Id) != SettingsCatalog.Normalize(d, d.Default)) n++; Set(d.Id, d.Default, false); if (!Bound.ContainsKey(d.Id)) _s.Values.Remove(d.Id); }
            return n;
        }

        /// <summary>Every value of the catalog now, by id (what a profile keeps).</summary>
        public Dictionary<string, string> Snapshot() => SettingsCatalog.All.Where(d => d.IsValue && !d.Device).ToDictionary(d => d.Id, d => Get(d.Id));

        public IReadOnlyList<string> Profiles => _s.Profiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        public string ActiveProfile => _s.ActiveProfile ?? "";

        /// <summary>Saves the values of now under a name (at most 10 profiles; a name of 1 to 40 letters). Null when fine, else the reason.</summary>
        public string SaveProfile(string name)
        {
            name = (name ?? "").Trim(); if (name.Length == 0 || name.Length > 40) return "Dê ao perfil um nome de 1 a 40 letras.";
            if (!_s.Profiles.ContainsKey(name) && _s.Profiles.Count >= 10) return "Você já tem 10 perfis: apague um antes.";
            _s.Profiles[name] = Snapshot(); _s.ActiveProfile = name; return null;
        }

        /// <summary>Puts a saved profile back (ids it does not know keep their value). False when there is no such profile.</summary>
        public bool LoadProfile(string name)
        {
            if (!_s.Profiles.TryGetValue(name ?? "", out var values)) return false;
            foreach (var pair in values) if (SettingsCatalog.Find(pair.Key) is { IsValue: true }) Set(pair.Key, pair.Value, false);
            _s.ActiveProfile = name; return true;
        }

        public bool DeleteProfile(string name) { var gone = _s.Profiles.Remove(name ?? ""); if (gone && _s.ActiveProfile == name) _s.ActiveProfile = ""; return gone; }
    }
}
