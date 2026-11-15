using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Interaction;

namespace LifeVerse.Client.Scripts;

/// What a right click landed on.
public abstract record ContextTarget(string Title, string Icon);
public sealed record SelfTarget(string Name) : ContextTarget(Name, "user");
public sealed record AvatarTarget(int Peer, string Name, Vector3 Position) : ContextTarget(Name, "user");
public sealed record ObjectTarget(BuildObjectView View) : ContextTarget(string.IsNullOrWhiteSpace(View.State.Name) ? "Objeto" : View.State.Name, "cube");
public sealed record SeatTarget(InteractableSeat Seat) : ContextTarget("Assento", "user");
public sealed record GroundTarget(Vector3 Point) : ContextTarget("Terreno", "mountain");
public sealed record VehicleTarget(VehicleView View) : ContextTarget(string.IsNullOrWhiteSpace(View.DisplayName) ? "Veículo" : View.DisplayName, "run");
/// An item of the inventory (right click on its card). Rights are the bits of <see cref="LifeVerse.Shared.Items.ItemRights"/>.
public sealed record InventoryItemTarget(Guid ItemId, string Name, string Category, int Rights, bool InTrash = false, bool Offered = false) : ContextTarget(Name, "bag");
/// A folder of the inventory tree.
public sealed record InventoryFolderTarget(Guid FolderId, string Name) : ContextTarget(Name, "folder-filled");
/// A panel a script of an object shows on this player's screen.
public sealed record HudTarget(Guid Script, string Name) : ContextTarget(string.IsNullOrWhiteSpace(Name) ? "Painel" : Name, "layers");

/// What the menu can do, handed in by the window that owns each thing (so this file depends on none of them).
public sealed class ContextServices
{
    public Action ToggleInventory { get; init; } = () => { };
    public Action ToggleGestures { get; init; } = () => { };
    public Action TogglePeople { get; init; } = () => { };
    public Action OpenSettings { get; init; } = () => { };
    public Action<string> SearchPerson { get; init; } = _ => { };
    public Action<string> OpenProfile { get; init; } = _ => { };
    public Action OpenMyProfile { get; init; } = () => { };
    public Action<string> AddFriend { get; init; } = _ => { };
    public Action<string> Message { get; init; } = _ => { };
    public Action<string> InviteToGroup { get; init; } = _ => { };
    public Action<string> Block { get; init; } = _ => { };
    public Action<string> Report { get; init; } = _ => { };
    /// The relation to a person as last reported by the server (empty until looked up), and the actions that depend on it.
    public Func<string, string> Relation { get; init; } = _ => "";
    public Action<string> Pay { get; init; } = _ => { };
    /// A teleport between two people on the same island: kind is "offer" (take them to me) or "request" (take me to them).
    public Action<string, string> Teleport { get; init; } = (_, _) => { };
    public Action<string> RemoveFriend { get; init; } = _ => { };
    public Action<string> AcceptFriend { get; init; } = _ => { };
    public Action<string> CopyPersonId { get; init; } = _ => { };
    public Action<string> Prefetch { get; init; } = _ => { };
    /// What this player chose to hide or silence on their own screen (by the peer number of the avatar).
    public Func<int, bool> IsHidden { get; init; } = _ => false;
    public Func<int, bool> IsMuted { get; init; } = _ => false;
    /// Whether this person owns or administers the island (asked of the server), and the moderation calls that follow.
    public Func<bool> IsIslandAdmin { get; init; } = () => false;
    public Action<string, Vector3> EjectFromParcel { get; init; } = (_, _) => { };
    public Action<string, Vector3, int?> BanFromParcel { get; init; } = (_, _, _) => { };
    public Action<string> KickFromRegion { get; init; } = _ => { };
    public Action<int> ToggleHidden { get; init; } = _ => { };
    public Action<int> ToggleMuted { get; init; } = _ => { };
    public Action<string> Tell { get; init; } = _ => { };
    public Action<Vector3> WalkTo { get; init; } = _ => { };
    public Action<string> Gesture { get; init; } = _ => { };
    public Action<InteractableSeat> Sit { get; init; } = _ => { };
    public Action<Guid> Touch { get; init; } = _ => { };
    public Action<Guid, LifeVerse.Client.Build.BuildGizmoMode?> EditObject { get; init; } = (_, _) => { };
    public Action<Guid, string> ObjectCommand { get; init; } = (_, _) => { };
    public Action<Guid> SelectObject { get; init; } = _ => { };
    public Action<Guid> DeleteObject { get; init; } = _ => { };
    /// Puts an object of the person in the inventory (second argument: also remove it from the world).
    public Action<Guid, string, bool> TakeObject { get; init; } = (_, _, _) => { };
    public Action StartBuilding { get; init; } = () => { };
    public Action StartPlacing { get; init; } = () => { };
    public Action<Vector3> AboutLand { get; init; } = _ => { };
    public Action SelectLand { get; init; } = () => { };
    /// What the game knows about the parcel under a point of the ground.
    public Func<Vector3, ParcelFacts> Parcel { get; init; } = _ => ParcelFacts.Unknown;
    public Action EditTerrain { get; init; } = () => { };
    public Action ManageLand { get; init; } = () => { };
    public Action<Vector3> ParcelOwner { get; init; } = _ => { };
    /// Whether this person owns an object (only then are the editing slices shown; the server checks again whatever the screen says).
    public Func<BuildObjectView, bool> Owns { get; init; } = _ => false;
    /// Who is clicking: used by the permission resolver to decide which slices are available.
    public Func<ContextActor> Actor { get; init; } = () => new ContextActor(Guid.Empty);
    public Action<int> EnterVehicle { get; init; } = _ => { };
    public Action ExitVehicle { get; init; } = () => { };
    /// Whether this person is the one driving that vehicle right now.
    public Func<int, bool> IsDriving { get; init; } = _ => false;
    /// Runs an action of the inventory's own menu (the number it uses) on the selected item or folder.
    public Action<int> InventoryAction { get; init; } = _ => { };
    /// Closes a panel a script put on the screen.
    public Action<Guid> CloseHud { get; init; } = _ => { };
}

