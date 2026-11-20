using LifeVerse.Shared.Updates;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Asks the platform once, when the game opens, whether there is a newer client (GET /api/v1/client/release?platform=windows, the answer the Godot client reads too) and compares it with this build's version using the shared
    /// <see cref="UpdateRules"/>. It never blocks by itself: the login screen shows the notice, and refuses to sign in only when the version is below the minimum the platform sets. No answer (offline, an older API)
    /// is "unknown" and changes nothing.
    /// </summary>
    public sealed class UpdateCheck
    {
        public const string Route = "/api/v1/client/release?platform=windows";
        public UpdateStatus Status { get; private set; } = UpdateStatus.Unknown;
        public ReleaseInfo Release { get; private set; }
        /// <summary>The SHA-256 of the installer, when the platform published one (an optional "sha256" of the same answer).</summary>
        public string Sha256 { get; private set; } = "";
        public string Current { get; }

        public UpdateCheck(string currentVersion) { Current = currentVersion; }

        /// <summary>Reads the answer of the platform (a status other than 200 or a broken body is "unknown").</summary>
        public void Apply(int status, string body)
        {
            Release = status == 200 ? ReleaseInfo.TryParse(body) : null; Sha256 = "";
            if (Release != null) { try { using var d = System.Text.Json.JsonDocument.Parse(body); if (d.RootElement.TryGetProperty("sha256", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.String) Sha256 = (h.GetString() ?? "").Trim().ToLowerInvariant(); if (Sha256.Length != 64) Sha256 = ""; } catch (System.Text.Json.JsonException) { } }
            Status = Release == null ? UpdateStatus.Unknown : UpdateRules.Evaluate(Current, Release.Latest, Release.Minimum);
        }

        public bool Required => Status == UpdateStatus.Required;
        /// <summary>Whether the link is something this game can download itself (an installer over a secure link).</summary>
        public bool CanDownload => Release != null && Release.HasSafeDownloadLink && UpdateDownloader.IsAllowed(Release.DownloadUrl, out _);

        public string Describe() => Status switch
        {
            UpdateStatus.Required => "Atualização obrigatória: instale a versão " + Release?.Latest + " para continuar.",
            UpdateStatus.Available => "Nova versão disponível: " + Release?.Latest + " (você tem " + Current + ").",
            _ => "",
        };

        /// <summary>Opens the download page, only when it is a plain http(s) address. Returns whether it was opened.</summary>
        public bool OpenDownload(System.Action<string> open = null)
        {
            if (Release == null || !Release.HasSafeDownloadLink) return false; (open ?? Application.OpenURL)(Release.DownloadUrl); return true;
        }
    }
}
