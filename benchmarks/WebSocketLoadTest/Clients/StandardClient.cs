using System.Net.WebSockets;

namespace WebSocketLoadTest.Clients;

/// <summary>Original direct ClientWebSocket baseline, without the standalone Pipe adapter.</summary>
public sealed class StandardClient : ILoadTestClient
{
    /// <summary>Original receive buffer capacity.</summary>
    private const int RECEIVE_BUFFER_BYTES = 8_192;
    /// <summary>Framework transport used directly, as in GapSonar.</summary>
    private readonly ClientWebSocket _socket = new();
    /// <summary>Original atomic counters.</summary>
    private long _messagesReceived, _bytesReceived;
    /// <summary>Observed receive task.</summary>
    private Task? _receiveTask;
    /// <summary>First unexpected client error.</summary>
    private Exception? _error;
    /// <summary>Whether shutdown has begun.</summary>
    private int _stopping;
    /// <summary>Complete messages received.</summary>
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);
    /// <summary>Application bytes received.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>Connects and immediately starts the original direct receive loop.</summary>
    public async Task ConnectAsync(Uri uri, CancellationToken token)
    {
        // LAN comparisons use the same direct path as the custom TCP client.
        _socket.Options.Proxy = null;
        await _socket.ConnectAsync(uri, token).ConfigureAwait(false);
        // Keep connection readiness independent of continuously buffered receive work.
        _receiveTask = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Retains the original Memory overload, buffer size and per-message counter operations.</summary>
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[RECEIVE_BUFFER_BYTES];
        var memory = new Memory<byte>(buffer);
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                // As in the original test, hot receives use no token; disposal aborts the socket.
                var result = await _socket.ReceiveAsync(memory, CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                Interlocked.Add(ref _bytesReceived, result.Count);
                if (result.EndOfMessage) Interlocked.Increment(ref _messagesReceived);
            }
        }
        catch (Exception error) when (Volatile.Read(ref _stopping) != 0 && error is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception error) { Interlocked.CompareExchange(ref _error, error, null); }
    }

    /// <summary>Rejects failed or disconnected clients during measurement.</summary>
    public void EnsureHealthy()
    {
        if (Volatile.Read(ref _error) is { } error) throw new IOException("ClientWebSocket baseline failed.", error);
        if (_socket.State != WebSocketState.Open || _receiveTask?.IsCompleted == true)
            throw new IOException("ClientWebSocket baseline disconnected.");
    }

    /// <summary>Aborts pending I/O and observes the receive task.</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        _socket.Abort();
        if (_receiveTask is not null) await _receiveTask.ConfigureAwait(false);
        _socket.Dispose();
    }
}
