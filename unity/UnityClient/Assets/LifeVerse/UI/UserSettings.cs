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
        public bool ReduceMotion { get; set; }
        public bool LargeText { get; set; }
        public bool HighContrast { get; set; }
        // conta e janelas
        public string LastLogin { get; set; } = "";
        public string LastIsland { get; set; } = "";
        public Dictionary<string, float[]> Windows { get; set; } = new Dictionary<string, float[]>();

        public const int MaxSavedWindows = 200;

        /// <summary>Brings every value within its limits (what a hand-edited or old file may break).</summary>
        public UserSettings Clamped()
        {
            static float F(float v, float min, float max, float fallback) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : fallback;
            GraphicsPreset = Mathf.Clamp(GraphicsPreset, 0, PresetCount - 1); FpsLimit = FpsLimit <= 0 ? 0 : Mathf.Clamp(FpsLimit, 30, 360);
            MasterVolume = F(MasterVolume, 0f, 1f, 1f); MusicVolume = F(MusicVolume, 0f, 1f, .8f); EffectsVolume = F(EffectsVolume, 0f, 1f, 1f); VoiceVolume = F(VoiceVolume, 0f, 1f, 1f);
            MouseSensitivity = F(MouseSensitivity, .02f, .6f, .15f); InterfaceScale = F(InterfaceScale, MinScale, MaxScale, 1f); ChatOpacity = F(ChatOpacity, .3f, 1f, .8f); ChatFontSize = Mathf.Clamp(ChatFontSize, 10, 28);
            LastLogin = (LastLogin ?? "").Length > 64 ? LastLogin.Substring(0, 64) : LastLogin ?? ""; LastIsland = (LastIsland ?? "").Length > 64 ? LastIsland.Substring(0, 64) : LastIsland ?? "";
            Windows ??= new Dictionary<string, float[]>();
            foreach (var key in new List<string>(Windows.Keys)) if (Windows[key] == null || Windows[key].Length != 2 || !float.IsFinite(Windows[key][0]) || !float.IsFinite(Windows[key][1])) Windows.Remove(key);
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
