using System.Security.Cryptography;

namespace LifeVerse.Api.Modules.Assets;

public sealed class AssetStorageOptions
{
    public string Endpoint { get; init; } = "http://localhost:9000";
    public string Bucket { get; init; } = "lifeverse-assets";
    public string LocalRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "asset-data");
}

public sealed class AssetStorageService(AssetStorageOptions options, ILogger<AssetStorageService> logger)
{
    public async Task<string> PutAsync(Guid ownerId, string hash, byte[] bytes, CancellationToken ct)
    {
        var key = $"{ownerId:N}/{hash}"; var path = Path.Combine(options.LocalRoot, ownerId.ToString("N"), hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes, ct); logger.LogInformation("Asset {Hash} stored through object adapter", hash); return key;
    }
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = Path.Combine(options.LocalRoot, key.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(path)) return Task.FromResult<Stream?>(null); return Task.FromResult<Stream?>(File.OpenRead(path));
    }
}

public sealed record AssetInspection(bool Valid, string? Error, string MimeType, int? Width, int? Height, double? DurationSeconds);

public static class AssetValidator
{
    public static async Task<AssetInspection> InspectAsync(string fileName, string contentType, byte[] bytes, CancellationToken ct)
    {
        await Task.Yield(); var extension = Path.GetExtension(fileName).ToLowerInvariant(); if (bytes.Length == 0) return new(false, "Empty files are not accepted.", contentType, null, null, null);
        if (extension is ".glb" or ".gltf")
        {
            if (bytes.Length > 100 * 1024 * 1024) return new(false, "Mesh exceeds the 100 MB limit.", contentType, null, null, null);
            if (extension == ".glb" && (bytes.Length < 12 || bytes[0] != (byte)'g' || bytes[1] != (byte)'l' || bytes[2] != (byte)'T' || bytes[3] != (byte)'F')) return new(false, "Invalid GLB header.", contentType, null, null, null);
            if (extension == ".gltf" && !LooksLikeJson(bytes)) return new(false, "Invalid glTF JSON.", contentType, null, null, null);
            if (ContainsAscii(bytes, "script") || ContainsAscii(bytes, "javascript")) return new(false, "Embedded scripts are not permitted.", contentType, null, null, null);
            if (CountAscii(bytes, "primitives") > 10000) return new(false, "Mesh complexity exceeds the primitive limit.", contentType, null, null, null);
            return new(true, null, extension == ".glb" ? "model/gltf-binary" : "model/gltf+json", null, null, null);
        }
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            if (bytes.Length > 20 * 1024 * 1024) return new(false, "Image exceeds the 20 MB limit.", contentType, null, null, null);
            var dimensions = ImageDimensions(extension, bytes); if (dimensions is null) return new(false, "Invalid or unsupported image header.", contentType, null, null, null); if (dimensions.Value.Width > 8192 || dimensions.Value.Height > 8192) return new(false, "Image resolution exceeds 8192x8192.", contentType, dimensions.Value.Width, dimensions.Value.Height, null);
            return new(true, null, extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : extension == ".webp" ? "image/webp" : "image/png", dimensions.Value.Width, dimensions.Value.Height, null);
        }
        if (extension is ".ogg" or ".wav")
        {
            if (bytes.Length > 50 * 1024 * 1024) return new(false, "Audio exceeds the 50 MB limit.", contentType, null, null, null);
            if (extension == ".ogg" && !Has(bytes, "OggS")) return new(false, "Invalid OGG header.", contentType, null, null, null); if (extension == ".wav" && (!Has(bytes, "RIFF") || !Has(bytes, "WAVE"))) return new(false, "Invalid WAV header.", contentType, null, null, null);
            return new(true, null, extension == ".ogg" ? "audio/ogg" : "audio/wav", null, null, null);
        }
        return new(false, "Only GLB/glTF, PNG/JPEG/WebP, OGG and WAV are accepted.", contentType, null, null, null);
    }
    private static bool LooksLikeJson(byte[] bytes) { var text = System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 4096))); return text.TrimStart().StartsWith('{') && text.Contains("\"asset\"", StringComparison.Ordinal); }
    private static bool Has(byte[] bytes, string value) => bytes.Length >= value.Length && System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))).Contains(value, StringComparison.Ordinal);
    private static bool ContainsAscii(byte[] bytes, string value) => System.Text.Encoding.ASCII.GetString(bytes).Contains(value, StringComparison.OrdinalIgnoreCase);
    private static int CountAscii(byte[] bytes, string value) { var text = System.Text.Encoding.ASCII.GetString(bytes); var count = 0; for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++; return count; }
    private static (int Width, int Height)? ImageDimensions(string extension, byte[] bytes)
    {
        if (extension == ".png" && bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80) return ((int)Read32(bytes, 16), (int)Read32(bytes, 20));
        if (extension is ".jpg" or ".jpeg" && bytes.Length > 4 && bytes[0] == 0xff && bytes[1] == 0xd8)
        {
            for (var offset = 2; offset + 9 < bytes.Length;)
            {
                if (bytes[offset] != 0xff) { offset++; continue; } var marker = bytes[offset + 1]; offset += 2; if (marker is 0xd8 or 0xd9) continue; if (offset + 2 > bytes.Length) break; var length = (bytes[offset] << 8) | bytes[offset + 1]; if (length < 2 || offset + length > bytes.Length) break; if (marker is >= 0xc0 and <= 0xc3) return ((bytes[offset + 5] << 8) | bytes[offset + 6], (bytes[offset + 3] << 8) | bytes[offset + 4]); offset += length;
            }
        }
        if (extension == ".webp" && bytes.Length >= 30 && Has(bytes, "RIFF") && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" && System.Text.Encoding.ASCII.GetString(bytes, 12, 4) == "VP8X") return (1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16), 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16));
        return null;
    }
    private static uint Read32(byte[] bytes, int offset) => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
}
