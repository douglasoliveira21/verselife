using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>The HUDs that scripts show on the screen of the avatars who allowed them: the island sends the picture to that one avatar (and again when they come back) and takes the player's clicks and texts back to the script.</summary>
public sealed partial class IslandScripting
{
    private readonly HashSet<Guid> _hudAvatars = [];

    /// <summary>Shows, updates or removes the HUD of a script on one avatar's screen.</summary>
    public void ShowHud(ScriptDefinition script, Guid avatarId, HudSnapshot hud)
    {
        if (SendToAvatar is null) return;
        var objectName = _objects.TryGetValue(script.ObjectId, out var source) ? source.Name : "Objeto";
        SendToAvatar(avatarId, JsonSerializer.Serialize(new { operation = "script_hud", script = script.Id, objectName, visible = hud.Visible, title = hud.Title, x = hud.X, y = hud.Y, width = hud.Width, elements = hud.Elements.Select(e => new { id = e.Id, type = e.Type, text = e.Text, color = e.Color, value = e.Value, max = e.Max, placeholder = e.Placeholder }) }));
    }

    /// <summary>An avatar that was not on the island before is sent the HUDs scripts keep for them (they were away or have just arrived).</summary>
    private void RestoreHuds(IReadOnlyList<AvatarPresence> avatars)
    {
        foreach (var avatar in avatars) if (_hudAvatars.Add(avatar.AvatarId)) foreach (var (script, hud) in _host.HudsOf(avatar.AvatarId)) ShowHud(script, avatar.AvatarId, hud);
        if (_hudAvatars.Count > avatars.Count + 64) _hudAvatars.RemoveWhere(id => avatars.All(a => a.AvatarId != id));
        foreach (var gone in _hudAvatars.Where(id => avatars.All(a => a.AvatarId != id)).ToList()) _hudAvatars.Remove(gone);
    }

    /// <summary>A click, a text or a close from the HUD of a script (it comes through the same message as dialog answers, marked <c>hud</c>).</summary>
    private bool TryHudAction(AvatarPresence who, JsonElement root)
    {
        if (!root.TryGetProperty("hud", out var flag) || flag.ValueKind != JsonValueKind.True) return false;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (Guid.TryParse(Text("script"), out var script)) _host.HudAction(script, who.AvatarId, Text("action"), Text("element"), root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
        return true;
    }
}
