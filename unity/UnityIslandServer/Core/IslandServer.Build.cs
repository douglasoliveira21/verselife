using System.Text.Json;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Networking;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Core;

/// <summary>
/// Building: the island does not own objects, the API does. The island checks the shape of the request, the rate and the edit locks, forwards it to
/// POST servers/{id}/build-operations as the player, and tells everyone what the API accepted (the same messages as the Godot server).
/// </summary>
public sealed partial class IslandServer
{
    private sealed record StructureLink(Guid ObjectId, Guid? GroupId, Guid? ParentId, int ExpectedRevision);
    private sealed record BuildCommand(string Operation, Guid? ObjectId, BuildObjectCommand? Object, int ExpectedRevision, Guid[]? ObjectIds = null, LivePose[]? Poses = null, Guid? PackageItemId = null,
        float OriginX = 0, float OriginY = 0, float OriginZ = 0, StructureLink[]? Links = null, bool? Locked = null, JsonElement? Properties = null);
    private sealed record BuildObjectCommand(string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ,
        string? MaterialJson, string? PhysicsJson, Guid? GroupId, int ExpectedRevision = 0, string? GeometryJson = null, string? Name = null, string? Description = null, Guid? ParentObjectId = null,
        string? PermissionsJson = null, string? ContentJson = null, float? PivotX = null, float? PivotY = null, float? PivotZ = null);

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly EditLocks _locks = new();
    private DateTimeOffset _lastLockSweep = DateTimeOffset.UtcNow;
    private static readonly string[] BuildOperations = { "create", "update", "delete", "return", "rez", "structure", "protect", "properties", "buy", "lock", "unlock", "live" };

    private static bool ValidBuildCommand(BuildObjectCommand c)
    {
        var values = new[] { c.PositionX, c.PositionY, c.PositionZ, c.RotationX, c.RotationY, c.RotationZ, c.ScaleX, c.ScaleY, c.ScaleZ };
        return PrimitiveCatalog.IsKnown(c.PrimitiveType) && values.All(float.IsFinite)
            && new[] { c.PivotX, c.PivotY, c.PivotZ }.All(p => p is null || (float.IsFinite(p.Value) && Math.Abs(p.Value) <= 64f))
            && new[] { c.ScaleX, c.ScaleY, c.ScaleZ }.All(x => x is >= .01f and <= 64f)
            && (c.GeometryJson?.Length ?? 0) <= 16384 && (c.MaterialJson?.Length ?? 0) <= 32768;
    }

    private void BuildRejected(Player player, string code) => _wire.Send(player.PeerId, new BuildOperationRejectedMessage(code));

    private void OnBuildOperation(Player actor, string operationJson)
    {
        if (operationJson.Length > 100_000) return;
        var now = DateTimeOffset.UtcNow;
        while (actor.BuildTimes.Count > 0 && now - actor.BuildTimes.Peek() > TimeSpan.FromSeconds(1)) actor.BuildTimes.Dequeue();
        if (actor.BuildTimes.Count >= 20) { BuildRejected(actor, "rate_limit"); return; }
        BuildCommand? command;
        try { command = JsonSerializer.Deserialize<BuildCommand>(operationJson, WebJson); } catch (JsonException) { command = null; }
        if (command is null || Array.IndexOf(BuildOperations, command.Operation) < 0 || (command.Object is not null && !ValidBuildCommand(command.Object)) || (command.ObjectIds?.Length ?? 0) > 500)
        { BuildRejected(actor, "invalid_operation"); return; }
        actor.BuildTimes.Enqueue(now);
        if (command.Operation is "lock" or "unlock") { HandleLock(actor, command, now); return; }
        if (command.Operation == "live")
        {
            if (LivePoses.Accept(command.Poses, _locks, actor.UserId, now))
                foreach (var other in _players.Values.Where(x => x.Authenticated && x.PeerId != actor.PeerId))
                    _wire.Send(other.PeerId, new BuildOperationAppliedMessage(JsonSerializer.Serialize(new { operation = "live", userId = actor.UserId, poses = command.Poses })));
            return;
        }
        // someone else is editing it: the island refuses before the API is even asked
        Guid[] targets = command.Operation is "create" or "rez" ? Array.Empty<Guid>()
            : command.Operation == "structure" ? command.Links?.Select(l => l.ObjectId).ToArray() ?? Array.Empty<Guid>()
            : command.ObjectIds ?? (command.ObjectId is { } one ? new[] { one } : Array.Empty<Guid>());
        if (_locks.FirstHeldByOther(targets, actor.UserId, now) is { } held) { BuildRejected(actor, $"locked:{NameOf(held.Holder)}"); return; }
        _ = ForwardBuildAsync(actor, command, targets, now);
    }