/// Which slices each kind of target gets. Only things that really work are offered. Whether a slice is available comes from the shared
/// <see cref="ContextPermissions"/>: a slice that is not allowed stays in place, grey, with the reason as its tip (nothing is hidden by surprise).
public static class InteractionRegistry
{
    public const int InvNew = 11, InvRename = 12, InvDeleteFolder = 2, InvPlace = 3, InvEquip = 4, InvFavorite = 5, InvRestore = 6, InvRights = 8, InvOffer = 9, InvCopy = 10, InvDelete = 13, InvPurge = 14, InvPreview = 16, InvVersions = 21;

    public static List<RadialItem> Build(ContextTarget target, ContextServices s) => target switch
    {
        SelfTarget => ForSelf(s),
        AvatarTarget avatar => ForAvatar(avatar, s),
        ObjectTarget @object => ForObject(@object.View, s),
        SeatTarget seat => [new RadialItem { Label = "Sentar", Icon = "user", Run = () => s.Sit(seat.Seat), Tooltip = "Senta neste assento" }],
        GroundTarget ground => ForGround(ground, s),
        VehicleTarget car => ForVehicle(car.View, s),
        InventoryItemTarget item => ForItem(item, s),
        InventoryFolderTarget folder => ForFolder(folder, s),
        HudTarget hud => ForHud(hud, s),
        _ => []
    };

    /// A slice whose availability comes from a decision: when not allowed it is grey and says why.
    private static RadialItem Slice(string label, string icon, Decision decision, Action? run, string? tip = null, bool destructive = false, string? confirm = null, List<RadialItem>? children = null, bool side = false) => new()
    {
        Label = label, Icon = icon, Enabled = decision.Allowed, Run = run, Children = children, SidePanel = side, Destructive = destructive, Confirm = confirm,
        Tooltip = decision.Allowed ? tip : decision.Reason
    };

    private static List<RadialItem> Gestures(ContextServices s) =>
    [
        new() { Label = "Acenar", Icon = "hand", Run = () => s.Gesture("wave"), Tooltip = "Acena para quem está por perto" },
        new() { Label = "Dançar", Icon = "volume", Run = () => s.Gesture("dance"), Tooltip = "Dança por alguns segundos" },
        new() { Label = "Mais gestos", Icon = "dots", Run = s.ToggleGestures, Tooltip = "Abre a janela de gestos" }
    ];

