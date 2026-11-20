using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    public sealed class DownloadResult { public bool Ok; public string Path = "", Message = "", Sha256 = ""; }

    /// <summary>
    /// Brings the new version to this computer: downloads the installer the platform points to (only a plain https link, or http on this computer for tests; only an installer file; never more than <see cref="MaxBytes"/>), checks its SHA-256
    /// against the one the platform published (when it published one) and only then lets the person open it. Nothing is ever run without the person pressing the button, and a file whose checksum does not match is deleted.
    /// </summary>
    public static class UpdateDownloader
    {
        public const long MaxBytes = 500L * 1024 * 1024;
        public static readonly string[] Extensions = { ".exe", ".msi", ".zip" };

        /// <summary>Whether a link may be downloaded: https, or http to this very computer; the file must be an installer or a zip.</summary>
        public static bool IsAllowed(string url, out string reason)
        {
            reason = ""; if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { reason = "O endereço da atualização não é válido."; return false; }
            var local = uri.IsLoopback; if (uri.Scheme != "https" && !(uri.Scheme == "http" && local)) { reason = "A atualização precisa vir por uma conexão segura (https)."; return false; }
            var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(); if (Array.IndexOf(Extensions, ext) < 0) { reason = "O endereço não aponta para um instalador (.exe, .msi ou .zip)."; return false; }
            return true;
        }

        public static string FileNameOf(string url) { var name = Path.GetFileName(new Uri(url).AbsolutePath); foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name.Length == 0 ? "LifeVerse-update.exe" : name; }

        public static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }

        public static bool SameHash(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim().ToLowerInvariant(), b.Trim().ToLowerInvariant(), StringComparison.Ordinal);

        public static async Task<DownloadResult> DownloadAsync(string url, string expectedSha256, string folder, Action<float> progress = null, CancellationToken token = default)
        {
            if (!IsAllowed(url, out var reason)) return new DownloadResult { Message = reason };
            var target = Path.Combine(folder, FileNameOf(url)); var part = target + ".part";
            try
            {
                Directory.CreateDirectory(folder); if (File.Exists(part)) File.Delete(part);
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) }; using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode) return new DownloadResult { Message = "O servidor não entregou o arquivo (" + (int)response.StatusCode + ")." };
                var total = response.Content.Headers.ContentLength ?? -1; if (total > MaxBytes) return new DownloadResult { Message = "O arquivo é grande demais." };
                long done = 0; using (var source = await response.Content.ReadAsStreamAsync()) using (var file = File.Create(part))
                {
                    var buffer = new byte[81920]; int read; while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0) { done += read; if (done > MaxBytes) { file.Close(); File.Delete(part); return new DownloadResult { Message = "O arquivo é grande demais." }; } file.Write(buffer, 0, read); progress?.Invoke(total > 0 ? (float)done / total : 0f); }
                }
                var actual = Hash(part);
                if (!string.IsNullOrWhiteSpace(expectedSha256) && !SameHash(actual, expectedSha256)) { File.Delete(part); return new DownloadResult { Message = "O arquivo baixado não confere com a assinatura publicada (SHA-256). Ele foi apagado." }; }
                if (File.Exists(target)) File.Delete(target); File.Move(part, target);
                return new DownloadResult { Ok = true, Path = target, Sha256 = actual, Message = string.IsNullOrWhiteSpace(expectedSha256) ? "Baixado. A plataforma não publicou uma assinatura para conferir o arquivo." : "Baixado e conferido (SHA-256)." };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is TaskCanceledException || exception is UnauthorizedAccessException) { try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } return new DownloadResult { Message = "Não foi possível baixar: " + exception.Message }; }
        }

        /// <summary>Opens the downloaded file: an installer runs (Windows asks for permission itself), a zip is shown in its folder. Returns whether something was opened.</summary>
        public static bool Open(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".zip") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                return true;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException) { return false; }
        }

        public static string DefaultFolder => Path.Combine(Application.persistentDataPath, "updates");
    }
}
