using WebSocketLoadTest;
using WebSocketLoadTest.Clients;

namespace GapSonar.TransportLab.Tests;

/// <summary>Verifies counter sampling when reception advances between individual atomic reads.</summary>
public sealed class CounterSnapshotTests
{
    /// <summary>Compact application payload used to assert exact byte relationships.</summary>
    private const int PAYLOAD_BYTES = 128;
    /// <summary>Distinct counts make a torn observation visible.</summary>
    private const long INITIAL_MESSAGES = 1, FINAL_MESSAGES = 7;

    /// <summary>Aggregates matching observations from all clients.</summary>
    [Fact]
    public void Read_StableClients_AggregatesMatchingCounters()
    {
        var first = new ScriptedClient(FINAL_MESSAGES, FINAL_MESSAGES * PAYLOAD_BYTES);
        var second = new ScriptedClient(INITIAL_MESSAGES, INITIAL_MESSAGES * PAYLOAD_BYTES);
        var result = CounterSnapshot.Read([first, second]);
        Assert.Equal(FINAL_MESSAGES + INITIAL_MESSAGES, result.Messages);
        Assert.Equal((FINAL_MESSAGES + INITIAL_MESSAGES) * PAYLOAD_BYTES, result.Bytes);
    }

    /// <summary>A count change around the byte read discards the torn pair and retries.</summary>
    [Fact]
    public void Read_MessageCountAdvancesDuringByteRead_RetriesTornObservation()
    {
        var client = new ScriptedClient(FINAL_MESSAGES, FINAL_MESSAGES * PAYLOAD_BYTES, advanceOnce: true);
        var result = CounterSnapshot.Read([client]);
        Assert.Equal(FINAL_MESSAGES, result.Messages);
        Assert.Equal(FINAL_MESSAGES * PAYLOAD_BYTES, result.Bytes);
        const int EXPECTED_MESSAGE_READS = 4;
        Assert.Equal(EXPECTED_MESSAGE_READS, client.MessageReads);
    }

    /// <summary>The producer may have added bytes for one frame immediately before incrementing its message count.</summary>
    [Fact]
    public void Read_ByteUpdatePrecedesMessageUpdate_PreservesInFlightFrame()
    {
        long inFlightBytes = (FINAL_MESSAGES + 1) * PAYLOAD_BYTES;
        var result = CounterSnapshot.Read([new ScriptedClient(FINAL_MESSAGES, inFlightBytes)]);
        Assert.Equal(FINAL_MESSAGES, result.Messages);
        Assert.Equal(inFlightBytes, result.Bytes);
    }

    /// <summary>Persistent interference fails within a fixed number of reads rather than spinning indefinitely.</summary>
    [Fact]
    public void Read_MessageCountNeverStabilizes_ThrowsWithinRetryBound()
    {
        var client = new ScriptedClient(FINAL_MESSAGES, FINAL_MESSAGES * PAYLOAD_BYTES, alwaysAdvance: true);
        Assert.Throws<InvalidDataException>(() => CounterSnapshot.Read([client]));
        Assert.Equal(2 * CounterSnapshot.MAX_SNAPSHOT_ATTEMPTS, client.MessageReads);
    }

    /// <summary>Deterministically schedules count changes around the sampler's byte observation.</summary>
    private sealed class ScriptedClient(long messages, long bytes, bool advanceOnce = false, bool alwaysAdvance = false) : ILoadTestClient
    {
        /// <summary>Number of message reads used to expose the retry behavior.</summary>
        internal int MessageReads { get; private set; }
        /// <summary>Returns a stable count, one scripted transition, or continuous interference.</summary>
        public long MessagesReceived
        {
            get
            {
                MessageReads++;
                if (alwaysAdvance) return MessageReads;
                return advanceOnce && MessageReads == 1 ? INITIAL_MESSAGES : messages;
            }
        }
        /// <summary>Byte observation corresponding to the completed scripted transition.</summary>
        public long BytesReceived => bytes;
        /// <summary>No network is used by the deterministic counter source.</summary>
        public Task ConnectAsync(Uri uri, CancellationToken token) => Task.CompletedTask;
        /// <summary>The scripted source has no background receiver.</summary>
        public void EnsureHealthy() { }
        /// <summary>The scripted source owns no resources.</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
