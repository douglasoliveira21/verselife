using System;
using System.Collections.Generic;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class SocialHubTests
    {
        [Test]
        public void CreatingAGroupChecksEachStep()
        {
            var d = new GroupsHubWindow.Draft { Name = "ab" }; Assert.IsNotNull(GroupsHubWindow.StepProblem(0, d)); d.Name = "Construtores"; d.Tag = "x"; Assert.IsNotNull(GroupsHubWindow.StepProblem(0, d)); d.Tag = "cons"; Assert.IsNull(GroupsHubWindow.StepProblem(0, d));
            d.Privacy = "private"; d.Policy = "open"; Assert.IsNotNull(GroupsHubWindow.StepProblem(1, d)); d.Policy = "invite"; Assert.IsNull(GroupsHubWindow.StepProblem(1, d)); d.Privacy = "public"; d.Policy = "request"; d.Fee = 10; Assert.IsNotNull(GroupsHubWindow.StepProblem(1, d)); d.Policy = "open"; Assert.IsNull(GroupsHubWindow.StepProblem(1, d));
            d.Limit = 1; Assert.IsNotNull(GroupsHubWindow.StepProblem(2, d)); d.Limit = 50; d.Cover = "http://x"; Assert.IsNotNull(GroupsHubWindow.StepProblem(2, d)); d.Cover = ""; Assert.IsNull(GroupsHubWindow.StepProblem(2, d));
            Assert.AreEqual(4, GroupsHubWindow.Steps.Length); Assert.AreEqual(10, GroupProfileWindow.Sections.Length);
        }

        [Test]
        public void GroupRefusalsAndAmountsAreWords()
        {
            Assert.AreEqual("O líder não pode sair: passe a liderança antes (Configurações).", GroupsHubWindow.Words(409, "{\"error\":\"leader_cannot_leave\"}")); Assert.AreEqual("Entrada livre", GroupsHubWindow.PolicyWords("open"));
            Assert.AreEqual(1500, GroupProfileWindow.AmountOf(" 1.500 ")); Assert.IsNull(GroupProfileWindow.AmountOf("0")); Assert.IsNull(GroupProfileWindow.AmountOf("abc")); Assert.IsNull(GroupProfileWindow.AmountOf("-5"));
        }

        [Test]
        public void ProposalsAreChecked()
        {
            Assert.IsNull(GroupProfileWindow.ProposalProblem("Festa?", new[] { "Sim", "Não" }, 72)); Assert.IsNotNull(GroupProfileWindow.ProposalProblem("", new[] { "Sim", "Não" }, 72));
            Assert.IsNotNull(GroupProfileWindow.ProposalProblem("Festa?", new[] { "Sim" }, 72)); Assert.IsNotNull(GroupProfileWindow.ProposalProblem("Festa?", new[] { "Sim", "sim" }, 72)); Assert.IsNotNull(GroupProfileWindow.ProposalProblem("Festa?", new[] { "Sim", "Não" }, 0)); Assert.IsNotNull(GroupProfileWindow.ProposalProblem("Festa?", new[] { "Sim", "Não" }, 24 * 31));
        }

        [Test]
        public void WalletWordsAndMoney()
        {
            Assert.AreEqual("R$ 19,90", WalletHubWindow.Reais(1990)); Assert.AreEqual("5%", WalletHubWindow.Percent(500)); Assert.AreEqual("2,5%", WalletHubWindow.Percent(250));
            Assert.AreEqual("esperando o pagamento", WalletHubWindow.StatusWords("pending")); Assert.AreEqual("A compra de VerseCoin ainda não está ligada neste servidor.", WalletHubWindow.Words(503, "{\"error\":\"payments_not_configured\"}")); Assert.AreEqual("Mercado", WalletHubWindow.KindWords("market"));
        }

        [Test]
        public void ProfilePostsAndGalleryOrder()
        {
            Assert.IsNotNull(ProfileHubWindow.PostProblem("  ", null, "public")); Assert.IsNull(ProfileHubWindow.PostProblem("", Guid.NewGuid(), "friends")); Assert.IsNotNull(ProfileHubWindow.PostProblem("oi", null, "todos")); Assert.IsNotNull(ProfileHubWindow.PostProblem(new string('a', 2001), null, "public"));
            Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(); var order = new List<Guid> { a, b, c };
            CollectionAssert.AreEqual(new[] { b, a, c }, ProfileHubWindow.Moved(order, b, -1)); CollectionAssert.AreEqual(new[] { a, b, c }, ProfileHubWindow.Moved(order, a, -1)); CollectionAssert.AreEqual(new[] { a, c, b }, ProfileHubWindow.Moved(order, b, 1));
            Assert.AreEqual("ausente", ProfileHubWindow.PresenceWords("away"));
        }
    }
}
