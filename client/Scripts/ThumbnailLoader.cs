using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Real previews for inventory items. Images are downloaded and shrunk, 3D models are rendered once into a small
/// viewport, and everything is cached on disk by content hash, so each asset is only ever fetched once per computer.
/// Sounds, scripts and other kinds get a generated icon. Callers get the placeholder immediately and the preview later.
public partial class ThumbnailLoader : Node
{
    public const int Size = 128;
    private const long MaxImageBytes = 4 * 1024 * 1024;
    private const long MaxModelBytes = 12 * 1024 * 1024;
    private sealed record Request(Guid AssetId, string Key, string Mime, string Category, List<Action<Texture2D>> Waiting);
    private readonly Dictionary<string, Texture2D> _memory = [];
    private readonly Dictionary<string, Request> _pending = [];
    private readonly Queue<Request> _queue = new();
    private HttpRequest? _http;
    private Request? _current;

    public int PendingDownloads => _queue.Count + (_current is null ? 0 : 1);

    /// `done` runs now with a placeholder and, when a real preview exists, again once it is ready.
    public void Get(Guid assetId, string contentHash, string mime, string category, long byteSize, Action<Texture2D> done)
    {
        var key = string.IsNullOrEmpty(contentHash) ? assetId.ToString("N") : contentHash;
        if (_memory.TryGetValue(key, out var cached)) { done(cached); return; }
        var cachePath = $"user://thumbs128/{key}.png";
        if (Godot.FileAccess.FileExists(cachePath) && Image.LoadFromFile(cachePath) is { } stored && !stored.IsEmpty())
        {
            var texture = ImageTexture.CreateFromImage(stored); _memory[key] = texture; done(texture); return;
        }
        done(Placeholder(category));
        var wanted = mime.StartsWith("image/", StringComparison.Ordinal) ? byteSize <= MaxImageBytes : mime.StartsWith("model/", StringComparison.Ordinal) && byteSize <= MaxModelBytes;
        if (!wanted) return;
        if (_pending.TryGetValue(key, out var existing)) { existing.Waiting.Add(done); return; }
        var request = new Request(assetId, key, mime, category, [done]);
        _pending[key] = request; _queue.Enqueue(request);
        Next();
    }

    private void Next()
    {
        if (_current is not null || _queue.Count == 0) return;
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { _queue.Clear(); _pending.Clear(); return; }
        _current = _queue.Dequeue();
        if (_http is null) { _http = new HttpRequest { Timeout = 30, BodySizeLimit = 20 * 1024 * 1024 }; AddChild(_http); _http.RequestCompleted += OnDownloaded; }
        if (_http.Request($"{app.ApiBaseUrl}/api/v1/assets/{_current.AssetId}/download", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) Finish(null);
    }

    private async void OnDownloaded(long result, long code, string[] headers, byte[] body)
    {
        var request = _current;
        if (request is null) return;
        if (code is < 200 or >= 300) { Finish(null); return; }
        Image? image = request.Mime.StartsWith("image/", StringComparison.Ordinal) ? Decode(request.Mime, body) : await RenderModel(body);
        Finish(image);
    }

    private void Finish(Image? image)
    {
        var request = _current; _current = null;
        if (request is not null)
        {
            _pending.Remove(request.Key);
            if (image is not null)
            {
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://thumbs128"));
                image.SavePng($"user://thumbs128/{request.Key}.png");
                var texture = ImageTexture.CreateFromImage(image); _memory[request.Key] = texture;
                foreach (var callback in request.Waiting) callback(texture);
            }
        }
        Next();
    }

    private static Image? Decode(string mime, byte[] bytes)
    {
        var image = new Image();
        var error = mime switch { "image/png" => image.LoadPngFromBuffer(bytes), "image/jpeg" => image.LoadJpgFromBuffer(bytes), "image/webp" => image.LoadWebpFromBuffer(bytes), _ => Error.FileUnrecognized };
        return error == Error.Ok && !image.IsEmpty() ? Square(image) : null;
    }

    /// Centre-crops to a square and shrinks to the thumbnail size.
    private static Image Square(Image image)
    {
        image.Convert(Image.Format.Rgba8);
        var side = Math.Min(image.GetWidth(), image.GetHeight());
        var square = image.GetRegion(new Rect2I((image.GetWidth() - side) / 2, (image.GetHeight() - side) / 2, side, side));
        square.Resize(Size, Size, Image.Interpolation.Lanczos);
        return square;
    }

    public Task<Image?> RenderForTesting(byte[] bytes) => RenderModel(bytes);

    /// Draws the model once with a camera and a light in a throwaway viewport.
    private async Task<Image?> RenderModel(byte[] bytes)
    {
        var (model, size) = ModelCache.Parse(bytes);
        if (model is null) return null;
        var viewport = new SubViewport { Size = new Vector2I(Size * 2, Size * 2), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var camera = new Camera3D { Current = true, Fov = 35 };
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -35, 0), LightEnergy = 1.2f });
        var environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.85f, .88f, .95f), AmbientLightEnergy = .7f };
        viewport.AddChild(new WorldEnvironment { Environment = environment });
        viewport.AddChild(model); viewport.AddChild(camera);
        model.RotationDegrees = new Vector3(-12, 35, 0);
        AddChild(viewport);
        var distance = Math.Max(size.Length(), .5f) / (2f * Mathf.Tan(Mathf.DegToRad(camera.Fov) / 2f)) + .6f;
        camera.Position = new Vector3(0, .1f, distance * 1.25f);
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        if (image is null || image.IsEmpty()) return null;
        image.Convert(Image.Format.Rgba8);
        image.Resize(Size, Size, Image.Interpolation.Lanczos);
        return image;
    }

    /// Generated icon for a category: a coloured tile with a simple shape (cube, checker, bars, disc).
    public static Texture2D Placeholder(string category)
    {
        var hue = (Math.Abs(category.GetHashCode()) % 360) / 360f;
        var tile = Color.FromHsv(hue, .45f, .55f); var glyph = Color.FromHsv(hue, .15f, .95f);
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        for (var y = 4; y < Size - 4; y++) for (var x = 4; x < Size - 4; x++) image.SetPixel(x, y, tile);
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var u = x * 48f / Size; var w = y * 48f / Size; var cx = u - 24f; var cy = w - 24f;
            var inside = category switch
            {
                "models" or "objects" => Math.Abs(cx) + Math.Abs(cy) < 14,
                "textures" => u is > 10 and < 38 && w is > 10 and < 38 && (((int)(u / 7) + (int)(w / 7)) % 2 == 0),
                "sounds" or "animations" => u is > 9 and < 39 && Math.Abs(cy) < 4 + 11 * Math.Abs(Mathf.Sin(u * .55f)),
                _ => cx * cx + cy * cy < 120
            };
            if (inside) image.SetPixel(x, y, glyph);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
