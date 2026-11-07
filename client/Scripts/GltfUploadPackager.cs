using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Makes a single safe upload artifact from a desktop glTF and the files it references.
/// The server deliberately never follows paths or URLs supplied by a user; instead the
/// client reads only sibling files selected with the glTF and writes one self-contained GLB.
/// </summary>
public static class GltfUploadPackager
{
    private const int MaxBytes = 100 * 1024 * 1024;

    public static bool TryPack(string gltfPath, byte[] source, out byte[] glb, out string? error)
    {
        glb = []; error = null;
        try
        {
            var offset = source.Length >= 3 && source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF ? 3 : 0;
            var root = JsonNode.Parse(Encoding.UTF8.GetString(source, offset, source.Length - offset)) as JsonObject;
            if (root is null || root["asset"]?["version"]?.GetValue<string>()?.StartsWith('2') != true) { error = "O arquivo não é um glTF 2.0 válido."; return false; }

            var folder = Path.GetFullPath(Path.GetDirectoryName(gltfPath) ?? ".");
            var binary = new List<byte>();
            var buffers = root["buffers"] as JsonArray;
            if (buffers is null || buffers.Count == 0) { error = "O glTF não possui buffers de geometria."; return false; }
            var starts = new List<int>();
            foreach (var buffer in buffers)
            {
                if (buffer is not JsonObject entry || entry["uri"]?.GetValue<string>() is not { } uri) { error = "O glTF possui um buffer sem arquivo incorporado."; return false; }
                var bytes = ReadResource(folder, uri, out var readError);
                if (bytes is null) { error = readError; return false; }
                Pad(binary); starts.Add(binary.Count); binary.AddRange(bytes);
            }

            var views = root["bufferViews"] as JsonArray ?? [];
            root["bufferViews"] = views;
            foreach (var view in views.OfType<JsonObject>())
            {
                var bufferIndex = view["buffer"]?.GetValue<int>() ?? -1;
                if (bufferIndex < 0 || bufferIndex >= starts.Count) { error = "O glTF possui uma referência de buffer inválida."; return false; }
                view["byteOffset"] = starts[bufferIndex] + (view["byteOffset"]?.GetValue<int>() ?? 0);
                view["buffer"] = 0;
            }

            if (root["images"] is JsonArray images)
                foreach (var image in images.OfType<JsonObject>())
                {
                    if (image["uri"]?.GetValue<string>() is not { } uri) continue;
                    var bytes = ReadResource(folder, uri, out var readError);
                    if (bytes is null) { error = readError; return false; }
                    Pad(binary); var imageOffset = binary.Count; binary.AddRange(bytes);
                    var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = imageOffset, ["byteLength"] = bytes.Length };
                    views.Add(view);
                    image.Remove("uri"); image["bufferView"] = views.Count - 1; image["mimeType"] = MimeFor(uri, bytes);
                }

            Pad(binary);
            root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = binary.Count });
            var json = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            var jsonLength = Align4(json.Length); var binLength = binary.Count;
            var total = 12L + 8 + jsonLength + (binLength == 0 ? 0 : 8L + binLength);
            if (total > MaxBytes) { error = "O modelo reunido com suas texturas ultrapassa o limite de 100 MB."; return false; }

            glb = new byte[(int)total];
            "glTF"u8.CopyTo(glb); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)jsonLength); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
            json.CopyTo(glb, 20); Array.Fill(glb, (byte)' ', 20 + json.Length, jsonLength - json.Length);
            if (binLength > 0) { var start = 20 + jsonLength; BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(start), (uint)binLength); BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(start + 4), 0x004E4942); binary.CopyTo(glb, start + 8); }
            return true;
        }
        catch (JsonException) { error = "O JSON do arquivo glTF é inválido."; return false; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { error = "Não foi possível ler os arquivos referenciados pelo glTF."; return false; }
    }

    private static byte[]? ReadResource(string folder, string uri, out string? error)
    {
        error = null;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return DecodeDataUri(uri, out error);
        if (Uri.TryCreate(uri, UriKind.Absolute, out _)) { error = "O glTF referencia uma URL externa, que não é permitida."; return null; }
        var relative = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(folder, relative));
        var rootPrefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) { error = "O glTF referencia um arquivo fora da pasta selecionada."; return null; }
        if (!File.Exists(path)) { error = $"Arquivo referenciado não encontrado: {Path.GetFileName(relative)}."; return null; }
        return File.ReadAllBytes(path);
    }

    private static byte[]? DecodeDataUri(string uri, out string? error)
    {
        error = null; var separator = uri.IndexOf(',');
        if (separator < 0 || !uri[..separator].Contains(";base64", StringComparison.OrdinalIgnoreCase)) { error = "Um recurso incorporado do glTF não está em base64 válido."; return null; }
        try { return Convert.FromBase64String(uri[(separator + 1)..]); }
        catch (FormatException) { error = "Um recurso incorporado do glTF não está em base64 válido."; return null; }
    }

    private static string MimeFor(string uri, byte[] bytes)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return uri[5..uri.IndexOf(';')];
        return Path.GetExtension(uri).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "image/png" };
    }

    private static void Pad(List<byte> bytes) { while (bytes.Count % 4 != 0) bytes.Add(0); }
    private static int Align4(int value) => (value + 3) & ~3;
}
