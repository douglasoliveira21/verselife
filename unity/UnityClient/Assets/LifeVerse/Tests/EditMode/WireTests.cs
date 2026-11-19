#nullable enable
using System.Linq;
using LifeVerse.Unity.Platform;
using LifeVerse.Wire;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>LifeVerse Wire dentro da Unity (Mono): o mesmo protocolo dos testes xUnit, agora com LiteNetLib carregado pelo editor.</summary>
    public sealed class WireTests
    {
        [Test]
        public void TheProtocolHas44MessagesAndRefusesMalformedFrames()
        {
            Assert.AreEqual(44, WireCodec.Count);
            Assert.AreEqual(22, WireCodec.Messages(WireDirection.ClientToServer).Count);
            Assert.AreEqual(22, WireCodec.Messages(WireDirection.ServerToClient).Count);
            var bytes = WireCodec.Encode(new SubmitInputMessage(5u, 1.5f, -2f, 3));
            var decoded = WireCodec.Decode(bytes, WireDirection.ClientToServer);
            Assert.IsTrue(decoded.Ok);
            Assert.AreEqual(new SubmitInputMessage(5u, 1.5f, -2f, 3), decoded.Message);
            Assert.AreEqual("wrong_direction", WireCodec.Decode(bytes, WireDirection.ServerToClient).Error);
            Assert.AreEqual("malformed", WireCodec.Decode(bytes.Take(bytes.Length - 1).ToArray(), WireDirection.ClientToServer).Error);
            Assert.AreEqual(12, WireProtocol.Version);
        }

        [Test]
        public void ServerAndClientTalkOverUdpOnThisMachine()
        {
            Assert.IsNull(WireSelfCheck.Run());
        }
    }
}
