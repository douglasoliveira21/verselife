using LifeVerse.Api.Modules.Worlds;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What a Pay click on an object offers (the amount to start with and up to four quick buttons) lives in the properties of the object.</summary>
public sealed class BuildPayPriceTests
{
    [Fact]
    public void NothingIsSetByDefault() { var pay = BuildSharing.PayPrice(null); Assert.Equal(0, pay.Default); Assert.Empty(pay.Quick); }

    [Fact]
    public void TheDefaultAndTheQuickButtonsAreKept()
    {
        var json = BuildSharing.WithPay("{\"anyoneMove\":true}", 25, [10, 25, 50, 100]);
        var pay = BuildSharing.PayPrice(json); Assert.Equal(25, pay.Default); Assert.Equal([10, 25, 50, 100], pay.Quick); Assert.True(BuildSharing.AnyoneMove(json));      // other properties stay
    }

    [Fact]
    public void AtMostFourDistinctPositiveButtons() => Assert.Equal([5, 7, 9, 11], BuildSharing.PayPrice(BuildSharing.WithPay(null, null, [5, 5, 0, -3, 7, 9, 11, 13])).Quick);

    [Fact]
    public void ZeroClearsTheDefaultAndAnEmptyListClearsTheButtons()
    {
        var json = BuildSharing.WithPay(null, 40, [1, 2]); json = BuildSharing.WithPay(json, 0, []);
        var pay = BuildSharing.PayPrice(json); Assert.Equal(0, pay.Default); Assert.Empty(pay.Quick);
    }

    [Fact]
    public void NotTouchingKeepsWhatWasThere() { var pay = BuildSharing.PayPrice(BuildSharing.WithPay(BuildSharing.WithPay(null, 30, [3]), null, null)); Assert.Equal(30, pay.Default); Assert.Equal([3], pay.Quick); }
}
