using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The live channel of the platform: while signed in, one WebSocket stays open to <c>/api/v1/notifications/ws</c> and the API pushes small events (<c>{"type":…,"data":{…},"at":…}</c>) through it: messages, friend
    /// and group requests, item offers, teleport invites, a balance that changed. Events are read on a background task and handed to <see cref="Received"/> on the game's thread. A dropped connection is opened again after
    /// a pause that grows (2 s up to a minute); each attempt uses the access token of that moment, so a renewed session carries on. Nothing is sent: what the channel misses is always available again through the normal routes.
    /// </summary>
    public sealed class LiveChannel : MonoBehaviour
    {
        public const string Path = "/api/v1/notifications/ws";
        public const float FirstPause = 2f, LongestPause = 60f;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private CancellationTokenSource _stop; private Func<string> _token; private string _url;
        private volatile bool _connected; private int _connects, _received;

        /// <summary>One event as the API sent it (raw JSON), on the game's thread.</summary>
        public event Action<string> Received;
        public bool Connected => _connected;
        public int Connects => _connects;
        public int ReceivedCount => _received;
        public bool Running => _stop != null;
        /// <summary>Why the last attempt ended (empty while it is open), for the tests and the performance overlay.</summary>
        public string LastError { get; private set; } = "";

        /// <summary>The address of the channel for an API address (https becomes wss, http becomes ws).</summary>
        public static string AddressOf(string apiBase)
        {
            var root = (apiBase ?? "").TrimEnd('/');
            if (root.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) root = "wss://" + root.Substring(8); else if (root.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) root = "ws://" + root.Substring(7);
            return root + Path;
        }

        /// <summary>The next pause after a failed attempt: double the last one, never more than a minute.</summary>
        public static float NextPause(float pause) => Mathf.Min(Mathf.Max(FirstPause, pause * 2f), LongestPause);

        /// <summary>Opens the channel at an address of its own (a test server); otherwise <see cref="Begin"/> derives it from the API.</summary>
        public void BeginAt(string url, Func<string> token) { End(); _url = url; _token = token; _stop = new CancellationTokenSource(); var stop = _stop.Token; _ = Task.Run(() => Loop(stop)); }

        public void Begin(string apiBase, Func<string> token)
        {
            End(); _url = AddressOf(apiBase); _token = token; _stop = new CancellationTokenSource(); var stop = _stop.Token; _ = Task.Run(() => Loop(stop));
        }

        public void End() { if (_stop == null) return; _stop.Cancel(); _stop.Dispose(); _stop = null; _connected = false; }

        private void OnDestroy() => End();

        private async Task Loop(CancellationToken stop)
        {
            var pause = 0f;
            while (!stop.IsCancellationRequested)
            {
                if (pause > 0f) { try { await Task.Delay(TimeSpan.FromSeconds(pause), stop); } catch (TaskCanceledException) { return; } }
                var token = _token?.Invoke(); if (string.IsNullOrEmpty(token)) { pause = FirstPause; continue; }
                using (var socket = new ClientWebSocket())
                {
                    try
                    {
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + token); socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        await socket.ConnectAsync(new Uri(_url), stop); _connected = true; Interlocked.Increment(ref _connects); pause = 0f;
                        var buffer = new byte[8192]; var text = new StringBuilder();
                        while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
                        {
                            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                            if (text.Length > 64 * 1024) { text.Clear(); continue; }   // an oversized event is dropped, never kept
                            if (result.EndOfMessage) { _inbox.Enqueue(text.ToString()); text.Clear(); }
                        }
                    }
                    catch (Exception exception) when (exception is WebSocketException || exception is OperationCanceledException || exception is ObjectDisposedException || exception is System.Net.Http.HttpRequestException || exception is InvalidOperationException) { LastError = exception.GetType().Name + ": " + exception.Message + (exception.InnerException != null ? " / " + exception.InnerException.Message : ""); }
                    finally { _connected = false; }
                }
                if (stop.IsCancellationRequested) return;
                pause = NextPause(pause);
            }
        }

        private void Update()
        {
            while (_inbox.TryDequeue(out var json)) { _received++; try { Received?.Invoke(json); } catch (Exception exception) { Debug.LogWarning("[LifeVerse] evento ao vivo: " + exception.Message); } }
        }

        /// <summary>Hands an event to the listeners as if it had come through the socket (the tests, and replays).</summary>
        public void Inject(string json) => _inbox.Enqueue(json);
    }
}
