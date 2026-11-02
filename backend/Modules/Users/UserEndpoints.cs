using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/users/me").RequireAuthorization().WithTags("Users");
        group.MapGet("", GetMeAsync)
            .Produces<UserSummary>().ProducesProblem(401).ProducesProblem(404);
        group.MapGet("/avatar", GetAvatarAsync).Produces<AvatarResponse>().ProducesProblem(404);
        group.MapPut("/avatar", SaveAvatarAsync).Produces<AvatarResponse>().ProducesValidationProblem().ProducesProblem(404);
        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().Include(x => x.Avatar)
            .SingleOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, ct);
        return user is null ? Results.NotFound() : Results.Ok(new UserSummary(user.Id, user.Username, user.Email, user.Avatar?.DisplayName ?? user.Username));
    }

    private static async Task<IResult> GetAvatarAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
        var avatar = await db.Avatars.AsNoTracking().Include(x => x.Appearance).Include(x => x.EquippedItems)
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return avatar is null ? Results.NotFound() : Results.Ok(ToResponse(avatar));
    }

    private static async Task<IResult> SaveAvatarAsync(SaveAvatarRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
        var errors = AvatarCatalog.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var avatar = await db.Avatars.Include(x => x.Appearance).Include(x => x.EquippedItems)
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (avatar is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        avatar.DisplayName = request.DisplayName.Trim();
        avatar.BaseModel = request.BaseModel;
        avatar.UpdatedAt = now;
        avatar.Appearance ??= new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = request.SkinColor, HairColor = request.HairColor, HairStyle = request.HairStyle, CreatedAt = now, UpdatedAt = now };
        avatar.Appearance.SkinColor = request.SkinColor.ToUpperInvariant();
        avatar.Appearance.HairColor = request.HairColor.ToUpperInvariant();
        avatar.Appearance.HairStyle = request.HairStyle;
        avatar.Appearance.EyeColor = string.IsNullOrWhiteSpace(request.EyeColor) ? avatar.Appearance.EyeColor : request.EyeColor.ToUpperInvariant();
        avatar.Appearance.BeardStyle = string.IsNullOrWhiteSpace(request.BeardStyle) ? avatar.Appearance.BeardStyle : request.BeardStyle;
        avatar.Appearance.BodySettingsJson = ValidJsonOrDefault(request.BodySettingsJson, avatar.Appearance.BodySettingsJson);
        avatar.Appearance.FaceSettingsJson = ValidJsonOrDefault(request.FaceSettingsJson, avatar.Appearance.FaceSettingsJson);
        avatar.Appearance.OutfitsJson = ValidJsonOrDefault(request.OutfitsJson, avatar.Appearance.OutfitsJson);
        avatar.Appearance.AttachmentsJson = ValidJsonOrDefault(request.AttachmentsJson, avatar.Appearance.AttachmentsJson);
        avatar.Appearance.CameraSensitivity = request.CameraSensitivity;
        avatar.Appearance.InvertCameraY = request.InvertCameraY;
        avatar.Appearance.UpdatedAt = now;

        var requestedSlots = request.EquippedItems.Select(x => x.Slot).ToHashSet(StringComparer.Ordinal);
        db.EquippedItems.RemoveRange(avatar.EquippedItems.Where(x => !requestedSlots.Contains(x.Slot)));
        foreach (var item in request.EquippedItems)
        {
            var equipped = avatar.EquippedItems.SingleOrDefault(x => x.Slot == item.Slot);
            if (equipped is null)
            {
                equipped = new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = item.Slot, ItemKey = item.ItemKey, TintColor = item.TintColor.ToUpperInvariant(), EquippedAt = now };
                avatar.EquippedItems.Add(equipped);
                db.EquippedItems.Add(equipped);
            }
            else
            {
                equipped.ItemKey = item.ItemKey;
                equipped.TintColor = item.TintColor.ToUpperInvariant();
                equipped.EquippedAt = now;
            }
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(avatar));
    }

    private static bool TryGetUserId(HttpContext context, out Guid userId) =>
        Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

    private static AvatarResponse ToResponse(Avatar avatar)
    {
        var appearance = avatar.Appearance;
        return new AvatarResponse(avatar.Id, avatar.DisplayName, avatar.BaseModel,
            appearance?.SkinColor ?? "#C98F65", appearance?.HairColor ?? "#2B1B14", appearance?.HairStyle ?? "short",
            appearance?.EyeColor ?? "#4A3326", appearance?.BeardStyle ?? "none", appearance?.BodySettingsJson ?? "{}",
            appearance?.FaceSettingsJson ?? "{}", appearance?.OutfitsJson ?? "[]", appearance?.AttachmentsJson ?? "[]",
            appearance?.CameraSensitivity ?? 0.003f, appearance?.InvertCameraY ?? false,
            avatar.EquippedItems.OrderBy(x => x.Slot).Select(x => new EquippedItemRequest(x.Slot, x.ItemKey, x.TintColor)).ToList());
    }
    private static string ValidJsonOrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
