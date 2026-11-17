using LifeVerse.Api.Modules.Economy;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class EconomyFeesTests
{
    private static IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();

    [Theory]
    [InlineData(1000, 500, 50)]
    [InlineData(19, 500, 0)]        // under whole coins: no fee
    [InlineData(250, 0, 0)]
    [InlineData(1000, 1250, 125)]
    [InlineData(10_000_000_000, 500, 500_000_000)]
    public void TheFeeIsRoundedDownToWholeCoins(long price, long bps, long expected) => Assert.Equal(expected, EconomyFees.Of(price, bps));

    [Fact]
    public void TheRatesComeFromTheConfigurationAndBadValuesFallBack()
    {
        Assert.Equal(200, EconomyFees.Read(Config(("Economy:Fees:MarketplaceBps", "200")), "Economy:Fees:MarketplaceBps"));
        Assert.Equal(0, EconomyFees.Read(Config(("Economy:Fees:MarketplaceBps", "0")), "Economy:Fees:MarketplaceBps"));
        foreach (var bad in new[] { "-1", "5001", "abc", "" }) Assert.Equal(EconomyFees.DefaultBps, EconomyFees.Read(Config(("Economy:Fees:MarketplaceBps", bad)), "Economy:Fees:MarketplaceBps"));
        Assert.Equal(EconomyFees.DefaultBps, EconomyFees.Read(Config(), "Economy:Fees:MarketplaceBps"));
    }

    [Fact]
    public void ConfigureSetsBothRates()
    {
        try
        {
            EconomyFees.Configure(Config(("Economy:Fees:MarketplaceBps", "300"), ("Economy:Fees:ObjectSaleBps", "100")));
            Assert.Equal((300L, 100L), (EconomyFees.MarketplaceBps, EconomyFees.ObjectSaleBps));
        }
        finally { EconomyFees.Configure(Config()); }
        Assert.Equal((500L, 500L), (EconomyFees.MarketplaceBps, EconomyFees.ObjectSaleBps));
    }
}
