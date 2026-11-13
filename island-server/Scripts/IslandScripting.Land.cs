using LifeVerse.Scripting;
using LifeVerse.Shared.Land;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>The land as scripts see it: which parcel is where (name, owner, how it is opened) and who walks from one parcel to another.</summary>
public sealed partial class IslandScripting
{
    private readonly Dictionary<Guid, Guid?> _avatarParcels = [];
    private readonly Dictionary<Guid, ParcelData> _parcelCache = [];

    /// <summary>The parcels of the island, as the API's land-sync reports them. Set by the server.</summary>
    public LandInfo Land { get; set; } = LandInfo.Empty;

    public ParcelData? GetParcelAt(double x, double z) => Land.At(x, z) is { } p ? new ParcelData(p.Id, p.Name, p.Owner, p.Access, p.Build, p.ForSale) : null;

    /// <summary>Notices avatars that stepped onto another parcel and tells the scripts of the objects standing on the parcel they left and on the one they entered.</summary>
    private void WatchParcels(IReadOnlyList<AvatarPresence> avatars)
    {
        if (Land.Parcels == 0 && _avatarParcels.Count == 0) return;
        foreach (var avatar in avatars)
        {
            var now = GetParcelAt(avatar.X, avatar.Z); var was = _avatarParcels.TryGetValue(avatar.AvatarId, out var known) ? known : (Guid?)null;
            if (now?.Id == was) continue;
            if (now is not null) _parcelCache[now.Id] = now;
            _avatarParcels[avatar.AvatarId] = now?.Id;
            _host.AvatarMovedParcels(avatar.AvatarId, avatar.Name, was is { } old && _parcelCache.TryGetValue(old, out var before) ? before : null, now);
        }
        foreach (var gone in _avatarParcels.Keys.Where(id => avatars.All(a => a.AvatarId != id)).ToList()) _avatarParcels.Remove(gone);
    }
}
