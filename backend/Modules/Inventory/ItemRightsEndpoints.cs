using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Items;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record SetRightsRequest(int Owner, int Group, int NextOwner, long? ExpectedRevision);
public sealed record ShareGroupRequest(Guid? GroupId);
public sealed record HistoryVisibilityRequest(bool Visible);

/// <summary>The rights of an item, read and changed by its holder. Every answer comes from <see cref="ItemRightsService"/>; nothing here trusts the game.</summary>
public static class ItemRightsEndpoints
{
    public static IEndpointRouteBuilder MapItemRightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory/items/{itemId:guid}").WithTags("Item rights").RequireAuthorization();
        group.MapGet("/rights", async (Guid itemId, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : await ItemRightsService.ViewAsync(db, me, itemId, ct) is { } view ? Results.Ok(view) : Results.NotFound());
        group.MapPut("/rights", async (Guid itemId, SetRightsRequest body, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Answer(await ItemRightsService.SetPermissionsAsync(db, me, itemId, new ItemPermissions(body.Owner, body.Group, body.NextOwner), body.ExpectedRevision, t.GetUtcNow(), ct)));
        group.MapPut("/group", async (Guid itemId, ShareGroupRequest body, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Answer(await ItemRightsService.ShareWithGroupAsync(db, me, itemId, body.GroupId, t.GetUtcNow(), ct)));
        group.MapPut("/history-visibility", async (Guid itemId, HistoryVisibilityRequest body, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Answer(await ItemRightsService.SetHistoryVisibilityAsync(db, me, itemId, body.Visible, t.GetUtcNow(), ct)));
        group.MapGet("/history", async (Guid itemId, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : await ItemRightsService.HistoryAsync(db, me, itemId, ct) is { } lines ? Results.Ok(lines) : Results.NotFound());
        return endpoints;
    }

    internal static IResult Answer((bool Ok, string? Error) result) => result.Ok ? Results.NoContent() : result.Error switch
    {
        "not_found" => Results.NotFound(),
        "stale" or "item_busy" => Results.Conflict(new { error = result.Error }),
        _ => Results.BadRequest(new { error = result.Error }),
    };

    private static bool Who(HttpContext c, out Guid user) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
}
