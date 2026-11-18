namespace LifeVerse.Client.Scripts;

/// <summary>
/// Makes a single GLB from a COLLADA (.dae) file chosen on the desktop and the textures it uses, exactly like <see cref="GltfUploadPackager"/> does for glTF.
/// The textures are looked for only inside the folder of the .dae (never anywhere else on the computer, never on the internet), and TGA and BMP
/// pictures, which the game does not take, are turned into PNG on the way.
/// </summary>
public static class DaeUploadPackager
{
    private const long MaxTextureBytes = 20L * 1024 * 1024;

    public static bool TryPack(string daePath, byte[] source, out byte[] glb, out string? error, out IReadOnlyList<string> warnings)
    {
        string folder;
        try { folder = Path.GetFullPath(Path.GetDirectoryName(daePath) ?? "."); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { glb = []; warnings = []; error = "O caminho do arquivo .dae não é válido."; return false; }
        byte[]? Read(string reference) => ReadTexture(folder, reference);
        return ColladaConverter.TryConvert(source, Read, out glb, out error, out warnings);
    }

    private static byte[]? ReadTexture(string folder, string reference)
    {
        var cleaned = reference.Trim();
        if (cleaned.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned["file:".Length..].TrimStart('/');
        if (cleaned.Contains("://", StringComparison.Ordinal) || cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;      // nothing is fetched from the internet
        cleaned = Uri.UnescapeDataString(cleaned).Replace('\\', '/');

        var rootPrefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        string? found = null;
        if (!Path.IsPathRooted(cleaned))
        {
            var direct = Path.GetFullPath(Path.Combine(folder, cleaned.Replace('/', Path.DirectorySeparatorChar)));
            if (direct.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(direct)) found = direct;
        }
        if (found is null)
        {
            // files are often moved after the export: look for the same name in the folder and the ones inside it
            var name = Path.GetFileName(cleaned);
            if (name.Length == 0) return null;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
            found = Directory.EnumerateFiles(folder, name, options).FirstOrDefault();
        }
        if (found is null || new FileInfo(found).Length > MaxTextureBytes) return null;

        var bytes = File.ReadAllBytes(found);
        var extension = Path.GetExtension(found).ToLowerInvariant();
        if (extension is ".tga" or ".bmp")
        {
            var image = new Godot.Image();
            var result = extension == ".tga" ? image.LoadTgaFromBuffer(bytes) : image.LoadBmpFromBuffer(bytes);
            return result == Godot.Error.Ok ? image.SavePngToBuffer() : null;
        }
        return bytes;
    }
}
