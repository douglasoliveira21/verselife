using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Media on a surface: a picture or a video from a public https address, shown where the base picture would be, with an optional glow. Each viewer decides whether to play it
/// (a setting of their own computer), only the nearest few videos play at once, and nothing is fetched from an address the server did not accept. Video is Ogg Theora; its sound is
/// muted (the world has no way to place it in space yet). The pictures and videos are kept on disk once fetched, so they are not downloaded again.
/// </summary>
public partial class BuildObjectView
{
    /// <summary>What this computer is willing to play: the person can switch media off, and the limits keep a crowded island from overloading anybody.</summary>
    public static class MediaSettings
    {
        public const int MaxPlayingVideos = 2, MaxImageBytes = 8 * 1024 * 1024, MaxVideoBytes = 25 * 1024 * 1024;
        public const float PlayDistance = 40f;
        private const string ConfigPath = "user://media.cfg";
        private static bool? _enabled;
        public static bool Enabled
        {
            get { if (_enabled is { } known) return known; var file = new ConfigFile(); _enabled = file.Load(ConfigPath) != Error.Ok || file.GetValue("media", "enabled", true).AsBool(); return _enabled.Value; }
            set { _enabled = value; var file = new ConfigFile(); file.Load(ConfigPath); file.SetValue("media", "enabled", value); file.Save(ConfigPath); }
        }
        public static int PlayingVideos { get; internal set; }
        /// <summary>Tests answer for the download: the bytes at an address, or null.</summary>
        public static Func<string, byte[]?>? FetchOverride { get; set; }
    }

    private static readonly Dictionary<string, Texture2D> MediaImages = [];

    /// <summary>One piece of media: it fetches the picture or the video, fills the textures of the surfaces that show it, and plays or pauses the video as the settings and the distance allow.</summary>
    public partial class MediaSurface : Node
    {
        private readonly List<ShaderMaterial> _targets = [];
        private HttpRequest? _http;
        private VideoStreamPlayer? _player;
        private Texture2D? _texture;
        private bool _glow, _playing, _loop, _counted, _failed;
        private string _type = "image", _url = string.Empty;
        private double _clock;

        public Texture2D? Texture => _texture;
        public bool Failed => _failed;
        public bool VideoPlaying => _player is { } p && IsInstanceValid(p) && p.IsPlaying() && !p.Paused;
        public string UrlForTesting => _url;
        public string Kind => _type;

        public void Setup(JsonObject media, ShaderMaterial material)
        {
            _type = media["type"] is JsonValue t && t.TryGetValue<string>(out var type) ? type : "image"; _url = media["url"] is JsonValue u && u.TryGetValue<string>(out var url) ? url : string.Empty;
            _glow = media["glow"] is JsonValue g && g.TryGetValue<bool>(out var glow) && glow; _playing = media["playing"] is JsonValue p && p.TryGetValue<bool>(out var playing) && playing; _loop = media["loop"] is JsonValue l && l.TryGetValue<bool>(out var loop) && loop;
            _targets.Add(material);
            if (!MediaSettings.Enabled) return; // this computer has media off: the surface stays plain and nothing is fetched
            Start();
        }

        private void Start()
        {
            if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { _failed = true; return; }
            if (_type == "image" && MediaImages.TryGetValue(_url, out var known)) { Arrived(known); return; }
            var path = CachePath(); if (_type == "video" && Godot.FileAccess.FileExists(path)) { OpenVideo(path); return; }
            if (MediaSettings.FetchOverride is { } fetch) { Got(fetch(_url)); return; }
            _http = new HttpRequest { Timeout = 30, BodySizeLimit = _type == "video" ? MediaSettings.MaxVideoBytes : MediaSettings.MaxImageBytes, DownloadFile = string.Empty }; AddChild(_http);
            _http.RequestCompleted += (_, code, _, body) => { _http.QueueFree(); _http = null; Got(code is >= 200 and < 300 ? body : null); };
            if (_http.Request(_url, ["Accept: " + (_type == "video" ? "video/ogg" : "image/*")]) != Error.Ok) { _http.QueueFree(); _http = null; _failed = true; }
        }

        private string CachePath() => "user://media/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_url)))[..24].ToLowerInvariant() + (_type == "video" ? ".ogv" : ".img");

