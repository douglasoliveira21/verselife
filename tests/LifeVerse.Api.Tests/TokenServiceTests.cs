using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void Access_token_has_configured_expiration()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new TokenService(new JwtOptions
        {
            Issuer = "test-issuer", Audience = "test-audience",
            SigningKey = "a-test-signing-key-that-is-at-least-32-characters",
            AccessTokenMinutes = 15, RefreshTokenDays = 30
        }, new FixedTimeProvider(now));
        var user = new User
        {
            Id = Guid.NewGuid(), Username = "test", NormalizedUsername = "TEST",
            Email = "test@example.test", NormalizedEmail = "TEST@EXAMPLE.TEST",
            PasswordHash = "unused", CreatedAt = now, UpdatedAt = now
        };

        var result = service.CreateAccessToken(user, Guid.NewGuid());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal(now.AddMinutes(15), result.ExpiresAt);
        Assert.Equal(result.ExpiresAt.UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public void Refresh_tokens_are_random_and_hash_without_storing_plaintext()
    {
        var service = new TokenService(new JwtOptions
        {
            Issuer = "test", Audience = "test", SigningKey = new string('x', 32)
        }, TimeProvider.System);
        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();
        Assert.NotEqual(first, second);
        Assert.Equal(64, TokenService.HashRefreshToken(first).Length);
        Assert.DoesNotContain(first, TokenService.HashRefreshToken(first));
    }

    [Fact]
    public void Expired_access_token_is_rejected()
    {
        var options = new JwtOptions
        {
            Issuer = "test-issuer", Audience = "test-audience",
            SigningKey = "a-test-signing-key-that-is-at-least-32-characters",
            AccessTokenMinutes = 15
        };
        var then = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            Id = Guid.NewGuid(), Username = "test", NormalizedUsername = "TEST",
            Email = "test@example.test", NormalizedEmail = "TEST@EXAMPLE.TEST",
            PasswordHash = "unused", CreatedAt = then, UpdatedAt = then
        };
        var token = new TokenService(options, new FixedTimeProvider(then)).CreateAccessToken(user, Guid.NewGuid()).Token;
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = options.Issuer,
            ValidateAudience = true, ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenExpiredException>(() => new JwtSecurityTokenHandler().ValidateToken(token, validation, out _));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
