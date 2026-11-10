using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace GapSonar.Core.Exchange.Networking.Core;

public interface IFastWebSocket : IAsyncDisposable
{
    WebSocketState State { get; }

    /// <summary>
    /// Proxy address (e.g., http://1.2.3.4:8080).
    /// </summary>
    string? ProxyAddress { get; set; }

    /// <summary>
    /// Connects to the specified URI.
    /// </summary>
    Task ConnectAsync(Uri uri, CancellationToken token = default);

    /// <summary>
    /// Gracefully disconnects from the server.
    /// </summary>
    Task DisconnectAsync(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string? statusDescription = null, CancellationToken token = default);

    /// <summary>
    /// Sends a message.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type, bool endOfMessage = true, CancellationToken token = default);

    /// <summary>
    /// Sends a Ping frame.
    /// </summary>
    ValueTask SendPingAsync(ReadOnlyMemory<byte> payload, CancellationToken token = default);
}
