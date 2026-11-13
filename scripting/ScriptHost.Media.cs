using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>What a script asks the players to listen to: an mp3 or ogg file by HTTPS address, heard in 3D around the object.</summary>
public sealed record MediaSpec(string Url, float Volume, bool Loop, float Range, string PlayId);

public sealed partial class ScriptHost
{
    public const int MaxMediaPlaysPerMinute = 10;
    public const float MinMediaRange = 3f, MaxMediaRange = 60f, DefaultMediaRange = 25f;

    /// <summary>media.*: sound from the internet at the object, for scripts whose owner gave CONTROL_MEDIA (editor, tab Permissões). One stream per script; it ends when the script ends, stops or restarts.</summary>
    private void InstallMediaApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var media = new Table(script); script.Globals["media"] = media;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) =>
        {
            instance.Metrics.ApiCalls++;
            if (!_permissions.Has(instance.Definition.Id, Guid.Empty, ScriptPermission.ControlMedia)) throw new ScriptRuntimeException("media needs the CONTROL_MEDIA permission: the owner of the object gives it in the editor (tab Permissões)");
            return body(args);
        });
        media["play"] = Fn(args =>
        {
            var url = args.Count > 0 ? args[0].CastToString()?.Trim() : null;
            if (MediaUrl.Check(url) is { } why) throw new ScriptRuntimeException(why switch { "not_https" => "media.play: the address must start with https:// (no login in it)", "bad_host" => "media.play: that address is not allowed (internal network or invalid name)", "bad_type" => "media.play: only .mp3 or .ogg files", _ => "media.play: the address is too long (512 letters at most)" });
            var volume = 0.8f; var loop = false; var range = DefaultMediaRange;
            if (args.Count > 1 && args[1].Type == DataType.Table)
            {
                var o = args[1].Table;
                if (o.Get("volume") is { Type: DataType.Number } v && double.IsFinite(v.Number)) volume = (float)Math.Clamp(v.Number, 0, 1);
                if (o.Get("loop") is { Type: DataType.Boolean } l) loop = l.Boolean;
                if (o.Get("range") is { Type: DataType.Number } r && double.IsFinite(r.Number)) range = (float)Math.Clamp(r.Number, MinMediaRange, MaxMediaRange);
            }
            var now = world.Now; while (instance.MediaPlays.Count > 0 && now - instance.MediaPlays.Peek() > 60) instance.MediaPlays.Dequeue();
            if (instance.MediaPlays.Count >= MaxMediaPlaysPerMinute) throw new ScriptRuntimeException($"media.play: at most {MaxMediaPlaysPerMinute} a minute"); instance.MediaPlays.Enqueue(now);
            var spec = new MediaSpec(url!, volume, loop, range, Guid.NewGuid().ToString("N"));
            if (!world.PlayMedia(instance.Definition, spec)) return DynValue.False;
            instance.Media = spec; return DynValue.True;
        });
        media["stop"] = Fn(_ => { CloseMedia(instance); return DynValue.Nil; });
        media["setVolume"] = Fn(args =>
        {
            if (instance.Media is not { } current) return DynValue.False;
            var spec = current with { Volume = Math.Clamp(Number(args, 0, 0.8f), 0f, 1f) };
            if (!world.PlayMedia(instance.Definition, spec)) return DynValue.False;
            instance.Media = spec; return DynValue.True;       // the same stream continues at the new volume (same play)
        });
        media["isPlaying"] = Fn(_ => DynValue.NewBoolean(instance.Media is not null));
    }

    /// <summary>The stream of a script stops (it asked, or the script ended, stopped or restarted).</summary>
    private void CloseMedia(Instance instance)
    {
        if (instance.Media is null) return;
        instance.Media = null; world.StopMedia(instance.Definition);
    }

    /// <summary>A client finished playing the stream a script started (and told the island): the script hears <c>on_media_finished</c>. Only for the play that is current; a loop never finishes.</summary>
    public void MediaFinished(Guid scriptId, string playId)
    {
        if (Find(scriptId) is not { } instance || instance.Media is not { Loop: false } current || current.PlayId != playId) return;
        instance.Media = null; world.StopMedia(instance.Definition);
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "media_finished", new Dictionary<string, object?> { ["url"] = current.Url }, scriptId));
    }

    /// <summary>A sound the object played (audio.play) reached its end on the client of somebody near: its scripts hear <c>on_sound_finished</c>.</summary>
    public void SoundFinished(Guid objectId) => Enqueue(new ScriptEvent(objectId, "sound_finished", new Dictionary<string, object?>()));
}
