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

    /// <summary>Whether this file hash is the one of a body that comes with the game.</summary>
    public static bool IsBuiltIn(string hash) => _manHash is not null && string.Equals(hash, _manHash, StringComparison.OrdinalIgnoreCase);

    private static Node? _manPrototype;

    /// <summary>A new copy of the scene of the man. The file is read once; every copy is made from that scene and shares its meshes, materials and pictures (nothing of the copy is changed in place by the game when it is drawn in full).</summary>
    public static Node? NewManScene()
    {
        if (_manPrototype is null || !GodotObject.IsInstanceValid(_manPrototype))
        {
            if (ManBytes() is not { } bytes) return null;
            var document = new GltfDocument(); var state = new GltfState();
            if (document.AppendFromBuffer(bytes, string.Empty, state) != Error.Ok) return null;
            _manPrototype = document.GenerateScene(state);
        }
        return _manPrototype?.Duplicate();
    }

    /// <summary>The colour the skin of the man has in his pictures, found once (the middle of the colour map of his body): a skin colour chosen for the character is turned into a tint relative to it.</summary>
    public static Color SkinReference(Texture2D? colourMap)
    {
        if (_skinReference is { } known) return known;
        if (colourMap?.GetImage() is not { } image) return new Color("#C99B80");
        var copy = (Image)image.Duplicate(); if (copy.IsCompressed()) copy.Decompress(); copy.Resize(1, 1, Image.Interpolation.Lanczos); return (_skinReference = copy.GetPixel(0, 0)).Value;
    }
    private static Color? _skinReference;

    /// <summary>The colour the character editor starts with: it leaves the man's skin as it was made (any other choice tints it).</summary>
    public const string DefaultSkinChoice = "#C98F65";
}

public sealed partial class AvatarRig
{
    private static readonly string[] SkinMaterials = ["Ga_Skin_Head", "Ga_Skin_Body", "Ga_Skin_Arm", "Ga_Skin_Leg", "Ga_Nails"];

    /// <summary>The skin colour of the character tints the skin of the man (his skin is a picture, so the choice multiplies it). Each avatar gets its own copy of the skin materials; the pictures stay shared.</summary>
    private void TintBuiltInSkin(Node scene)
    {
        if (_lastData is not { } data || string.Equals(data.SkinColor, BuiltInBodies.DefaultSkinChoice, StringComparison.OrdinalIgnoreCase) || !Color.HtmlIsValid(data.SkinColor)) return;
        var chosen = Color.FromHtml(data.SkinColor); Color? reference = null;
        foreach (var mesh in Meshes(scene))
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
                if (mesh.GetActiveMaterial(surface) is BaseMaterial3D material && Array.IndexOf(SkinMaterials, material.ResourceName) >= 0)
                {
                    reference ??= BuiltInBodies.SkinReference(material.AlbedoTexture);
                    var own = (BaseMaterial3D)material.Duplicate();
                    own.AlbedoColor = new Color(Mathf.Clamp(chosen.R / Mathf.Max(.05f, reference.Value.R), .3f, 1.7f), Mathf.Clamp(chosen.G / Mathf.Max(.05f, reference.Value.G), .3f, 1.7f), Mathf.Clamp(chosen.B / Mathf.Max(.05f, reference.Value.B), .3f, 1.7f));
                    mesh.SetSurfaceOverrideMaterial(surface, own);
                }
    }
}
