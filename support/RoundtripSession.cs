using System.Buffers;
using System.Net.WebSockets;
using GapSonar.Core.Exchange.Networking.Core;
using GapSonar.Core.Exchange.Networking.Messaging;

namespace GapSonar.TransportLab.Support;

/// <summary>Single-flight, payload-validating local transport harness shared by the demo and benchmarks.</summary>
internal sealed class RoundtripSession : IWebSocketMessageHandler, IAsyncDisposable
{
    /// <summary>Maximum lifetime of an individual benchmark case or demonstration.</summary>
    private static readonly TimeSpan SESSION_TIMEOUT = TimeSpan.FromMinutes(10);
    /// <summary>Local .NET WebSocket echo peer.</summary>
    private readonly LoopbackServer _server = new();
    /// <summary>Per-session deadline and shutdown token.</summary>
    private readonly CancellationTokenSource _lifetime = new(SESSION_TIMEOUT);
    /// <summary>Acknowledgment signal reused across messages.</summary>
    private readonly SemaphoreSlim _received = new(0, 1);
    /// <summary>Transport being measured.</summary>
    private IFastWebSocket? _client;
    /// <summary>Server echo loop.</summary>
    private Task? _peer;
    /// <summary>Expected payload, borrowed until its matching roundtrip completes.</summary>
    private ReadOnlyMemory<byte> _expected;
    /// <summary>Expected message type.</summary>
    private WebSocketMessageType _expectedType;
    /// <summary>Sticky integrity or transport failure.</summary>
    private Exception? _failure;
    /// <summary>Whether one response is pending.</summary>
    private int _pending;
    /// <summary>Number of verified responses.</summary>
    public long VerifiedMessages { get; private set; }

    /// <summary>Starts the echo peer and connects the selected implementation.</summary>
    public async Task ConnectAsync(bool standard)
    {
        _peer = _server.EchoAsync(_lifetime.Token);
        _client = standard ? new StdWebSocketClient(this) : new FastWebSocketClient(this);
        await _client.ConnectAsync(_server.Uri, _lifetime.Token).ConfigureAwait(false);
    }

    /// <summary>Sends one message and fails on corruption, duplication, type mismatch, or transport error.</summary>
    public async Task RoundtripAsync(ReadOnlyMemory<byte> payload, WebSocketMessageType type = WebSocketMessageType.Binary)
    {
        if (_failure is not null) throw new InvalidDataException("Session previously failed.", _failure);
        _expected = payload;
        _expectedType = type;
        if (Interlocked.Exchange(ref _pending, 1) != 0) throw new InvalidOperationException("Only one request may be in flight.");
        await _client!.SendAsync(payload, type, token: _lifetime.Token).ConfigureAwait(false);
        await _received.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        if (_failure is not null) throw new InvalidDataException("Roundtrip integrity verification failed.", _failure);
        VerifiedMessages++;
    }

    /// <summary>Checks every returned byte before releasing the borrowed Pipe buffer.</summary>
    public ValueTask OnMessageAsync(ReadOnlySequence<byte> message, WebSocketMessageType type, bool endOfBatch)
    {
        if (Interlocked.Exchange(ref _pending, 0) != 1 || type != _expectedType ||
            message.Length != _expected.Length || !new SequenceReader<byte>(message).IsNext(_expected.Span))
            _failure = new InvalidDataException("Unexpected, duplicate, or corrupt response.");
        if (_received.CurrentCount == 0) _received.Release();
        return ValueTask.CompletedTask;
    }

    /// <summary>Receives connection notifications.</summary>
    public void OnConnected() { }
    /// <summary>Records unexpected disconnects while a request is pending.</summary>
    public void OnDisconnected(WebSocketCloseStatus? status, string? description)
    {
        if (Volatile.Read(ref _pending) != 0) OnError(new IOException($"Disconnected: {status} {description}"));
    }
    /// <summary>Fails any current or subsequent roundtrip.</summary>
    public void OnError(Exception ex)
    {
        _failure = ex;
        if (_received.CurrentCount == 0) _received.Release();
    }

    /// <summary>Stops both peers and observes their tasks.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _server.Dispose();
        if (_peer is not null)
        {
            try { await _peer.ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException or IOException) { }
        }
        _lifetime.Dispose();
        _received.Dispose();
    }
}
