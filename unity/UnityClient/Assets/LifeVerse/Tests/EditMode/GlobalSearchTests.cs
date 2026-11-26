using System;
using System.Collections.Generic;
using System.Text.Json;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class GlobalSearchTests
    {
        [Test]
        public void TabsSearchTheirKinds()
        {
            Assert.AreEqual(0, GlobalSearchWindow.TypesOf("tudo").Length); CollectionAssert.AreEqual(new[] { "island", "parcel", "poi" }, GlobalSearchWindow.TypesOf("lugares")); CollectionAssert.AreEqual(new[] { "product" }, GlobalSearchWindow.TypesOf("itens"));
            Assert.AreEqual("Você não pode denunciar o que é seu.", GlobalSearchWindow.Words(400, "{\"error\":\"own_item\"}")); Assert.AreEqual("Denúncias demais seguidas; espere um pouco.", GlobalSearchWindow.Words(429, ""));
            Assert.AreEqual("Ponto de interesse", GlobalSearchWindow.KindWords("poi")); Assert.AreEqual(9, GlobalSearchWindow.Sections.Length);
        }

        [Test]
        public void SearchAddressesCarryTheFilters()
        {
            Assert.AreEqual("/api/v1/search?page=1&pageSize=20", GlobalSearchWindow.SearchPath("", null, 0, ""));
            Assert.AreEqual("/api/v1/search?page=2&pageSize=20&q=casa%20azul&types=island,parcel,poi&sort=popularity", GlobalSearchWindow.SearchPath(" casa azul ", GlobalSearchWindow.TypesOf("lugares"), 2, "popularity"));
            var items = GlobalSearchWindow.SearchPath("sofá", new[] { "product" }, 1, "price_asc", "10", "x", "copy,modify", "Bia", false);
            StringAssert.Contains("&minPrice=10", items); StringAssert.DoesNotContain("maxPrice", items); StringAssert.Contains("&rights=copy,modify", items); StringAssert.Contains("&creator=Bia", items);
            StringAssert.EndsWith("&free=true", GlobalSearchWindow.SearchPath("", new[] { "event" }, 1, "start", free: true));
        }

        [Test]
        public void TheHistoryKeepsTheLastTenWithoutRepeats()
        {
            var history = new List<string>(); for (var i = 0; i < 12; i++) history = GlobalSearchWindow.Remember(history, "busca " + i);
            Assert.AreEqual(10, history.Count); Assert.AreEqual("busca 11", history[0]);
            history = GlobalSearchWindow.Remember(history, "BUSCA 5"); Assert.AreEqual("BUSCA 5", history[0]); Assert.AreEqual(10, history.Count); Assert.AreEqual(1, history.FindAll(h => h.Equals("busca 5", StringComparison.OrdinalIgnoreCase)).Count);
            Assert.AreEqual(10, GlobalSearchWindow.Remember(history, "  ").Count);
        }

        [Test]
        public void LinksGoAndComeBack()
        {
            var id = Guid.NewGuid();
            Assert.AreEqual("lifeverse://person/" + id, GlobalSearchWindow.LinkOf("person", id)); Assert.AreEqual("lifeverse://item/" + id, GlobalSearchWindow.LinkOf("product", id));
            var place = GlobalSearchWindow.LinkOf("parcel", id, 12.34f, -5f); Assert.AreEqual("lifeverse://island/" + id + "?x=12.3&z=-5", place);
            Assert.IsTrue(GlobalSearchWindow.TryParseLink(place, out var kind, out var back, out var x, out var z)); Assert.AreEqual("island", kind); Assert.AreEqual(id, back); Assert.AreEqual(12.3f, x.Value, 1e-4); Assert.AreEqual(-5f, z.Value);
            Assert.IsFalse(GlobalSearchWindow.TryParseLink("https://evil.example/" + id, out _, out _, out _, out _)); Assert.IsFalse(GlobalSearchWindow.TryParseLink("lifeverse://admin/" + id, out _, out _, out _, out _)); Assert.IsFalse(GlobalSearchWindow.TryParseLink("lifeverse://person/" + id + "/../x", out _, out _, out _, out _));
        }

        [Test]
        public void AResultIsDescribed()
        {
            using var d = JsonDocument.Parse("{\"kind\":\"product\",\"kindLabel\":\"Produto\",\"subtitle\":\"Loja da Bia\",\"category\":\"móveis\",\"priceCoins\":120,\"rating\":\"general\"}");
            Assert.AreEqual("Produto · Loja da Bia · móveis · 120 VC", GlobalSearchWindow.Describe(d.RootElement));
            using var e = JsonDocument.Parse("{\"kind\":\"event\",\"priceCoins\":0,\"rating\":\"mature\"}"); Assert.AreEqual("Evento · grátis · adulto", GlobalSearchWindow.Describe(e.RootElement));
        }
    }
}
