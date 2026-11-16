using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record GroupShownRequest(bool Shown);
public sealed record GalleryAddRequest(Guid AssetId);
public sealed record GalleryOrderRequest(List<Guid>? Ids);
public sealed record CoverRequest(Guid? AssetId);
public sealed record PostRequest(string? Text, Guid? GalleryImageId, string? Visibility);
public sealed record ProposeRequest(Guid UserId, string? Message);
public sealed record AccountTypeRequest(string Type);

/// <summary>The server side of the profile window: reading a profile (by id or by name), editing mine, the photo, the groups I show, marriage and the type of account.</summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/v1/users").WithTags("Profile").RequireAuthorization();
        users.MapGet("/profile", ByNameAsync);
        users.MapGet("/{userId:guid}/profile/full", ViewAsync);
        users.MapGet("/{userId:guid}/profile/photo", PhotoAsync);
        users.MapPut("/me/profile/details", UpdateAsync);
        users.MapPut("/me/groups/{groupId:guid}/profile", GroupShownAsync);
        users.MapPut("/me/parcels/{parcelId:guid}/profile", ParcelShownAsync);
        users.MapPost("/me/profile/gallery", GalleryAddAsync);
        users.MapDelete("/me/profile/gallery/{imageId:guid}", GalleryRemoveAsync);
        users.MapPut("/me/profile/gallery/order", GalleryOrderAsync);
        users.MapPut("/me/profile/gallery/{imageId:guid}/main", GalleryMainAsync);
        users.MapGet("/{userId:guid}/profile/gallery/{imageId:guid}", GalleryImageAsync);
        users.MapPut("/me/profile/cover", CoverSetAsync);
        users.MapGet("/{userId:guid}/profile/feed", FeedAsync);
        users.MapPost("/me/profile/posts", PostAsync);
        users.MapDelete("/me/profile/posts/{postId:guid}", PostDeleteAsync);
        users.MapGet("/{userId:guid}/profile/posts/{postId:guid}/image", PostImageAsync);
        users.MapGet("/{userId:guid}/profile/cover", CoverImageAsync);
        var marriage = endpoints.MapGroup("/api/v1/marriage").WithTags("Profile").RequireAuthorization();
        marriage.MapGet("", MineAsync); marriage.MapPost("/propose", ProposeAsync);
        marriage.MapPost("/{id:guid}/accept", (Guid id, HttpContext c, LifeVerseDbContext d, NotificationHub h, TimeProvider t, CancellationToken ct) => AnswerAsync(id, "accept", c, d, h, t, ct));
        marriage.MapPost("/{id:guid}/decline", (Guid id, HttpContext c, LifeVerseDbContext d, NotificationHub h, TimeProvider t, CancellationToken ct) => AnswerAsync(id, "decline", c, d, h, t, ct));
        marriage.MapPost("/{id:guid}/cancel", (Guid id, HttpContext c, LifeVerseDbContext d, NotificationHub h, TimeProvider t, CancellationToken ct) => AnswerAsync(id, "cancel", c, d, h, t, ct));
        marriage.MapPost("/divorce", DivorceAsync);
        endpoints.MapPost("/api/v1/admin/users/{id:guid}/account-type", SetAccountTypeAsync).WithTags("Administration").RequireAuthorization();
        return endpoints;
    }

    private static bool TryUser(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static IResult Problem(ProfileFailure failure) => Results.Json(new { error = failure.Code, message = failure.Message }, statusCode: failure.Code == "not_found" ? 404 : 400);

    internal static async Task<IResult> ViewAsync(Guid userId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        return await ProfileService.ViewAsync(db, viewer, userId, clock.GetUtcNow(), ct) is { } profile ? Results.Ok(profile) : Results.NotFound();
    }

    internal static async Task<IResult> ByNameAsync(string name, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        if (await ProfileService.FindByNameAsync(db, name, ct) is not { } id) return Results.NotFound();
        return await ProfileService.ViewAsync(db, viewer, id, clock.GetUtcNow(), ct) is { } profile ? Results.Ok(profile) : Results.NotFound();
    }

    internal static async Task<IResult> UpdateAsync(ProfileUpdate request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        if (await ProfileService.UpdateAsync(db, me, request, clock.GetUtcNow(), ct) is { } failure) return Problem(failure);
        return Results.Ok(await ProfileService.ViewAsync(db, me, me, clock.GetUtcNow(), ct));
    }

    /// <summary>The picture of a profile, for anybody who may see that profile (the asset itself stays private to its owner).</summary>
    internal static async Task<IResult> PhotoAsync(Guid userId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, ct);
        if (user?.ProfilePhotoAssetId is not { } assetId) return Results.NotFound();
        if (viewer != userId && ProfileService.PrivacyOf(user.PrivacyJson).ProfilePrivate) return Results.NotFound();
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerUserId == userId && x.DeletedAt == null, ct);
        if (asset is null) return Results.NotFound();
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == assetId && x.Version == asset.CurrentVersion, ct);
        if (version is null || version.MimeType is not ("image/png" or "image/jpeg" or "image/webp")) return Results.NotFound();
        var stream = await storage.OpenReadAsync(version.StorageKey, ct);
        return stream is null ? Results.NotFound() : Results.File(stream, version.MimeType);
    }

    internal static async Task<IResult> GroupShownAsync(Guid groupId, GroupShownRequest request, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        return await ProfileService.SetGroupShownAsync(db, me, groupId, request.Shown, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Mine(HttpContext context, LifeVerseDbContext db, TimeProvider clock, Func<Guid, Task<ProfileFailure?>> change, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        if (await change(me) is { } failure) return Problem(failure);
        return Results.Ok(await ProfileService.ViewAsync(db, me, me, clock.GetUtcNow(), ct));
    }

    internal static Task<IResult> GalleryAddAsync(GalleryAddRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => Mine(context, db, clock, me => ProfileGallery.AddAsync(db, me, request.AssetId, clock.GetUtcNow(), ct), ct);
    internal static Task<IResult> GalleryRemoveAsync(Guid imageId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => Mine(context, db, clock, me => ProfileGallery.RemoveAsync(db, me, imageId, clock.GetUtcNow(), ct), ct);
    internal static Task<IResult> GalleryMainAsync(Guid imageId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => Mine(context, db, clock, me => ProfileGallery.SetMainAsync(db, me, imageId, clock.GetUtcNow(), ct), ct);
    internal static Task<IResult> GalleryOrderAsync(GalleryOrderRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => Mine(context, db, clock, me => ProfileGallery.ReorderAsync(db, me, request.Ids ?? [], ct), ct);
    internal static Task<IResult> CoverSetAsync(CoverRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => Mine(context, db, clock, me => ProfileGallery.SetCoverAsync(db, me, request.AssetId, clock.GetUtcNow(), ct), ct);

    /// <summary>The bytes of an image asset of <paramref name="owner"/>, for a viewer who may see that profile.</summary>
    private static async Task<IResult> ImageOfAsync(Guid viewer, Guid owner, Guid? assetId, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (assetId is not { } id) return Results.NotFound();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == owner && x.DeletedAt == null, ct);
        if (user is null || (viewer != owner && ProfileService.PrivacyOf(user.PrivacyJson).ProfilePrivate)) return Results.NotFound();
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner && x.DeletedAt == null, ct);
        if (asset is null) return Results.NotFound();
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == id && x.Version == asset.CurrentVersion, ct);
        if (version is null || version.MimeType is not ("image/png" or "image/jpeg" or "image/webp")) return Results.NotFound();
        var stream = await storage.OpenReadAsync(version.StorageKey, ct);
        return stream is null ? Results.NotFound() : Results.File(stream, version.MimeType);
    }

    internal static async Task<IResult> FeedAsync(Guid userId, DateTimeOffset? before, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        return await ProfileFeed.PageAsync(db, viewer, userId, before, clock.GetUtcNow(), ct) is { } page ? Results.Ok(page) : Results.NotFound();
    }

    internal static async Task<IResult> PostAsync(PostRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        Guid? asset = null;
        if (request.GalleryImageId is { } image)
        {
            asset = (await db.ProfileGallery.AsNoTracking().SingleOrDefaultAsync(x => x.Id == image && x.UserId == me, ct))?.AssetId;
            if (asset is null) return Problem(new("not_found", "Imagem não encontrada na sua galeria."));
        }
        if (await ProfileFeed.PostAsync(db, me, request.Text, asset, request.Visibility, clock.GetUtcNow(), ct) is { } failure) return Problem(failure);
        return Results.Ok(await ProfileFeed.PageAsync(db, me, me, null, clock.GetUtcNow(), ct));
    }

    internal static async Task<IResult> PostDeleteAsync(Guid postId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        if (await ProfileFeed.DeleteAsync(db, me, postId, clock.GetUtcNow(), ct) is { } failure) return Problem(failure);
        return Results.Ok(await ProfileFeed.PageAsync(db, me, me, null, clock.GetUtcNow(), ct));
    }

    internal static async Task<IResult> PostImageAsync(Guid userId, Guid postId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        return await ImageOfAsync(viewer, userId, await ProfileFeed.ImageOfAsync(db, viewer, userId, postId, ct), db, storage, ct);
    }

    internal static async Task<IResult> GalleryImageAsync(Guid userId, Guid imageId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        var image = await db.ProfileGallery.AsNoTracking().SingleOrDefaultAsync(x => x.Id == imageId && x.UserId == userId, ct);
        return await ImageOfAsync(viewer, userId, image?.AssetId, db, storage, ct);
    }

    internal static async Task<IResult> CoverImageAsync(Guid userId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var viewer)) return Results.Unauthorized();
        var cover = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.ProfileCoverAssetId).SingleOrDefaultAsync(ct);
        return await ImageOfAsync(viewer, userId, cover, db, storage, ct);
    }

    internal static async Task<IResult> ParcelShownAsync(Guid parcelId, GroupShownRequest request, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        var result = await ProfileService.SetParcelShownAsync(db, me, parcelId, request.Shown, ct);
        return result is null ? Results.NotFound() : result == "too_many" ? Results.Json(new { error = "too_many", message = $"Você pode destacar até {ProfileService.MaxParcelsShown} terrenos." }, statusCode: 400) : Results.NoContent();
    }

    internal static async Task<IResult> MineAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        return Results.Ok(await ProfileService.MineAsync(db, me, clock.GetUtcNow(), ct));
    }

    internal static async Task<IResult> ProposeAsync(ProposeRequest request, HttpContext context, LifeVerseDbContext db, NotificationHub hub, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        var (marriage, failure) = await ProfileService.ProposeAsync(db, me, request.UserId, request.Message, clock.GetUtcNow(), ct);
        if (failure is not null) return Problem(failure);
        await hub.NotifyAsync(request.UserId, "marriage_proposal", new { from = me, marriageId = marriage!.Id }, ct);
        return Results.Ok(new { marriage.Id, marriage.Status });
    }

    internal static async Task<IResult> AnswerAsync(Guid id, string answer, HttpContext context, LifeVerseDbContext db, NotificationHub hub, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        if (await ProfileService.AnswerAsync(db, me, id, answer, clock.GetUtcNow(), ct) is { } failure) return Problem(failure);
        var other = await db.Marriages.AsNoTracking().Where(x => x.Id == id).Select(x => x.ProposerUserId == me ? x.TargetUserId : x.ProposerUserId).SingleAsync(ct);
        await hub.NotifyAsync(other, answer switch { "accept" => "marriage_accepted", "decline" => "marriage_declined", _ => "marriage_cancelled" }, new { from = me, marriageId = id }, ct);
        return Results.NoContent();
    }

    internal static async Task<IResult> DivorceAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        return await ProfileService.DivorceAsync(db, me, clock.GetUtcNow(), ct) is { } failure ? Problem(failure) : Results.NoContent();
    }

    internal static async Task<IResult> SetAccountTypeAsync(Guid id, AccountTypeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!AdminEndpoints.Admin(context, out var admin)) return Results.Forbid();
        if (await ProfileService.SetAccountTypeAsync(db, id, request.Type, clock.GetUtcNow(), ct) is { } failure) return Problem(failure);
        await AdminEndpoints.Audit(db, admin, "account_type:" + request.Type, "user", id, clock.GetUtcNow(), ct); await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
