using System.Buffers.Binary;
using GapSonar.TransportLab.Support;

const int MESSAGE_COUNT = 100, PAYLOAD_BYTES = 128;
var payload = new byte[PAYLOAD_BYTES];
foreach (bool standard in new[] { false, true })
{
    await using var session = new RoundtripSession();
    await session.ConnectAsync(standard);
    for (int messageIndex = 0; messageIndex < MESSAGE_COUNT; messageIndex++)
    {
        payload.AsSpan().Fill(unchecked((byte)messageIndex));
        BinaryPrimitives.WriteInt32LittleEndian(payload, messageIndex);
        await session.RoundtripAsync(payload);
    }
    if (session.VerifiedMessages != MESSAGE_COUNT) throw new InvalidDataException("Missing responses.");
    Console.WriteLine($"PASS: {(standard ? "ClientWebSocket" : "Pipelines")}: {MESSAGE_COUNT} ordered messages, every byte verified.");
}
