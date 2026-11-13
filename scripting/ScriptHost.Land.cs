using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>What anybody may know about a parcel (nothing private: the same a visitor sees on the land map).</summary>
public sealed record ParcelData(Guid Id, string Name, Guid Owner, string Access, string Build, bool ForSale);

public sealed partial class ScriptHost
{
    private void InstallLandApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var parcel = new Table(script); script.Globals["parcel"] = parcel;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        ParcelData? Mine() => world.GetObject(instance.Definition.ObjectId) is { } me ? world.GetParcelAt(me.Position.X, me.Position.Z) : null;
        DynValue Describe(ParcelData? p) => p is null ? DynValue.Nil : DynValue.NewTable(ParcelTable(script, p));
        parcel["getAt"] = Fn(args => Describe(world.GetParcelAt(Number(args, 0), Number(args, 1))));
        parcel["getId"] = Fn(_ => Mine() is { } p ? DynValue.NewString(p.Id.ToString()) : DynValue.Nil);
        parcel["getName"] = Fn(_ => Mine() is { } p ? DynValue.NewString(p.Name) : DynValue.Nil);
        parcel["getOwner"] = Fn(_ => Mine() is { } p ? DynValue.NewString(p.Owner.ToString()) : DynValue.Nil);
        parcel["getPermissions"] = Fn(_ => { if (Mine() is not { } p) return DynValue.Nil; var table = new Table(script); table["access"] = p.Access; table["build"] = p.Build; table["forSale"] = p.ForSale; return DynValue.NewTable(table); });
        script.Globals.Get("object").Table["getParcel"] = Fn(_ => Mine() is { } p ? DynValue.NewString(p.Id.ToString()) : DynValue.Nil);
    }

    private static Table ParcelTable(Script script, ParcelData p) { var table = new Table(script); table["id"] = p.Id.ToString(); table["name"] = p.Name; table["owner"] = p.Owner.ToString(); table["access"] = p.Access; table["build"] = p.Build; table["forSale"] = p.ForSale; return table; }

    /// <summary>An avatar stepped from one parcel to another. The scripts of objects standing on the parcel they left hear <c>on_parcel_leave</c>, those on the parcel they entered <c>on_parcel_enter</c>.</summary>
    public void AvatarMovedParcels(Guid avatarId, string avatarName, ParcelData? from, ParcelData? to)
    {
        if (from?.Id == to?.Id) return;
        var who = new Dictionary<string, object?> { ["id"] = avatarId, ["name"] = avatarName };
        Dictionary<string, object?> Data(ParcelData p) => new() { ["avatar_id"] = avatarId, ["avatar_name"] = avatarName, ["avatar"] = who, ["parcel"] = new Dictionary<string, object?> { ["id"] = p.Id.ToString(), ["name"] = p.Name, ["owner"] = p.Owner.ToString(), ["access"] = p.Access, ["build"] = p.Build, ["forSale"] = p.ForSale } };
        if (from is not null) foreach (var objectId in ObjectsWanting("parcel_leave").ToList()) if (world.GetObject(objectId) is { } o && world.GetParcelAt(o.Position.X, o.Position.Z)?.Id == from.Id) Enqueue(new ScriptEvent(objectId, "parcel_leave", Data(from)));
        if (to is not null) foreach (var objectId in ObjectsWanting("parcel_enter").ToList()) if (world.GetObject(objectId) is { } o && world.GetParcelAt(o.Position.X, o.Position.Z)?.Id == to.Id) Enqueue(new ScriptEvent(objectId, "parcel_enter", Data(to)));
    }
}

public sealed partial class ScriptHost
{
    /// <summary>The island changed as a whole: every script that handles <c>on_region_changed</c> hears what changed (<c>sea_level</c> and/or <c>terrain</c>) and the sea level now.</summary>
    public void RegionChanged(IReadOnlyList<string> kinds, float seaLevel)
    {
        var data = new Dictionary<string, object?> { ["kinds"] = string.Join(",", kinds), ["sea_level"] = seaLevel, ["sea_level_changed"] = kinds.Contains("sea_level"), ["terrain_changed"] = kinds.Contains("terrain") };
        foreach (var objectId in ObjectsWanting("region_changed").ToList()) Enqueue(new ScriptEvent(objectId, "region_changed", data));
    }
}
