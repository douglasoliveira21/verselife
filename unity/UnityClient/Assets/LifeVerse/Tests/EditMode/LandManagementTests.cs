using System;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class LandManagementTests
    {
        [Test]
        public void RefusalsOfTheLandAreWords()
        {
            Assert.AreEqual("Essa área já pertence a outro terreno.", LandManagementWindow.Words(409, "{\"error\":\"overlap\"}"));
            Assert.AreEqual("O preço mudou: confira e tente de novo.", LandManagementWindow.Words(409, "{\"error\":\"price_changed\"}"));
            Assert.IsNotEmpty(LandManagementWindow.Words(500, "oops"));
        }

        [Test]
        public void AClaimIsChecked()
        {
            Assert.IsNotNull(LandManagementWindow.ClaimProblem("a", 3, "", out _)); Assert.IsNotNull(LandManagementWindow.ClaimProblem("Casa", 0, "", out _)); Assert.IsNotNull(LandManagementWindow.ClaimProblem("Casa", 3, "x", out _));
            Assert.IsNull(LandManagementWindow.ClaimProblem("Casa", 3, "", out var none)); Assert.IsNull(none);
            Assert.IsNull(LandManagementWindow.ClaimProblem("Casa", 3, " 40 ", out var max)); Assert.AreEqual(40, max);
        }

        [Test]
        public void KindsAndCategoriesHaveWords()
        {
            Assert.AreEqual("à venda", LandManagementWindow.KindWords("sale")); Assert.AreEqual("Área comum", LandManagementWindow.CategoryWords("comum")); Assert.AreEqual("em vigor", LandManagementWindow.ContractWords("active"));
            Assert.AreEqual("dividiu", LandManagementWindow.WhatWords("split")); Assert.AreEqual("xyz", LandManagementWindow.WhatWords("xyz")); Assert.AreEqual(10, LandManagementWindow.Sections.Length); Assert.AreEqual(8, ParcelInfoWindow.Sections.Length);
            Assert.AreNotEqual(LandManagementWindow.KindColor("sale"), LandManagementWindow.KindColor("rented"));
        }

        [Test]
        public void TheArrivalIsChecked()
        {
            Assert.IsNull(ParcelInfoWindow.ArrivalProblem("", "", "90", out var x, out var z, out var yaw)); Assert.IsNull(x); Assert.IsNull(z); Assert.AreEqual(90, yaw);
            Assert.IsNull(ParcelInfoWindow.ArrivalProblem("12,5", "-3", "", out x, out z, out _)); Assert.AreEqual(12.5, x); Assert.AreEqual(-3, z);
            Assert.IsNotNull(ParcelInfoWindow.ArrivalProblem("1", "", "0", out _, out _, out _)); Assert.IsNotNull(ParcelInfoWindow.ArrivalProblem("a", "b", "0", out _, out _, out _)); Assert.IsNotNull(ParcelInfoWindow.ArrivalProblem("1", "2", "400", out _, out _, out _));
        }

        [Test]
        public void TheParcelUnderASpotIsFound()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var land = "{\"parcels\":[{\"id\":\"" + a + "\",\"runs\":[[0,0,2]]},{\"id\":\"" + b + "\",\"runs\":[[-1,-3,-1]]},{\"id\":\"x\"}]}";
            Assert.AreEqual(a, ParcelInfoWindow.ParcelAt(land, 5f, 1f));       // cell (1, 0)
            Assert.AreEqual(b, ParcelInfoWindow.ParcelAt(land, -6f, -2f));     // cell (-2, -1)
            Assert.AreEqual(Guid.Empty, ParcelInfoWindow.ParcelAt(land, 40f, 40f)); Assert.AreEqual(Guid.Empty, ParcelInfoWindow.ParcelAt("nada", 0, 0));
        }
    }
}
