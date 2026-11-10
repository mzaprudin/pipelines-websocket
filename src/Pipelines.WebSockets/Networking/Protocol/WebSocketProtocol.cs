namespace GapSonar.Core.Exchange.Networking.Protocol;

/// <summary>Framing and resource bounds for the standalone transport.</summary>
internal static class WebSocketProtocol
{
    /// <summary>Maximum logical message and frame payload: one mebibyte.</summary>
    internal const int MAX_MESSAGE_PAYLOAD_BYTES = 1_048_576;
    /// <summary>Maximum HTTP upgrade response.</summary>
    internal const int MAX_HANDSHAKE_HEADER_BYTES = 16_384;
    /// <summary>Maximum protocol control payload.</summary>
    internal const int MAX_CONTROL_PAYLOAD_BYTES = 125;
    /// <summary>Control opcodes.</summary>
    internal const int CLOSE_OPCODE = 8, PING_OPCODE = 9, PONG_OPCODE = 10;
    /// <summary>Reserved bits requiring negotiated extensions.</summary>
    internal const byte RESERVED_BITS_MASK = 0x70;
    /// <summary>GUID used by the WebSocket upgrade challenge.</summary>
    internal const string ACCEPT_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
}