    private static List<RadialItem> ForSelf(ContextServices s) =>
    [
        new() { Label = "Meu perfil", Icon = "user", Run = s.OpenMyProfile, Tooltip = "Foto, biografia, aniversário, grupos e casamento" },
        new() { Label = "Inventário", Icon = "bag", Run = s.ToggleInventory, Tooltip = "Abre o seu inventário" },
        new() { Label = "Animações", Icon = "hand", Children = Gestures(s), SidePanel = true, Tooltip = "Acenar, dançar e outros gestos" },
        new() { Label = "Pessoas e grupos", Icon = "people", Run = s.TogglePeople, Tooltip = "Amigos, grupos e busca de pessoas" },
        new() { Label = "Construir", Icon = "hammer", Run = s.StartBuilding, Tooltip = "Abre as ferramentas de construção" },
        new() { Label = "Configurações", Icon = "settings", Run = s.OpenSettings }
    ];

    private static List<RadialItem> ForAvatar(AvatarTarget avatar, ContextServices s)
    {
        var actor = s.Actor(); var relation = s.Relation(avatar.Name); var hidden = s.IsHidden(avatar.Peer); var muted = s.IsMuted(avatar.Peer);
        var parcel = s.Parcel(avatar.Position); var onParcel = parcel.Known && parcel.State is not ("free" or "public");
        var facts = new PersonFacts(relation, false, hidden, muted, s.IsIslandAdmin(), onParcel, parcel.Known && parcel.Mine);
        Decision D(Capability c) => ContextPermissions.ForPerson(c, actor, facts);
        var name = avatar.Name;

        var friendship = new List<RadialItem>();
        if (relation == "pending_in") friendship.Add(Slice("Aceitar pedido", "check", D(Capability.PersonAcceptFriend), () => s.AcceptFriend(name), "Aceita o pedido de amizade desta pessoa"));
        else if (relation == "friend") friendship.Add(Slice("Remover amizade", "user-add", D(Capability.PersonRemoveFriend), () => s.RemoveFriend(name), "Desfaz a amizade", destructive: true, confirm: $"Desfazer a amizade com {name}?"));
        else friendship.Add(Slice("Adicionar amigo", "user-add", D(Capability.PersonAddFriend), () => s.AddFriend(name), "Envia um pedido de amizade"));
        friendship.Add(new RadialItem { Label = "Ver na lista", Icon = "search", Run = () => s.SearchPerson(name), Tooltip = "Abre a janela Pessoas com este nome (permissões e detalhes da amizade)" });

        List<RadialItem> moderation =
        [
            Slice("Bloquear", "ban", D(Capability.PersonBlock), () => s.Block(name), "Impede que esta pessoa fale com você (vale na conta, em todos os aparelhos)", destructive: true, confirm: $"Bloquear {name}?"),
            Slice(muted ? "Reativar voz" : "Silenciar voz", muted ? "volume" : "mic-off", D(Capability.PersonMute), () => s.ToggleMuted(avatar.Peer), muted ? "Volta a ouvir esta pessoa" : "Para de ouvir a voz desta pessoa só neste aparelho, até você sair da ilha"),
            Slice(hidden ? "Mostrar avatar" : "Ocultar avatar", "eye", D(Capability.PersonHide), () => s.ToggleHidden(avatar.Peer), hidden ? "Volta a ver este avatar" : "Esconde este avatar só na sua tela, até você sair da ilha"),
            Slice("Denunciar", "alert", D(Capability.PersonReport), () => s.Report(name), "Conta à moderação o que aconteceu", destructive: true)
        ];
        // the tools of whoever owns the parcel or administers the island: only shown to someone who may use at least one of them
        var eject = D(Capability.PersonEjectParcel); var ban = D(Capability.PersonBanParcel); var kick = D(Capability.PersonKickRegion);
        if (eject.Allowed || ban.Allowed || kick.Allowed)
        {
            List<RadialItem> lengths = [.. new (string Label, int? Minutes)[] { ("1 hora", 60), ("1 dia", 1440), ("7 dias", 10080), ("Para sempre", null) }.Select(x => new RadialItem
            {
                Label = x.Label, Icon = "ban", Destructive = true, Confirm = $"Banir {name} do terreno ({x.Label.ToLowerInvariant()})?", Run = () => s.BanFromParcel(name, avatar.Position, x.Minutes), Tooltip = "A pessoa é tirada do terreno na hora e não consegue voltar enquanto durar o banimento"
            })];
            moderation.Add(Slice("Expulsar do terreno", "run", eject, () => s.EjectFromParcel(name, avatar.Position), "Tira a pessoa deste terreno por 1 minuto", destructive: true, confirm: $"Expulsar {name} do terreno?"));
            moderation.Add(Slice("Banir do terreno", "ban", ban, null, "Mantém a pessoa fora deste terreno por um tempo", destructive: true, children: lengths));
            moderation.Add(Slice("Expulsar da ilha", "power", kick, () => s.KickFromRegion(name), "Tira a pessoa da ilha; ela pode voltar (não é banimento)", destructive: true, confirm: $"Expulsar {name} da ilha?"));
        }
        List<RadialItem> more =
        [
            new() { Label = "Ir até lá", Icon = "run", Run = () => s.WalkTo(avatar.Position), Tooltip = "Caminha até esta pessoa" },
            new() { Label = "Copiar nome", Icon = "copy", Run = () => { DisplayServer.ClipboardSet(name); s.Tell("Nome copiado."); } },
            Slice("Copiar identificador", "link", D(Capability.PersonMessage), () => s.CopyPersonId(name), "Copia o identificador da conta desta pessoa"),
            new() { Label = "Buscar na lista", Icon = "search", Run = () => s.SearchPerson(name), Tooltip = "Abre a janela Pessoas com este nome" }
        ];
        return
        [
            new() { Label = "Perfil", Icon = "user", Run = () => s.OpenProfile(name), Tooltip = "Vê o perfil desta pessoa" },
            Slice("Mensagem", "chat", D(Capability.PersonMessage), () => s.Message(name), "Escreve uma mensagem privada"),
            Slice("Amizade", "user-add", Decision.Yes, null, relation == "friend" ? "Vocês são amigos" : "Adicionar ou remover amizade", children: friendship, side: true),
            Slice("Pagar", "coin", D(Capability.PersonPay), () => s.Pay(name), "Envia L$ a esta pessoa (você confirma o valor antes)"),
            new() { Label = "Interagir", Icon = "hand", Children = Gestures(s), SidePanel = true, Tooltip = "Gestos para quem está por perto" },
            new() { Label = "Grupos", Icon = "people", SidePanel = true, Tooltip = "Convidar para um grupo ou ver os grupos públicos", Children =
            [
                Slice("Convidar para grupo", "user-add", D(Capability.PersonInviteGroup), () => s.InviteToGroup(name), "Convida esta pessoa para um dos seus grupos"),
                new() { Label = "Ver grupos públicos", Icon = "list", Run = () => s.OpenProfile(name), Tooltip = "Os grupos públicos aparecem no perfil" }
            ] },
            Slice("Teleporte", "map", Decision.Yes, null, "Convidar a pessoa para onde você está, ou pedir para ir até ela", side: true, children:
            [
                Slice("Oferecer teleporte", "map", D(Capability.PersonTeleportOffer), () => s.Teleport(name, "offer"), "A pessoa decide se aceita; se aceitar, é levada até onde você está"),
                Slice("Pedir teleporte", "run", D(Capability.PersonTeleportRequest), () => s.Teleport(name, "request"), "Se a pessoa aceitar, você é levado até onde ela está")
            ]),
            Slice("Moderação", "shield", Decision.Yes, null, "Bloquear, silenciar, ocultar ou denunciar", children: moderation, side: true),
            Slice("Mais opções", "dots", Decision.Yes, null, "Ir até a pessoa, copiar nome e identificador", children: more, side: true)
        ];
    }

