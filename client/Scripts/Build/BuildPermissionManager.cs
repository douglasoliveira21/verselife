using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Build;

/// <summary>
/// Who may do what with what an object carries. The server decides in the end; this answers first so the tab never offers a button that would be refused. An item put into an object
/// is only a reference: it keeps the rights it has in the inventory, so being inside an object never widens them.
/// </summary>
public static class BuildPermissionManager
{
    /// <summary>The contents of an object can only change when the object may be modified.</summary>
    public static bool CanEditContents(BuildObjectState state) => ItemRights.Has(state.OwnerRights, Right.Modify);

    /// <summary>The rights an item has inside an object: exactly those it has in the inventory (null when they are not known here).</summary>
    public static int? RightsOf(InventoryPanel? inventory, Guid asset) => inventory?.OwnerRightsOfAsset(asset);

    /// <summary>"Copiar · Modificar · Transferir", or what is missing, for the Permissões of an item.</summary>
    public static string Describe(int? rights)
    {
        if (rights is null) return "Permissões desconhecidas (o item não está no seu inventário).";
        var parts = new List<string>();
        if (ItemRights.Has(rights.Value, Right.Copy)) parts.Add("copiar");
        if (ItemRights.Has(rights.Value, Right.Modify)) parts.Add("modificar");
        if (ItemRights.Has(rights.Value, Right.Transfer)) parts.Add("transferir");
        return parts.Count == 0 ? "Sem permissões: só pode ficar neste objeto." : "Pode " + string.Join(", ", parts) + ".";
    }

    /// <summary>Reasons an item cannot be put into an object (null when it can): the person must hold it and the object must be modifiable.</summary>
    public static string? CannotInsert(BuildObjectState state, bool inInventory)
    {
        if (!CanEditContents(state)) return "Este objeto não pode ser modificado: o conteúdo dele não muda.";
        if (!inInventory) return "Só itens do seu inventário podem entrar no objeto.";
        return null;
    }

    /// <summary>Copying an item out of an object needs the Copy right on it; without it the item can only be moved.</summary>
    public static bool CanCopy(int? rights) => rights is { } r && ItemRights.Has(r, Right.Copy);
}
