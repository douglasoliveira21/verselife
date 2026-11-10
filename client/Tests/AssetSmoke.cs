using System.Security.Cryptography;
using System.Text;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Tests;

/// <summary>The asset loading: the nearest assets first with real byte progress, a disk cache checked by hash, damaged or interrupted downloads, and the hand-over to the texture cache.</summary>
public partial class AssetSmoke : Node
{
    private readonly Dictionary<Guid, byte[]> _served = [];
    private readonly Dictionary<Guid, int> _interrupt = [];
    private readonly List<string> _requests = [];
    private readonly List<(long Done, long Total)> _progress = [];
    private string _manifest = "{}";
    private long _manifestStatus = 200;

    private static byte[] Png(int shade)
    {
        var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8); image.Fill(new Color(shade / 255f, .2f, .6f)); return image.SavePngToBuffer();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        var folder = $"user://asset_smoke_{Guid.NewGuid():N}";
        try
        {
            var network = GetNode<Autoloads.NetworkManager>("/root/NetworkManager"); network.WorldProgress += (key, done, total) => { if (key == "assets") _progress.Add((done, total)); };
            var textures = new TextureCache { Name = "TextureCache" }; AddChild(textures);
            var service = new AssetLoadingService { Name = "AssetLoadingService", CacheDirectory = folder, Transport = Fake }; AddChild(service); await Frames(2);

            var near = Guid.NewGuid(); var middle = Guid.NewGuid(); var far = Guid.NewGuid();
            _served[near] = Png(10); _served[middle] = Png(80); _served[far] = Png(200);
            string Entry(Guid id, double distance) => $"{{\"assetId\":\"{id}\",\"kind\":\"texture\",\"hash\":\"{Hash(_served[id])}\",\"size\":{_served[id].Length},\"mime\":\"image/png\",\"distance\":{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            _manifest = $"{{\"assets\":[{Entry(far, 500)},{Entry(near, 5)},{Entry(middle, 20)}]}}";

            // first visit: the two near ones are downloaded (progress in bytes), the far one afterwards
            await service.StartAsync(Guid.NewGuid());
            var essentialBytes = _served[near].Length + _served[middle].Length;
            Require(_progress[0] == (0, essentialBytes) && _progress[^1] == (essentialBytes, essentialBytes), $"progress runs from 0 to the bytes of the essential assets: {string.Join(",", _progress)}");
            Require(_progress.Zip(_progress.Skip(1)).All(p => p.Second.Done >= p.First.Done), "progress never goes back");
            Require(service.Downloads >= 2 && service.CacheHits == 0 && service.FedToCaches >= 2 && textures.CachedTextures >= 2, "the essential assets were downloaded and handed to the texture cache");
            var fileOrder = _requests.Where(r => r.Contains("/file")).ToList();
            Require(fileOrder[0].Contains(near.ToString()) && fileOrder[1].Contains(middle.ToString()), "the nearest asset is asked for first");
            await Wait(() => !service.BackgroundRunning);
            Require(service.Downloads == 3 && textures.CachedTextures == 3, "the far asset kept loading after the entry");
            Require(service.TryReadCached(near) is { } stored && stored.SequenceEqual(_served[near]), "the cache gives the bytes back");

            // second visit: everything comes from disk, nothing is downloaded
            _requests.Clear(); _progress.Clear();
            await service.StartAsync(Guid.NewGuid()); await Wait(() => !service.BackgroundRunning);
            Require(service.Downloads == 0 && service.CacheHits == 3 && !_requests.Any(r => r.Contains("/file")), "assets already on disk are not downloaded again");
            Require(_progress[^1] == (essentialBytes, essentialBytes), "cached assets still count as done");

            // a damaged file is replaced
            using (var file = Godot.FileAccess.Open($"{folder}/{Hash(_served[near])}.bin", Godot.FileAccess.ModeFlags.Write)) file.StoreString("lixo");
            _requests.Clear(); await service.StartAsync(Guid.NewGuid()); await Wait(() => !service.BackgroundRunning);
            Require(service.Downloads == 1 && _requests.Count(r => r.Contains("/file")) == 1 && service.TryReadCached(near) is { } repaired && repaired.SequenceEqual(_served[near]), "a damaged cached file is downloaded again");

            // an interrupted download is tried again
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath($"{folder}/{Hash(_served[middle])}.bin")); _interrupt[middle] = 1; _requests.Clear();
            await service.StartAsync(Guid.NewGuid()); await Wait(() => !service.BackgroundRunning);
            Require(service.Downloads == 1 && _requests.Count(r => r.Contains(middle.ToString())) == 2 && service.LastError is null, "an interrupted download is retried and succeeds");

            // bytes that do not match the announced hash are refused, never stored, and fail the loading
            var corrupt = Guid.NewGuid(); var good = Png(120); _served[corrupt] = Png(130);
            _manifest = $"{{\"assets\":[{{\"assetId\":\"{corrupt}\",\"kind\":\"texture\",\"hash\":\"{Hash(good)}\",\"size\":{good.Length},\"mime\":\"image/png\",\"distance\":3}}]}}"; _progress.Clear();
            await service.StartAsync(Guid.NewGuid());
            Require(_progress[^1].Total == -2 && service.Refused == 2 && service.LastError is { } error && error.Contains("imagem") && !Godot.FileAccess.FileExists($"{folder}/{Hash(good)}.bin"), $"a corrupted asset fails the loading and is not kept: {string.Join(",", _progress)}");
            Require(service.StoreIfKnownTest(corrupt) == false, "bytes of the wrong hash are not stored on demand");

            // an older server without a manifest: nothing to do; a broken one: failure
            _manifestStatus = 404; _progress.Clear(); await service.StartAsync(Guid.NewGuid()); Require(_progress[^1] == (0, 0), "no manifest, nothing to wait for");
            _manifestStatus = 500; _progress.Clear(); await service.StartAsync(Guid.NewGuid()); Require(_progress[^1] == (0, 0) && service.LastError is not null, "a manifest that fails does not stop the entry: the assets load on demand");

            // nothing essential near the arrival: complete at once
            _manifestStatus = 200; _manifest = $"{{\"assets\":[{Entry(far, 900)}]}}"; _progress.Clear(); await service.StartAsync(Guid.NewGuid());
            Require(_progress[0] == (0, 0), "assets far from the arrival do not hold the entry");

            DirRemoveAll(folder);
            GD.Print("event=asset_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            DirRemoveAll(folder);
            GD.PushError($"event=asset_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private Task<(long, byte[])> Fake(string path)
    {
        _requests.Add(path);
        if (path.Contains("/assets/manifest")) return Task.FromResult((_manifestStatus, Encoding.UTF8.GetBytes(_manifest)));
        var id = Guid.Parse(path.Split("/assets/")[1].Split('/')[0]);
        if (_interrupt.TryGetValue(id, out var left) && left > 0) { _interrupt[id] = left - 1; return Task.FromResult((0L, Array.Empty<byte>())); }
        return Task.FromResult(_served.TryGetValue(id, out var bytes) ? (200L, bytes) : (404L, Array.Empty<byte>()));
    }

    private static void DirRemoveAll(string folder)
    {
        using var dir = DirAccess.Open(folder); if (dir is null) return;
        foreach (var file in dir.GetFiles()) dir.Remove(file);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(folder));
    }

    private async Task Wait(Func<bool> condition) { for (var i = 0; i < 600 && !condition(); i++) await Frames(1); if (!condition()) throw new InvalidOperationException("timed out waiting for the assets"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
