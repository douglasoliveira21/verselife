using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
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

public sealed record OfferRequest(Guid ToUserId, string? Kind, string? Message, bool KeepCopy, string? IdempotencyKey);

/// <summary>Giving items to other people: offers, and the receiver answering them.</summary>
public static class ItemOfferEndpoints
{
    public static IEndpointRouteBuilder MapItemOfferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory").WithTags("Item offers").RequireAuthorization();
        group.MapPost("/items/{itemId:guid}/offers", async (Guid itemId, OfferRequest body, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var result = await ItemTransfers.OfferAsync(db, me, itemId, body.ToUserId, body.Kind, body.Message, body.KeepCopy, body.IdempotencyKey, t.GetUtcNow(), ct);
            if (result.Ok && result.Offer is { Status: "pending" } offer && t.GetUtcNow() - offer.CreatedAt < Fresh) await ItemOfferNotifier.OfferedAsync(hub, db, offer, ct); // the same request sent again is not announced again
            return Answer(result, created: true);
        });
        group.MapGet("/offers", async (string? box, bool? pending, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await ItemTransfers.ListAsync(db, me, box != "outgoing", pending ?? true, t.GetUtcNow(), ct)));
        group.MapGet("/transfers", async ([AsParameters] TransferQuery q, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var (total, lines) = await ItemTransferHistory.QueryAsync(db, me, q, ct); var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize <= 0 ? 25 : q.PageSize, 1, ItemTransferHistory.MaxPageSize);
            return Results.Ok(new { page, pageSize = size, total, hasMore = (long)page * size < total, items = lines });
        });
        group.MapPost("/offers/{offerId:guid}/accept", async (Guid offerId, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) => await Respond(c, db, hub, t, ct, (me, now) => ItemTransfers.AcceptAsync(db, me, offerId, now, ct)));
        group.MapPost("/offers/{offerId:guid}/reject", async (Guid offerId, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) => await Respond(c, db, hub, t, ct, (me, now) => ItemTransfers.RejectAsync(db, me, offerId, now, ct)));
        group.MapPost("/offers/{offerId:guid}/cancel", async (Guid offerId, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) => await Respond(c, db, hub, t, ct, (me, now) => ItemTransfers.CancelAsync(db, me, offerId, now, ct)));
        group.MapGet("/integrity", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await InventoryIntegrity.CheckAsync(db, me, ct)));
        group.MapPost("/integrity/repair", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await InventoryIntegrity.RepairAsync(db, me, t.GetUtcNow(), ct)));
        return endpoints;
    }

    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(15);

    /// <summary>Runs an answer to an offer and, when it just changed the offer, tells the other person.</summary>
    private static async Task<IResult> Respond(HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct, Func<Guid, DateTimeOffset, Task<OfferResult>> action)
    {
        if (!Who(c, out var me)) return Results.Unauthorized();
        var now = t.GetUtcNow(); var result = await action(me, now);
        if (result.Ok && result.Offer is { } offer && offer.Status is "accepted" or "rejected" or "cancelled" && offer.RespondedAt is { } at && now - at < Fresh) await ItemOfferNotifier.AnsweredAsync(hub, db, offer, offer.Status, ct);
        return Answer(result);
    }
    private static IResult Answer(OfferResult r, bool created = false) => r.Ok ? (created ? Results.Created($"/api/v1/inventory/offers/{r.Offer!.Id}", new { r.Offer.Id, r.Offer.Status, r.Offer.ExpiresAt }) : Results.Ok(new { r.Offer?.Id, r.Offer?.Status, itemId = r.Item?.Id })) : r.Error switch
    {
        "not_found" or "recipient_not_found" => Results.NotFound(new { error = r.Error }),
        "not_allowed" or "keep_copy_needs_copy" => Results.Json(new { error = r.Error }, statusCode: StatusCodes.Status403Forbidden),
        "stale" or "item_busy" or "offer_closed" or "offer_expired" or "item_unavailable" or "inventory_full" or "key_reused" => Results.Conflict(new { error = r.Error }),
        "too_many_offers" or "recipient_full" => Results.StatusCode(StatusCodes.Status429TooManyRequests),
        _ => Results.BadRequest(new { error = r.Error }),
    };

    private static bool Who(HttpContext c, out Guid user) => Guid.TryParse(c.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out user);
}
