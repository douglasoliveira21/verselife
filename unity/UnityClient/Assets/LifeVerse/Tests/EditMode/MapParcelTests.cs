using System;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class MapParcelTests
    {
        private static readonly string A = Guid.NewGuid().ToString(), B = Guid.NewGuid().ToString();
        private static string Json => "{\"islandId\":\"x\",\"size\":512,\"items\":[{\"id\":\"" + A + "\",\"code\":\"A-1\",\"name\":\"Cais\",\"state\":\"sale\",\"category\":\"comercial\",\"area\":900,\"bounds\":{\"minX\":-100,\"minZ\":-50,\"maxX\":-40,\"maxZ\":10},\"spawnX\":-70,\"spawnZ\":-20,\"teleport\":true,\"salePrice\":4500,\"owner\":\"Ana\"},"
            + "{\"id\":\"" + B + "\",\"name\":\"Terreno restrito\",\"state\":\"restricted\",\"bounds\":{\"minX\":0,\"minZ\":0,\"maxX\":30,\"maxZ\":30},\"teleport\":false},{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"sem forma\"},{\"id\":\"" + Guid.NewGuid() + "\",\"bounds\":{\"minX\":5,\"minZ\":5,\"maxX\":5,\"maxZ\":9}}]}";

        [Test]
        public void ParcelsAreReadAndThoseWithoutASquareAreSkipped()
        {
            var list = MapApi.ParseParcels(Json); Assert.AreEqual(2, list.Count); var cais = list[0];
            Assert.AreEqual("Cais", cais.Name); Assert.AreEqual("sale", cais.State); Assert.AreEqual(4500L, cais.SalePrice); Assert.IsNull(cais.RentPrice); Assert.AreEqual("Ana", cais.Owner); Assert.IsTrue(cais.Teleport); Assert.AreEqual(-100, cais.MinX); Assert.AreEqual("À venda", cais.StateWords);
            Assert.IsFalse(list[1].Teleport); Assert.AreEqual("Restrito", list[1].StateWords); Assert.AreEqual(0, MapApi.ParseParcels("oops").Count); Assert.AreEqual(0, MapApi.ParseParcels("{}").Count);
        }

        [Test]
        public void TheParcelUnderASpotIsFound()
        {
            var canvas = new MapCanvas(); var island = Guid.NewGuid(); canvas.Parcels[island] = MapApi.ParseParcels(Json);
            Assert.AreEqual("Cais", canvas.ParcelAt(island, -70, -20).Name); Assert.AreEqual("Terreno restrito", canvas.ParcelAt(island, 15, 15).Name); Assert.IsNull(canvas.ParcelAt(island, 200, 200)); Assert.IsNull(canvas.ParcelAt(Guid.NewGuid(), 0, 0));
        }

        [Test]
        public void EveryStateHasItsOwnColourAndWords()
        {
            var states = new[] { "mine", "group", "restricted", "sale", "rent", "common", "public" };
            for (var i = 0; i < states.Length; i++) for (var j = i + 1; j < states.Length; j++) Assert.AreNotEqual(MapApi.ParcelColor(states[i]), MapApi.ParcelColor(states[j]), states[i] + " " + states[j]);
            Assert.AreEqual("Seu terreno", MapApi.ParcelWords("mine")); Assert.AreEqual("Aberto", MapApi.ParcelWords("qualquer"));
        }
    }
}
