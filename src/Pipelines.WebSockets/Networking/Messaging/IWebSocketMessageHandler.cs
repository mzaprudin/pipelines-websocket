using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace GapSonar.Core.Exchange.Networking.Messaging;

/// <summary>
/// Low-level handler for raw WebSocket messages.
/// Receives borrowed message buffers from the WebSocket clients.
/// </summary>
public interface IWebSocketMessageHandler
{
    ValueTask OnMessageAsync(ReadOnlySequence<byte> message, WebSocketMessageType type, bool endOfBatch);
    void OnConnected();
    void OnDisconnected(WebSocketCloseStatus? status, string? description);
    void OnError(Exception ex);

    /// <summary>
    /// Called when a protocol-level Ping (0x9) is received from the server.
    /// </summary>
    void OnPing(ReadOnlySequence<byte> payload) { }

    /// <summary>
    /// Called when a protocol-level Pong (0xA) is received from the server.
    /// </summary>
    void OnPong(ReadOnlySequence<byte> payload) { }
}
