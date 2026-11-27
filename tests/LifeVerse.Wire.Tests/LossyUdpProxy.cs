using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace LifeVerse.Wire.Tests;

/// <summary>Um intermediário UDP que descarta uma porcentagem dos datagramas e atrasa os demais (entre o mínimo e o máximo), nos dois sentidos: uma internet ruim dentro do computador.</summary>
internal sealed class LossyUdpProxy : IDisposable
{
    private readonly UdpClient _front = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly IPEndPoint _server;
    private readonly ConcurrentDictionary<IPEndPoint, UdpClient> _upstreams = new();
    private readonly Random _random; private readonly int _loss, _maxDelay, _minDelay;
    private readonly CancellationTokenSource _stop = new();
    private int _dropped;

    public LossyUdpProxy(int serverPort, int lossPercent, int maxDelayMs, int seed, int minDelayMs = 0)
    {
        _server = new IPEndPoint(IPAddress.Loopback, serverPort); _loss = lossPercent; _maxDelay = maxDelayMs; _minDelay = Math.Min(minDelayMs, maxDelayMs); _random = new Random(seed);
        Task.Run(FrontLoop);
    }

    public int Port => ((IPEndPoint)_front.Client.LocalEndPoint!).Port;
    public int Dropped => Volatile.Read(ref _dropped);

    private bool Drop() { lock (_random) { return _random.Next(100) < _loss; } }
    private int Delay() { lock (_random) { return _random.Next(_minDelay, _maxDelay + 1); } }

    private async Task Forward(UdpClient from, byte[] data, IPEndPoint to)
    {
        if (Drop()) { Interlocked.Increment(ref _dropped); return; }
        var delay = Delay(); if (delay > 0) await Task.Delay(delay);
        try { await from.SendAsync(data, data.Length, to); } catch (ObjectDisposedException) { } catch (SocketException) { }
    }

    private async Task FrontLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await _front.ReceiveAsync(_stop.Token); } catch (OperationCanceledException) { return; } catch (ObjectDisposedException) { return; } catch (SocketException) { continue; }
            var client = packet.RemoteEndPoint;
            var upstream = _upstreams.GetOrAdd(client, endpoint => { var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); _ = Task.Run(() => BackLoop(socket, endpoint)); return socket; });
            _ = Forward(upstream, packet.Buffer, _server);
        }
    }

    private async Task BackLoop(UdpClient upstream, IPEndPoint client)
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await upstream.ReceiveAsync(_stop.Token); } catch (OperationCanceledException) { return; } catch (ObjectDisposedException) { return; } catch (SocketException) { continue; }
            _ = Forward(_front, packet.Buffer, client);
        }
    }

    public void Dispose() { _stop.Cancel(); _front.Dispose(); foreach (var socket in _upstreams.Values) socket.Dispose(); }
}
