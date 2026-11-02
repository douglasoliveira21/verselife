namespace LifeVerse.Api.Modules.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt issuer and audience are required.");
        if (SigningKey.Length < 32 || SigningKey.Contains("CONFIGURE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Jwt signing key must have at least 32 characters.");
        if (AccessTokenMinutes is < 1 or > 60 || RefreshTokenDays is < 1 or > 90)
            throw new InvalidOperationException("Jwt token lifetimes are outside supported limits.");
    }
}
