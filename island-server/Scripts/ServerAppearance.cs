using System.Text.Json;
using Godot;

namespace LifeVerse.IslandServer.Scripts;

/// A player changed how they look (clothes, body, animations) while playing. The client only says "refresh"; the server asks the API what
/// the player looks like now (the API answers from its database for the avatar the player was admitted with) and tells everybody. The client
/// never sends the appearance itself, so nobody can show something they have not been checked and approved for.
public partial class ServerMain
{
    private readonly Dictionary<int, DateTimeOffset> _lastAppearance = [];
    private static readonly TimeSpan AppearanceCooldown = TimeSpan.FromSeconds(3);
    private const int MaxAppearanceBytes = 96 * 1024;

    public void RefreshAppearance(int peerId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var now = DateTimeOffset.UtcNow;
        if (_lastAppearance.TryGetValue(peerId, out var last) && now - last < AppearanceCooldown) return;
        _lastAppearance[peerId] = now;
        _ = RefreshAppearanceAsync(peerId, player.AvatarId);
    }

    private async Task RefreshAppearanceAsync(int peerId, Guid avatarId)
    {
        try
        {
            using var response = await _api.GetAsync($"{_config.ApiBaseUrl}/api/v1/platform/avatars/{avatarId}/appearance", _shutdown.Token);
            if (!response.IsSuccessStatusCode) { Log("appearance_refresh_failed", new { peerId, status = (int)response.StatusCode }); return; }
            var text = await response.Content.ReadAsStringAsync(_shutdown.Token);
            if (text.Length > MaxAppearanceBytes) { Log("appearance_refresh_too_big", new { peerId, size = text.Length }); return; }
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement.GetProperty("appearanceJson").GetString() ?? "{}"; var name = document.RootElement.GetProperty("displayName").GetString() ?? string.Empty;
            if (!_players.TryGetValue(peerId, out var player) || player.AvatarId != avatarId || !player.Authenticated) return; // they left or changed while we asked
            player.AppearanceJson = json; if (name.Length > 0) player.DisplayName = name;
            foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.AppearanceChanged, peerId, player.DisplayName, json);
            Log("appearance_refreshed", new { peerId, avatarId });
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Log("appearance_refresh_failed", new { peerId, error = exception.Message }); }
    }
}
