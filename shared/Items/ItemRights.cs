using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Shared.Items;

/// <summary>What a person may do with an item they hold. Bits, so a set of rights is one small number.</summary>
[Flags]
public enum Right
{
    None = 0,
    /// <summary>Make another copy of it. Without this the item can only be moved (given away or sold), never duplicated.</summary>
    Copy = 1,
    /// <summary>Change it: its parts, textures, scripts, settings.</summary>
    Modify = 2,
    /// <summary>Give it to somebody else (or sell it) and so stop holding it.</summary>
    Transfer = 4,
    /// <summary>Sell it again on the marketplace. Only means something together with Transfer.</summary>
    Resell = 8,
}

/// <summary>
/// The rules about rights as plain functions, shared by the server (which decides) and the game (which only shows). A set of rights is a number:
/// Copy 1, Modify 2, Transfer 4, Resell 8. Resell without Transfer does not exist; a set can only be passed on if the giver holds it.
/// </summary>
public static class ItemRights
{
    public const int None = 0, Copy = (int)Right.Copy, Modify = (int)Right.Modify, Transfer = (int)Right.Transfer, Resell = (int)Right.Resell, All = Copy | Modify | Transfer | Resell;
    /// <summary>Copy, modify and transfer, without reselling: what a buyer normally gets of a product that is not made for resale.</summary>
    public const int Standard = Copy | Modify | Transfer;

    private static readonly (Right Right, string Key, string Name, char Letter)[] Table =
    [
        (Right.Copy, "copy", "Copiar", 'C'), (Right.Modify, "modify", "Modificar", 'M'), (Right.Transfer, "transfer", "Transferir", 'T'), (Right.Resell, "resell", "Revender", 'R'),
    ];

    /// <summary>Only the four known bits, and no Resell without Transfer.</summary>
    public static int Normalize(int mask)
    {
        mask &= All;
        return (mask & Transfer) == 0 ? mask & ~Resell : mask;
    }

    public static bool Has(int mask, Right right) => right != Right.None && (Normalize(mask) & (int)right) == (int)right;

    /// <summary>An item that cannot be copied can only move from one holder to another: whoever receives it, the giver no longer has it.</summary>
    public static bool IsCopyable(int mask) => Has(mask, Right.Copy);

    /// <summary>A set cannot give more than it holds: what is asked for, limited to the ceiling.</summary>
    public static int Clamp(int requested, int ceiling) => Normalize(requested) & Normalize(ceiling);

    /// <summary>Whether the set is inside the ceiling (nothing asked for beyond what is held).</summary>
    public static bool IsWithin(int mask, int ceiling) => (Normalize(mask) & ~Normalize(ceiling)) == 0;

    /// <summary>
    /// The rights of something made of parts (an object with children, a group of objects, a package): only what every part allows. One part that cannot be
    /// copied makes the whole thing impossible to copy. With no parts there is nothing to limit it.
    /// </summary>
    public static int Intersect(IEnumerable<int> parts)
    {
        var result = All;
        foreach (var part in parts) result &= Normalize(part);
        return Normalize(result);
    }

    /// <summary>"Copiar · Modificar" for people; "Nenhum direito" for an empty set.</summary>
    public static string Describe(int mask)
    {
        mask = Normalize(mask); var names = Table.Where(t => (mask & (int)t.Right) != 0).Select(t => t.Name).ToArray();
        return names.Length == 0 ? "Nenhum direito" : string.Join(" · ", names);
    }

    /// <summary>"CMT-" : one letter per right, a dash where it is missing (for lists and tags).</summary>
    public static string Letters(int mask)
    {
        mask = Normalize(mask);
        return new string([.. Table.Select(t => (mask & (int)t.Right) != 0 ? t.Letter : '-')]);
    }

    /// <summary>Reads {"copy":true,"modify":false,…}; anything missing is off. Unreadable text gives the fallback.</summary>
    public static int FromJson(string? json, int fallback = None)
    {
        if (string.IsNullOrWhiteSpace(json)) return Normalize(fallback);
        try
        {
            using var document = JsonDocument.Parse(json); if (document.RootElement.ValueKind != JsonValueKind.Object) return Normalize(fallback);
            var mask = 0; foreach (var t in Table) if (document.RootElement.TryGetProperty(t.Key, out var value) && value.ValueKind == JsonValueKind.True) mask |= (int)t.Right;
            return Normalize(mask);
        }
        catch (JsonException) { return Normalize(fallback); }
    }

    public static string ToJson(int mask)
    {
        mask = Normalize(mask); var o = new JsonObject();
        foreach (var t in Table) o[t.Key] = (mask & (int)t.Right) != 0;
        return o.ToJsonString();
    }

