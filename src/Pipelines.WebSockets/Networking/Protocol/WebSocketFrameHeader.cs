using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace GapSonar.Core.Exchange.Networking.Protocol;

internal static class WebSocketFrameHeader
{
    public const int MaxHeaderSize = 14; // 2 (base) + 8 (length) + 4 (mask)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteHeader(Span<byte> destination, int payloadLength, bool masked, bool fin, int opCode)
    {
        destination[0] = (byte)((fin ? 0x80 : 0x00) | (opCode & 0x0F));

        int headerLength;

        if (payloadLength <= 125)
        {
            destination[1] = (byte)(payloadLength | (masked ? 0x80 : 0x00));
            headerLength = 2;
        }
        else if (payloadLength <= 65535)
        {
            destination[1] = (byte)(126 | (masked ? 0x80 : 0x00));
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2), (ushort)payloadLength);
            headerLength = 4;
        }
        else
        {
            destination[1] = (byte)(127 | (masked ? 0x80 : 0x00));
            BinaryPrimitives.WriteInt64BigEndian(destination.Slice(2), payloadLength);
            headerLength = 10;
        }

        return headerLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyMask(Span<byte> payload, Span<byte> maskKey)
    {
        // Vectorization can be added here for larger payloads,
        // but for now a simple loop with loop unrolling by JIT is sufficient for most HFT message sizes (usually small JSONs).
        // For very large payloads, Vector<byte> would be better.

        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] ^= maskKey[i % 4];
        }
    }
}
