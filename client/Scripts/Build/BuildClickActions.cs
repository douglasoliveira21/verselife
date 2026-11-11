using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// What a click on an object does, as the owner chose in the GERAL tab: touch (the object's script runs), sit, buy, pay, open, zoom, ignore (the click goes through to what is
/// behind) or nothing. It works for everybody in the world, with either editor, and never changes the object.
/// </summary>
public partial class BuildClickActions : Node
{
    private NetworkManager? _network;
    private BuildNameDirectory _names = null!;
    private ObjectPurchaseWindow _purchase = null!;
    private ObjectPaymentWindow _payment = null!;
    private ObjectContentsWindow _contents = null!;
    private ObjectZoomCamera? _zoom;

    public ObjectPurchaseWindow Purchase => _purchase;
    public ObjectPaymentWindow Payment => _payment;
    public ObjectContentsWindow Contents => _contents;
    public ObjectZoomCamera? Zoom => _zoom;
    public BuildNameDirectory Names => _names;
    public Guid LastTouchForTesting { get; private set; }
    /// <summary>Tests count touches here instead of sending them to the server.</summary>
    public Action<Guid>? TouchSink { get; set; }

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        _names = new BuildNameDirectory { Name = "Names" }; AddChild(_names);
        _purchase = new ObjectPurchaseWindow { Name = "Purchase" }; AddChild(_purchase);
        _payment = new ObjectPaymentWindow { Name = "Payment" }; AddChild(_payment);
        _contents = new ObjectContentsWindow { Name = "Contents" }; AddChild(_contents);
    }

    /// <summary>The click action of an object that does not go through (everything but "ignore").</summary>
    public static bool Ignores(BuildObjectView view) => view.State.ClickAction == "ignore";

    public void Perform(BuildObjectView view)
    {
        var state = view.State; var mine = state.OwnerUserId == (GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty);
        switch (state.ClickAction)
        {
            case "none": case "ignore": return;
            case "sit": SitOn(view); return;
            case "buy": _purchase.Open(state, _names.NameOf(state.OwnerUserId, name => { if (_purchase.IsOpen) _purchase.Open(state, name); })); return;
            case "pay": _payment.Open(state, _names.NameOf(state.OwnerUserId)); return;
            case "open": _contents.Open(state, mine); return;
            case "zoom": ZoomTo(view); return;
            default: LastTouchForTesting = state.Id; if (TouchSink is not null) TouchSink(state.Id); else _network?.TouchObject(state.Id); return;
        }
    }

    // ---- zoom ----------------------------------------------------------------------------------------------------------

    public void ZoomTo(BuildObjectView view)
    {
        if (_zoom is null || !IsInstanceValid(_zoom)) { _zoom = new ObjectZoomCamera { Name = "ZoomCamera" }; AddChild(_zoom); }
        if (_zoom.Active) _zoom.Exit();
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        var radius = .5f * new Vector3(view.State.ScaleX, view.State.ScaleY, view.State.ScaleZ).Length();
        _zoom.Enter(camera, view.GlobalPosition, radius);
    }

    // ---- sitting ---------------------------------------------------------------------------------------------------------

    private readonly Dictionary<Guid, InteractableSeat> _seats = [];

    /// <summary>
    /// Sits the avatar on top of the object. The seat belongs to the object (it goes where the object goes). Who is sitting is known only on this computer: the island server
    /// sees that the avatar sits, not on what, so two people can still choose the same object.
    /// </summary>
    public bool SitOn(BuildObjectView view)
    {
        var player = GetParent()?.GetNodeOrNull<PlayerController>("Player"); if (player is null) return false;
        if (!_seats.TryGetValue(view.State.Id, out var seat) || !IsInstanceValid(seat))
        {
            seat = new InteractableSeat { Name = "ObjectSeat", TopLevel = true }; seat.AddChild(new Marker3D { Name = "SeatAnchor" }); seat.AddChild(new Marker3D { Name = "ExitAnchor" });
            view.AddChild(seat); _seats[view.State.Id] = seat;
        }
        var top = view.GlobalPosition + Vector3.Up * (view.State.ScaleY / 2f + .02f);
        var facing = view.GlobalTransform.Basis.Orthonormalized();
        seat.GlobalTransform = new Transform3D(Basis.Identity, top);
        seat.GetNode<Marker3D>("SeatAnchor").GlobalTransform = new Transform3D(facing, top);
        seat.GetNode<Marker3D>("ExitAnchor").GlobalPosition = top + facing.Z * (Mathf.Max(view.State.ScaleX, view.State.ScaleZ) / 2f + .9f);
        if (seat.IsOccupied) return false;
        player.SitOn(seat); return true;
    }
}
