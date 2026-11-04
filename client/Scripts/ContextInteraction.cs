using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// What a right click landed on.
public abstract record ContextTarget(string Title, string Icon);
public sealed record SelfTarget(string Name) : ContextTarget(Name, "user");
public sealed record AvatarTarget(int Peer, string Name, Vector3 Position) : ContextTarget(Name, "user");
public sealed record ObjectTarget(BuildObjectView View) : ContextTarget(string.IsNullOrWhiteSpace(View.State.Name) ? "Objeto" : View.State.Name, "cube");
public sealed record SeatTarget(InteractableSeat Seat) : ContextTarget("Assento", "user");
public sealed record GroundTarget(Vector3 Point) : ContextTarget("Terreno", "map");
public sealed record VehicleTarget(VehicleView View) : ContextTarget(string.IsNullOrWhiteSpace(View.DisplayName) ? "Veículo" : View.DisplayName, "run");

/// What the menu can do, handed in by the window that owns each thing (so this file depends on none of them).
public sealed class ContextServices
{
    public Action ToggleInventory { get; init; } = () => { };
    public Action ToggleGestures { get; init; } = () => { };
    public Action TogglePeople { get; init; } = () => { };
    public Action OpenSettings { get; init; } = () => { };
    public Action<string> SearchPerson { get; init; } = _ => { };
    public Action<string> AddFriend { get; init; } = _ => { };
    public Action<string> Message { get; init; } = _ => { };
    public Action<string> Block { get; init; } = _ => { };
    public Action<string> Report { get; init; } = _ => { };
    public Action<string> Tell { get; init; } = _ => { };
    public Action<Vector3> WalkTo { get; init; } = _ => { };
    public Action<string> Gesture { get; init; } = _ => { };
    public Action<InteractableSeat> Sit { get; init; } = _ => { };
    public Action<Guid> Touch { get; init; } = _ => { };
    public Action<Guid, GizmoMode?> EditObject { get; init; } = (_, _) => { };
    public Action<Guid, string> ObjectCommand { get; init; } = (_, _) => { };
    public Action StartBuilding { get; init; } = () => { };
    public Action StartPlacing { get; init; } = () => { };
    public Action<Vector3> AboutLand { get; init; } = _ => { };
    public Action SelectLand { get; init; } = () => { };
    /// Whether this person owns an object (only then are the editing slices shown; the server checks again whatever the screen says).
    public Func<BuildObjectView, bool> Owns { get; init; } = _ => false;
    public Action<int> EnterVehicle { get; init; } = _ => { };
    public Action ExitVehicle { get; init; } = () => { };
    /// Whether this person is the one driving that vehicle right now.
    public Func<int, bool> IsDriving { get; init; } = _ => false;
}

/// Which slices each kind of target gets. Only things that really work are offered, and what does not make sense for a target is left out:
/// an object you do not own has no editing slices, a seat can be sat on, the ground can be walked to and built on.
public static class InteractionRegistry
{
    private static RadialItem Close => new() { Label = "Fechar", Icon = "close", Accent = true, Tooltip = "Fecha o menu (Esc)" };

    public static List<RadialItem> Build(ContextTarget target, ContextServices s) => target switch
    {
        SelfTarget => ForSelf(s),
        AvatarTarget avatar => ForAvatar(avatar, s),
        ObjectTarget @object => ForObject(@object.View, s),
        SeatTarget seat => [new RadialItem { Label = "Sentar", Icon = "user", Run = () => s.Sit(seat.Seat), Tooltip = "Senta neste assento" }, Close],
        GroundTarget ground => ForGround(ground, s),
        VehicleTarget car => ForVehicle(car.View, s),
        _ => [Close]
    };

    private static List<RadialItem> Gestures(ContextServices s) =>
    [
        new() { Label = "Acenar", Icon = "hand", Run = () => s.Gesture("wave"), Tooltip = "Acena para quem está por perto" },
        new() { Label = "Dançar", Icon = "volume", Run = () => s.Gesture("dance"), Tooltip = "Dança por alguns segundos" },
        new() { Label = "Mais gestos", Icon = "dots", Run = s.ToggleGestures, Tooltip = "Abre a janela de gestos" }
    ];

    private static List<RadialItem> ForSelf(ContextServices s) =>
    [
        new() { Label = "Inventário", Icon = "bag", Run = s.ToggleInventory, Tooltip = "Abre o seu inventário" },
        new() { Label = "Animações", Icon = "hand", Children = Gestures(s), Tooltip = "Acenar, dançar e outros gestos" },
        new() { Label = "Pessoas e grupos", Icon = "people", Run = s.TogglePeople, Tooltip = "Amigos, grupos e busca de pessoas" },
        new() { Label = "Construir", Icon = "hammer", Run = s.StartBuilding, Tooltip = "Abre as ferramentas de construção" },
        new() { Label = "Configurações", Icon = "settings", Run = s.OpenSettings },
        Close
    ];

    private static List<RadialItem> ForAvatar(AvatarTarget avatar, ContextServices s) =>
    [
        new() { Label = "Adicionar amigo", Icon = "user-add", Run = () => s.AddFriend(avatar.Name), Tooltip = "Envia um pedido de amizade" },
        new() { Label = "Mensagem", Icon = "chat", Run = () => s.Message(avatar.Name), Tooltip = "Escreve uma mensagem privada" },
        new() { Label = "Interagir", Icon = "hand", Children = Gestures(s), Tooltip = "Gestos para quem está por perto" },
        new() { Label = "Ir até lá", Icon = "run", Run = () => s.WalkTo(avatar.Position), Tooltip = "Caminha até esta pessoa" },
        new() { Label = "Mais opções", Icon = "dots", Tooltip = "Bloquear ou denunciar", Children =
        [
            new() { Label = "Buscar na lista", Icon = "search", Run = () => s.SearchPerson(avatar.Name), Tooltip = "Abre a janela Pessoas com este nome" },
            new() { Label = "Bloquear", Icon = "alert", Accent = true, Run = () => s.Block(avatar.Name), Tooltip = "Impede que esta pessoa fale com você" },
            new() { Label = "Denunciar", Icon = "error", Accent = true, Run = () => s.Report(avatar.Name), Tooltip = "Conta à moderação o que aconteceu" }
        ] },
        Close
    ];

