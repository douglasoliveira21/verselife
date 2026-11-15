using LifeVerse.Shared.Items;

namespace LifeVerse.Shared.Interaction;

/// <summary>Who is clicking: the signed-in person, whether they own the parcel they are on or administer the island, and their groups.</summary>
public sealed record ContextActor(Guid UserId, bool IsParcelOwner = false, bool IsAdmin = false, IReadOnlySet<Guid>? Groups = null)
{
    public bool InGroup(Guid? group) => group is { } g && Groups is not null && Groups.Contains(g);
}

/// <summary>What the menu needs to know about an object in the world. Everything here is data the island already sends; the server decides again when an action runs.</summary>
public sealed record ObjectFacts(Guid Owner, Guid Creator, Guid? Group, int OwnerRights, string ClickAction, bool EditLocked, bool IsTemporary = false);

/// <summary>What the menu needs to know about an inventory item.</summary>
public sealed record ItemFacts(Guid Owner, int OwnerRights, string Category, bool InTrash, bool Offered = false);

/// <summary>What the menu knows about the parcel under a point of the ground (data the game already holds for the land overlay).</summary>
public sealed record ParcelFacts(bool Known, string State, bool Mine, bool CanEditTerrain, string BuildMode)
{
    public static ParcelFacts Unknown => new(false, "", false, false, "owner");
}

/// <summary>What the menu knows about another player: their relation to you as the social search reports it ("none", "friend", "pending_out", "pending_in"), empty until it has been looked up.</summary>
public sealed record PersonFacts(string Relation, bool IsSelf = false, bool LocallyHidden = false, bool LocallyMuted = false, bool IsIslandAdmin = false, bool OnParcel = false, bool OnMyParcel = false);

public enum Capability
{
    Touch, Buy, Pay, Open, Inspect, Edit, Take, TakeCopy, Delete, Return, CopyIdentifier,
    ItemPlace, ItemWear, ItemCopy, ItemSend, ItemDelete, ItemRestore, ItemPurge, ItemRights, ItemRename, ItemPreview,
    TerrainEdit, BuyLand, RentLand, ManageLand, BuildHere, AboutLand,
    PersonMessage, PersonAddFriend, PersonAcceptFriend, PersonRemoveFriend, PersonPay, PersonInviteGroup, PersonBlock, PersonReport, PersonHide, PersonMute,
    PersonEjectParcel, PersonBanParcel, PersonKickRegion, PersonTeleportOffer, PersonTeleportRequest,
}

/// <summary>An answer for one action: available, or why not (shown as the tooltip of the grey slice).</summary>
public readonly record struct Decision(bool Allowed, string? Reason = null)
{
    public static Decision Yes => new(true);
    public static Decision No(string reason) => new(false, reason);
}

/// <summary>
/// The one place that says which slices a person may use on a thing, so every menu agrees and the reason a slice is grey is always written down.
/// It only mirrors rules the server enforces (ownership, rights, protection); a slice that is allowed here is still checked again by the server.
/// </summary>
public static class ContextPermissions
{
    private static bool Owns(ContextActor actor, Guid owner) => owner != Guid.Empty && owner == actor.UserId;

    /// <summary>The click action of an object decides what "Interagir" is called and which of Tocar / Comprar / Pagar / Abrir it offers.</summary>
    public static Capability PrimaryAction(string clickAction) => clickAction switch { "buy" => Capability.Buy, "pay" => Capability.Pay, "open" => Capability.Open, _ => Capability.Touch };

    public static string PrimaryLabel(string clickAction) => clickAction switch { "buy" => "Comprar", "pay" => "Pagar", "open" => "Abrir", "sit" => "Sentar", "zoom" => "Aproximar", _ => "Tocar" };