    private static List<RadialItem> ForObject(BuildObjectView view, ContextServices s)
    {
        var id = view.State.Id; var state = view.State; var actor = s.Actor();
        // an old caller that only knows "do I own it" keeps working: the owner flag overrides the resolver's identity check
        if (actor.UserId == Guid.Empty && s.Owns(view)) actor = new ContextActor(state.OwnerUserId);
        var facts = new ObjectFacts(state.OwnerUserId, state.CreatorUserId, state.GroupId, state.OwnerRights, state.ClickAction, state.EditLocked, state.ExpiresAt is not null);
        Decision D(Capability c) => ContextPermissions.ForObject(c, actor, facts);
        var primary = ContextPermissions.PrimaryAction(state.ClickAction);

        var edit = Slice("Editar", "hammer", D(Capability.Edit), null, "Mover, girar, esticar, duplicar ou excluir", children:
        [
            new() { Label = "Mover", Icon = "move", Run = () => s.EditObject(id, LifeVerse.Client.Build.BuildGizmoMode.Move) },
            new() { Label = "Girar", Icon = "rotate", Run = () => s.EditObject(id, LifeVerse.Client.Build.BuildGizmoMode.Rotate) },
            new() { Label = "Esticar", Icon = "expand", Run = () => s.EditObject(id, LifeVerse.Client.Build.BuildGizmoMode.Scale) },
            new() { Label = "Textura", Icon = "image", Run = () => s.EditObject(id, null), Tooltip = "Abre o editor com o objeto selecionado (aba Textura)" },
            new() { Label = "Duplicar", Icon = "copy-plus", Run = () => { s.EditObject(id, null); s.ObjectCommand(id, "duplicate"); } },
            new() { Label = "Excluir", Icon = "trash", Destructive = true, Confirm = "Excluir o objeto?", Run = () => { s.EditObject(id, null); s.ObjectCommand(id, "delete"); }, Tooltip = "Exclui o objeto (Ctrl+Z desfaz)" }
        ]);

        List<RadialItem> more =
        [
            new() { Label = "Copiar identificador", Icon = "link", Run = () => { DisplayServer.ClipboardSet(id.ToString()); s.Tell("Identificador copiado."); } },
            new() { Label = "Copiar posição", Icon = "pin", Run = () => { DisplayServer.ClipboardSet($"{view.GlobalPosition.X:0.##}, {view.GlobalPosition.Y:0.##}, {view.GlobalPosition.Z:0.##}"); s.Tell("Posição copiada."); } },
            new() { Label = "Copiar tamanho", Icon = "ruler", Run = () => { DisplayServer.ClipboardSet($"{view.Scale.X:0.##}, {view.Scale.Y:0.##}, {view.Scale.Z:0.##}"); s.Tell("Tamanho copiado."); } },
            Slice("Selecionar no editor", "pointer", Decision.Yes, () => s.SelectObject(id), "Seleciona o objeto sem abrir as ferramentas"),
            Slice("Excluir", "trash", D(Capability.Delete), () => s.DeleteObject(id), "Exclui o objeto (Ctrl+Z desfaz)", destructive: true, confirm: "Excluir o objeto?")
        ];
        return
        [
            Slice(ContextPermissions.PrimaryLabel(state.ClickAction), primary is Capability.Buy or Capability.Pay ? "coin" : primary == Capability.Open ? "folder-open" : "hand", D(primary), () => s.Touch(id), "Toca o objeto (ativa o script dele, se tiver)"),
            Slice("Inspecionar", "search", D(Capability.Inspect), () => s.Tell(Describe(view, actor)), "Mostra os dados do objeto"),
            edit,
            Slice("Pegar", "bag", D(Capability.Take), () => s.TakeObject(id, state.Name, true), "Guarda no inventário e tira do mundo (Ctrl+Z traz de volta)"),
            Slice("Pegar cópia", "copy-plus", D(Capability.TakeCopy), () => s.TakeObject(id, state.Name, false), "Guarda uma cópia no inventário e deixa o objeto onde está"),
            Slice("Mais opções", "dots", Decision.Yes, null, "Copiar dados, selecionar ou excluir", children: more, side: true)
        ];
    }

