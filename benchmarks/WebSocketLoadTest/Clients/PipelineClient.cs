using System.Buffers;
using System.Net.WebSockets;
using GapSonar.Core.Exchange.Networking.Core;
using GapSonar.Core.Exchange.Networking.Messaging;

namespace WebSocketLoadTest.Clients;

/// <summary>Original Pipelines receive hot path: add bytes and increment one message counter.</summary>
public sealed class PipelineClient : ILoadTestClient, IWebSocketMessageHandler
{
    /// <summary>Custom transport with frame metrics disabled by its default context.</summary>
    private readonly FastWebSocketClient _client;
    /// <summary>Original atomic message counter.</summary>
    private long _messagesReceived;
    /// <summary>Original atomic byte counter.</summary>
    private long _bytesReceived;
    /// <summary>First unexpected client error.</summary>
    private Exception? _error;

    /// <summary>Constructs the same custom transport used by the original load test.</summary>
    public PipelineClient() => _client = new FastWebSocketClient(this);
    /// <summary>Complete messages received.</summary>
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);
    /// <summary>Application bytes received.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    /// <summary>Establishes the connection.</summary>
    public Task ConnectAsync(Uri uri, CancellationToken token) => _client.ConnectAsync(uri, token);
    /// <summary>Retains the original two atomic operations without JSON parsing or payload copies.</summary>
    public ValueTask OnMessageAsync(ReadOnlySequence<byte> message, WebSocketMessageType type, bool endOfBatch)
    {
        Interlocked.Add(ref _bytesReceived, message.Length);
        Interlocked.Increment(ref _messagesReceived);
        return ValueTask.CompletedTask;
    }
    /// <summary>Receives the connection notification.</summary>
    public void OnConnected() { }
    /// <summary>Health checks also observe the transport state.</summary>
    public void OnDisconnected(WebSocketCloseStatus? status, string? description) { }
    /// <summary>Captures the first failure outside the successful hot path.</summary>
    public void OnError(Exception ex) => Interlocked.CompareExchange(ref _error, ex, null);
    /// <summary>Rejects incomplete runs rather than treating a stopped client as a slower client.</summary>
    public void EnsureHealthy()
    {
        if (Volatile.Read(ref _error) is { } error) throw new IOException("Pipelines client failed.", error);
        if (_client.State != WebSocketState.Open) throw new IOException("Pipelines client disconnected.");
    }
    /// <summary>Observes transport shutdown.</summary>
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
