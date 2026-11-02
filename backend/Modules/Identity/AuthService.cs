using LifeVerse.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeVerse.Api.Modules.Identity;

public sealed class AuthService(
    LifeVerseDbContext db,
    TokenService tokens,
    JwtOptions options,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, HttpContext context, CancellationToken ct)
    {
        var normalizedUsername = request.Username.Trim().ToUpperInvariant();
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(x => x.DeletedAt == null &&
            (x.NormalizedUsername == normalizedUsername || x.NormalizedEmail == normalizedEmail), ct)) return null;

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username.Trim(),
            NormalizedUsername = normalizedUsername,
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        user.Avatar = new Avatar
        {
            Id = Guid.NewGuid(), UserId = user.Id, DisplayName = user.Username, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        var response = CreateSession(user, Guid.NewGuid(), context, now).Response;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
        logger.LogInformation("User {UserId} registered", user.Id);
        return response;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, HttpContext context, CancellationToken ct)
    {
        var normalized = request.Login.Trim().ToUpperInvariant();
        var user = await db.Users.Include(x => x.Avatar).SingleOrDefaultAsync(x => x.DeletedAt == null &&
            (x.NormalizedUsername == normalized || x.NormalizedEmail == normalized), ct);
        if (user is null || _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return null;
        return await PersistSessionAsync(user, Guid.NewGuid(), context, ct);
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken, HttpContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = TokenService.HashRefreshToken(refreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var old = await db.Sessions.AsNoTracking().Include(x => x.User).ThenInclude(x => x.Avatar)
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
        if (old is null || old.ExpiresAt <= now || old.User.DeletedAt != null) return null;

        if (old.RevokedAt != null)
        {
            await db.Sessions.Where(x => x.FamilyId == old.FamilyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            await transaction.CommitAsync(ct);
            logger.LogWarning("Refresh token reuse detected for session family {FamilyId}", old.FamilyId);
            return null;
        }

        var revoked = await db.Sessions.Where(x => x.Id == old.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        if (revoked == 0)
        {
            await db.Sessions.Where(x => x.FamilyId == old.FamilyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            await transaction.CommitAsync(ct);
            logger.LogWarning("Concurrent refresh reuse detected for session family {FamilyId}", old.FamilyId);
            return null;
        }

        var created = CreateSession(old.User, old.FamilyId, context, now);
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(x => x.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplacedBySessionId, created.SessionId), ct);
        await transaction.CommitAsync(ct);
        return created.Response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(refreshToken);
        var now = timeProvider.GetUtcNow();
        await db.Sessions.Where(x => x.RefreshTokenHash == hash && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    private async Task<AuthResponse> PersistSessionAsync(User user, Guid familyId, HttpContext context, CancellationToken ct)
    {
        var response = CreateSession(user, familyId, context, timeProvider.GetUtcNow()).Response;
        await db.SaveChangesAsync(ct);
        return response;
    }

    private (AuthResponse Response, Guid SessionId) CreateSession(User user, Guid familyId, HttpContext context, DateTimeOffset now)
    {
        var refreshToken = tokens.CreateRefreshToken();
        var session = new Session
        {
            Id = Guid.NewGuid(), UserId = user.Id, FamilyId = familyId,
            RefreshTokenHash = TokenService.HashRefreshToken(refreshToken), CreatedAt = now,
            ExpiresAt = now.AddDays(options.RefreshTokenDays),
            UserAgent = context.Request.Headers.UserAgent.ToString()[..Math.Min(context.Request.Headers.UserAgent.ToString().Length, 512)],
            IpAddress = context.Connection.RemoteIpAddress?.ToString()
        };
        db.Sessions.Add(session);
        var access = tokens.CreateAccessToken(user);
        return (new AuthResponse(access.Token, refreshToken, access.ExpiresAt,
            new UserSummary(user.Id, user.Username, user.Email, user.Avatar?.DisplayName ?? user.Username)), session.Id);
    }
}