    /// The text "Inspecionar" shows: what the object is and what the person may do with it (the server still decides).
    public static string Describe(BuildObjectView view, ContextActor actor)
    {
        var state = view.State; var mine = state.OwnerUserId != Guid.Empty && state.OwnerUserId == actor.UserId;
        var parts = new List<string>
        {
            $"{(string.IsNullOrWhiteSpace(state.Name) ? "Objeto" : state.Name)}: {state.PrimitiveType}, {view.Scale.X:0.##} × {view.Scale.Y:0.##} × {view.Scale.Z:0.##} m",
            mine ? "é seu" : "é de outra pessoa",
            $"direitos {LifeVerse.Shared.Items.ItemRights.Letters(state.OwnerRights)}",
            $"clique: {ContextPermissions.PrimaryLabel(state.ClickAction).ToLowerInvariant()}"
        };
        if (state.EditLocked) parts.Add("protegido contra edição");
        if (state.ExpiresAt is not null) parts.Add("temporário");
        parts.Add($"revisão {state.Revision}");
        return string.Join(" · ", parts) + ".";
    }

    private static List<RadialItem> ForVehicle(VehicleView car, ContextServices s)
    {
        var id = car.VehicleId;
        var items = new List<RadialItem>();
        if (s.IsDriving(id)) items.Add(new RadialItem { Label = "Sair", Icon = "undo", Run = s.ExitVehicle, Tooltip = "Desce do veículo" });
        else items.Add(new RadialItem { Label = "Entrar", Icon = "run", Run = () => s.EnterVehicle(id), Tooltip = "Entra e dirige (precisa estar a até 4 m)" });
        items.Add(new RadialItem { Label = "Inspecionar", Icon = "search", Run = () => s.Tell(car.DriverPeer == 0 ? $"{car.DisplayName}: livre." : $"{car.DisplayName}: em uso."), Tooltip = "Mostra se o veículo está livre" });
        return items;
    }

