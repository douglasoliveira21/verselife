using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Wire;

namespace LifeVerse.IslandServer.Core;

/// <summary>
/// How a player looks. The client only says "refresh"; the island asks the API what the avatar looks like now and tells everybody, so nobody can show
/// something they have not been checked and approved for. Worn attachments dragged live are relayed only for what the player really wears.
/// </summary>
public sealed partial class IslandServer
{
    private static readonly TimeSpan AppearanceCooldown = TimeSpan.FromSeconds(3);
    private const int MaxAppearanceBytes = 96 * 1024;

    private void OnRefreshAppearance(Player player)
    {
        var now = DateTimeOffset.UtcNow;
        if (player.LastAppearance is { } last && now - last < AppearanceCooldown)
        {
            // too soon after the last one: it is not dropped, it is done as soon as the wait is over (a change right after another would otherwise never reach the others)
            if (!player.LateAppearance) { player.LateAppearance = true; _ = RefreshLaterAsync(player, AppearanceCooldown - (now - last)); }
            return;
        }
        player.LastAppearance = now;
        _ = RefreshAppearanceAsync(player);
    }

    private async Task RefreshLaterAsync(Player player, TimeSpan wait)
    {
        try { await Task.Delay(wait + TimeSpan.FromMilliseconds(50), _cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        _inbox.Enqueue(() =>
        {
            if (!player.LateAppearance) return;
            player.LateAppearance = false;
            if (!player.Authenticated || !_players.ContainsKey(player.PeerId)) return;
            player.LastAppearance = DateTimeOffset.UtcNow;
            _ = RefreshAppearanceAsync(player);
        });
    }

    private async Task RefreshAppearanceAsync(Player player)
    {
        var avatarId = player.AvatarId;
        try
        {
            var (status, text) = await _api.GetAppearanceAsync(avatarId, _cts.Token).ConfigureAwait(false);
            if (status is < 200 or >= 300) { _log.Write("appearance_refresh_failed", new { player.PeerId, status }); return; }
            if (text.Length > MaxAppearanceBytes) { _log.Write("appearance_refresh_too_big", new { player.PeerId, size = text.Length }); return; }
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement.GetProperty("appearanceJson").GetString() ?? "{}";
            var name = document.RootElement.GetProperty("displayName").GetString() ?? string.Empty;
            _inbox.Enqueue(() =>
            {
                if (!_players.TryGetValue(player.PeerId, out var current) || !ReferenceEquals(current, player) || player.AvatarId != avatarId || !player.Authenticated) return; // they left while we asked
                player.AppearanceJson = json; if (name.Length > 0) player.DisplayName = name;
                foreach (var target in _players.Values.Where(x => x.Authenticated)) _wire.Send(target.PeerId, new AppearanceChangedMessage(player.PeerId, player.DisplayName, json));
                _log.Write("appearance_refreshed", new { player.PeerId, avatarId });
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        { _log.Write("appearance_refresh_failed", new { player.PeerId, error = exception.Message }); }
    }

    private HashSet<Guid> WornBy(Player player)
    {
        if (player.WornJson is not null && ReferenceEquals(player.WornJson, player.AppearanceJson)) return player.WornIds;
        player.WornIds = AttachmentLive.WornIds(player.AppearanceJson); player.WornJson = player.AppearanceJson;
        return player.WornIds;
    }

    /// <summary>A live preview of an attachment being dragged: relayed only for one the player wears, a few times a second, with numbers the API would accept. Nothing is kept.</summary>
    private void OnAttachmentLive(Player player, AttachmentLiveMessage message)
    {
        var now = DateTimeOffset.UtcNow;
        if (!AttachmentLive.MayRelay(player.LastAttachmentLive, now)) return;
        if (!Guid.TryParse(message.Id, out var attachment) || !WornBy(player).Contains(attachment)) { _log.Write("attachment_live_refused", new { player.PeerId, reason = "not_worn" }); return; }
        if (AttachmentLive.Check(message.Values) is { } problem) { _log.Write("attachment_live_refused", new { player.PeerId, reason = problem }); return; }
        player.LastAttachmentLive = now;
        foreach (var target in _players.Values.Where(x => x.Authenticated && x.PeerId != player.PeerId))
            _wire.Send(target.PeerId, new AttachmentLiveChangedMessage(player.PeerId, message.Id, message.Values));
    }
}
