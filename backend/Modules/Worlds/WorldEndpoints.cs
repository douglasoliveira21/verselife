using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record CreateIslandRequest(string Name, string Slug, bool IsPublic);
public sealed record IslandResponse(Guid Id, string Name, string Slug, bool IsPublic);
public sealed record IslandEntryResponse(string Token, string Host, int Port, DateTimeOffset ExpiresAt, int ProtocolVersion);

public static class WorldEndpoints
{
    public static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds").WithTags("Worlds");
        group.MapGet("/islands", async (LifeVerseDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Islands.AsNoTracking().Where(x => x.DeletedAt == null && x.IsPublic)
                .Select(x => new IslandResponse(x.Id, x.Name, x.Slug, x.IsPublic)).ToListAsync(ct)));
        group.MapPost("/islands", CreateAsync).RequireAuthorization().Produces<IslandResponse>(201).ProducesValidationProblem().ProducesProblem(409);
        group.MapPost("/islands/{islandId:guid}/entry-token", RequestEntryAsync).RequireAuthorization()
            .Produces<IslandEntryResponse>().ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        return endpoints;
    }

    private static async Task<IResult> RequestEntryAsync(Guid islandId, HttpContext context, LifeVerseDbContext db,
        IslandEntryTokenService tokens, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ||
            !Guid.TryParse(context.User.FindFirst("sid")?.Value, out var sessionId)) return Results.Unauthorized();
        var now = timeProvider.GetUtcNow();
        var sessionValid = await db.Sessions.AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct);
        if (!sessionValid) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        if (!island.IsPublic && island.OwnerUserId != userId) return Results.Forbid();
        var user = await db.Users.AsNoTracking().Include(x => x.Avatar).SingleAsync(x => x.Id == userId, ct);
        if (user.Avatar is null) return Results.Problem(statusCode: 409, title: "Avatar is required before entering an island.");
        var server = await db.ServerInstances.AsNoTracking()
            .Where(x => x.IslandId == islandId && x.StoppedAt == null && x.Status == "healthy" && x.LastHeartbeatAt > now.AddSeconds(-45))
            .OrderByDescending(x => x.LastHeartbeatAt).FirstOrDefaultAsync(ct);
        if (server is null) return Results.Problem(statusCode: 503, title: "No healthy island server is available.");
        var issued = tokens.Create(user, user.Avatar, island, sessionId);
        return Results.Ok(new IslandEntryResponse(issued.Token, server.Hostname, server.Port, issued.Identity.ExpiresAt, issued.Identity.ProtocolVersion));
    }

    private static async Task<IResult> CreateAsync(CreateIslandRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return Results.Unauthorized();
        var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 3 or > 80) errors["name"] = ["Name must contain 3-80 characters."];
        if (slug.Length is < 3 or > 80 || slug.Any(c => !char.IsLetterOrDigit(c) && c != '-')) errors["slug"] = ["Slug must contain lowercase letters, numbers, or hyphens."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.Islands.AnyAsync(x => x.Slug == slug && x.DeletedAt == null, ct)) return Results.Conflict();
        var now = timeProvider.GetUtcNow();
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = userId, Name = name, Slug = slug, IsPublic = request.IsPublic, CreatedAt = now, UpdatedAt = now };
        db.Islands.Add(island);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/worlds/islands/{island.Id}", new IslandResponse(island.Id, island.Name, island.Slug, island.IsPublic));
    }
}
