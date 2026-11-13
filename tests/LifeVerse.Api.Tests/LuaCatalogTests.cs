using System.Text;
using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The documentation (the catalog) against the code: everything documented exists, everything that exists is documented, and the validator tells real mistakes from real code. Also the example scripts of the manual, run for real.</summary>
public sealed class LuaCatalogTests
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<(ScriptDefinition Script, ObjectChange Change)> Changes { get; } = [];
        public List<string> Said { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes.Add((script, change));
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public RegionInfo? GetRegion() => new(Guid.NewGuid(), "Ilha", 1);
    }

    private static (ScriptHost Host, World World, Guid Object, Guid Script) Run(string source, string? eventName = null, Dictionary<string, object?>? data = null, int budget = 100_000)
    {
        var world = new World(); var obj = Guid.NewGuid(); var script = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Guid.NewGuid(), "Porta", new Vec3(0, 0, 0), new Vec3(0, 0, 0), new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 5000)); host.Sync([new ScriptDefinition(script, obj, world.Objects[obj].OwnerUserId, "s", source, 1, budget)]); host.Process(1000);
        if (eventName is not null) { host.Enqueue(new ScriptEvent(obj, eventName, data)); host.Process(1000); }
        return (host, world, obj, script);
    }

    [Fact]
    public void EveryDocumentedFunctionExists()
    {
        var checks = new StringBuilder("local missing = {} local function check(ns, name) local t = ns == 'global' and _G or _G[ns] if type(t) ~= 'table' or type(t[name]) ~= 'function' then missing[#missing + 1] = ns .. '.' .. name end end\n");
        foreach (var function in ScriptCatalog.Functions) checks.Append($"check('{function.Namespace}', '{function.Name}')\n");
        foreach (var name in ScriptCatalog.LegacyObject) checks.Append($"check('object', '{name}')\n");
        checks.Append("storage.set('missing', table.concat(missing, ' '))");
        var (host, _, _, script) = Run("function on_start() " + checks + " end");
        Assert.Null(host.AllMetrics().Single().LastError); Assert.Equal("\"\"", host.TakeDirtyState()[script]["missing"]);
    }

    [Fact]
    public void EveryFunctionTheApiHasIsDocumented()
    {
        var documented = string.Join(",", ScriptCatalog.Functions.Select(f => $"'{f.Namespace}.{f.Name}'").Concat(ScriptCatalog.LegacyObject.Select(n => $"'object.{n}'")));
        var (host, _, _, script) = Run("function on_start() local known = {} for _, n in ipairs({" + documented + "}) do known[n] = true end local extra = {} "
            + "for _, ns in ipairs({'object', 'chat', 'timer', 'storage', 'debug', 'region', 'script', 'permissions', 'dialog', 'audio', 'http', 'physics', 'economy', 'inventory', 'avatar', 'hud', 'particles', 'light', 'lv'}) do for name, value in pairs(_G[ns]) do if type(value) == 'function' and not known[ns .. '.' .. name] then extra[#extra + 1] = ns .. '.' .. name end end end storage.set('extra', table.concat(extra, ' ')) end");
        Assert.Null(host.AllMetrics().Single().LastError); Assert.Equal("\"\"", host.TakeDirtyState()[script]["extra"]);
    }

    [Fact]
    public void EveryDocumentedEventIsOneTheHostCalls_AndEntriesAreComplete()
    {
        Assert.All(ScriptCatalog.Events, e => Assert.Contains(e.Name, ScriptHost.KnownHandlers));
        Assert.All(ScriptHost.KnownHandlers, name => Assert.Contains(name, ScriptCatalog.EventNames));
        Assert.All(ScriptCatalog.Functions, f => { Assert.False(string.IsNullOrWhiteSpace(f.Signature)); Assert.False(string.IsNullOrWhiteSpace(f.Description)); Assert.False(string.IsNullOrWhiteSpace(f.Returns)); Assert.False(string.IsNullOrWhiteSpace(f.Permission)); Assert.False(string.IsNullOrWhiteSpace(f.Limits)); Assert.False(string.IsNullOrWhiteSpace(f.Errors)); Assert.False(string.IsNullOrWhiteSpace(f.Example)); });
        Assert.Equal(ScriptCatalog.Functions.Count, ScriptCatalog.Functions.Select(f => f.FullName).Distinct().Count());
    }

    [Fact]
    public void EveryExampleInTheCatalogCompiles()
    {
        foreach (var function in ScriptCatalog.Functions)
        {
            var result = ScriptCompiler.Compile($"function on_start()\n  {function.Example}\nend");
            Assert.True(result.Valid, $"{function.FullName}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
            Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "warning");
        }
        foreach (var e in ScriptCatalog.Events)
        {
            var result = ScriptCompiler.Compile(e.Example);
            Assert.True(result.Valid, $"{e.Name}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"); Assert.Equal([e.Name], result.Handlers); Assert.Empty(result.Diagnostics);
        }
    }

    // ---- the validator ------------------------------------------------------------------------------------------------

    [Fact]
    public void Compiler_FindsSyntaxErrorsWithTheirLine()
    {
        var result = ScriptCompiler.Compile("function on_start()\n  local x = \nend end"); Assert.False(result.Valid); Assert.Equal("error", result.Diagnostics[0].Severity); Assert.True(result.Diagnostics[0].Line is 3 or 2, $"line {result.Diagnostics[0].Line}");
        Assert.False(ScriptCompiler.Compile("").Valid); Assert.False(ScriptCompiler.Compile(new string('x', 70_000)).Valid);
    }

    [Theory]
    [InlineData("os.execute('ls')", "os")]
    [InlineData("io.open('x')", "io")]
    [InlineData("require('x')", "require")]
    [InlineData("local f = load('return 1')", "load")]
    [InlineData("dofile('a')", "dofile")]
    [InlineData("package.loadlib('a', 'b')", "package")]
    [InlineData("setmetatable({}, {})", "setmetatable")]
    [InlineData("local c = coroutine.create(print)", "coroutine")]
    [InlineData("debug.getinfo(1)", "debug.getinfo")]
    [InlineData("debug.sethook()", "debug.sethook")]
    public void Compiler_RejectsWhatTheSandboxDoesNotHave(string code, string mention)
    {
        var result = ScriptCompiler.Compile($"function on_start()\n  {code}\nend"); Assert.False(result.Valid);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == "error"); Assert.Equal(2, error.Line); Assert.Contains(mention, error.Message);
    }

    [Fact]
    public void Compiler_WarnsAboutTyposAndUnknownEvents_WithASuggestion()
    {
        var result = ScriptCompiler.Compile("function on_touch_strat(e) end\nfunction on_start()\n  chat.sya('oi')\n  object.setPostion(1, 2, 3)\n  timer.every(1, function() end)\nend");
        Assert.True(result.Valid);   // warnings do not stop a script
        Assert.Contains(result.Diagnostics, d => d.Line == 1 && d.Message.Contains("on_touch_start")); Assert.Contains(result.Diagnostics, d => d.Line == 3 && d.Message.Contains("chat.say")); Assert.Contains(result.Diagnostics, d => d.Line == 4 && d.Message.Contains("object.setPosition"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Line == 5);
    }

    [Fact]
    public void Compiler_IgnoresCommentsAndStrings_AndAcceptsTheOldObjectNames()
    {
        var source = "-- os.execute('x') e chat.sya\n--[[ io.open('y')\n function on_nada() end ]]\nfunction on_touch(event)\n  chat.say('io.open os.execute chat.sya')\n  object.set_position({x=1,y=2,z=3}) object.say('legado') local s = [[ require('x') ]]\nend";
        var result = ScriptCompiler.Compile(source); Assert.True(result.Valid, string.Join("; ", result.Diagnostics.Select(d => d.Message))); Assert.Empty(result.Diagnostics); Assert.Equal(["on_touch"], result.Handlers);
        Assert.Equal(source.Count(c => c == '\n'), ScriptCompiler.Strip(source).Count(c => c == '\n'));          // line numbers still match
    }

    [Fact]
    public void Compiler_WarnsWhenNothingIsHandled_OrAnEventIsDefinedTwice()
    {
        Assert.Contains(ScriptCompiler.Compile("local x = 1").Diagnostics, d => d.Message.Contains("nenhum evento"));
        Assert.Contains(ScriptCompiler.Compile("function on_start() end\nfunction on_start() end").Diagnostics, d => d.Message.Contains("2 vezes"));
    }

    // ---- the example scripts of the manual, run for real -----------------------------------------------------------------

    [Fact]
    public void Example_Door_OpensAndClosesOnTouch()
    {
        var (host, world, obj, _) = Run("local aberta = false\nfunction on_touch(event)\n  aberta = not aberta\n  if aberta then\n    object.setRotation(0, 90, 0)\n    chat.say('Porta aberta')\n  else\n    object.setRotation(0, 0, 0)\n    chat.say('Porta fechada')\n  end\nend");
        host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(100); host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(100);
        Assert.Equal(["Porta aberta", "Porta fechada"], world.Said); Assert.Equal([90f, 0f], world.Changes.Select(c => c.Change.Rotation!.Value.Y).ToArray());
    }

    [Fact]
    public void Example_Spinner_TurnsWithATimer()
    {
        var (host, world, _, _) = Run("function on_start()\n  timer.every(0.5, function()\n    local rot = object.getRotation()\n    object.setRotation(rot.x, rot.y + 2, rot.z)\n  end)\nend");
        for (var i = 0; i < 3; i++) { world.Now += .6; host.Tick(world.Now); host.Process(100); }
        Assert.Equal(3, world.Changes.Count); Assert.All(world.Changes, c => Assert.Equal(2f, c.Change.Rotation!.Value.Y));   // the pretend world does not apply changes, so each turn starts from 0
    }

    [Fact]
    public void Example_Greeter_UsesTheAvatarName()
    {
        var (_, world, _, _) = Run("function on_avatar_enter(event)\n  chat.say('Bem-vindo, ' .. event.avatar.name)\nend", "enter", new() { ["avatar"] = new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["name"] = "Ana" } });
        Assert.Equal(["Bem-vindo, Ana"], world.Said);
    }
}
