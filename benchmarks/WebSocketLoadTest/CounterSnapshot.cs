using WebSocketLoadTest.Clients;

namespace WebSocketLoadTest;

/// <summary>Reads matching message/byte counter observations without changing receive hot paths.</summary>
internal static class CounterSnapshot
{
    /// <summary>Bounds retries if a client cannot provide a stable observation.</summary>
    internal const int MAX_SNAPSHOT_ATTEMPTS = 1_024;

    /// <summary>Retries each client's read if its message count advances while the byte count is observed.</summary>
    internal static (long Messages, long Bytes) Read(IReadOnlyList<ILoadTestClient> clients)
    {
        long messages = 0, bytes = 0;
        for (int i = 0; i < clients.Count; i++)
        {
            var client = clients[i];
            bool captured = false;
            for (int attempt = 0; attempt < MAX_SNAPSHOT_ATTEMPTS; attempt++)
            {
                long before = client.MessagesReceived;
                long observedBytes = client.BytesReceived;
                long after = client.MessagesReceived;
                if (before != after) continue;
                messages += before;
                bytes += observedBytes;
                captured = true;
                break;
            }
            if (!captured) throw new InvalidDataException("Could not obtain a stable receiver counter snapshot.");
        }
        return (messages, bytes);
    }
}
