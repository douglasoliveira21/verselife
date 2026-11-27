using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the person chose in the settings window, kept in a JSON file in the user's data folder (the Godot client keeps the same groups in its own config files): graphics preset,
    /// sync and frame limit, volumes, mouse and movement, the interface scale and chat, and accessibility. Every value is brought within its limits when it is read, so a file edited by
    /// hand or from an older version can never leave the game unusable. The password is never kept; only the last user name is remembered, when asked.
    /// </summary>
    public sealed class UserSettings
    {
        public const float MinScale = .75f, MaxScale = 1.5f;
        public const int PresetCount = 4;
        public static readonly string[] PresetNames = { "Baixo", "Médio", "Alto", "Ultra" };

        // gráficos
        public int GraphicsPreset { get; set; } = 2;
        public bool VSync { get; set; } = true;
        public int FpsLimit { get; set; }                 // 0 = sem limite
        // áudio
        public float MasterVolume { get; set; } = 1f;
        public float MusicVolume { get; set; } = .8f;
        public float EffectsVolume { get; set; } = 1f;
        public float VoiceVolume { get; set; } = 1f;
        public float AmbientVolume { get; set; } = .8f;
        public float MediaVolume { get; set; } = 1f;
        /// <summary>How loud the voice must be for an open microphone to send (lower is more sensitive).</summary>
        public float VoiceSensitivity { get; set; } = .12f;
        public bool EchoGuard { get; set; }
        /// <summary>The microphone chosen ("auto" is the system default).</summary>
        public string InputDevice { get; set; } = "auto";
        /// <summary>Whether the music of parcels may play at all.</summary>
        public bool ParcelMusic { get; set; } = true;
        /// <summary>The small sounds of the interface (clicks, windows, notices).</summary>
        public bool UiSounds { get; set; } = true;
        // controles
        public float MouseSensitivity { get; set; } = .15f;
        public bool InvertY { get; set; }
        public bool AlwaysRun { get; set; }
        // interface
        public bool AutoScale { get; set; } = true;
        public float InterfaceScale { get; set; } = 1f;
        public float ChatOpacity { get; set; } = .8f;
        public int ChatFontSize { get; set; } = 14;
        // acessibilidade
        /// <summary>The keys the person changed (action name to key name); what is not here keeps its default.</summary>
        public Dictionary<string, string> Keys { get; set; } = new Dictionary<string, string>();
        public bool ReduceMotion { get; set; }
        public bool LargeText { get; set; }
        public bool HighContrast { get; set; }
        // sincronia com a conta (ver AccountSync): ligada para quem entrou, e a revisão que este computador viu
        // avisos na tela (o sino guarda tudo de qualquer jeito)
        /// <summary>The values of the settings catalog that have no property of their own (by id, as text), and the named profiles of all values.</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, string>> Profiles { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public string ActiveProfile { get; set; } = "";
        /// <summary>The last searches of the global search (kept on this computer only, and only while the person wants).</summary>
        public List<string> RecentSearches { get; set; } = new List<string>();
        public bool KeepSearchHistory { get; set; } = true;
        public bool ToastGroups { get; set; } = true;
        public bool ToastInvites { get; set; } = true;
        public bool ToastSystem { get; set; } = true;
        public bool SyncAccount { get; set; }
        public int SyncRevision { get; set; }
        public string SyncUser { get; set; } = "";
        // conta e janelas
        public string LastLogin { get; set; } = "";
        public string LastIsland { get; set; } = "";
        public Dictionary<string, float[]> Windows { get; set; } = new Dictionary<string, float[]>();
        /// <summary>The bottom toolbar: the order of its buttons and the ones hidden (ids).</summary>
        /// <summary>The music of the login screen is off.</summary>
        public bool LoginMusicMuted { get; set; }
        /// <summary>The people seen lately, per account (user id).</summary>
        public Dictionary<string, List<string>> RecentPeople { get; set; } = new Dictionary<string, List<string>>();
        public List<string> ToolbarOrder { get; set; } = new List<string>();
        public List<string> ToolbarHidden { get; set; } = new List<string>();
        /// <summary>Whether the person arranged the bar (otherwise it shows the default buttons in the default order).</summary>
        public bool ToolbarCustomized { get; set; }
        /// <summary>Where the toolbar stands: "bottom", "left" or "right".</summary>
        public string ToolbarEdge { get; set; } = "bottom";
        /// <summary>Where the camera looked last in each island (the yaw, by island id).</summary>
        public Dictionary<string, float> IslandYaw { get; set; } = new Dictionary<string, float>();

        public const int MaxSavedWindows = 200;

        /// <summary>Brings every value within its limits (what a hand-edited or old file may break).</summary>
        public UserSettings Clamped()
        {
            static float F(float v, float min, float max, float fallback) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : fallback;
            GraphicsPreset = Mathf.Clamp(GraphicsPreset, 0, PresetCount - 1); FpsLimit = FpsLimit <= 0 ? 0 : Mathf.Clamp(FpsLimit, 30, 360);
            MasterVolume = F(MasterVolume, 0f, 1f, 1f); MusicVolume = F(MusicVolume, 0f, 1f, .8f); EffectsVolume = F(EffectsVolume, 0f, 1f, 1f); VoiceVolume = F(VoiceVolume, 0f, 1f, 1f); AmbientVolume = F(AmbientVolume, 0f, 1f, .8f); MediaVolume = F(MediaVolume, 0f, 1f, 1f); VoiceSensitivity = F(VoiceSensitivity, .02f, .6f, .12f); InputDevice = string.IsNullOrWhiteSpace(InputDevice) ? "auto" : InputDevice.Length > 120 ? InputDevice.Substring(0, 120) : InputDevice;
            MouseSensitivity = F(MouseSensitivity, .02f, .6f, .15f); InterfaceScale = F(InterfaceScale, MinScale, MaxScale, 1f); ChatOpacity = F(ChatOpacity, .3f, 1f, .8f); ChatFontSize = Mathf.Clamp(ChatFontSize, 10, 28);
            Values = (Values ?? new Dictionary<string, string>()).Where(p => p.Key != null && p.Key.Length <= 64 && (p.Value ?? "").Length <= 128).Take(400).ToDictionary(p => p.Key, p => p.Value ?? "");
            Profiles = (Profiles ?? new Dictionary<string, Dictionary<string, string>>()).Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 40 && p.Value != null).Take(10).ToDictionary(p => p.Key, p => p.Value);
            ActiveProfile = ActiveProfile != null && Profiles.ContainsKey(ActiveProfile) ? ActiveProfile : "";
            RecentSearches = GlobalSearchWindow.Remember(null, null).Concat(RecentSearches ?? new List<string>()).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Length > 120 ? h.Substring(0, 120) : h).Distinct().Take(GlobalSearchWindow.MaxHistory).ToList();
            LastLogin = (LastLogin ?? "").Length > 64 ? LastLogin.Substring(0, 64) : LastLogin ?? ""; LastIsland = (LastIsland ?? "").Length > 64 ? LastIsland.Substring(0, 64) : LastIsland ?? "";
            Keys ??= new Dictionary<string, string>(); foreach (var key in new List<string>(Keys.Keys)) if (string.IsNullOrEmpty(Keys[key]) || key.Length > 40 || Keys[key].Length > 40) Keys.Remove(key); while (Keys.Count > 40) Keys.Remove(new List<string>(Keys.Keys)[0]);
            SyncRevision = Mathf.Max(0, SyncRevision); SyncUser = (SyncUser ?? "").Length > 40 ? SyncUser.Substring(0, 40) : SyncUser ?? "";
            Windows ??= new Dictionary<string, float[]>();
            if (ToolbarEdge != "left" && ToolbarEdge != "right") ToolbarEdge = "bottom";
            foreach (var key in new List<string>(Windows.Keys)) if (Windows[key] == null || (Windows[key].Length != 2 && Windows[key].Length != 4) || System.Array.Exists(Windows[key], v => !float.IsFinite(v))) Windows.Remove(key);
            while (Windows.Count > MaxSavedWindows) { var first = new List<string>(Windows.Keys)[0]; Windows.Remove(first); }
            return this;
        }

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        public static UserSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new UserSettings();
            try { return (JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings()).Clamped(); }
            catch (JsonException) { return new UserSettings(); }
        }

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static UserSettings Load(string path = null)
        {
            path ??= DefaultPath;
            try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new UserSettings(); } catch (IOException) { return new UserSettings(); } catch (UnauthorizedAccessException) { return new UserSettings(); }
        }

        /// <summary>Writes the file through a temporary one so a crash in the middle never leaves a half-written settings file.</summary>
        public bool Save(string path = null)
        {
            path ??= DefaultPath;
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); var temp = path + ".tmp"; File.WriteAllText(temp, Clamped().ToJson()); if (File.Exists(path)) File.Delete(path); File.Move(temp, path); return true; }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>The scale the interface is drawn with: chosen, or from the screen height when automatic (1080 px is 1).</summary>
        public float EffectiveScale(float screenHeight) => (AutoScale ? Mathf.Clamp(screenHeight / 1080f, MinScale, MaxScale) : InterfaceScale) * (LargeText ? 1.15f : 1f);
    }
}
