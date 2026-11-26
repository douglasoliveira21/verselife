using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Lembrar senha" of the login screen, as the Godot client's <c>SavedLogin</c>, but kept by Windows: the user and the password are sealed with the Data Protection API of the Windows account
    /// (<c>CryptProtectData</c>), so only the same person on the same computer can open them, and they live in their own file, never in the settings. Other systems do not offer it.
    /// A file that cannot be opened (another account, damaged) is deleted and the login asks again.
    /// </summary>
    public static class SavedLogin
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LifeVerse.SavedLogin.v1");
        public static bool Available => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "login.sealed");

        public static bool Save(string login, string password, string path = null)
        {
            if (!Available || string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password)) return false;
            try { var sealedBytes = Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { login, password }))); if (sealedBytes == null) return false; File.WriteAllBytes(path ?? DefaultPath, sealedBytes); return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { return false; }
        }

        public static (string Login, string Password)? Load(string path = null)
        {
            path ??= DefaultPath; if (!Available || !File.Exists(path)) return null;
            try
            {
                var open = Unprotect(File.ReadAllBytes(path)); if (open == null) { Clear(path); return null; }
                using var d = JsonDocument.Parse(open); var login = d.RootElement.GetProperty("login").GetString(); var password = d.RootElement.GetProperty("password").GetString();
                return string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password) ? ((string, string)?)null : (login, password);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is InvalidOperationException || exception is System.Collections.Generic.KeyNotFoundException) { Clear(path); return null; }
        }

        public static void Clear(string path = null) { try { var p = path ?? DefaultPath; if (File.Exists(p)) File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        // ---- the Data Protection API of Windows ----

        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        private const int UiForbidden = 0x1;

        public static byte[] Protect(byte[] data) => Call(data, true);
        public static byte[] Unprotect(byte[] data) => Call(data, false);

        private static byte[] Call(byte[] data, bool protect)
        {
            if (!Available || data == null) return null;
            var input = new Blob(); var entropy = new Blob(); var output = new Blob();
            try
            {
                input.Size = data.Length; input.Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)); Marshal.Copy(data, 0, input.Data, data.Length);
                entropy.Size = Entropy.Length; entropy.Data = Marshal.AllocHGlobal(Entropy.Length); Marshal.Copy(Entropy, 0, entropy.Data, Entropy.Length);
                var ok = protect ? CryptProtectData(ref input, "LifeVerse", ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output) : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok || output.Data == IntPtr.Zero) return null;
                var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result;
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException) { return null; }
            finally { if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data); if (entropy.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropy.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
        }
    }
}
