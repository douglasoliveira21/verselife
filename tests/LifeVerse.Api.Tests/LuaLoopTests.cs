using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Everything a script can do to repeat itself: timers (repeating, chained, on the top level, after an error, after a restart), the legacy repeating timer and loops inside one event.</summary>
public sealed class LuaLoopTests
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<(ScriptDefinition Script, ObjectChange Change)> Changes { get; } = [];
        public List<string> Spoken { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes.Add((script, change));
        public void Say(ScriptDefinition script, string text) => Spoken.Add(text);
        public void Chat(ScriptDefinition script, ChatKind kind, string text) => Spoken.Add(text);
        public bool TellAvatar(ScriptDefinition script, Guid avatarId, string text) => true;
        public RegionInfo? GetRegion() => null;
        public bool StartHttp(ScriptDefinition script, Guid requestId, HttpSpec spec) => false;
        public bool ApplyPhysics(ScriptDefinition script, string action, Vec3 value) => false;
        public Vec3? GetVelocity(Guid objectId) => null;
        public bool StartPayment(ScriptDefinition script, Guid requestId, Guid avatarId, long amount) => false;
        public string? AvatarName(Guid avatarId) => "Ana";
        public bool PlayAnimation(ScriptDefinition script, Guid avatarId, string name, double seconds) => true;
        public void StopAnimation(ScriptDefinition script, Guid avatarId) { }
        public IReadOnlyList<ScriptItem> GetContents(Guid objectId) => [];
        public IReadOnlyList<ScriptObjectState> GetLinkset(Guid objectId) => Objects.TryGetValue(objectId, out var one) ? [one] : [];
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class Rig
    {
        public World World { get; } = new(); public ScriptHost Host { get; }
        public List<ScriptReport> Reports { get; } = [];
        public List<ScriptDefinition> Definitions { get; } = [];
        public Guid Object { get; } = Guid.NewGuid();
        public Rig() { Host = new ScriptHost(World, new ScriptHostOptions(MillisecondsPerEvent: 2000)); Host.Report += Reports.Add; World.Objects[Object] = new ScriptObjectState(Object, Owner, "Porta", new Vec3(10, 1, 20), new Vec3(0, 90, 0), new Vec3(1, 2, 3)); }
        public void Add(string source) { Definitions.Add(new ScriptDefinition(Guid.NewGuid(), Object, Owner, "script", source, 1)); Host.Sync(Definitions); Host.Process(500); }
        public void Advance(double seconds, int steps = 1) { for (var i = 0; i < steps; i++) { World.Now += seconds / steps; Host.Tick(World.Now); Host.Process(500); } }
        public int Said(string prefix) => World.Spoken.Count(s => s.StartsWith(prefix, StringComparison.Ordinal));
        public int Turns => World.Changes.Count;
    }

    [Fact]
    public void ATimerCreatedOnTheTopLevelOfTheScriptKeepsRepeating()
    {
        var rig = new Rig(); rig.Add("n = 0 timer.every(1, function() n = n + 1 chat.say('tic' .. n) end)");
        rig.Advance(5, 20);
        Assert.Equal(5, rig.Said("tic")); Assert.Empty(rig.Reports);
    }

    [Fact]
    public void ATimerCreatedInOnStartKeepsRepeating()
    {
        var rig = new Rig(); rig.Add("n = 0 function on_start() timer.every(1, function() n = n + 1 chat.say('tic' .. n) end) end");
        rig.Advance(5, 20);
        Assert.Equal(5, rig.Said("tic"));
    }

    [Fact]
    public void ATimerThatSchedulesTheNextOneKeepsGoing()
    {
        var rig = new Rig(); rig.Add("n = 0 function passo() n = n + 1 object.setRotation(0, n, 0) timer.after(1, passo) end function on_start() passo() end");
        rig.Advance(6, 24);
        Assert.True(rig.Turns >= 6, $"steps: {rig.Turns}");
    }

    [Fact]
    public void ATimerKeepsRepeatingAfterOneRunFailed()
    {
        var rig = new Rig(); rig.Add("n = 0 timer.every(1, function() n = n + 1 if n == 2 then error('boom') end chat.say('tic' .. n) end)");
        rig.Advance(5, 20);
        Assert.Equal(4, rig.Said("tic")); Assert.Single(rig.Reports);
    }

    [Fact]
    public void TheLegacyRepeatingTimerFiresOnTimerEveryTime()
    {
        var rig = new Rig(); rig.Add("n = 0 function on_start() object.create_timer(1, true) end function on_timer(e) n = n + 1 chat.say('t' .. n) end");
        rig.Advance(4, 16);
        Assert.Equal(4, rig.Said("t"));
    }

    [Fact]
    public void TheNamedRepeatingTimerFiresOnIntervalEveryTime()
    {
        var rig = new Rig(); rig.Add("function on_start() timer.every(1, 'ritmo') end function on_interval(e) chat.say('i' .. e.count) end");
        rig.Advance(4, 16);
        Assert.Equal(4, rig.Said("i"));
    }

    [Fact]
    public void ARestartedScriptStartsItsTimersAgain()
    {
        var rig = new Rig(); rig.Add("n = 0 function on_start() timer.every(1, function() n = n + 1 chat.say('tic' .. n) end) end");
        rig.Advance(2, 8); var before = rig.Said("tic");
        rig.Host.Sync(rig.Definitions); rig.Host.Process(500);          // the island sends the same scripts again (it restarted)
        rig.Advance(3, 12);
        Assert.True(rig.Said("tic") >= before + 3, $"before {before}, after {rig.Said("tic")}");
    }

    [Fact]
    public void ATimerThatTalksEverySecondKeepsRunningForAMinute()
    {
        var rig = new Rig(); rig.Add("n = 0 timer.every(1, function() n = n + 1 object.setRotation(0, n, 0) chat.say('tic' .. n) end)");
        rig.Advance(60, 240);
        Assert.True(rig.Turns >= 58, $"turns: {rig.Turns}, reports: {string.Join(" | ", rig.Reports.Select(r => r.Message).Distinct())}");
    }

    [Theory]
    [InlineData("function on_start() while true do object.setName('x') end end")]
    [InlineData("function on_start() while 1 do end end")]
    [InlineData("function on_start() repeat local a = 1 until false end")]
    public void AnEndlessLoopIsReportedBeforeItRuns(string source)
    {
        var result = ScriptCompiler.Compile(source);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Severity == "error" && d.Message.Contains("nunca termina", StringComparison.Ordinal) && d.Message.Contains("timer.every", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("function on_start() while true do if x then break end end end")]
    [InlineData("function on_start() while true do return end end")]
    [InlineData("function on_start() repeat x = 1 until x == 1 end")]
    [InlineData("function on_start() local x = 0 while x < 3 do x = x + 1 end end")]
    [InlineData("function on_start() for i = 1, 3 do object.setName('x') end end")]
    public void ALoopThatCanLeaveIsLeftAlone(string source)
    {
        var result = ScriptCompiler.Compile(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("nunca termina", StringComparison.Ordinal));
        Assert.True(result.Valid);
    }

    [Theory]
    [InlineData("function on_start() timer.every(0.1, function() end) end")]
    [InlineData("function on_start() timer.after(0, function() end) end")]
    [InlineData("function on_start() object.create_timer(0.05, true) end")]
    [InlineData("function on_start() timer.every(100000, function() end) end")]
    public void ATimerOutOfRangeIsReported(string source)
    {
        var result = ScriptCompiler.Compile(source);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Severity == "error" && d.Message.Contains("0,25 s", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("function on_start() timer.every(0.25, function() end) end")]
    [InlineData("function on_start() timer.every(1, function() end) end")]
    [InlineData("function on_start() local t = 2 timer.every(t, function() end) end")]
    public void ATimerInRangeIsAccepted(string source) => Assert.True(ScriptCompiler.Compile(source).Valid);

    [Theory]
    [InlineData("https://cdn.example.com/musica.mp3", null)]
    [InlineData("https://cdn.example.com/musica.OGG?x=1", null)]
    [InlineData("https://cdn.example.com/baixar/12345", "bad_type")]      // the address must name an .mp3 or .ogg file
    [InlineData("https://cdn.example.com/video.mp4", "bad_type")]
    [InlineData("https://cdn.example.com/som.wav", "bad_type")]
    [InlineData("https://cdn.example.com/", "bad_type")]
    [InlineData("http://cdn.example.com/musica.mp3", "not_https")]
    [InlineData("https://127.0.0.1/musica.mp3", "bad_host")]
    public void TheMediaAddressRulesAreTheSameEverywhere(string url, string? expected)
    {
        Assert.Equal(expected, MediaUrl.Check(url));
        Assert.Equal(expected, LifeVerse.Shared.Land.LandMediaUrl.Check(url));
        Assert.Equal(expected, LifeVerse.Api.Modules.Land.LandPresentation.CheckMediaUrl(url));
    }

    [Theory]
    [InlineData("https://cdn.example.com/baixar/12345", null)]
    [InlineData("https://127.0.0.1/x", "bad_host")]
    [InlineData("http://cdn.example.com/x", "not_https")]
    [InlineData("https://user:pass@cdn.example.com/x", "not_https")]
    [InlineData("https://intranet.local/x", "bad_host")]
    [InlineData("https://10.0.0.5/x", "bad_host")]
    public void ARedirectIsFollowedOnlyToAPublicHttpsAddress(string url, string? expected) => Assert.Equal(expected, LifeVerse.Shared.Land.LandMediaUrl.CheckRedirect(url));

    [Fact]
    public void ALoopInsideOneEventRunsAllItsTurns()
    {
        var rig = new Rig(); rig.Add("function on_start() local s = 0 for i = 1, 500 do s = s + i end object.setRotation(0, s, 0) end");
        Assert.Equal(125250f, rig.World.Changes.Single().Change.Rotation!.Value.Y); Assert.Empty(rig.Reports);
    }
}

