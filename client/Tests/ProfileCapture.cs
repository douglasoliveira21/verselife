using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the profile over the world (mine and somebody else's) and saves screenshots to LIFEVERSE_CAPTURE_PATH.</summary>
public partial class ProfileCapture : Node
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111"), Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid G1 = Guid.NewGuid(), G2 = Guid.NewGuid(), G3 = Guid.NewGuid();
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private static string Profile(Guid id, bool self) => JsonSerializer.Serialize(new
    {
        id, username = self ? "lunavalente.vl" : "caio.rocha", displayName = self ? "Luna Valente" : "Caio Rocha", isSelf = self, privateProfile = false, joinedAt = "2024-03-15T10:00:00Z", characterAgeYears = 2,
        accountType = self ? "donor" : "standard", donorSince = self ? "2025-06-01T00:00:00Z" : null, presence = "online", bio = "Exploradora, criadora e amante de mundos virtuais.\nBuild · Moda · Eventos · Social",
        firstLifeBio = "", interests = "Construção, Moda, Eventos, Fotografia, Exploração, Social", hasPhoto = true, gallery = new[] { new { id = G1, main = true }, new { id = G2, main = false }, new { id = G3, main = false } }, hasCover = true, favorites = new[] { new { kind = "island", label = "Ilha", id = Guid.NewGuid(), title = "Ilha Esmeralda", subtitle = "Social", islandId = Guid.NewGuid() }, new { kind = "event", label = "Evento", id = Guid.NewGuid(), title = "Festival de Música", subtitle = "Ilha Aurora", islandId = Guid.NewGuid() } }, achievements = new[] { new { key = "first_parcel", title = "Primeiro terreno", description = "Tornou-se dono de um terreno.", icon = "home", unlockedAt = "2025-01-10T00:00:00Z" }, new { key = "veteran_1y", title = "Um ano de LifeVerse", description = "Completou um ano desde a criação do personagem.", icon = "calendar", unlockedAt = "2025-03-15T00:00:00Z" } }, stats = new { joinedAt = "2024-03-15T10:00:00Z", daysMember = 610, groups = 5, parcels = 3, creations = 12, events = 8, pictures = 3 }, birthday = "06/06", married = false, marriage = (object?)null,
        parcels = new[] { new { id = Guid.NewGuid(), islandId = Guid.NewGuid(), islandName = "Ilha Esmeralda", name = "Luna's Home", category = "residencial", area = 625, code = "L-03", shown = true }, new { id = Guid.NewGuid(), islandId = Guid.NewGuid(), islandName = "Ilha Esmeralda", name = "Luna Store", category = "comercial", area = 400, code = "L-07", shown = true } },
        groups = new[] { new { id = Guid.NewGuid(), name = "VerseLife Brasil", tag = "VLB", role = "owner", color = "#12F2E0", shown = true }, new { id = Guid.NewGuid(), name = "Builders Hub", tag = "BLD", role = "member", color = "#F4A340", shown = true } },
        settings = self ? new { hideGroups = false, hideMarriage = false, privateProfile = false, hideParcels = false } : null
    });

    private static string FeedJson(bool self)
    {
        var now = new DateTimeOffset(2026, 11, 12, 20, 30, 0, TimeSpan.Zero);
        object Post(string text, int minutes, bool friends, bool image) => new { kind = "post", key = Guid.NewGuid(), at = now.AddMinutes(-minutes), title = "", text, icon = (string?)null, postId = Guid.NewGuid(), hasImage = image, visibility = friends ? "friends" : "public" };
        object[] items = [Post("Terminei a decoração da casa nova na Ilha Esmeralda. Quem quiser visitar, a porta está aberta!", 30, false, true), Post("Evento de música ao vivo no sábado. Vou transmitir do palco.", 240, true, false),
            new { kind = "achievement", key = "founder", at = now.AddDays(-9), title = "Fundador(a)", text = "Criou um grupo.", icon = "target", postId = (Guid?)null, hasImage = false, visibility = (string?)null },
            Post("Primeira publicação por aqui. Olá, mundo!", 60 * 24 * 12, false, false)];
        return JsonSerializer.Serialize(new { items, more = true, hidden = (string?)null, canPost = self });
    }
public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(40);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var window = (ProfileWindow)world.FindChild("ProfileWindow", true, false); var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            window.MyIdOverride = () => Me;
            Image Pic(Color a, Color b, int w, int h) { var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8); for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) img.SetPixel(x, y, a.Lerp(b, (x / (float)w + y / (float)h) / 2f)); return img; }
            var shots = new Dictionary<string, byte[]> { [G1.ToString()] = Pic(new Color("#12F2E0"), new Color("#1B262E"), 400, 700).SavePngToBuffer(), [G2.ToString()] = Pic(new Color("#F4A340"), new Color("#23323C"), 400, 700).SavePngToBuffer(), [G3.ToString()] = Pic(new Color("#39D98A"), new Color("#162129"), 400, 700).SavePngToBuffer(), ["cover"] = Pic(new Color("#1B262E"), new Color("#12F2E0"), 900, 120).SavePngToBuffer() };
            shots["image"] = Pic(new Color("#F4A340"), new Color("#12F2E0"), 800, 420).SavePngToBuffer();
            window.ImageFetcher = path => Task.FromResult<byte[]?>(path.EndsWith("/cover") ? shots["cover"] : shots.TryGetValue(path[(path.LastIndexOf('/') + 1)..], out var b) ? b : null);
            window.UseTransportForTesting((method, route, body) => Task.FromResult<(long, string)>(route.Contains("/profile/feed") ? (200, FeedJson(route.Contains(Me.ToString()))) : route.EndsWith("/profile/full") ? (200, Profile(route.Contains(Me.ToString()) ? Me : Other, route.Contains(Me.ToString()))) : route.StartsWith("/api/v1/social/people") ? (200, "[]") : route == "/api/v1/marriage" ? (200, "{\"current\":null,\"incoming\":[],\"outgoing\":[]}") : (404, "{}")));
            await window.OpenSelfAsync(); await Frames(10); GD.Print($"dbg view={GetViewport().GetVisibleRect().Size} win={window.WindowSizeForTesting}");
            var image = Image.CreateEmpty(400, 700, false, Image.Format.Rgba8);
            for (var y = 0; y < 700; y++) for (var x = 0; x < 400; x++) image.SetPixel(x, y, new Color(.2f + y / 1400f, .15f + x / 1200f, .45f, 1));
            await Frames(15); Save(path, "propio");
            window.SelectTabForTesting(1); await Frames(8); Save(path, "feed");
            foreach (var (tab, name) in new[] { (3, "terrenos"), (4, "favoritos"), (5, "conquistas"), (6, "estatisticas") }) { window.SelectTabForTesting(tab); await Frames(5); Save(path, name); }
            window.SelectTabForTesting(0); await Frames(3);
            window.ResizeForTesting(new Vector2(900, 600)); await Frames(5); Save(path, "medio");
            window.ResizeForTesting(new Vector2(700, 500)); await Frames(5); Save(path, "estreito");
            window.ResizeForTesting(new Vector2(1350, 760)); await window.OpenByIdAsync(Other); await Frames(10); Save(path, "outro"); window.SelectTabForTesting(1); await Frames(8); Save(path, "feed-outro");
            GD.Print("event=profile_capture result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=profile_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private void Save(string path, string name) { if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print($"event=profile_capture name={name}"); }
}
