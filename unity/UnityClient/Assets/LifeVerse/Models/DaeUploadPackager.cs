#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace LifeVerse.Unity.Models;

/// <summary>
/// Makes a single GLB from a COLLADA (.dae) file of the person's computer and the pictures it uses, as <see cref="GltfUploadPackager"/> does for glTF.
/// The pictures are looked for only inside the folder of the .dae (never anywhere else on the computer, never on the internet): first where the file says, and, since models are
/// often moved after the export or keep the path of another computer, then by name in that folder and the ones inside it. TGA and BMP pictures, which the game does not take,
/// become PNG on the way.
/// </summary>
public static class DaeUploadPackager
{
    private const long MaxTextureBytes = 20L * 1024 * 1024;
    /// <summary>How deep below the folder of the model a picture is looked for, and how many files and folders at most (a model saved straight into "Downloads" must not hang the game).</summary>
    public const int SearchDepth = 3, SearchLimit = 20000;

    public static bool TryPack(string daePath, byte[] source, out byte[] glb, out string? error, out IReadOnlyList<string> warnings)
    {
        string folder;
        try { folder = Path.GetFullPath(Path.GetDirectoryName(daePath) is { Length: > 0 } directory ? directory : "."); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { glb = Array.Empty<byte>(); warnings = Array.Empty<string>(); error = "O caminho do arquivo .dae não é válido."; return false; }
        return ColladaConverter.TryConvert(source, reference => ReadTexture(folder, reference), out glb, out error, out warnings);
    }

    /// <summary>The bytes of the picture a model refers to (a PNG for a TGA or a BMP), or null when there is none inside the folder.</summary>
    public static byte[]? ReadTexture(string folder, string reference)
    {
        try
        {
            var found = FindTexture(folder, reference);
            if (found is null || new FileInfo(found).Length > MaxTextureBytes) return null;
            var bytes = File.ReadAllBytes(found);
            var extension = Path.GetExtension(found).ToLowerInvariant();
            // a picture that cannot be turned into PNG goes on as it is: the converter then says its format is not accepted
            return extension is ".tga" or ".bmp" && PictureConverter.TryToPng(bytes, extension, out var png) ? png : bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return null; }
    }

    /// <summary>The file inside <paramref name="folder"/> that a reference of the model means, or null.</summary>
    public static string? FindTexture(string folder, string reference)
    {
        var cleaned = (reference ?? string.Empty).Trim();
        if (cleaned.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring("file:".Length).TrimStart('/');
        if (cleaned.Contains("://") || cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;      // nothing is fetched from the internet
        cleaned = Uri.UnescapeDataString(cleaned).Replace('\\', '/');
        if (cleaned.Length == 0 || cleaned.IndexOf('\0') >= 0) return null;

        folder = Path.GetFullPath(folder);
        var root = folder.EndsWith(Path.DirectorySeparatorChar.ToString()) ? folder : folder + Path.DirectorySeparatorChar;
        var absolute = cleaned[0] == '/' || cleaned.Length > 1 && cleaned[1] == ':';   // a path of whoever exported the file: only its name is used
        if (!absolute)
        {
            var direct = Path.GetFullPath(Path.Combine(folder, cleaned.Replace('/', Path.DirectorySeparatorChar)));
            if (direct.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(direct)) return direct;
        }
        var name = cleaned.Substring(cleaned.LastIndexOf('/') + 1);
        return name.Length == 0 || name == "." || name == ".." ? null : SearchByName(folder, name);
    }

    // level by level, so that the nearest picture of that name wins; folders that are links are not followed (they could lead out of the folder)
    private static string? SearchByName(string folder, string name)
    {
        var level = new List<string> { folder }; var seen = 0;
        for (var depth = 0; depth <= SearchDepth && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var directory in level)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        if (++seen > SearchLimit) return null;
                        if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) return file;
                    }
                    if (depth == SearchDepth) continue;
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        if (++seen > SearchLimit) return null;
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) next.Add(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }   // a folder that cannot be read is passed over
            }
            next.Sort(StringComparer.OrdinalIgnoreCase); level = next;
        }
        return null;
    }
}
