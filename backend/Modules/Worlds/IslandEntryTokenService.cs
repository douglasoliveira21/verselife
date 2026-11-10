using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LifeVerse.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace LifeVerse.Api.Modules.Worlds;

public sealed class IslandEntryOptions
{
    public const string SectionName = "IslandEntry";
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int LifetimeSeconds { get; init; } = 45;
    public int ProtocolVersion { get; init; } = 1;

    public void Validate()
    {
        if (SigningKey.Length < 32 || SigningKey.Contains("CONFIGURE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("IslandEntry:SigningKey must be a secure value of at least 32 characters.");
        if (LifetimeSeconds is < 10 or > 120 || ProtocolVersion < 1)
            throw new InvalidOperationException("Island entry lifetime or protocol version is invalid.");
    }
}

public sealed record IslandEntryIdentity(
    Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId, int ProtocolVersion, DateTimeOffset ExpiresAt, Guid? TransferId = null);

public sealed class IslandEntryTokenService(IslandEntryOptions options, TimeProvider timeProvider)
{
    private readonly TokenValidationParameters _validation = new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(3)
    };

    /// <summary>A token to enter an island. With a transfer id it is the ticket of a trip: the API accepts it once, for that trip only.</summary>
    public (string Token, IslandEntryIdentity Identity) Create(User user, Avatar avatar, Island island, Guid sessionId, Guid? transferId = null, Guid? tokenId = null)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddSeconds(options.LifetimeSeconds);
        var jti = tokenId ?? Guid.NewGuid();
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("avatar_id", avatar.Id.ToString()),
            new Claim("island_id", island.Id.ToString()),
            new Claim("sid", sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()),
            new Claim("protocol_version", options.ProtocolVersion.ToString())
        };
        if (transferId is { } transfer) claims.Add(new Claim("transfer_id", transfer.ToString()));
        var descriptor = new JwtSecurityToken(options.Issuer, options.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler { MapInboundClaims = false }.WriteToken(descriptor),
            new IslandEntryIdentity(user.Id, avatar.Id, island.Id, sessionId, jti, options.ProtocolVersion, expires, transferId));
    }

    public IslandEntryIdentity? Validate(string token)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, _validation, out var validated);
            if (validated is not JwtSecurityToken jwt || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ||
                !Guid.TryParse(principal.FindFirst("avatar_id")?.Value, out var avatarId) || !Guid.TryParse(principal.FindFirst("island_id")?.Value, out var islandId) ||
                !Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId) || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, out var tokenId) ||
                !int.TryParse(principal.FindFirst("protocol_version")?.Value, out var protocol)) return null;
            Guid? transfer = Guid.TryParse(principal.FindFirst("transfer_id")?.Value, out var transferId) ? transferId : null;
            return new IslandEntryIdentity(userId, avatarId, islandId, sessionId, tokenId, protocol, new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero), transfer);
        }
        catch (SecurityTokenException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
