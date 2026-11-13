using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>media.*: only with CONTROL_MEDIA, only mp3/ogg by HTTPS from outside the private network, limits, one stream per script that goes with it, and the finished events.</summary>
public sealed class LuaMediaTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private const string Song = "https://exemplo.com/musica.mp3";

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = [];
        public List<MediaSpec> Played { get; } = []; public int Stopped; public bool CanPlay { get; set; } = true;
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public bool PlayMedia(ScriptDefinition script, MediaSpec spec) { if (CanPlay) Played.Add(spec); return CanPlay; }
        public void StopMedia(ScriptDefinition script) => Stopped++;
    }

    private static (ScriptHost Host, World World, Guid Object, Guid Script, List<ScriptReport> Reports) Rig(string source, bool allowed = true)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Radio", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        var script = Guid.NewGuid(); host.Sync([new ScriptDefinition(script, obj, Owner, "radio", source, 1)]); if (allowed) host.Permissions.Grant(script, Guid.Empty, ScriptPermission.ControlMedia); host.Process(500); return (host, world, obj, script, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Theory]
    [InlineData("https://exemplo.com/a.mp3", null)]
    [InlineData("https://cdn.exemplo.com/pasta/a.OGG?x=1", null)]
    [InlineData("http://exemplo.com/a.mp3", "not_https")]
    [InlineData("https://user:senha@exemplo.com/a.mp3", "not_https")]
    [InlineData("https://localhost/a.mp3", "bad_host")]
    [InlineData("https://127.0.0.1/a.mp3", "bad_host")]
    [InlineData("https://192.168.0.5/a.mp3", "bad_host")]
    [InlineData("https://10.0.0.1/a.mp3", "bad_host")]
    [InlineData("https://servidor/a.mp3", "bad_host")]
    [InlineData("https://exemplo.com/a.wav", "bad_type")]
    [InlineData("https://exemplo.com/video.mp4", "bad_type")]
    [InlineData("", "not_https")]
    [InlineData(null, "not_https")]
    public void OnlyPublicHttpsAudioFilesAreAccepted(string? url, string? why) => Assert.Equal(why, MediaUrl.Check(url));

    [Fact]
    public void ItNeedsTheOwnersPermission()
    {
        var (host, world, obj, _, reports) = Rig($"function on_touch() media.play('{Song}') end", allowed: false);
        Touch(host, obj); Assert.Contains("CONTROL_MEDIA", Assert.Single(reports).Message); Assert.Empty(world.Played);
    }

    [Fact]
    public void AScriptPlaysWithItsOptionsClamped_AndStopsAndChangesTheVolume()
    {
        var (host, world, obj, _, reports) = Rig($"function on_touch() chat.say(tostring(media.play('{Song}', {{volume = 5, loop = true, range = 500}}))) chat.say(tostring(media.isPlaying())) chat.say(tostring(media.setVolume(0.25))) media.stop() chat.say(tostring(media.isPlaying())) chat.say(tostring(media.setVolume(1))) end");
        Touch(host, obj); Assert.Empty(reports); Assert.Equal(["true", "true", "true", "false", "false"], world.Said);
        var first = world.Played[0]; Assert.Equal((Song, 1f, true, ScriptHost.MaxMediaRange), (first.Url, first.Volume, first.Loop, first.Range));
        Assert.Equal((0.25f, first.PlayId), (world.Played[1].Volume, world.Played[1].PlayId));                                  // a new volume is the same play
        Assert.Equal(1, world.Stopped);
    }

    [Fact]
    public void TheDefaultsAreAModerateVolumeAndRange_AndAWorldThatCannotPlayAnswersFalse()
    {
        var (host, world, obj, _, _) = Rig($"function on_touch() media.play('{Song}') end"); Touch(host, obj);
        Assert.Equal((0.8f, false, ScriptHost.DefaultMediaRange), (world.Played[0].Volume, world.Played[0].Loop, world.Played[0].Range));
        var (host2, world2, obj2, _, reports2) = Rig($"function on_touch() chat.say(tostring(media.play('{Song}'))) chat.say(tostring(media.isPlaying())) end"); world2.CanPlay = false; Touch(host2, obj2); Assert.Empty(reports2); Assert.Equal(["false", "false"], world2.Said);
    }

    [Theory]
    [InlineData("media.play('http://exemplo.com/a.mp3')")]
    [InlineData("media.play('https://127.0.0.1/a.mp3')")]
    [InlineData("media.play('https://exemplo.com/a.wav')")]
    [InlineData("media.play()")]
    [InlineData("media.play(42)")]
    public void BadAddressesAreAnError(string call)
    {
        var (host, world, obj, _, reports) = Rig($"function on_touch() {call} end"); Touch(host, obj); Assert.Single(reports); Assert.Empty(world.Played);
    }

    [Fact]
    public void ScriptsCannotFloodThePlayers_AndLaterTheyMay()
    {
        var (host, world, obj, _, reports) = Rig($"function on_touch() media.play('{Song}') end");
        for (var i = 0; i < ScriptHost.MaxMediaPlaysPerMinute; i++) Touch(host, obj); Assert.Empty(reports); Touch(host, obj); Assert.Contains("at most", Assert.Single(reports).Message);
        world.Now += 61; reports.Clear(); Touch(host, obj); Assert.Empty(reports);
    }

    [Fact]
    public void TheStreamGoesWhenTheScriptStopsOrRestarts()
    {
        var (host, world, obj, script, _) = Rig($"function on_touch() media.play('{Song}') end"); Touch(host, obj);
        host.Stop(script); Assert.Equal(1, world.Stopped); host.Restart(script); host.Process(500); Touch(host, obj); Assert.Equal(2, world.Played.Count); host.Restart(script); Assert.Equal(2, world.Stopped);
    }

    [Fact]
    public void TheScriptHearsWhenAStreamEnds_OnlyTheCurrentOneAndNeverALoop()
    {
        var (host, world, obj, script, reports) = Rig($"function on_touch() media.play('{Song}') end function on_media_finished(e) chat.say('fim ' .. e.url) end");
        Touch(host, obj); var play = world.Played[0].PlayId;
        host.MediaFinished(script, "outro"); host.MediaFinished(Guid.NewGuid(), play); host.Process(500); Assert.Empty(world.Said);                         // not the current play / not its script
        host.MediaFinished(script, play); host.MediaFinished(script, play); host.Process(500); Assert.Equal([$"fim {Song}"], world.Said); Assert.Equal(1, world.Stopped);   // once
        var (host2, world2, obj2, script2, _) = Rig($"function on_touch() media.play('{Song}', {{loop = true}}) end function on_media_finished(e) chat.say('nunca') end"); Touch(host2, obj2);
        host2.MediaFinished(script2, world2.Played[0].PlayId); host2.Process(500); Assert.Empty(world2.Said);
        Assert.Empty(reports);
    }

    [Fact]
    public void ASoundThatEndedReachesTheObjectsScripts()
    {
        var (host, world, obj, _, reports) = Rig("function on_sound_finished(e) chat.say('acabou') end");
        host.SoundFinished(obj); host.Process(500); Assert.Empty(reports); Assert.Equal(["acabou"], world.Said);
    }
}
