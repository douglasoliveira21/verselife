using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// The live channel of the test API (the HttpListener of the Unity runtime cannot accept WebSockets): a minimal WebSocket server on its own port that checks the path and the token exactly as the API does, answers the
    /// handshake, and sends text frames with <see cref="Push"/>. What the client sends is read only to notice that it left.
    /// </summary>
    public sealed class StubLiveServer : IDisposable
    {
        public const string Path = "/api/v1/notifications/ws";
        private readonly TcpListener _listener; private readonly Func<string> _token; private readonly List<NetworkStream> _open = new List<NetworkStream>(); private readonly List<TcpClient> _clients = new List<TcpClient>();
        public string Url { get; }
        public int Refused { get; private set; }
        public string RefusedWhy { get; private set; } = "";
        public int Connections { get { lock (_open) return _open.Count; } }

        public StubLiveServer(Func<string> token)
        {
            _token = token; _listener = new TcpListener(IPAddress.Loopback, 0); _listener.Start(); Url = "ws://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + Path; _ = Task.Run(Accept);
        }

        private async Task Accept()
        {
            while (true)
            {
                TcpClient client; try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); } catch (Exception) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        public static string AcceptKey(string key) { using var sha = SHA1.Create(); return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))); }

        private async Task Serve(TcpClient client)
        {
            var stream = client.GetStream(); var head = new StringBuilder(); var one = new byte[1];
            try
            {
                while (!head.ToString().EndsWith("\r\n\r\n")) { if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) <= 0) { client.Close(); return; } head.Append((char)one[0]); if (head.Length > 16384) { client.Close(); return; } }
                var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries); var path = lines[0].Split(' ')[1];
                var headers = lines.Skip(1).Select(l => l.Split(new[] { ':' }, 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
                headers.TryGetValue("authorization", out var auth); headers.TryGetValue("sec-websocket-key", out var key);
                if (path != Path || key == null || auth != "Bearer " + _token())
                {
                    Refused++; RefusedWhy = "path=" + path + " key=" + (key != null) + " auth=" + (auth ?? "(none)").Length;
                    var no = Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(no, 0, no.Length).ConfigureAwait(false); client.Close(); return;
                }
                var yes = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + AcceptKey(key) + "\r\n\r\n"); await stream.WriteAsync(yes, 0, yes.Length).ConfigureAwait(false);
                lock (_open) { _open.Add(stream); _clients.Add(client); }
                // read frames until the client closes: a close frame (opcode 8) or the end of the stream
                var header = new byte[2];
                while (true)
                {
                    if (!await ReadExactly(stream, header, 2).ConfigureAwait(false)) break; var opcode = header[0] & 0x0F; long length = header[1] & 0x7F; var masked = (header[1] & 0x80) != 0;
                    if (length == 126) { var ext = new byte[2]; if (!await ReadExactly(stream, ext, 2).ConfigureAwait(false)) break; length = (ext[0] << 8) | ext[1]; }
                    else if (length == 127) { var ext = new byte[8]; if (!await ReadExactly(stream, ext, 8).ConfigureAwait(false)) break; length = 0; foreach (var b in ext) length = (length << 8) | b; }
                    var skip = (masked ? 4 : 0) + length; if (skip > 1 << 20) break; var rest = new byte[skip]; if (skip > 0 && !await ReadExactly(stream, rest, (int)skip).ConfigureAwait(false)) break;
                    if (opcode == 8) break;
                }
            }
            catch (Exception) { }
            finally { lock (_open) { _open.Remove(stream); _clients.Remove(client); } try { client.Close(); } catch (Exception) { } }
        }

        private static async Task<bool> ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var read = 0; while (read < count) { var n = await stream.ReadAsync(buffer, read, count - read).ConfigureAwait(false); if (n <= 0) return false; read += n; } return true;
        }

        /// <summary>A text frame (server frames are not masked).</summary>
        public static byte[] Frame(string text)
        {
            var payload = Encoding.UTF8.GetBytes(text); var frame = new List<byte> { 0x81 };
            if (payload.Length < 126) frame.Add((byte)payload.Length); else if (payload.Length <= ushort.MaxValue) { frame.Add(126); frame.Add((byte)(payload.Length >> 8)); frame.Add((byte)payload.Length); } else { frame.Add(127); for (var i = 7; i >= 0; i--) frame.Add((byte)((long)payload.Length >> (8 * i))); }
            frame.AddRange(payload); return frame.ToArray();
        }

        public int Push(string json)
        {
            var frame = Frame(json); List<NetworkStream> streams; lock (_open) streams = _open.ToList(); var sent = 0;
            foreach (var s in streams) { try { s.Write(frame, 0, frame.Length); s.Flush(); sent++; } catch (Exception) { } }
            return sent;
        }

        /// <summary>Drops every connection from this side, as a restarted API would.</summary>
        public void Drop() { List<TcpClient> clients; lock (_open) { clients = _clients.ToList(); _clients.Clear(); _open.Clear(); } foreach (var c in clients) { try { c.Close(); } catch (Exception) { } } }

        public void Dispose() { Drop(); _listener.Stop(); }
    }
}