    /// <summary>Updates only the rights that a stored JSON object names, keeping its other keys (an object's JSON also holds use, move, delete…).</summary>
    public static string MergeIntoJson(string? existing, int mask)
    {
        JsonObject o; try { o = JsonNode.Parse(string.IsNullOrWhiteSpace(existing) ? "{}" : existing) as JsonObject ?? []; } catch (JsonException) { o = []; }
        mask = Normalize(mask); foreach (var t in Table) o[t.Key] = (mask & (int)t.Right) != 0;
        return o.ToJsonString();
    }
}

/// <summary>
/// The three sets of rights an item carries: for whoever owns it now, for the members of the group it is shared with, and for whoever gets it next
/// (the buyer, the person it is given to). Each set is inside the one above it: the group and the next owner cannot get what the owner does not have.
/// </summary>
public sealed record ItemPermissions(int Owner, int Group, int NextOwner)
{
    /// <summary>Everything for the owner, nothing for a group, everything for the next owner: the starting point of what a person makes themself.</summary>
    public static ItemPermissions Creator { get; } = new(ItemRights.All, ItemRights.None, ItemRights.All);

    /// <summary>The same permissions with every set cleaned and cut to fit inside the owner's.</summary>
    public ItemPermissions Normalized()
    {
        var owner = ItemRights.Normalize(Owner);
        return new ItemPermissions(owner, ItemRights.Clamp(Group, owner), ItemRights.Clamp(NextOwner, owner));
    }

    /// <summary>Whether every set is clean and inside the owner's (what the server accepts as written).</summary>
    public bool IsValid => this == Normalized() && Owner == ItemRights.Normalize(Owner);

    /// <summary>What the item looks like the moment it arrives at the next owner: they hold the "next owner" rights, the group sharing is gone, and what they pass on is no more than they hold.</summary>
    public ItemPermissions ForNextOwner()
    {
        var holds = ItemRights.Normalize(NextOwner);
        return new ItemPermissions(holds, ItemRights.None, holds);
    }

    /// <summary>The rights a viewer has: the owner's set for the owner, the group's set for a member of the item's group, nothing for anyone else.</summary>
    public int For(bool isOwner, bool isGroupMember) => isOwner ? ItemRights.Normalize(Owner) : isGroupMember ? ItemRights.Normalize(Group) : ItemRights.None;

    /// <summary>The permissions of something made of parts: for each of the three sets, only what every part allows.</summary>
    public static ItemPermissions Compose(IEnumerable<ItemPermissions> parts)
    {
        var list = parts.Select(p => p.Normalized()).ToList();
        return list.Count == 0 ? Creator : new ItemPermissions(ItemRights.Intersect(list.Select(p => p.Owner)), ItemRights.Intersect(list.Select(p => p.Group)), ItemRights.Intersect(list.Select(p => p.NextOwner))).Normalized();
    }
}

/// <summary>What kind of thing is inside an item. Scripts, animations, textures and models are more sensitive than a plain shape: the rules follow the kind.</summary>
public enum ContentKind { Other, Script, Animation, Texture, Model, Material, Sound }

/// <summary>The extra rules of each kind of content: what it takes to read its source, to take it out as a file, and to put it inside something that can be sold.</summary>
public sealed record ContentRule(Right ReadSource, Right Export, Right Embed);

public static class ContentRules
{
    /// <summary>
    /// A script's source can only be read or edited by someone who may modify it (otherwise it runs but stays closed). Models, textures and animations can
    /// only be taken out as a file by someone who may copy them. Putting any of them inside an object that may be given away needs Transfer, because the
    /// content goes along with the object.
    /// </summary>
    public static ContentRule For(ContentKind kind) => kind switch
    {
        ContentKind.Script => new(Right.Modify, Right.Modify, Right.Transfer),
        ContentKind.Animation or ContentKind.Texture or ContentKind.Model or ContentKind.Material or ContentKind.Sound => new(Right.None, Right.Copy, Right.Transfer),
        _ => new(Right.None, Right.None, Right.None),
    };

    /// <summary>The kind of content for an asset category as the inventory names it ("script", "mesh", "texture", "animation"…).</summary>
    public static ContentKind FromCategory(string? category) => (category ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "script" or "lua" or "scripts" => ContentKind.Script,
        "animation" or "anim" or "animations" => ContentKind.Animation,
        "texture" or "image" or "textures" => ContentKind.Texture,
        "model" or "mesh" or "glb" or "models" => ContentKind.Model,
        "material" or "materials" => ContentKind.Material,
        "sound" or "audio" or "music" => ContentKind.Sound,
        _ => ContentKind.Other,
    };

    public static bool CanReadSource(ContentKind kind, int held) => For(kind).ReadSource == Right.None || ItemRights.Has(held, For(kind).ReadSource);
    public static bool CanExport(ContentKind kind, int held) => For(kind).Export == Right.None || ItemRights.Has(held, For(kind).Export);
    public static bool CanEmbed(ContentKind kind, int held) => For(kind).Embed == Right.None || ItemRights.Has(held, For(kind).Embed);
}