    private static List<RadialItem> ForGround(GroundTarget ground, ContextServices s)
    {
        var actor = s.Actor(); var parcel = s.Parcel(ground.Point);
        Decision D(Capability c) => ContextPermissions.ForParcel(c, actor, parcel);
        var rent = parcel.Known && parcel.State == "rent" && !parcel.Mine;
        var acquire = rent ? Slice("Alugar", "calendar", D(Capability.RentLand), () => s.AboutLand(ground.Point), "Abre as informações do terreno, onde está o aluguel")
                           : Slice("Comprar", "coin", D(Capability.BuyLand), () => s.AboutLand(ground.Point), "Abre as informações do terreno, onde está a compra");
        List<RadialItem> more =
        [
            new() { Label = "Selecionar terreno", Icon = "grid", Run = s.SelectLand, Tooltip = "Arraste sobre o chão para selecionar uma área e ver os limites dos terrenos" },
            new() { Label = "Ver proprietário", Icon = "user", Run = () => s.ParcelOwner(ground.Point), Tooltip = "Diz de quem é este terreno" },
            new() { Label = "Copiar local", Icon = "copy", Run = () => { DisplayServer.ClipboardSet($"{ground.Point.X:0.#}, {ground.Point.Y:0.#}, {ground.Point.Z:0.#}"); s.Tell("Localização copiada."); }, Tooltip = "Copia as coordenadas deste ponto" },
            Slice("Alugar terreno", "calendar", D(Capability.RentLand), () => s.AboutLand(ground.Point), "Abre as informações do terreno, onde está o aluguel")
        ];
        return
        [
            new() { Label = "Ir até aqui", Icon = "run", Run = () => s.WalkTo(ground.Point), Tooltip = "Caminha até este ponto" },
            Slice("Editar terreno", "mountain", D(Capability.TerrainEdit), s.EditTerrain, "Abre as ferramentas de terreno (elevar, rebaixar, nivelar, suavizar, pintar)"),
            acquire,
            Slice("Gerenciar", "settings", D(Capability.ManageLand), s.ManageLand, "Abre a janela de gerenciamento de terrenos"),
            Slice("Sobre a parcela", "info", D(Capability.AboutLand), () => s.AboutLand(ground.Point), "Mostra de quem é este terreno, o tamanho e quanto já foi construído"),
            Slice("Construir", "hammer", D(Capability.BuildHere), s.StartBuilding, "Abre as ferramentas de construção"),
            Slice("Criar objeto", "cube", D(Capability.BuildHere), s.StartPlacing, "Escolha onde colocar a forma selecionada no editor"),
            new() { Label = "Mais opções", Icon = "dots", SidePanel = true, Children = more, Tooltip = "Selecionar, proprietário, copiar local" }
        ];
    }

    // ---- inventory --------------------------------------------------------------------------------------------------------------