        private void Got(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length > (_type == "video" ? MediaSettings.MaxVideoBytes : MediaSettings.MaxImageBytes)) { _failed = true; return; }
            if (_type == "image")
            {
                var image = new Image(); var error = LooksLike(bytes, "png") ? image.LoadPngFromBuffer(bytes) : LooksLike(bytes, "jpg") ? image.LoadJpgFromBuffer(bytes) : LooksLike(bytes, "webp") ? image.LoadWebpFromBuffer(bytes) : Error.FileUnrecognized;
                if (error != Error.Ok || image.GetWidth() < 1) { _failed = true; return; }
                image.GenerateMipmaps(); var texture = ImageTexture.CreateFromImage(image); MediaImages[_url] = texture; Arrived(texture); return;
            }
            if (!(bytes.Length > 4 && bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S')) { _failed = true; return; } // an Ogg file, nothing else
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://media"));
            using (var file = Godot.FileAccess.Open(CachePath(), Godot.FileAccess.ModeFlags.Write)) { if (file is null) { _failed = true; return; } file.StoreBuffer(bytes); }
            OpenVideo(CachePath());
        }

        private static bool LooksLike(byte[] b, string kind) => kind switch
        {
            "png" => b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G',
            "jpg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8,
            _ => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P'
        };

        private void OpenVideo(string path)
        {
            try
            {
                _player = new VideoStreamPlayer { Name = "Video", Stream = new VideoStreamTheora { File = path }, Loop = _loop, VolumeDb = -80, Autoplay = false }; AddChild(_player);
                _player.Finished += () => { if (_counted) { MediaSettings.PlayingVideos--; _counted = false; } };
                Arrived(_player.GetVideoTexture());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { _failed = true; GD.PushWarning($"event=media_video_failed error={exception.GetType().Name}"); }
        }

        private void Arrived(Texture2D texture)
        {
            _texture = texture;
            foreach (var material in _targets) Bind(material);
        }

        public void AddTarget(ShaderMaterial material) { _targets.Add(material); if (_texture is not null) Bind(material); }

        private void Bind(ShaderMaterial material)
        {
            if (!IsInstanceValid(material) || _texture is null) return;
            material.SetShaderParameter("albedo_tex", _texture); material.SetShaderParameter("has_albedo", true);
            if (_glow) { material.SetShaderParameter("emissive_tex", _texture); material.SetShaderParameter("has_emissive", true); }
        }

        public override void _Process(double delta)
        {
            if (_player is null || !IsInstanceValid(_player)) return;
            _clock += delta; if (_clock < .5) return; _clock = 0;
            var view = GetParent() as BuildObjectView; var camera = GetViewport().GetCamera3D();
            var near = view is not null && camera is not null && view.GlobalPosition.DistanceTo(camera.GlobalPosition) <= MediaSettings.PlayDistance;
            var want = _playing && MediaSettings.Enabled && near && (_counted || MediaSettings.PlayingVideos < MediaSettings.MaxPlayingVideos);
            if (want && !_counted) { _counted = true; MediaSettings.PlayingVideos++; }
            if (want) { if (!_player.IsPlaying()) _player.Play(); _player.Paused = false; }
            else { if (_counted) { _counted = false; MediaSettings.PlayingVideos--; } if (_player.IsPlaying()) _player.Paused = true; }
        }

        public override void _ExitTree() { if (_counted) { MediaSettings.PlayingVideos--; _counted = false; } }
    }

    /// <summary>Starts the media of every surface of this object that has some. The materials are the object's own copies (a surface with media is never shared with another object).</summary>
    private void AttachMedia(MeshInstance3D visual, JsonObject material, Func<int, JsonObject?> entryOf, int surfaces)
    {
        var sources = new Dictionary<string, MediaSurface>();
        for (var i = 0; i < surfaces; i++)
        {
            if (visual.GetSurfaceOverrideMaterial(i) is not ShaderMaterial shader) continue;
            if (MaterialBuilder.MediaOf(material, entryOf(i)) is not { } media) continue;
            var key = media.ToJsonString();
            if (!sources.TryGetValue(key, out var surface)) { surface = new MediaSurface { Name = "Media" + sources.Count }; AddChild(surface); surface.Setup(media, shader); sources[key] = surface; }
            else surface.AddTarget(shader);
        }
    }

    /// <summary>For tests: the media surfaces of this object.</summary>
    public IReadOnlyList<MediaSurface> MediaSurfaces => [.. GetChildren().OfType<MediaSurface>()];
}
