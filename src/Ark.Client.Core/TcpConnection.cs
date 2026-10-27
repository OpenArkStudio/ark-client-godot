using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Ark.Client.Core;

public sealed record TransportEvent(string Kind, Frame? Frame = null, string? Message = null);
public sealed record TransportMetrics(string? PeerEndpoint, long TxFrames, long TxBytes, long RxFrames, long RxBytes)
{
    public static TransportMetrics Empty { get; } = new(null, 0, 0, 0, 0);
}

/// <summary>One TCP session. Reconnect creates a new instance and fresh receive state.</summary>
public sealed class TcpConnection : IDisposable
{
    private readonly TcpClient _client = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentQueue<TransportEvent> _events = new();
    private readonly FrameCodec _codec = new();
    private readonly object _metricsLock = new();
    private TransportMetrics _metrics = TransportMetrics.Empty;
    private int _queued;
    private int _disposed;
    private int _connected;
    private int _started;
    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public TransportMetrics Metrics
    {
        get
        {
            lock (_metricsLock)
            {
                return _metrics;
            }
        }
    }

    public Task RunAsync(string host, int port)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A connection can only start once.");
        }

        return ReceiveAsync(host, port);
    }

    private void Enqueue(TransportEvent value)
    {
        if (Interlocked.Increment(ref _queued) > 64)
        {
            Interlocked.Decrement(ref _queued);
            throw new InvalidDataException("Receive event limit exceeded.");
        }
        _events.Enqueue(value);
    }

    public bool TryDequeue(out TransportEvent? value)
    {
        if (!_events.TryDequeue(out value))
        {
            return false;
        }

        Interlocked.Decrement(ref _queued);
        return true;
    }

    private async Task ReceiveAsync(string host, int port)
    {
        string reason = "Connection closed.";
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _client.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
            _client.NoDelay = true;
            lock (_metricsLock)
            {
                _metrics = _metrics with
                {
                    PeerEndpoint = _client.Client.RemoteEndPoint?.ToString()
                };
            }
            Volatile.Write(ref _connected, 1);
            Enqueue(new TransportEvent("connected"));
            var buffer = new byte[4096];
            NetworkStream stream = _client.GetStream();
            while (!_stop.IsCancellationRequested)
            {
                int count = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                lock (_metricsLock)
                {
                    _metrics = _metrics with
                    {
                        RxBytes = _metrics.RxBytes + count
                    };
                }
                foreach (Frame frame in _codec.Append(buffer.AsSpan(0, count)))
                {
                    lock (_metricsLock)
                    {
                        _metrics = _metrics with
                        {
                            RxFrames = _metrics.RxFrames + 1
                        };
                    }
                    Enqueue(new TransportEvent("frame", frame));
                }
            }
        }
        catch (Exception error) when (error is SocketException or IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or ArgumentException)
        {
            reason = _stop.IsCancellationRequested ? "Disconnected." : error.Message;
        }
        finally
        {
            Volatile.Write(ref _connected, 0);
            _client.Dispose();
            _codec.Reset();
            // A saturated consumer still receives a terminal event without growing the queue.
            while (Volatile.Read(ref _queued) >= 64 && TryDequeue(out _))
            {
            }
            Enqueue(new TransportEvent("closed", Message: reason));
        }
    }

    public async Task SendAsync(byte[] frame)
    {
        if (!IsConnected || Volatile.Read(ref _disposed) != 0)
        {
            throw new IOException("Connection is not ready.");
        }
        // One pending send is enough for the bounded 10 Hz demo producer.
        if (!await _sendLock.WaitAsync(0, _stop.Token).ConfigureAwait(false))
        {
            throw new IOException("A previous send is still pending.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _client.GetStream().WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            // Local completion does not establish receipt or acknowledgement by the peer.
            lock (_metricsLock)
            {
                _metrics = _metrics with
                {
                    TxFrames = _metrics.TxFrames + 1,
                    TxBytes = _metrics.TxBytes + frame.Length
                };
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _connected, 0);
        _stop.Cancel();
        _client.Dispose();
    }
}