    private static List<RadialItem> ForItem(InventoryItemTarget item, ContextServices s)
    {
        var actor = s.Actor(); var facts = new ItemFacts(Guid.Empty, item.Rights, item.Category, item.InTrash, item.Offered);
        Decision D(Capability c) => ContextPermissions.ForItem(c, actor, facts);
        void Run(int action) => s.InventoryAction(action);
        if (item.InTrash)
            return
            [
                Slice("Restaurar", "undo", D(Capability.ItemRestore), () => Run(InvRestore), "Tira da lixeira e devolve ao inventário"),
                Slice("Excluir de vez", "trash", D(Capability.ItemPurge), () => Run(InvPurge), "Remove para sempre; não dá para desfazer", destructive: true, confirm: "Excluir de vez? Não dá para desfazer."),
                Slice("Direitos", "settings", D(Capability.ItemRights), () => Run(InvRights), "Direitos e propriedades do item"),
                Slice("Pré-visualizar", "eye", D(Capability.ItemPreview), () => Run(InvPreview))
            ];
        return
        [
            Slice("Pré-visualizar", "eye", D(Capability.ItemPreview), () => Run(InvPreview), "Mostra o item sem usá-lo"),
            Slice("Colocar no mundo", "cube", D(Capability.ItemPlace), () => Run(InvPlace), "Coloca o modelo ou a construção no mundo"),
            Slice("Vestir", "user", D(Capability.ItemWear), () => Run(InvEquip), "Veste ou equipa o item no avatar"),
            Slice("Copiar", "copy-plus", D(Capability.ItemCopy), () => Run(InvCopy), "Faz uma cópia no inventário"),
            Slice("Enviar", "send", D(Capability.ItemSend), () => Run(InvOffer), "Oferece o item a outra pessoa"),
            Slice("Favoritar", "star", Decision.Yes, () => Run(InvFavorite), "Marca ou desmarca como favorito"),
            Slice("Mais opções", "dots", Decision.Yes, null, "Versões, direitos e lixeira", side: true, children:
            [
                Slice("Direitos e propriedades", "settings", D(Capability.ItemRights), () => Run(InvRights)),
                Slice("Versões", "history", Decision.Yes, () => Run(InvVersions)),
                Slice("Enviar à lixeira", "trash", D(Capability.ItemDelete), () => Run(InvDelete), "Dá para restaurar depois", destructive: true)
            ])
        ];
    }

    private static List<RadialItem> ForFolder(InventoryFolderTarget folder, ContextServices s) =>
    [
        new() { Label = "Nova subpasta", Icon = "plus", Run = () => s.InventoryAction(InvNew), Tooltip = "Cria uma pasta dentro desta" },
        new() { Label = "Renomear", Icon = "doc", Run = () => s.InventoryAction(InvRename), Tooltip = "Muda o nome desta pasta" },
        new() { Label = "Excluir", Icon = "trash", Destructive = true, Confirm = $"Excluir a pasta {folder.Name}?", Run = () => s.InventoryAction(InvDeleteFolder), Tooltip = "Exclui a pasta (o servidor recusa se ela não estiver vazia ou for especial)" }
    ];

    // ---- script panels ----------------------------------------------------------------------------------------------------------

    private static List<RadialItem> ForHud(HudTarget hud, ContextServices s) =>
    [
        new() { Label = "Fechar painel", Icon = "close", Run = () => s.CloseHud(hud.Script), Tooltip = "Fecha este painel (o script pode abri-lo de novo se você permitir)" },
        new() { Label = "Sobre o painel", Icon = "info", Run = () => s.Tell($"Painel «{hud.Title}» de um script de objeto. Só mostra texto e botões; o jogo confere cada clique com a ilha."), Tooltip = "Explica de onde vem este painel" }
    ];
}

/// Right click in the world: finds what is under the cursor (yourself, another avatar, an object, a seat or the ground) and opens the radial
/// menu for it. It does nothing while a build or terrain tool owns the mouse or another window is under the cursor.
public partial class ContextInteractionManager : Node
{
    private const float PickReach = 80f, AvatarGrab = .55f;
    private Node _world = null!;
    private PlayerController? _player;
    private RadialMenu _menu = null!;
    private ContextServices _services = new();
    private NetworkManager? _network;
    private ObjectTarget? _selectedObject;
    private ContextTarget? _target;

    public RadialMenu Menu => _menu;

    public void Setup(Node world, PlayerController? player, RadialMenu menu, ContextServices services)
    {
        _world = world; _player = player; _menu = menu; _services = services;
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        _menu.DeleteRequested += () => DeleteSelectedObject();
    }

    /// Opens the menu for whatever is under a screen position; returns the kind of target, or null when there is nothing to show.
    public ContextTarget? OpenAt(Vector2 screen)
    {
        if (_menu.IsOpen) { _menu.Close(); }
        var target = Resolve(screen);
        if (target is null) return null;
        _selectedObject = target as ObjectTarget;
        // A right click is an explicit object selection, even with the build window closed.
        if (_selectedObject is { } selected) _services.SelectObject(selected.View.State.Id);
        _target = target;
        _menu.Open(screen, target.Title, target.Icon, InteractionRegistry.Build(target, _services));
        if (target is AvatarTarget person) _services.Prefetch(person.Name);
        return target;
    }

