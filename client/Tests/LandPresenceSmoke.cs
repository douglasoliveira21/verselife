using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Tests;

/// <summary>Phase 6 of the land system on the client: arrival and teleport, the parcel's environment and music, the parcel map and the property search.</summary>
public partial class LandPresenceSmoke : Node
{
    private static string Parcel(Guid id, string name, string state, bool mine, int cx0, int cz0, int cx1, int cz1, string extra = "") =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"description\":\"\",\"ownerName\":\"{(mine ? "Marina" : "Rafa")}\",\"mine\":{(mine ? "true" : "false")},\"state\":\"{state}\",\"area\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1) * 16},\"cells\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1)},\"maxObjects\":100,\"maxCost\":2000,\"objects\":0,\"cost\":0,\"revision\":1,\"runs\":[{string.Join(",", Enumerable.Range(cz0, cz1 - cz0 + 1).Select(z => $"[{z},{cx0},{cx1}]"))}]{extra}}}";

    public override async void _Ready()
    {
        try
        {
            var world = new Node3D(); AddChild(world);
            var environment = new IslandEnvironmentController { Name = "EnvironmentController", DayNightCycle = false, Hour = 10, Weather = "clear" }; world.AddChild(environment);
            var land = new LandManager { Name = "LandManager" }; world.AddChild(land);
            var map = new LandMapWindow { Name = "LandMapWindow" }; world.AddChild(map);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var said = new List<string>(); land.Said += said.Add;
            var calls = new List<string>(); var teleports = new List<string>();
            land.TeleportTransport = (parcel, x, z) => teleports.Add($"{parcel}|{x}|{z}");

            var mine = Guid.NewGuid(); var other = Guid.NewGuid(); var sale = Guid.NewGuid(); var closed = Guid.NewGuid();
            var rich = "{\\\"hour\\\":22,\\\"weather\\\":\\\"rain\\\",\\\"light\\\":0.6,\\\"sky\\\":\\\"#334466\\\"}";
            var media = "{\\\"musicUrl\\\":\\\"https://cdn.example.com/a.mp3\\\",\\\"volume\\\":0.4,\\\"autoplay\\\":true,\\\"name\\\":\\\"Radio\\\"}";
            var listing = "{\"size\":256,\"version\":5,\"parcels\":[" +
                Parcel(mine, "Praça", "own", true, 0, 0, 5, 3, $",\"arrivalX\":8.0,\"arrivalZ\":6.0,\"arrivalYaw\":90,\"teleportMode\":\"forced\",\"environment\":\"{rich}\",\"media\":\"{media}\",\"environmentAllowed\":true") + "," +
                Parcel(other, "Loja", "owned", false, 6, 0, 9, 3, ",\"teleportMode\":\"disabled\",\"environment\":\"{}\",\"media\":\"{}\",\"allowVoice\":false") + "," +
                Parcel(sale, "Colina", "sale", false, 10, 0, 13, 3, ",\"salePrice\":900,\"environment\":\"{\\\"hour\\\":3}\",\"media\":\"{}\",\"environmentAllowed\":false") + "," +
                Parcel(closed, "Cofre", "restricted", false, 14, 0, 17, 3, ",\"allowMedia\":false") + "]}";
            land.Transport = (method, path, body) => { calls.Add($"{method} {path} {body}"); return (200, listing); };
            land.Apply(listing);

            var praca = land.Parcels.First(p => p.Name == "Praça");
            Require(praca.HasArrival && praca.ArrivalX == 8 && praca.ArrivalZ == 6 && praca.ArrivalYaw == 90 && praca.TeleportMode == "forced", "arrival point and mode are read");
            Require(praca.Environment.Hour == 22 && praca.Environment.Weather == "rain" && praca.Environment.Light == .6f && praca.Environment.Sky == "#334466", "the parcel environment is read");
            Require(praca.MusicUrl == "https://cdn.example.com/a.mp3" && Mathf.IsEqualApprox(praca.MusicVolume, .4f) && praca.MusicName == "Radio" && praca.MusicAutoplay, "the music is read");
            Require(!land.Parcels.First(p => p.Name == "Loja").HasArrival && land.Parcels.First(p => p.Name == "Loja").MusicUrl == "", "a parcel without settings has none");

            // ---- environment follows the parcel
            land.Presence.Apply(praca);
            Require(environment.ParcelLookActive && environment.ParcelBlend == 0, "entering a parcel starts a fade, not a jump");
            environment.FinishBlendForTesting();
            Require(Mathf.IsEqualApprox(environment.HourShown, 22f) && environment.WeatherShown == "rain" && Mathf.IsEqualApprox(environment.LightShown, .6f), $"inside, the parcel's hour, weather and light show: {environment.HourShown}/{environment.WeatherShown}/{environment.LightShown}");
            land.Presence.Apply(null);
            environment.FinishBlendForTesting();
            Require(!environment.ParcelLookActive && Mathf.IsEqualApprox(environment.HourShown, 10f) && environment.WeatherShown == "clear" && Mathf.IsEqualApprox(environment.LightShown, 1f), "outside, the island's own look is back");
            land.Presence.Apply(land.Parcels.First(p => p.Name == "Colina"));
            Require(!environment.ParcelLookActive, "an island that forbids parcel environments is not overridden");
            land.Presence.Apply(null);

            // ---- the look of the whole island (set by the owner) shows on open ground, under the parcel's own settings
            var islandId = Guid.NewGuid(); var served = "{\"look\":{\"hour\":3,\"weather\":\"fog\",\"light\":0.5,\"sky\":\"#112244\"}}"; var asked = new List<string>(); var fail = false;
            var islandLook = new IslandLookService { Name = "IslandLookService", IslandProvider = () => islandId };
            islandLook.UseTransportForTesting((method, path, body) => { asked.Add($"{method} {path}"); return Task.FromResult<(long, string)>(fail ? (0, "{}") : (200, served)); });
            world.AddChild(islandLook); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await islandLook.RefreshAsync();
            Require(asked.Count >= 1 && asked[0] == $"GET /api/v1/worlds/islands/{islandId}/environment" && islandLook.Look is { Hour: 3f, Weather: "fog" }, "the look of the island is asked for when the person is on it");
            land.Presence.Apply(null, force: true); environment.FinishBlendForTesting();
            Require(environment.ParcelLookActive && Mathf.IsEqualApprox(environment.HourShown, 3f) && environment.WeatherShown == "fog" && Mathf.IsEqualApprox(environment.LightShown, .5f), "on open ground the island look shows");
            land.Presence.Apply(praca, force: true); environment.FinishBlendForTesting();
            Require(Mathf.IsEqualApprox(environment.HourShown, 22f) && environment.WeatherShown == "rain" && Mathf.IsEqualApprox(environment.LightShown, .6f), "inside a parcel its own settings win over the island look");
            land.Presence.Apply(land.Parcels.First(p => p.Name == "Loja"), force: true); environment.FinishBlendForTesting();
            Require(Mathf.IsEqualApprox(environment.HourShown, 3f) && environment.WeatherShown == "fog", "a parcel that sets nothing shows the island look");
            land.Presence.Apply(land.Parcels.First(p => p.Name == "Colina"), force: true); environment.FinishBlendForTesting();
            Require(Mathf.IsEqualApprox(environment.HourShown, 3f), "a parcel that is not allowed its own look shows the island look");
            land.Presence.PositionForTesting = () => new Vector3(-50, 0, -50); // open ground, so the service itself re-applies the look when it changes
            land.Presence.Apply(null, force: true);
            served = "{\"look\":{\"hour\":15}}"; await islandLook.RefreshAsync(); environment.FinishBlendForTesting();
            Require(Mathf.IsEqualApprox(environment.HourShown, 15f), "a change of the look reaches the person on the next ask");
            fail = true; await islandLook.RefreshAsync(); Require(islandLook.Look is { Hour: 15f }, "if the server cannot be reached the last look stays");
            fail = false; served = "{\"look\":{}}"; await islandLook.RefreshAsync(); environment.FinishBlendForTesting();
            Require(islandLook.Look is null && !environment.ParcelLookActive && Mathf.IsEqualApprox(environment.HourShown, 10f), "an island without a look goes back to the game's own sky");

            // ---- the music of the whole island plays where no parcel plays its own
            var songs = new List<string>(); land.Presence.ClearCacheForTesting(); land.Presence.FetcherForTesting = url => { songs.Add(url); return [1, 2, 3, 4]; };
            land.Presence.PositionForTesting = () => new Vector3(-50, 0, -50);
            served = "{\"look\":{},\"media\":{\"musicUrl\":\"https://cdn.example.com/ilha.mp3\",\"volume\":0.5,\"autoplay\":true,\"name\":\"Rádio\"}}"; await islandLook.RefreshAsync();
            Require(islandLook.Music is { Url: "https://cdn.example.com/ilha.mp3", Autoplay: true, Name: "Rádio" } && Mathf.IsEqualApprox(islandLook.Music.Volume, .5f), "the music of the island is read with the look");
            Require(songs.Count == 1 && songs[0] == "https://cdn.example.com/ilha.mp3", "on open ground the music of the island is fetched");
            land.Presence.ClearCacheForTesting(); songs.Clear(); land.Presence.Apply(praca, force: true);
            Require(songs.Count == 1 && songs[0] == "https://cdn.example.com/a.mp3", "inside a parcel with its own music the parcel's plays, not the island's");
            land.Presence.ClearCacheForTesting(); songs.Clear(); land.Presence.Apply(land.Parcels.First(p => p.Name == "Loja"), force: true);
            Require(songs.Count == 1 && songs[0] == "https://cdn.example.com/ilha.mp3", "a parcel that plays nothing has the island's music");
            land.Presence.MusicEnabled = false; songs.Clear(); land.Presence.ClearCacheForTesting(); land.Presence.Apply(null, force: true);
            Require(songs.Count == 0 && !land.Presence.Playing, "with music switched off in the preferences nothing is fetched"); land.Presence.MusicEnabled = true;
            served = "{\"look\":{},\"media\":{\"musicUrl\":\"https://cdn.example.com/ilha.mp3\",\"volume\":0.5,\"autoplay\":false}}"; await islandLook.RefreshAsync(); land.Presence.Apply(land.Parcels.First(p => p.Name == "Loja"), force: true); land.Presence.Apply(null);
            Require(land.Presence.Status.Contains("da ilha não toca sozinha"), "a music that does not start by itself says so");
            served = "{\"look\":{},\"media\":{\"musicUrl\":\"http://cdn.example.com/ilha.mp3\"}}"; await islandLook.RefreshAsync();
            Require(islandLook.Music is null, "an address the game would not play is not used");
            served = "{\"look\":{},\"media\":{}}"; await islandLook.RefreshAsync();
            Require(islandLook.Music is null && !land.Presence.Playing, "an island without music plays none");
            land.Presence.PositionForTesting = null;
            islandLook.QueueFree();

            // ---- music
            var fetched = new List<string>();
            land.Presence.FetcherForTesting = url => { fetched.Add(url); return [1, 2, 3, 4]; };
            land.Presence.Apply(praca);
            Require(fetched.Count == 1 && fetched[0] == "https://cdn.example.com/a.mp3", "entering a parcel with music fetches it");
            land.Presence.Apply(null);
            land.Presence.MusicEnabled = false; fetched.Clear();
            land.Presence.Apply(praca);
            Require(fetched.Count == 0 && !land.Presence.Playing, "with parcel music switched off nothing is fetched");
            land.Presence.MusicEnabled = true; land.Presence.Apply(null);
            land.Presence.ClearCacheForTesting(); land.Presence.FetcherForTesting = _ => null; land.Presence.Apply(praca, force: true);
            Require(land.Presence.Status.Contains("Não foi possível"), "a failed download is said, not hidden");
            land.Presence.Apply(null);
            Require(LandPresence.Refuse("http://cdn.example.com/a.mp3") == "not_https" && LandPresence.Refuse("https://127.0.0.1/a.mp3") == "bad_host" && LandPresence.Refuse("https://cdn.example.com/a.wav") == "bad_type", "the client refuses unsafe addresses itself");

            // ---- voice: a parcel can switch it off, and the presence says so while the avatar stands there
            Require(!land.Parcels.First(p => p.Name == "Cofre").AllowMedia && land.Parcels.Where(p => p.Name != "Cofre").All(p => p.AllowMedia), "the parcel list carries the media rule (on unless the server says off)");
            var shop = land.Parcels.First(p => p.Name == "Loja"); Require(!shop.AllowVoice && land.Parcels.First(p => p.Name == "Praça").AllowVoice, "the parcel list carries the voice rule (on unless the server says off)");
            land.Presence.Apply(null, force: true); Require(land.Presence.VoiceAllowed && land.Presence.VoiceOffIn.Length == 0, "open ground has voice");
            land.Presence.Apply(shop); Require(!land.Presence.VoiceAllowed && land.Presence.VoiceOffIn == "Loja", "standing on a parcel with voice off says so, with its name");
            land.Presence.Apply(land.Parcels.First(p => p.Name == "Praça")); Require(land.Presence.VoiceAllowed, "another parcel brings voice back");
            land.Presence.Apply(null);

            // ---- the Ambiente tab
            land.AboutAt(new Vector3(8, 0, 8));
            land.ShowStyleForTesting();
            Require(land.StyleEditableForTesting && land.TeleportModeShownForTesting == 1 && land.ArrivalTextForTesting.Contains("8") && land.ArrivalTextForTesting.Contains("90"), $"the owner sees and can edit the settings: {land.ArrivalTextForTesting}");
            calls.Clear();
            land.SetEnvironmentFormForTesting(6.5f, 3, null, 1.2f, "#112233");
            land.SaveEnvironmentForTesting();
            var put = calls.First(c => c.StartsWith("Put /parcels/" + mine + "/environment"));
            using (var body = JsonDocument.Parse(put[(put.IndexOf('{'))..]))
            {
                var sent = ParcelEnvironment.Parse(body.RootElement.GetProperty("environment").GetString());
                Require(sent.Hour == 6.5f && sent.Weather == "rain" && sent.Fog is null && sent.Light == 1.2f && sent.Sky == "#112233", $"only the ticked settings are sent: {put}");
            }
            calls.Clear();
            land.SetTeleportModeForTesting(2); land.SaveArrivalForTesting();
            Require(calls.Any(c => c.StartsWith("Put /parcels/" + mine + "/arrival") && c.Contains("\"mode\":\"disabled\"") && c.Contains("\"x\":8")), "the arrival and the mode are sent together");
            calls.Clear();
            land.SetMusicFormForTesting("http://x.com/a.mp3", "x", .5f, true); land.SaveMusicForTesting();
            Require(calls.Count == 0 && said[^1].Contains("https"), "a bad music address is refused before asking the server");
            land.SetMusicFormForTesting("https://cdn.example.com/b.ogg", "Nova", .5f, false); land.SaveMusicForTesting();
            Require(calls.Any(c => c.StartsWith("Put /parcels/" + mine + "/media") && c.Contains("b.ogg") && c.Contains("\"autoplay\":false")), "a good music address is sent");
            calls.Clear(); land.RemoveMusicForTesting();
            Require(calls.Any(c => c.Contains("/media") && c.Contains("\"musicUrl\":\"\"")), "removing the music sends an empty address");

            land.AboutAt(new Vector3(30, 0, 8)); // Loja: somebody else's, teleport disabled
            land.ShowStyleForTesting();
            Require(!land.StyleEditableForTesting && !land.TeleportHereEnabledForTesting, "a visitor cannot edit, and a parcel that refuses teleports offers no button");

            // ---- teleporting
            teleports.Clear(); said.Clear();
            land.TeleportTo(land.Parcels.First(p => p.Name == "Loja"));
            Require(teleports.Count == 0 && said.Count == 1, "a disabled parcel is not even asked");
            land.TeleportTo(praca);
            Require(teleports.Count == 1 && teleports[0].StartsWith(mine.ToString()), "teleporting asks for the parcel");
            land.TeleportToPoint(100, -20);
            Require(teleports[^1] == "|100|-20", "teleporting to a spot sends the spot");

            // ---- the map
            map.Open();
            map.Pick(8, 8);
            Require(map.Selected?.Name == "Praça" && map.TitleForTesting == "Praça" && !map.BuyVisibleForTesting && map.DetailForTesting.Contains("Seu terreno"), "picking a parcel on the map describes it");
            map.Pick(50, 8); // Colina is at x 40..56
            Require(map.Selected?.Name == "Colina" && map.BuyVisibleForTesting && map.DetailForTesting.Contains("900"), $"a parcel for sale offers to buy: {map.DetailForTesting}");
            map.Pick(-80, -80);
            Require(map.Selected is null && map.TitleForTesting == "Terreno livre", "bare ground is said to be free");
            map.TeleportPointForTesting();
            Require(teleports[^1] == "|-80|-80", "the map teleports to a spot of bare ground");
            map.SetFilterForTesting(1);
            Require(map.PassesForTesting(land.Parcels.First(p => p.Name == "Colina")) && !map.PassesForTesting(praca), "the 'for sale' filter keeps only parcels for sale");
            map.SetFilterForTesting(3);
            Require(map.PassesForTesting(praca) && !map.PassesForTesting(land.Parcels.First(p => p.Name == "Cofre")), "the 'mine' filter keeps only my parcels");
            map.SetFilterForTesting(5);
            Require(map.PassesForTesting(land.Parcels.First(p => p.Name == "Cofre")), "the 'restricted' filter shows restricted parcels");

            // ---- the property search
            var queries = new List<string>();
            var island = Guid.NewGuid(); var parcelHit = Guid.NewGuid();
            map.SearchTransport = query => { queries.Add(query); return (200, $"[{{\"parcelId\":\"{parcelHit}\",\"islandId\":\"{island}\",\"islandName\":\"Atol\",\"name\":\"Duna\",\"area\":640,\"offer\":\"rent\",\"price\":30,\"rentPeriodDays\":30,\"ownerName\":\"Ana\"}},{{\"parcelId\":\"{sale}\",\"islandId\":\"{Guid.Empty}\",\"islandName\":\"Aqui\",\"name\":\"Colina\",\"area\":256,\"offer\":\"sale\",\"price\":900,\"rentPeriodDays\":0,\"ownerName\":\"Rafa\"}}]"); };
            map.SetSearchFormForTesting("duna praia", 2, "10", "500", "100", 1);
            map.Search();
            Require(queries.Count == 1 && queries[0].Contains("q=duna%20praia") && queries[0].Contains("offer=rent") && queries[0].Contains("minPrice=10") && queries[0].Contains("maxPrice=500") && queries[0].Contains("minArea=100") && queries[0].Contains("sort=price"), $"the form becomes a query: {queries.FirstOrDefault()}");
            Require(map.Hits.Count == 2 && map.Hits[0].Name == "Duna" && map.Hits[0].Offer == "rent" && map.Hits[0].Price == 30 && map.Hits[1].Offer == "sale", "the results are read");
            Guid? joined = null; map.JoinIsland = id => joined = id;
            map.SelectResultForTesting(0);
            Require(map.GoTextForTesting.Contains("Atol"), $"a result on another island offers to go there: {map.GoTextForTesting}");
            map.GoToResultForTesting();
            Require(joined == island, "going to a result on another island joins that island");
            map.SearchTransport = _ => (500, "");
            map.Search();
            Require(map.Hits.Count == 0 && map.NoteForTesting.Contains("500"), "a failed search says so");
            map.SearchTransport = _ => (200, "not json");
            map.Search();
            Require(map.Hits.Count == 0 && map.NoteForTesting.Contains("ler"), "an unreadable answer is handled");

            GD.Print("event=land_presence_smoke result=passed");
            GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError($"event=land_presence_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
