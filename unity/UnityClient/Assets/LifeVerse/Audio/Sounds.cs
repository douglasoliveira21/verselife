using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LifeVerse.Shared.Audio;
using UnityEngine;
using UnityEngine.Networking;

namespace LifeVerse.Unity.Audio
{
    /// <summary>The recordings that come with the game (sneakers on concrete, a keyboard, the login theme). They are copied from client/Assets/audio into StreamingAssets when the player is built and read from there once.</summary>
    public static class SoundLibrary
    {
        public const string Footsteps = "footsteps", Typing = "typing", Login = "login";
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string> { [Footsteps] = "footsteps_concrete.mp3", [Typing] = "keyboard_typing.mp3", [Login] = "login_theme.mp3" };
        public static string Folder => Path.Combine(Application.streamingAssetsPath, "audio");
        public static AudioClip Get(string name) => Clips.TryGetValue(name, out var clip) ? clip : null;
        public static bool Has(string name) => Get(name) != null;
        public static bool FileExists(string name) => Files.TryGetValue(name, out var file) && File.Exists(Path.Combine(Folder, file));
        /// <summary>Puts a clip in place of a recording (tests give short synthetic ones; nothing needs the files).</summary>
        public static void Override(string name, AudioClip clip) { if (clip == null) Clips.Remove(name); else Clips[name] = clip; }
        public static void Forget() => Clips.Clear();

        public static IEnumerator Load(string name)
        {
            if (Has(name) || !FileExists(name)) yield break;
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.Combine(Folder, Files[name])).AbsoluteUri, AudioType.MPEG);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { Debug.LogWarning("[LifeVerse] som " + name + ": " + request.error); yield break; }
            var clip = DownloadHandlerAudioClip.GetContent(request); if (clip != null) { clip.name = name; Clips[name] = clip; }
        }

        public static IEnumerator LoadAll() { foreach (var name in Files.Keys) yield return Load(name); }

        /// <summary>A clip made of a tone, for tests and as the fallback when a recording is missing.</summary>
        public static AudioClip Tone(string name, float seconds, float hertz, float volume = .2f, int rate = 22050)
        {
            var frames = Mathf.Max(1, (int)(seconds * rate)); var data = new float[frames]; for (var i = 0; i < frames; i++) data[i] = Mathf.Sin(2f * Mathf.PI * hertz * i / rate) * volume;
            var clip = AudioClip.Create(name, frames, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }

    /// <summary>Turning the bytes of a sound file (wav, mp3 or ogg) into a clip, and fetching a sound from the internet under the same rules as the island: https, no machine inside a network, no redirects, a size limit.</summary>
    public static class AudioDecode
    {
        public const int MaxBytes = 12 * 1024 * 1024;

        /// <summary>What the bytes are: "wav", "ogg", "mp3", or null when they are not a sound.</summary>
        public static string Sniff(byte[] b)
        {
            if (b == null || b.Length < 12) return null;
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E') return "wav";
            if (b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S') return "ogg";
            if (b[0] == 'I' && b[1] == 'D' && b[2] == '3') return "mp3";
            if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return "mp3";
            return null;
        }

        public static async Task<AudioClip> DecodeAsync(byte[] bytes, string name = "sound")
        {
            var kind = Sniff(bytes); if (kind == null || bytes.Length > MaxBytes) return null;
            if (kind == "wav")
            {
                var wav = WavReader.Read(bytes); if (wav == null || wav.Frames <= 0 || wav.Seconds > 600) return null;
                var clip = AudioClip.Create(name, wav.Frames, wav.Channels, wav.SampleRate, false); clip.SetData(wav.Samples, 0); return clip;
            }
            string path; using (var sha = SHA256.Create()) path = Path.Combine(Application.temporaryCachePath, "lvaudio", BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 24) + "." + kind);
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); if (!File.Exists(path)) File.WriteAllBytes(path, bytes); } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { return null; }
            var source = new TaskCompletionSource<AudioClip>(); var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, kind == "ogg" ? AudioType.OGGVORBIS : AudioType.MPEG);
            request.SendWebRequest().completed += _ => { try { source.TrySetResult(request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null); } finally { request.Dispose(); } };
            var decoded = await source.Task; if (decoded != null) decoded.name = name; return decoded;
        }

        /// <summary>Null when the address may be fetched (https, public name, mp3 or ogg), otherwise why not.</summary>
        public static string Refuse(string url) => LifeVerse.Shared.Land.LandMediaUrl.Check(url);

        /// <summary>The bytes of a file on the internet, or null (refused address, failure, too big, not a sound). Redirects are not followed.</summary>
        public static Task<byte[]> Fetch(string url)
        {
            var source = new TaskCompletionSource<byte[]>(); if (Refuse(url) != null) { source.SetResult(null); return source.Task; }
            var request = UnityWebRequest.Get(url); request.timeout = 20; request.redirectLimit = 0; request.downloadHandler = new DownloadHandlerBuffer();
            request.SendWebRequest().completed += _ =>
            {
                try { var ok = request.result == UnityWebRequest.Result.Success && request.downloadedBytes <= MaxBytes; var data = ok ? request.downloadHandler.data : null; source.TrySetResult(data != null && Sniff(data) != null ? data : null); }
                finally { request.Dispose(); }
            };
            return source.Task;
        }
    }
}