    private async Task ForwardBuildAsync(Player actor, BuildCommand command, Guid[] targets, DateTimeOffset now)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                userId = actor.UserId, operation = command.Operation, objectId = command.ObjectId, @object = command.Object, expectedRevision = command.ExpectedRevision, objectIds = command.ObjectIds,
                packageItemId = command.PackageItemId, links = command.Links, locked = command.Locked, properties = command.Properties, originX = command.OriginX, originY = command.OriginY, originZ = command.OriginZ,
            }, WebJson);
            var (status, accepted) = await _api.BuildOperationAsync(body, _cts.Token).ConfigureAwait(false);
            _inbox.Enqueue(() => FinishBuild(actor, command, targets, now, status, accepted));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _log.Write("build_operation_failed", new { actor.PeerId, error = exception.Message });
            _inbox.Enqueue(() => BuildRejected(actor, "service_unavailable"));
        }
    }

    private void FinishBuild(Player actor, BuildCommand command, Guid[] targets, DateTimeOffset now, int status, string accepted)
    {
        if (status is < 200 or >= 300)
        {
            BuildRejected(actor, ErrorCodeOf(accepted) ?? (status == 409 ? "revision_conflict" : $"server_rejected_{status}"));
            return;
        }
        if (command.Operation == "return") { AnnounceReturned(accepted); return; }
        _scripting?.OnObjectApplied(accepted);
        foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, new BuildOperationAppliedMessage(accepted));
        if (command.Operation == "delete") BroadcastUnlock(_locks.Release(targets, actor.UserId), actor.UserId);
        else if (targets.Length > 0 && _locks.HolderOf(targets[0], DateTimeOffset.UtcNow) == actor.UserId) _locks.TryAcquire(targets, actor.UserId, DateTimeOffset.UtcNow, out _); // working on it keeps the lease alive
    }

    /// <summary>The short "code" the API puts in a refusal, so the player gets a precise message instead of a status number.</summary>
    private static string? ErrorCodeOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                && code.GetString() is { Length: > 0 and <= 40 } text && text.All(c => char.IsLetterOrDigit(c) || c == '_') ? text : null;
        }
        catch (JsonException) { return null; }
    }

    private string NameOf(Guid userId) => _players.Values.FirstOrDefault(x => x.Authenticated && x.UserId == userId)?.DisplayName is { Length: > 0 } name ? name : "outro jogador";

    private void BroadcastApplied(object message)
    {
        var json = JsonSerializer.Serialize(message);
        foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, new BuildOperationAppliedMessage(json));
    }

    private void HandleLock(Player actor, BuildCommand command, DateTimeOffset now)
    {
        var ids = (command.ObjectIds ?? Array.Empty<Guid>()).Distinct().ToArray();
        if (ids.Length == 0) return;
        if (command.Operation == "unlock") { BroadcastUnlock(_locks.Release(ids, actor.UserId), actor.UserId); return; }
        if (!_locks.TryAcquire(ids, actor.UserId, now, out var holder)) { BuildRejected(actor, holder == actor.UserId ? "lock_limit" : $"locked:{NameOf(holder)}"); return; }
        BroadcastApplied(new { operation = "lock", objectIds = ids, userId = actor.UserId, userName = actor.DisplayName, expiresAtMs = (now + EditLocks.Lease).ToUnixTimeMilliseconds() });
    }

    private void BroadcastUnlock(IReadOnlyList<Guid> freed, Guid userId)
    {
        if (freed.Count > 0) BroadcastApplied(new { operation = "unlock", objectIds = freed, userId });
    }

    private void SweepLocks()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastLockSweep < TimeSpan.FromSeconds(1)) return;
        _lastLockSweep = now;
        foreach (var byUser in _locks.Expire(now).GroupBy(x => x.User)) BroadcastUnlock(byUser.Select(x => x.Object).ToList(), byUser.Key);
    }

    /// <summary>A newcomer learns which objects are being edited right now.</summary>
    private void SendLocks(int peerId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var byUser in _locks.Snapshot(now).GroupBy(x => x.User))
            _wire.Send(peerId, new BuildOperationAppliedMessage(JsonSerializer.Serialize(new { operation = "lock", objectIds = byUser.Select(x => x.Object).ToArray(), userId = byUser.Key, userName = NameOf(byUser.Key), expiresAtMs = byUser.Max(x => x.Expires).ToUnixTimeMilliseconds() })));
    }

    /// <summary>The API returned objects to their owners' inventories: everyone sees them leave, as an ordinary delete.</summary>
    private void AnnounceReturned(string accepted)
    {
        try
        {
            using var document = JsonDocument.Parse(accepted);
            if (document.RootElement.TryGetProperty("healed", out var healed) && healed.ValueKind == JsonValueKind.Array && healed.GetArrayLength() > 0)
            { var structure = JsonSerializer.Serialize(new { operation = "structure", objects = healed }); _scripting?.OnObjectApplied(structure); ToAll(structure); } // what hung under the returned objects was re-hung: everybody sees the new tree
            if (!document.RootElement.TryGetProperty("objectIds", out var ids)) return;
            foreach (var id in ids.EnumerateArray())
            {
                if (!Guid.TryParse(id.GetString(), out var objectId)) continue;
                { var removal = JsonSerializer.Serialize(new { operation = "delete", objectId, returned = true }); _scripting?.OnObjectApplied(removal); ToAll(removal); }
                foreach (var holder in _locks.Snapshot(DateTimeOffset.UtcNow).Where(x => x.Object == objectId)) BroadcastUnlock(_locks.Release(new[] { objectId }, holder.User), holder.User);
            }
        }
        catch (JsonException) { }
    }
}
