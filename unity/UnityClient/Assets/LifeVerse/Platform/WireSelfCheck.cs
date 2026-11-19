#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using LifeVerse.Wire;

namespace LifeVerse.Unity.Platform
{
    /// <summary>
    /// Servidor e cliente LifeVerse Wire na mesma máquina, em UDP, dentro da Unity (editor ou player): admissão, mensagens confiáveis em ordem
    /// e um quadro grande fragmentado. Usado pelos testes de modo Edit e pela autoverificação do player.
    /// </summary>
    public static class WireSelfCheck
    {
        /// <summary>Devolve null quando tudo passou, ou o motivo da falha.</summary>
        public static string? Run()
        {
            using var server = new WireServer(maxPeers: 4);
            if (!server.Start(0, loopbackOnly: true)) return "wire: não abriu a porta";
            using var client = new WireClient();

            var serverInbox = new System.Collections.Generic.List<(int Peer, IWireMessage Message)>();
            server.MessageReceived += (peer, message) => serverInbox.Add((peer, message));
            var clientInbox = new System.Collections.Generic.List<IWireMessage>();
            client.MessageReceived += clientInbox.Add;
            var connected = false; client.Connected += () => connected = true;

            if (!client.Connect("127.0.0.1", server.Port)) return "wire: connect recusado";
            if (!Pump(server, client, () => connected, 8000)) return "wire: tempo esgotado conectando";

            client.Send(new AuthenticateMessage("ticket", WireProtocol.Version));
            if (!Pump(server, client, () => serverInbox.Count >= 1, 8000)) return "wire: tempo esgotado no Authenticate";
            if (serverInbox[0].Message is not AuthenticateMessage { ProtocolVersion: WireProtocol.Version }) return "wire: Authenticate inesperado";
            var peerId = serverInbox[0].Peer;
            server.MarkAuthenticated(peerId);
            server.Send(peerId, new AuthResultMessage(true, "ok", 7));
            if (!Pump(server, client, () => clientInbox.Count >= 1, 8000) || clientInbox[0] is not AuthResultMessage { Success: true, ServerTick: 7 }) return "wire: AuthResult";

            serverInbox.Clear();
            for (var i = 0; i < 100; i++) client.Send(new SendChatMessage("m" + i));
            if (!Pump(server, client, () => serverInbox.Count >= 100, 8000)) return "wire: tempo esgotado nas 100 mensagens";
            for (var i = 0; i < 100; i++) if (serverInbox[i].Message is not SendChatMessage chat || chat.Message != "m" + i) return "wire: ordem das mensagens";

            var terrain = new byte[300_000]; new Random(5).NextBytes(terrain);
            server.Send(peerId, new TerrainFullMessage(terrain, true));
            clientInbox.Clear();
            if (!Pump(server, client, () => clientInbox.Any(m => m is TerrainFullMessage), 8000)) return "wire: tempo esgotado no terreno";
            var received = clientInbox.OfType<TerrainFullMessage>().First();
            if (!received.Data.AsSpan().SequenceEqual(terrain)) return "wire: terreno corrompido";
            return null;
        }

        private static bool Pump(WireServer server, WireClient client, Func<bool> done, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (!done())
            {
                if (watch.ElapsedMilliseconds > timeoutMs) return false;
                server.Poll(); client.Poll();
                System.Threading.Thread.Sleep(1);
            }
            return true;
        }
    }
}