    public static Decision ForObject(Capability capability, ContextActor actor, ObjectFacts facts)
    {
        var owner = Owns(actor, facts.Owner);
        var manages = actor.IsParcelOwner || actor.IsAdmin;
        switch (capability)
        {
            case Capability.Touch or Capability.Buy or Capability.Pay or Capability.Open:
                return facts.ClickAction is "ignore" ? Decision.No("Este objeto não responde a cliques.") : Decision.Yes;
            case Capability.Inspect: case Capability.CopyIdentifier:
                return Decision.Yes;
            case Capability.Edit:
                if (!owner && !actor.IsAdmin) return Decision.No("Você não é o proprietário deste objeto.");
                if (facts.EditLocked) return Decision.No("O objeto está protegido contra edição. Desproteja-o no editor.");
                return ItemRights.Has(facts.OwnerRights, Right.Modify) || actor.IsAdmin ? Decision.Yes : Decision.No("Os direitos deste objeto não permitem modificar.");
            case Capability.Take:
                if (!owner) return Decision.No("Só o proprietário pode pegar o objeto.");
                return facts.EditLocked ? Decision.No("O objeto está protegido contra edição.") : Decision.Yes;
            case Capability.TakeCopy:
                if (!owner) return Decision.No("Só o proprietário pode copiar o objeto.");
                return ItemRights.Has(facts.OwnerRights, Right.Copy) ? Decision.Yes : Decision.No("Os direitos deste objeto não permitem copiar.");
            case Capability.Delete:
                if (!owner && !actor.IsAdmin) return Decision.No("Você não é o proprietário deste objeto.");
                return facts.EditLocked ? Decision.No("O objeto está protegido contra edição.") : Decision.Yes;
            case Capability.Return:
                if (owner) return Decision.No("O objeto já é seu.");
                return manages ? Decision.Yes : Decision.No("Só quem administra a parcela ou a ilha pode devolver objetos de outras pessoas.");
            default:
                return Decision.No("Não se aplica a objetos do mundo.");
        }
    }


    /// <summary>The ground menu: what this person may do on the parcel under the cursor. Without parcel data only "about" works (it selects the cell).</summary>
    public static Decision ForParcel(Capability capability, ContextActor actor, ParcelFacts parcel)
    {
        if (capability == Capability.AboutLand) return Decision.Yes;
        if (!parcel.Known) return Decision.No("Os dados deste terreno ainda não chegaram.");
        switch (capability)
        {
            case Capability.TerrainEdit:
                return parcel.CanEditTerrain ? Decision.Yes : Decision.No("Você não pode editar o terreno aqui.");
            case Capability.BuyLand:
                if (parcel.Mine) return Decision.No("Este terreno já é seu.");
                return parcel.State == "sale" ? Decision.Yes : Decision.No("Este terreno não está à venda.");
            case Capability.RentLand:
                if (parcel.Mine) return Decision.No("Este terreno já é seu.");
                return parcel.State == "rent" ? Decision.Yes : Decision.No("Este terreno não está para alugar.");
            case Capability.ManageLand:
                return parcel.Mine ? Decision.Yes : Decision.No("Só quem é dono ou inquilino do terreno pode gerenciá-lo.");
            case Capability.BuildHere:
                if (parcel.Mine || parcel.BuildMode is "everyone" or "list") return Decision.Yes;      // "list": the server checks the builders' list
                return parcel.BuildMode == "none" ? Decision.No("A construção está desligada neste terreno.") : Decision.No("Só o dono do terreno pode construir aqui.");
            default:
                return Decision.No("Não se aplica ao terreno.");
        }
    }


