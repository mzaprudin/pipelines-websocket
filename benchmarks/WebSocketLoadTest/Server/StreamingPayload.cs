using System.Buffers.Binary;
using System.Text;

namespace WebSocketLoadTest.Server;

/// <summary>Builds bounded, exact-size JSON depth messages and complete server frames before measurement.</summary>
internal static class StreamingPayload
{
    /// <summary>Primary LAN payload, measured in application bytes.</summary>
    internal const int DEFAULT_BYTES = 1_024;
    /// <summary>Original trade profile retained for reproducibility.</summary>
    internal const int ORIGINAL_BYTES = 87;
    /// <summary>Accepted configurable depth-profile bounds.</summary>
    internal const int MIN_DEPTH_BYTES = 128, MAX_BYTES = 1_048_576;
    /// <summary>Maximum prepared batch storage.</summary>
    internal const int MAX_BATCH_BYTES = 16_777_216;
    /// <summary>Original compact trade message.</summary>
    private const string ORIGINAL_TRADE = "{\"symbol\":\"BTC_USDT\",\"price\":\"100000.50\",\"qty\":\"0.005\",\"side\":\"buy\",\"ts\":1678888888888}";
    /// <summary>Depth message fragments; the short padding field fills the final incomplete level slot.</summary>
    private static readonly byte[] DEPTH_PREFIX = "{\"symbol\":\"BTC_USDT\",\"channel\":\"push.depth\",\"ts\":1678888888888,\"data\":["u8.ToArray();
    /// <summary>One representative price/quantity level.</summary>
    private static readonly byte[] LEVEL = "[\"100000.50\",\"0.005\"]"u8.ToArray();
    /// <summary>Final depth-array delimiter and padding property.</summary>
    private static readonly byte[] PADDING_PREFIX = "],\"padding\":\""u8.ToArray();
    /// <summary>WebSocket length-encoding and opcode constants.</summary>
    private const int SHORT_FRAME_MAX = 125, EXTENDED_LENGTH_CODE = 126, LARGE_LENGTH_CODE = 127,
        EXTENDED_FRAME_MAX = 65_535, EXTENDED_HEADER_BYTES = 4, LARGE_HEADER_BYTES = 10;
    /// <summary>Complete unmasked text frame.</summary>
    private const byte FINAL_TEXT_OPCODE = 0x81;

    /// <summary>Validates the legacy profile or a bounded depth-message size.</summary>
    internal static void ValidateLength(int bytes)
    {
        if (bytes != ORIGINAL_BYTES && (bytes < MIN_DEPTH_BYTES || bytes > MAX_BYTES))
            throw new ArgumentOutOfRangeException(nameof(bytes), $"Use {ORIGINAL_BYTES} or {MIN_DEPTH_BYTES}–{MAX_BYTES} payload bytes.");
    }

    /// <summary>Produces valid UTF-8 JSON containing full depth levels at exactly the requested size.</summary>
    internal static byte[] Create(int bytes)
    {
        ValidateLength(bytes);
        if (bytes == ORIGINAL_BYTES) return Encoding.UTF8.GetBytes(ORIGINAL_TRADE);
        var payload = new byte[bytes];
        DEPTH_PREFIX.CopyTo(payload, 0);
        int offset = DEPTH_PREFIX.Length;
        bool first = true;
        while (offset + LEVEL.Length + (first ? 0 : 1) + PADDING_PREFIX.Length + 2 <= payload.Length)
        {
            if (!first) payload[offset++] = (byte)',';
            LEVEL.CopyTo(payload, offset);
            offset += LEVEL.Length;
            first = false;
        }
        PADDING_PREFIX.CopyTo(payload, offset);
        offset += PADDING_PREFIX.Length;
        payload.AsSpan(offset, payload.Length - offset - 2).Fill((byte)'x');
        payload[^2] = (byte)'"';
        payload[^1] = (byte)'}';
        return payload;
    }

    /// <summary>Encodes one immutable RFC 6455 server frame, including extended payload lengths.</summary>
    internal static byte[] CreateFrame(ReadOnlySpan<byte> payload)
    {
        int headerBytes = HeaderBytes(payload.Length);
        var frame = new byte[headerBytes + payload.Length];
        frame[0] = FINAL_TEXT_OPCODE;
        if (headerBytes == 2) frame[1] = checked((byte)payload.Length);
        else if (headerBytes == EXTENDED_HEADER_BYTES)
        {
            frame[1] = EXTENDED_LENGTH_CODE;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), checked((ushort)payload.Length));
        }
        else
        {
            frame[1] = LARGE_LENGTH_CODE;
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2), checked((ulong)payload.Length));
        }
        payload.CopyTo(frame.AsSpan(headerBytes));
        return frame;
    }

    /// <summary>Number of unmasked frame-header bytes for the configured payload.</summary>
    internal static int HeaderBytes(int payloadBytes) => payloadBytes <= SHORT_FRAME_MAX ? 2 : payloadBytes <= EXTENDED_FRAME_MAX ? EXTENDED_HEADER_BYTES : LARGE_HEADER_BYTES;
}
