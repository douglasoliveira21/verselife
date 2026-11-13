namespace LifeVerse.Scripting;

/// <summary>
/// Runs a script that touches every layer of the API once, on a pretend island, so the first real events of a freshly started server do not pay for compiling those paths (which would count against their time budget).
/// </summary>
internal static class ScriptWarmUp
{
    private static bool _done;

    private sealed class Quiet : IScriptWorld
    {
        public double Now { get; set; }
        private readonly ScriptObjectState _me = new(Guid.NewGuid(), Guid.NewGuid(), "Aquecimento", new Vec3(1, 1, 1), new Vec3(0, 0, 0), new Vec3(1, 1, 1));
        public ScriptObjectState? GetObject(Guid id) => _me with { Id = id };
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) { }
        public RegionInfo? GetRegion() => new(Guid.NewGuid(), "Ilha", 1);
    }

    public static void Run()
    {
        if (_done) return;
        _done = true;
        var world = new Quiet(); var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 10_000), warm: false); var objectId = Guid.NewGuid(); var avatar = Guid.NewGuid();
        host.Request += _ => { };
        const string source = @"
function on_start() timer.every(1, function() object.setPosition(1, 1, 1) end) timer.after(1, 'x') chat.listen(5) storage.set('a', {n = 1, t = {1, 2}}) end
function on_touch(e)
  local p = object.getPosition() object.setPosition(p.x + 1, p.y, p.z) object.setColor(1, 0, 0) object.setAlpha(0.5) object.moveTo(2, 2, 2, 1)
  chat.say('a') chat.whisper('b') chat.sendChannel(5, 'c') print('x', 1) debug.log('y') storage.set('k', storage.get('a'))
  local t = storage.get('a') dialog.show(e.avatar_id, {title = 'T', message = 'M', buttons = {'A', 'B'}}) permissions.request(e.avatar_id, 'SHOW_HUD')
  string.format('%d', 5) table.sort({3, 1, 2}) string.find('abc', 'b') region.getName() object.getLinkCount() object.sendLinkMessage('m')
end
function on_dialog_response(e) end function on_chat(e) end function on_permission_granted(e) end function on_link_message(e) end function on_script_error(e) end";
        host.Sync([new ScriptDefinition(Guid.NewGuid(), objectId, Guid.NewGuid(), "warm", source, 1, 100_000)]); host.Process(10_000);
        for (var i = 0; i < 3; i++)
        {
            host.Enqueue(new ScriptEvent(objectId, "touch", new Dictionary<string, object?> { ["avatar_id"] = avatar, ["avatar_name"] = "A", ["avatar"] = new Dictionary<string, object?> { ["id"] = avatar, ["name"] = "A" } })); host.Process(10_000);
            world.Now += 2; host.Tick(world.Now); host.Process(10_000);
        }
        host.Stop(host.Scripts[0].ScriptId);
    }
}
