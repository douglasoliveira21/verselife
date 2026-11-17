using System.Security.Cryptography;
using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The bodies that come with the game: a full 3D body (skeleton, skin and first clothes) shipped as a file, drawn the same way as a body a person uploads, but without the server: every client already has it, so nothing is
/// downloaded and everybody sees it. A character asks for one with its base model (<see cref="AvatarRules.DefaultMan"/> is the man a new character starts with).
/// </summary>
public static class BuiltInBodies
{
    /// <summary>The file is a glTF binary with the extension of a plain data file, so that the engine keeps it as it is (and the export includes it).</summary>
    public const string ManPath = "res://Assets/avatars/lifeverse_man.bin";
    private static byte[]? _man; private static string? _manHash;

    /// <summary>The model id and the hash a body of this base model is asked for with, or false when the base model is drawn by the game itself (the standard bodies).</summary>
    public static bool TryFor(string baseModel, out Guid id, out string hash)
    {
        id = Guid.Empty; hash = string.Empty;
        if (baseModel != AvatarRules.DefaultMan || ManBytes() is not { } bytes) return false;
        id = AvatarRules.DefaultManModelId; hash = _manHash ??= Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); return true;
    }

    /// <summary>The file of a built-in body by its id; null for any other id.</summary>
    public static byte[]? Bytes(Guid id) => id == AvatarRules.DefaultManModelId ? ManBytes() : null;

    private static byte[]? ManBytes()
    {
        if (_man is not null) return _man;
        if (!Godot.FileAccess.FileExists(ManPath)) return null;
        _man = Godot.FileAccess.GetFileAsBytes(ManPath); return _man.Length > 0 ? _man : (_man = null);
    }

    /// <summary>The materials of the clothes and the hair of the body, named so that the game can colour them with the person\x27s choices.</summary>
    public const string TopMaterial = "LV_Top", BottomMaterial = "LV_Bottom", ShoesMaterial = "LV_Shoes", HairMaterial = "LV_Hair";
}

public sealed partial class AvatarRig
{
    /// <summary>The clothes and the hair of a body that comes with the game take the colours the person chose (their materials are named for it); any other body is left as its maker made it.</summary>
    private void TintBuiltInOutfit(Node scene)
    {
        if (_lastData is not { } data) return;
        var colours = new Dictionary<string, Color>
        {
            [BuiltInBodies.TopMaterial] = Color.FromHtml(data.Tint("top", "#3AAFC1")), [BuiltInBodies.BottomMaterial] = Color.FromHtml(data.Tint("bottom", "#263B56")),
            [BuiltInBodies.ShoesMaterial] = Color.FromHtml(data.Tint("shoes", "#E7E9EE")), [BuiltInBodies.HairMaterial] = Color.FromHtml(data.HairColor)
        };
        foreach (var mesh in Meshes(scene))
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
                if (mesh.GetActiveMaterial(surface) is BaseMaterial3D material && colours.TryGetValue(material.ResourceName, out var colour))
                    mesh.SetSurfaceOverrideMaterial(surface, (BaseMaterial3D)material.Duplicate() is var own ? Tinted(own, colour) : material);
    }

    private static BaseMaterial3D Tinted(BaseMaterial3D material, Color colour) { material.AlbedoColor = colour; return material; }
}
