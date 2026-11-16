using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>
/// The pictures of a profile: the gallery (a few images of the owner, in the owner's order, one of them the main one, which is the profile photo) and the wide cover. Only images the person owns can be used,
/// and only PNG, JPG or WebP up to a size; what visitors may see follows the privacy of the profile.
/// </summary>
public static class ProfileGallery
{
    public const int MaxImages = 12;
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>The asset must be an image of this person, in a format the game draws, of a reasonable size.</summary>
    internal static async Task<ProfileFailure?> CheckImageAsync(LifeVerseDbContext db, Guid userId, Guid assetId, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerUserId == userId && x.DeletedAt == null, ct);
        if (asset is null) return new("photo_not_owned", "Só uma imagem sua pode ser usada no perfil.");
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == assetId && x.Version == asset.CurrentVersion, ct);
        if (version is null || version.MimeType is not ("image/png" or "image/jpeg" or "image/webp")) return new("photo_not_image", "A imagem precisa ser PNG, JPG ou WebP.");
        if (version.ByteSize > MaxBytes) return new("photo_too_big", "A imagem passa de 8 MB.");
        return null;
    }

    public static async Task<List<ProfileGalleryImage>> ListAsync(LifeVerseDbContext db, Guid userId, CancellationToken ct) =>
        await db.ProfileGallery.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.Position).ThenBy(x => x.CreatedAt).ToListAsync(ct);

    /// <summary>Adds an image to the end of the gallery. The first one becomes the main picture.</summary>
    public static async Task<ProfileFailure?> AddAsync(LifeVerseDbContext db, Guid userId, Guid assetId, DateTimeOffset now, CancellationToken ct)
    {
        if (await CheckImageAsync(db, userId, assetId, ct) is { } bad) return bad;
        var current = await db.ProfileGallery.Where(x => x.UserId == userId).ToListAsync(ct);
        if (current.Any(x => x.AssetId == assetId)) return new("already_in_gallery", "Essa imagem já está na galeria.");
        if (current.Count >= MaxImages) return new("gallery_full", $"A galeria aceita até {MaxImages} imagens.");
        db.ProfileGallery.Add(new ProfileGalleryImage { Id = Guid.NewGuid(), UserId = userId, AssetId = assetId, Position = current.Count == 0 ? 0 : current.Max(x => x.Position) + 1, CreatedAt = now });
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (user.ProfilePhotoAssetId is null) user.ProfilePhotoAssetId = assetId;
        user.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    /// <summary>Takes an image out. When it was the main one, the first of the others takes its place (or there is no photo).</summary>
    public static async Task<ProfileFailure?> RemoveAsync(LifeVerseDbContext db, Guid userId, Guid imageId, DateTimeOffset now, CancellationToken ct)
    {
        var image = await db.ProfileGallery.SingleOrDefaultAsync(x => x.Id == imageId && x.UserId == userId, ct);
        if (image is null) return new("not_found", "Imagem não encontrada.");
        db.ProfileGallery.Remove(image);
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (user.ProfilePhotoAssetId == image.AssetId)
        {
            var next = await db.ProfileGallery.Where(x => x.UserId == userId && x.Id != imageId).OrderBy(x => x.Position).FirstOrDefaultAsync(ct);
            user.ProfilePhotoAssetId = next?.AssetId; if (next is null) user.ProfilePhotoUrl = null;
        }
        user.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    public static async Task<ProfileFailure?> SetMainAsync(LifeVerseDbContext db, Guid userId, Guid imageId, DateTimeOffset now, CancellationToken ct)
    {
        var image = await db.ProfileGallery.AsNoTracking().SingleOrDefaultAsync(x => x.Id == imageId && x.UserId == userId, ct);
        if (image is null) return new("not_found", "Imagem não encontrada.");
        if (await CheckImageAsync(db, userId, image.AssetId, ct) is { } bad) return bad;
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        user.ProfilePhotoAssetId = image.AssetId; user.ProfilePhotoUrl = null; user.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    /// <summary>The new order: every image of the gallery exactly once.</summary>
    public static async Task<ProfileFailure?> ReorderAsync(LifeVerseDbContext db, Guid userId, IReadOnlyList<Guid> order, CancellationToken ct)
    {
        var images = await db.ProfileGallery.Where(x => x.UserId == userId).ToListAsync(ct);
        if (order.Count != images.Count || order.Distinct().Count() != order.Count || order.Any(id => images.All(x => x.Id != id))) return new("bad_order", "A ordem precisa citar cada imagem da galeria uma vez.");
        for (var i = 0; i < order.Count; i++) images.Single(x => x.Id == order[i]).Position = i;
        await db.SaveChangesAsync(ct); return null;
    }

    /// <summary>Sets the cover (null: no cover).</summary>
    public static async Task<ProfileFailure?> SetCoverAsync(LifeVerseDbContext db, Guid userId, Guid? assetId, DateTimeOffset now, CancellationToken ct)
    {
        if (assetId is { } asset && await CheckImageAsync(db, userId, asset, ct) is { } bad) return bad;
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        user.ProfileCoverAssetId = assetId; user.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }
}
