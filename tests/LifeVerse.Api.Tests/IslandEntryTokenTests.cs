using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class IslandEntryTokenTests
{
    private static IslandEntryTokenService CreateService(string key) => new(new IslandEntryOptions
    {
        Issuer = "tests",
        Audience = "island-tests",
        SigningKey = key,
        LifetimeSeconds = 45,
        ProtocolVersion = 1
    }, TimeProvider.System);

    [Fact]
    public void EntryToken_RoundTripsBoundIdentity()
    {
        var service = CreateService(new string('a', 48));
        var user = new User { Id = Guid.NewGuid(), Username = "tester", NormalizedUsername = "TESTER", Email = "test@example.test", NormalizedEmail = "TEST@EXAMPLE.TEST", PasswordHash = "unused" };
        var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user.Id, DisplayName = "Tester", BaseModel = "placeholder_humanoid" };
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = user.Id, Name = "Test", Slug = "test" };
        var session = Guid.NewGuid();
        var issued = service.Create(user, avatar, island, session);
        var validated = service.Validate(issued.Token);
        Assert.NotNull(validated);
        Assert.Equal(user.Id, validated.UserId);
        Assert.Equal(avatar.Id, validated.AvatarId);
        Assert.Equal(island.Id, validated.IslandId);
        Assert.Equal(session, validated.SessionId);
    }

    [Fact]
    public void EntryToken_RejectsDifferentSigningKey()
    {
        var issued = CreateService(new string('a', 48)).Create(
            new User { Id = Guid.NewGuid(), Username = "tester", NormalizedUsername = "TESTER", Email = "test@example.test", NormalizedEmail = "TEST@EXAMPLE.TEST", PasswordHash = "unused" },
            new Avatar { Id = Guid.NewGuid(), DisplayName = "Tester", BaseModel = "placeholder_humanoid" },
            new Island { Id = Guid.NewGuid(), Name = "Test", Slug = "test" }, Guid.NewGuid());
        Assert.Null(CreateService(new string('b', 48)).Validate(issued.Token));
    }
}
