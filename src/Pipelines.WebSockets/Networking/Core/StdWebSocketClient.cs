using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using GapSonar.Core.Exchange.Networking.Messaging;
using GapSonar.Core.Exchange.Networking.Protocol;

using Microsoft.Extensions.Logging;

namespace GapSonar.Core.Exchange.Networking.Core;

/// <summary>ClientWebSocket adapter with the same borrowed-buffer message callback contract.</summary>
internal sealed class StdWebSocketClient(IWebSocketMessageHandler handler, ILogger? log = null) : IFastWebSocket
{
    private readonly IWebSocketMessageHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private ClientWebSocket? _client;
    private CancellationTokenSource? _internalCts;
    private Task? _readTask;
    private volatile bool _isRunning;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketState State => _client?.State ?? WebSocketState.None;

    public string? ProxyAddress { get; set; }

    /// <summary>
    /// Interval for standard WebSocket keep-alive pings.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Connects the framework client and starts a cancellable receive loop.</summary>
    public async Task ConnectAsync(Uri uri, CancellationToken token = default)
    {
        if (State == WebSocketState.Open || State == WebSocketState.Connecting)
            throw new InvalidOperationException("Socket is already connected or connecting.");

        _client = new ClientWebSocket();
        _client.Options.KeepAliveInterval = KeepAliveInterval;

        if (!string.IsNullOrEmpty(ProxyAddress))
        {
            string proxyUrl = ProxyAddress;
            if (!proxyUrl.Contains("://", StringComparison.Ordinal))
            {
                proxyUrl = "http://" + proxyUrl;
            }
            _client.Options.Proxy = new WebProxy(proxyUrl);
        }

        foreach (var kvp in FastWebSocketClient.HostHeaders)
        {
            if (uri.Host.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var header in kvp.Value)
                {
                    var parts = header.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        _client.Options.SetRequestHeader(parts[0].Trim(), parts[1].Trim());
                    }
                }
            }
        }

        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _isRunning = true;

        try
        {
            await _client.ConnectAsync(uri, token).ConfigureAwait(false);
            _handler.OnConnected();
            // Always enter loop cleanup, including cancellation inside OnConnected.
            var receiveToken = _internalCts.Token;
            _readTask = Task.Run(() => ReadLoopAsync(receiveToken));
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _handler.OnError(ex);
            throw;
        }
    }

    /// <summary>Coalesces bounded logical messages and dispatches borrowed Pipe buffers.</summary>
    private async Task ReadLoopAsync(CancellationToken token)
    {
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES + 1,
            resumeWriterThreshold: WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES / 2));
        long messageBytes = 0;
        try
        {
            while (_isRunning && !token.IsCancellationRequested && _client?.State == WebSocketState.Open)
            {
                var memory = pipe.Writer.GetMemory(65536);
                var result = await _client.ReceiveAsync(memory, token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _isRunning = false;
                    _handler.OnDisconnected(_client.CloseStatus, _client.CloseStatusDescription);
                    break;
                }

                messageBytes += result.Count;
                if (messageBytes > WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES)
                    throw new WebSocketException("Message exceeds the payload limit.");
                pipe.Writer.Advance(result.Count);

                if (result.EndOfMessage && messageBytes == 0)
                {
                    await _handler.OnMessageAsync(ReadOnlySequence<byte>.Empty, result.MessageType, true).ConfigureAwait(false);
                }
                else if (result.EndOfMessage)
                {
                    await pipe.Writer.FlushAsync(token).ConfigureAwait(false);
                    var readResult = await pipe.Reader.ReadAsync(token).ConfigureAwait(false);
                    try
                    {
                        await _handler.OnMessageAsync(readResult.Buffer, result.MessageType, endOfBatch: true).ConfigureAwait(false);
                    }
                    finally
                    {
                        pipe.Reader.AdvanceTo(readResult.Buffer.End);
                    }
                    messageBytes = 0;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_isRunning)
            {
                log?.LogError(ex, "[StdWebSocketClient] Read error");
                _handler.OnError(ex);
            }
        }
        finally
        {
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);

            if (_isRunning)
            {
                _isRunning = false;
                _handler.OnDisconnected(WebSocketCloseStatus.EndpointUnavailable, "Connection lost");
            }
            Cleanup();
        }
    }

    /// <summary>Sends one data fragment under the shared send lock.</summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type, bool endOfMessage = true, CancellationToken token = default)
    {
        if (_client is null || _client.State != WebSocketState.Open)
            throw new InvalidOperationException("Socket not open");

        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _client.SendAsync(message, type, endOfMessage, token).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>ClientWebSocket manages protocol keepalive; explicit ping frames are unavailable.</summary>
    public ValueTask SendPingAsync(ReadOnlyMemory<byte> payload, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        throw new NotSupportedException("ClientWebSocket does not expose protocol ping frames. Use KeepAliveInterval.");
    }

    /// <summary>Sends close output and cancels the receive loop within the caller deadline.</summary>
    public async Task DisconnectAsync(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string? statusDescription = null, CancellationToken token = default)
    {
        if (_client is null || _client.State == WebSocketState.Closed || _client.State == WebSocketState.None)
            return;

        _isRunning = false;
        try
        {
            await _client.CloseOutputAsync(status, statusDescription ?? "Closing", token).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            Cleanup();
        }
        if (_readTask is not null) await _readTask.WaitAsync(token).ConfigureAwait(false);
    }

    /// <summary>Stops the transport with a bounded shutdown deadline.</summary>
    public async ValueTask DisposeAsync()
    {
        const int SHUTDOWN_TIMEOUT_SECONDS = 5;
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(SHUTDOWN_TIMEOUT_SECONDS));
        try { await DisconnectAsync(token: shutdown.Token).ConfigureAwait(false); }
        finally
        {
            _client?.Dispose();
            _internalCts?.Dispose();
        }
    }

    /// <summary>Cancels the internal receive loop.</summary>
    private void Cleanup()
    {
        _isRunning = false;
        _internalCts?.Cancel();
    }
}
