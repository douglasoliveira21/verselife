using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Downloads the images that materials use (PNG, JPEG or WebP) from the island's asset endpoint, once each, and keeps them
/// as textures with mipmaps. Oversized images are shrunk so one careless upload cannot use all the video memory.
/// Callers get null when an image cannot be fetched or decoded, and then keep the plain colour.
public partial class TextureCache : Node
{
    public const int MaxSize = 2048;
    private readonly Dictionary<Guid, Texture2D?> _ready = [];
    private readonly Dictionary<Guid, List<Action<Texture2D?>>> _waiting = [];

    public int CachedTextures => _ready.Count;

    public void Request(Guid assetId, Action<Texture2D?> done)
    {
        if (_ready.TryGetValue(assetId, out var texture)) { done(texture); return; }
        if (_waiting.TryGetValue(assetId, out var list)) { list.Add(done); return; }
        _waiting[assetId] = [done];
        Fetch(assetId);
    }

    /// Supplies an image's bytes directly (tests, and the importer which already holds the file).
    public bool Preload(Guid assetId, byte[] bytes)
    {
        var texture = Decode(bytes);
        _ready[assetId] = texture;
        Resolve(assetId, texture);
        return texture is not null;
    }

    private void Fetch(Guid assetId)
    {
        var network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (network is null || session is null || app is null || !session.IsAuthenticated || network.CurrentIslandId == Guid.Empty) { Finish(assetId, null); return; }
        var loader = GetParent()?.GetNodeOrNull<AssetLoadingService>("AssetLoadingService");
        if (loader?.TryReadCached(assetId) is { } onDisk) { Finish(assetId, Decode(onDisk)); return; } // already on this computer and still right
        var http = new HttpRequest { Timeout = 30, BodySizeLimit = 32 * 1024 * 1024 };
        AddChild(http);
        http.RequestCompleted += (_, code, _, body) =>
        {
            http.QueueFree();
            if (code is >= 200 and < 300) loader?.StoreIfKnown(assetId, body);
            Finish(assetId, code is >= 200 and < 300 ? Decode(body) : null);
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{network.CurrentIslandId}/assets/{assetId}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); Finish(assetId, null); }
    }

    private void Finish(Guid assetId, Texture2D? texture) { _ready[assetId] = texture; Resolve(assetId, texture); }

    private void Resolve(Guid assetId, Texture2D? texture)
    {
        if (!_waiting.Remove(assetId, out var callbacks)) return;
        foreach (var callback in callbacks) callback(texture);
    }

    /// Reads the image by what the bytes are, not by what a file name claims.
    public static Texture2D? Decode(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        var image = new Image();
        Error error;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) error = image.LoadPngFromBuffer(bytes);
        else if (bytes[0] == 0xFF && bytes[1] == 0xD8) error = image.LoadJpgFromBuffer(bytes);
        else if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') error = image.LoadWebpFromBuffer(bytes);
        else return null;
        if (error != Error.Ok || image.IsEmpty()) return null;
        if (image.GetWidth() > MaxSize || image.GetHeight() > MaxSize)
        {
            var scale = MaxSize / (float)Math.Max(image.GetWidth(), image.GetHeight());
            image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Lanczos);
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