    /// <summary>The menu of another player. Social actions follow the relation the server reported; hiding and muting are local choices and always available.</summary>
    public static Decision ForPerson(Capability capability, ContextActor actor, PersonFacts person)
    {
        if (person.IsSelf) return Decision.No("Esta é a sua própria conta.");
        switch (capability)
        {
            case Capability.PersonMessage or Capability.PersonPay or Capability.PersonInviteGroup or Capability.PersonBlock or Capability.PersonReport or Capability.PersonHide or Capability.PersonMute or Capability.PersonTeleportOffer or Capability.PersonTeleportRequest:
                if (actor.UserId == Guid.Empty && capability is not (Capability.PersonHide or Capability.PersonMute)) return Decision.No("Entre na sua conta para fazer isso.");
                return Decision.Yes;
            case Capability.PersonEjectParcel or Capability.PersonBanParcel:
                if (actor.UserId == Guid.Empty) return Decision.No("Entre na sua conta para fazer isso.");
                if (!person.OnParcel) return Decision.No("Esta pessoa não está num terreno.");
                return person.OnMyParcel || person.IsIslandAdmin ? Decision.Yes : Decision.No("Só o dono ou inquilino do terreno, ou um administrador da ilha, pode fazer isso aqui.");
            case Capability.PersonKickRegion:
                if (actor.UserId == Guid.Empty) return Decision.No("Entre na sua conta para fazer isso.");
                return person.IsIslandAdmin ? Decision.Yes : Decision.No("Só o proprietário e os administradores da ilha podem expulsar da região.");
            case Capability.PersonAddFriend:
                if (actor.UserId == Guid.Empty) return Decision.No("Entre na sua conta para fazer isso.");
                return person.Relation switch
                {
                    "friend" => Decision.No("Vocês já são amigos."),
                    "pending_out" => Decision.No("O pedido de amizade já foi enviado."),
                    "pending_in" => Decision.No("Esta pessoa já pediu a sua amizade: aceite o pedido."),
                    _ => Decision.Yes
                };
            case Capability.PersonAcceptFriend:
                return person.Relation == "pending_in" ? Decision.Yes : Decision.No("Não há pedido de amizade desta pessoa para aceitar.");
            case Capability.PersonRemoveFriend:
                return person.Relation == "friend" ? Decision.Yes : Decision.No("Vocês não são amigos.");
            default:
                return Decision.No("Não se aplica a outras pessoas.");
        }
    }

    public static Decision ForItem(Capability capability, ContextActor actor, ItemFacts facts)
    {
        if (!Owns(actor, facts.Owner) && facts.Owner != Guid.Empty) return Decision.No("Este item não é seu.");
        if (facts.Offered && capability is not (Capability.ItemRights or Capability.ItemPreview)) return Decision.No("Este item foi oferecido e está esperando a resposta da outra pessoa.");
        var placeable = facts.Category is "models" or "packages";
        var wearable = facts.Category is "clothing" or "wearables" or "hair" or "accessories" or "models";
        switch (capability)
        {
            case Capability.ItemPreview or Capability.ItemRights: return Decision.Yes;
            case Capability.ItemPlace:
                if (facts.InTrash) return Decision.No("Está na lixeira. Restaure primeiro.");
                return placeable ? Decision.Yes : Decision.No("Só modelos e construções podem ser colocados no mundo.");
            case Capability.ItemWear:
                if (facts.InTrash) return Decision.No("Está na lixeira. Restaure primeiro.");
                return wearable ? Decision.Yes : Decision.No("Este tipo de item não pode ser vestido.");
            case Capability.ItemCopy:
                if (facts.InTrash) return Decision.No("Está na lixeira. Restaure primeiro.");
                return ItemRights.Has(facts.OwnerRights, Right.Copy) ? Decision.Yes : Decision.No("Os direitos deste item não permitem copiar.");
            case Capability.ItemSend:
                if (facts.InTrash) return Decision.No("Está na lixeira. Restaure primeiro.");
                return ItemRights.Has(facts.OwnerRights, Right.Transfer) ? Decision.Yes : Decision.No("Os direitos deste item não permitem transferir.");
            case Capability.ItemDelete: return facts.InTrash ? Decision.No("Já está na lixeira.") : Decision.Yes;
            case Capability.ItemRestore: return facts.InTrash ? Decision.Yes : Decision.No("O item não está na lixeira.");
            case Capability.ItemPurge: return facts.InTrash ? Decision.Yes : Decision.No("Só itens da lixeira podem ser excluídos de vez.");
            case Capability.ItemRename: return facts.InTrash ? Decision.No("Está na lixeira. Restaure primeiro.") : Decision.Yes;
            default: return Decision.No("Não se aplica a itens do inventário.");
        }
    }
}
