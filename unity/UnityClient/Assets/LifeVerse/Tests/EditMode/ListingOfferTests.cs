using System;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class ListingOfferTests
    {
        [Test]
        public void ASaleNeedsAPositivePrice()
        {
            StringAssert.Contains("\"price\":4500", ListingOfferWindow.SaleBody("4.500", true)); StringAssert.Contains("\"includeObjects\":true", ListingOfferWindow.SaleBody("10", true));
            Assert.IsNull(ListingOfferWindow.SaleBody("", false)); Assert.IsNull(ListingOfferWindow.SaleBody("0", false)); Assert.IsNull(ListingOfferWindow.SaleBody("-5", false)); Assert.IsNull(ListingOfferWindow.SaleBody("abc", false)); Assert.IsNull(ListingOfferWindow.SaleBody(null, false));
        }

        [Test]
        public void ARentalIsCheckedBeforeItIsSent()
        {
            var ok = ListingOfferWindow.RentBody("300", "7", "2", "8", "100", true); StringAssert.Contains("\"pricePerPeriod\":300", ok); StringAssert.Contains("\"periodDays\":7", ok); StringAssert.Contains("\"minPeriods\":2", ok); StringAssert.Contains("\"maxPeriods\":8", ok); StringAssert.Contains("\"depositCoins\":100", ok); StringAssert.Contains("\"autoRenewAllowed\":true", ok);
            Assert.IsNull(ListingOfferWindow.RentBody("0", "7", "1", "1", "0", false), "price"); Assert.IsNull(ListingOfferWindow.RentBody("10", "0", "1", "1", "0", false), "days below 1"); Assert.IsNull(ListingOfferWindow.RentBody("10", "91", "1", "1", "0", false), "days above 90");
            Assert.IsNull(ListingOfferWindow.RentBody("10", "7", "0", "1", "0", false), "min below 1"); Assert.IsNull(ListingOfferWindow.RentBody("10", "7", "5", "3", "0", false), "max under min"); Assert.IsNull(ListingOfferWindow.RentBody("10", "7", "1", "1", "-1", false), "negative deposit"); Assert.IsNull(ListingOfferWindow.RentBody("x", "7", "1", "1", "0", false));
        }
    }
}