    /// Something about a person arrived (their relation to you): a menu open on them is rebuilt with the right slices.
    public void RefreshFor(string name)
    {
        if (!_menu.IsOpen || _target is not AvatarTarget person || !string.Equals(person.Name, name, StringComparison.OrdinalIgnoreCase)) return;
        _menu.ReplaceRoot(InteractionRegistry.Build(person, _services));
    }

    /// Something about the island arrived (this person administers it): the open menu is rebuilt with the right slices.
    public void RefreshCurrent()
    {
        if (_menu.IsOpen && _target is AvatarTarget person) _menu.ReplaceRoot(InteractionRegistry.Build(person, _services));
    }

    /// Opens the menu for a target that does not come from the world (an inventory item or folder, a script panel) at a screen position.
    public bool OpenFor(ContextTarget target, Vector2 screen)
    {
        var items = InteractionRegistry.Build(target, _services);
        if (items.Count == 0) return false;
        if (_menu.IsOpen) _menu.Close();
        _selectedObject = null;
        _target = target; _menu.Open(screen, target.Title, target.Icon, items);
        return true;
    }

    /// Only the owner can ask to delete; the API validates the same rule authoritatively.
    public bool DeleteSelectedObject()
    {
        if (_selectedObject is not { } selected) return false;
        if (!_services.Owns(selected.View)) { _services.Tell("Você não é o proprietário deste objeto e não pode excluí-lo."); return false; }
        _menu.Close();
        _services.DeleteObject(selected.View.State.Id);
        return true;
    }

    public ContextTarget? Resolve(Vector2 screen)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * PickReach, uint.MaxValue);
        if (_player is not null) query.Exclude = [_player.GetRid()];
        var hit = _player is null ? new Godot.Collections.Dictionary() : _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var hitDistance = hit.Count > 0 ? origin.DistanceTo(hit["position"].AsVector3()) : float.PositiveInfinity;

        // avatars have no collision shape: match the ray against each body from the feet to the head
        ContextTarget? person = null; var nearest = float.PositiveInfinity;
        void Consider(ContextTarget candidate, Vector3 feet)
        {
            var distance = RayMath.DistanceToSegment(new System.Numerics.Vector3(origin.X, origin.Y, origin.Z), new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
                new System.Numerics.Vector3(feet.X, feet.Y, feet.Z), new System.Numerics.Vector3(feet.X, feet.Y + 1.8f, feet.Z), PickReach, out var along);
            if (distance <= AvatarGrab && along < nearest && along <= hitDistance + .3f) { nearest = along; person = candidate; }
        }
        if (_player is not null) Consider(new SelfTarget(SelfName()), _player.GlobalPosition);
        // a car is wide: grab it from the body's middle, a little more generously than a person
        if (_network is not null)
            foreach (var car in _network.Vehicles.Views)
            {
                var feet = car.GlobalPosition;
                var distance = RayMath.DistanceToSegment(new System.Numerics.Vector3(origin.X, origin.Y, origin.Z), new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
                    new System.Numerics.Vector3(feet.X, feet.Y + .2f, feet.Z), new System.Numerics.Vector3(feet.X, feet.Y + 1.6f, feet.Z), PickReach, out var along);
                if (distance <= 1.5f && along < nearest && along <= hitDistance + 1.5f) { nearest = along; person = new VehicleTarget(car); }
            }
        if (_network is not null) foreach (var (peer, name, position) in _network.RemoteAvatars) Consider(new AvatarTarget(peer, name, position), position);
        if (person is not null) return person;

        if (hit.Count == 0) return null;
        var node = hit["collider"].AsGodotObject() as Node;
        if (node is InteractableSeat seat) return new SeatTarget(seat);
        var walk = node;
        while (walk is not null and not BuildObjectView) walk = walk.GetParent();
        if (walk is BuildObjectView view) return new ObjectTarget(view);
        return new GroundTarget(hit["position"].AsVector3());
    }

    private string SelfName()
    {
        var avatars = GetNodeOrNull<AvatarManager>("/root/AvatarManager");
        return avatars?.Current is { } current && !string.IsNullOrWhiteSpace(current.DisplayName) ? current.DisplayName : "Você";
    }
}
