using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/users/me", GetMeAsync).RequireAuthorization().WithTags("Users")
            .Produces<UserSummary>().ProducesProblem(401).ProducesProblem(404);
        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().Include(x => x.Avatar)
            .SingleOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, ct);
        return user is null ? Results.NotFound() : Results.Ok(new UserSummary(user.Id, user.Username, user.Email, user.Avatar?.DisplayName ?? user.Username));
    }
}
