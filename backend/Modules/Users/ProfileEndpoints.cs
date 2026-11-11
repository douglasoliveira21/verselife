using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record GroupShownRequest(bool Shown);
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
