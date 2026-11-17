using LifeVerse.Api.Modules.Economy;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The packs of VerseCoin: the default list, the bonus in whole coins, and the packs the administrator configures.</summary>
public sealed class CoinPackageTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void WithoutConfigurationTheDefaultPacksAreOffered()
    {
        var packs = CoinPackages.For(Config([]));
        Assert.Equal(5, packs.Count);
        Assert.Equal(["inicial", "basico", "intermediario", "avancado", "premium"], packs.Select(p => p.Id));
    }

    [Theory]
    [InlineData("intermediario", 2500, 250, 2750)]
    [InlineData("avancado", 5000, 750, 5750)]
    [InlineData("premium", 10000, 2000, 12000)]
    [InlineData("inicial", 500, 0, 500)]
    public void TheBonusIsWholeCoinsAndTheTotalAddsUp(string id, long coins, long bonus, long total)
    {
        var pack = CoinPackages.Defaults.Single(p => p.Id == id);
        Assert.Equal((coins, bonus, total), (pack.Coins, pack.BonusCoins, pack.TotalCoins));
    }

    [Fact]
    public void ConfiguredPacksReplaceTheDefaults()
    {
        var packs = CoinPackages.For(Config(new() { ["Economy:CoinPackages:0:Id"] = "teste", ["Economy:CoinPackages:0:Name"] = "Teste", ["Economy:CoinPackages:0:Coins"] = "100", ["Economy:CoinPackages:0:PriceCents"] = "150", ["Economy:CoinPackages:0:BonusPercent"] = "5" }));
        var only = Assert.Single(packs);
        Assert.Equal((100L, 5L, 105L), (only.Coins, only.BonusCoins, only.TotalCoins));
    }

    [Fact]
    public void InvalidConfiguredPacksFallBackToTheDefaults()
    {
        var packs = CoinPackages.For(Config(new() { ["Economy:CoinPackages:0:Id"] = "ruim", ["Economy:CoinPackages:0:Name"] = "Ruim", ["Economy:CoinPackages:0:Coins"] = "0", ["Economy:CoinPackages:0:PriceCents"] = "100", ["Economy:CoinPackages:0:BonusPercent"] = "0" }));
        Assert.Equal(CoinPackages.Defaults.Length, packs.Count);
    }
}