    private static List<RadialItem> ForObject(BuildObjectView view, ContextServices s)
    {
        var id = view.State.Id; var owns = s.Owns(view);
        var items = new List<RadialItem>
        {
            new() { Label = "Interagir", Icon = "hand", Run = () => s.Touch(id), Tooltip = "Toca o objeto (ativa o script dele, se tiver)" },
            new() { Label = "Inspecionar", Icon = "search", Run = () => s.Tell($"{view.State.Name}: {view.State.PrimitiveType}, tamanho {view.Scale.X:0.##} × {view.Scale.Y:0.##} × {view.Scale.Z:0.##} m, revisão {view.State.Revision}."), Tooltip = "Mostra os dados do objeto" }
        };
        if (owns)
        {
            items.Add(new RadialItem
            {
                Label = "Editar", Icon = "hammer", Tooltip = "Mover, girar, esticar, duplicar ou excluir",
                Children =
                [
                    new() { Label = "Mover", Icon = "move", Run = () => s.EditObject(id, GizmoMode.Move) },
                    new() { Label = "Girar", Icon = "rotate", Run = () => s.EditObject(id, GizmoMode.Rotate) },
                    new() { Label = "Esticar", Icon = "expand", Run = () => s.EditObject(id, GizmoMode.Scale) },
                    new() { Label = "Textura", Icon = "image", Run = () => s.EditObject(id, null), Tooltip = "Abre o editor com o objeto selecionado (aba Textura)" },
                    new() { Label = "Duplicar", Icon = "copy-plus", Run = () => { s.EditObject(id, null); s.ObjectCommand(id, "duplicate"); } },
                    new() { Label = "Excluir", Icon = "trash", Accent = true, Run = () => { s.EditObject(id, null); s.ObjectCommand(id, "delete"); }, Tooltip = "Exclui o objeto (Ctrl+Z desfaz)" }
                ]
            });
            items.Add(new RadialItem { Label = "Copiar", Icon = "copy", Run = () => { s.EditObject(id, null); s.ObjectCommand(id, "copy"); s.Tell("Objeto copiado: Ctrl+V cola onde o cursor aponta."); }, Tooltip = "Copia o objeto (Ctrl+V cola)" });
        }
        items.Add(Close);
        return items;
    }

    private static List<RadialItem> ForVehicle(VehicleView car, ContextServices s)
    {
        var id = car.VehicleId;
        var items = new List<RadialItem>();
        if (s.IsDriving(id)) items.Add(new RadialItem { Label = "Sair", Icon = "undo", Run = s.ExitVehicle, Tooltip = "Desce do veículo" });
        else items.Add(new RadialItem { Label = "Entrar", Icon = "run", Run = () => s.EnterVehicle(id), Tooltip = "Entra e dirige (precisa estar a até 4 m)" });
        items.Add(new RadialItem { Label = "Inspecionar", Icon = "search", Run = () => s.Tell(car.DriverPeer == 0 ? $"{car.DisplayName}: livre." : $"{car.DisplayName}: em uso."), Tooltip = "Mostra se o veículo está livre" });
        items.Add(Close);
        return items;
    }

    private static List<RadialItem> ForGround(GroundTarget ground, ContextServices s) =>
    [
        new() { Label = "Ir até aqui", Icon = "run", Run = () => s.WalkTo(ground.Point), Tooltip = "Caminha até este ponto" },
        new() { Label = "Sobre o terreno", Icon = "info", Run = () => s.AboutLand(ground.Point), Tooltip = "Mostra de quem é este terreno, o tamanho e quanto já foi construído" },
        new() { Label = "Construir", Icon = "hammer", Run = s.StartBuilding, Tooltip = "Abre as ferramentas de construção" },
        new() { Label = "Criar objeto", Icon = "cube", Run = s.StartPlacing, Tooltip = "Escolha onde colocar a forma selecionada no editor" },
        new() { Label = "Selecionar terreno", Icon = "grid", Run = s.SelectLand, Tooltip = "Arraste sobre o chão para selecionar uma área e ver os limites dos terrenos" },
        new() { Label = "Copiar local", Icon = "copy", Run = () => { DisplayServer.ClipboardSet($"{ground.Point.X:0.#}, {ground.Point.Y:0.#}, {ground.Point.Z:0.#}"); s.Tell("Localização copiada."); }, Tooltip = "Copia as coordenadas deste ponto" },
        Close
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

    public RadialMenu Menu => _menu;

    public void Setup(Node world, PlayerController? player, RadialMenu menu, ContextServices services)
    {
        _world = world; _player = player; _menu = menu; _services = services;
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
    }

    /// Opens the menu for whatever is under a screen position; returns the kind of target, or null when there is nothing to show.
    public ContextTarget? OpenAt(Vector2 screen)
    {
        if (_menu.IsOpen) { _menu.Close(); }
        var target = Resolve(screen);
        if (target is null) return null;
        _menu.Open(screen, target.Title, target.Icon, InteractionRegistry.Build(target, _services));
        return target;
    }

    public ContextTarget? Resolve(Vector2 screen)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * PickReach, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
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
