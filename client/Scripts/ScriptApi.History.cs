using System.Text.Json;

namespace LifeVerse.Client.Scripts;

public sealed record ScriptVersionView(int Version, string Name, int Lines, int Bytes, string SavedAt);

/// <summary>The history of the code of a script: the versions kept, the code of one of them, and going back to it.</summary>
public sealed partial class ScriptApi
{
    public async Task<(int Current, IReadOnlyList<ScriptVersionView> Versions)?> VersionsAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("GET", $"{Base(objectId)}/{scriptId}/versions", null);
        if (status is < 200 or >= 300) return null;
        try
        {
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
            return ((int)Long(root, "current"), root.GetProperty("versions").EnumerateArray().Select(v => new ScriptVersionView((int)Long(v, "version"), Text(v, "name"), (int)Long(v, "lines"), (int)Long(v, "bytes"), Text(v, "savedAt"))).ToList());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    public async Task<string?> VersionSourceAsync(Guid objectId, Guid scriptId, int version)
    {
        var (status, body) = await transport("GET", $"{Base(objectId)}/{scriptId}/versions/{version}", null);
        if (status is < 200 or >= 300) return null;
        try { using var doc = JsonDocument.Parse(body); return Text(doc.RootElement, "source"); } catch (JsonException) { return null; }
    }

    public async Task<ScriptResult> RestoreVersionAsync(Guid objectId, Guid scriptId, int version)
    {
        var (status, body) = await transport("POST", $"{Base(objectId)}/{scriptId}/versions/{version}/restore", "{}");
        return status is >= 200 and < 300 ? new ScriptResult(true, $"Versão {version} restaurada como a versão atual.", scriptId) : new ScriptResult(false, Explain(status, body));
    }
}

public sealed partial class ScriptApi
{
    /// <summary>The owner takes back a permission an avatar gave the script.</summary>
    public async Task<ScriptResult> RevokeAvatarPermissionAsync(Guid objectId, Guid scriptId, Guid subject, string permission)
    {
        var (status, body) = await transport("DELETE", $"{Base(objectId)}/{scriptId}/avatar-permissions/{subject}/{permission}", null);
        return status is >= 200 and < 300 ? new ScriptResult(true, $"{permission} revogada: a ilha deixa de aceitá-la em até 15 segundos.") : new ScriptResult(false, Explain(status, body));
    }
}
