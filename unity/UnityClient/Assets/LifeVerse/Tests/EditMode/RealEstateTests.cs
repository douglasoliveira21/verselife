using System;
using System.Linq;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class RealEstateTests
    {
        private static readonly string Island = Guid.NewGuid().ToString(), Parcel = Guid.NewGuid().ToString();

        [Test]
        public void ListingsAreReadAndPricedInWords()
        {
            var page = MarketPage.Parse("{\"total\":20,\"page\":2,\"pageSize\":9,\"items\":[{\"parcelId\":\"" + Parcel + "\",\"islandId\":\"" + Island + "\",\"islandName\":\"Praia\",\"code\":\"A-1\",\"name\":\"Lote\",\"category\":\"comercial\",\"area\":1200,\"maxObjects\":300,\"offer\":\"rent\",\"state\":\"rent\",\"price\":1500,\"rentPeriodDays\":30,\"canVisit\":true,\"rentMinPeriods\":2,\"rentMaxPeriods\":6},{\"nada\":1}]}");
            Assert.AreEqual(1, page.Items.Count); Assert.AreEqual(3, page.Pages); Assert.AreEqual(2, page.Page); var item = page.Items[0];
            Assert.AreEqual("A-1 — Lote", item.Title); Assert.AreEqual("Terreno Comercial", item.TypeText); Assert.AreEqual("VC$ 1.500 / mensal", item.PriceText); Assert.IsTrue(item.CanVisit); Assert.AreEqual(2, item.RentMinPeriods); StringAssert.EndsWith("/land/parcels/" + Parcel, item.Path);
            Assert.IsNull(MarketPage.Parse("oops")); Assert.IsNull(MarketPage.Parse("{}")); Assert.AreEqual(0, MarketPage.Parse("{\"items\":[]}").Items.Count);
        }

        [Test]
        public void IslandsHaveTheirOwnPathAndLabel()
        {
            var item = new MarketListing { Kind = "island", IslandId = Guid.Parse(Island), Region = "homestead", Offer = "sale", Price = 90000 }; Assert.AreEqual("Ilha Homestead", item.TypeText); StringAssert.EndsWith("/real-estate", item.Path); Assert.AreEqual("VC$ 90.000", item.PriceText);
        }

        [Test]
        public void EachTabAsksForItsOwnKindOfListing()
        {
            var f = new MarketFilter(); f.ApplyTab("ilhas_venda"); StringAssert.Contains("type=island", f.ToQuery()); StringAssert.Contains("offer=sale", f.ToQuery());
            f.ApplyTab("comerciais"); StringAssert.Contains("category=comercial", f.ToQuery()); StringAssert.Contains("type=parcel", f.ToQuery()); f.ApplyTab("homesteads"); StringAssert.Contains("region=homestead", f.ToQuery());
            f.ApplyTab("todos"); Assert.IsFalse(f.ToQuery().Contains("type=")); f.Query = " praia azul "; f.MaxPrice = 5000; f.Page = 3; StringAssert.Contains("q=praia%20azul", f.ToQuery()); StringAssert.Contains("maxPrice=5000", f.ToQuery()); StringAssert.Contains("page=3", f.ToQuery());
            Assert.AreEqual(1500L, MarketFilter.Money("1.500")); Assert.IsNull(MarketFilter.Money("abc")); Assert.IsNull(MarketFilter.Money("-5"));
        }

        [Test]
        public void RefusalsAreExplained()
        {
            StringAssert.Contains("preço mudou", RealEstateMarketWindow.Words(409, "{\"code\":\"price_changed\"}")); StringAssert.Contains("Saldo", RealEstateMarketWindow.Words(409, "{\"code\":\"insufficient_funds\"}")); Assert.IsNotEmpty(RealEstateMarketWindow.Words(500, "oops"));
        }

        [Test]
        public void PropertiesAreReadWithTheirSummary()
        {
            var json = "{\"summary\":{\"total\":3,\"inUse\":2,\"rentedOut\":1,\"monthlyRevenue\":4000,\"expiringSoon\":1},\"islands\":[{\"id\":\"" + Island + "\",\"name\":\"Minha ilha\",\"parcels\":4,\"salePrice\":9000}],\"parcels\":[{\"id\":\"" + Parcel + "\",\"islandId\":\"" + Island + "\",\"code\":\"B-2\",\"name\":\"Lote\",\"area\":900,\"rentPrice\":200,\"canVisit\":true}],"
                + "\"contracts\":[{\"id\":\"" + Guid.NewGuid() + "\",\"islandId\":\"" + Island + "\",\"parcel\":\"Lote\",\"role\":\"tenant\",\"landlord\":\"Ana\",\"price\":300,\"periodDays\":7,\"status\":\"grace\",\"expiresAt\":\"2026-12-01T00:00:00Z\"}],\"payments\":[{\"type\":\"rent_payment\",\"direction\":\"out\",\"amount\":300,\"parcel\":\"Lote\"}],\"incomingTransfers\":[{\"islandId\":\"" + Island + "\",\"name\":\"Ilha X\",\"from\":\"Caio\"}]}";
            var data = PropertiesData.Parse(json); Assert.AreEqual(3, data.Total); Assert.AreEqual(4000, data.MonthlyRevenue); Assert.AreEqual(1, data.Islands.Count); Assert.AreEqual(9000L, data.Islands[0].SalePrice); Assert.AreEqual("B-2 — Lote", data.Parcels[0].Title);
            Assert.IsTrue(data.Contracts[0].Live); Assert.AreEqual("Em tolerância", PropertiesData.StatusText("grace")); Assert.IsFalse(data.Payments[0].Incoming); Assert.AreEqual("Pagamento de aluguel", PropertiesData.PaymentText("rent_payment")); Assert.IsFalse(data.Transfers[0].IsParcel);
            Assert.IsNull(PropertiesData.Parse("{}")); Assert.IsNull(PropertiesData.Parse("oops"));
        }
    }
}
