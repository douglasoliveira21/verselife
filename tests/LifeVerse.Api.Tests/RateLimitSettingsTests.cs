using LifeVerse.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class RateLimitSettingsTests
{
    private static IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();

    [Fact]
    public void ThePlayersOfOneAddressHaveRoomByDefault() { Assert.Equal(30, RateLimitSettings.AuthPerMinute(Config())); Assert.Equal(120, RateLimitSettings.RefreshPerMinute(Config())); }

    [Fact]
    public void TheAdministratorCanChangeThemAndBadValuesAreIgnored()
    {
        Assert.Equal(60, RateLimitSettings.AuthPerMinute(Config(("RateLimits:AuthPerMinute", "60")))); Assert.Equal(500, RateLimitSettings.RefreshPerMinute(Config(("RateLimits:RefreshPerMinute", "500"))));
        foreach (var bad in new[] { "0", "-3", "abc", "", "10001" }) Assert.Equal(30, RateLimitSettings.AuthPerMinute(Config(("RateLimits:AuthPerMinute", bad))));
    }
}
