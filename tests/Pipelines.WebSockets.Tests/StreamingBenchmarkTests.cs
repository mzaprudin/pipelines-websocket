using System.Buffers;
using System.Net.WebSockets;
using GapSonar.Core.Exchange.Networking.Messaging;
using WebSocketLoadTest;
using WebSocketLoadTest.Server;

namespace GapSonar.TransportLab.Tests;

/// <summary>Verifies streaming workload framing and actual-time measurement rather than a one-second assumption.</summary>
public sealed class StreamingBenchmarkTests
{
    /// <summary>Warmup counts are excluded and a delayed timer sample is normalized correctly.</summary>
    [Fact]
    public void Between_WarmupAndDelayedSample_UsesMeasuredCounterDeltaAndElapsedTime()
    {
        const long WARMUP_MESSAGES = 100, WARMUP_BYTES = 1_000, END_MESSAGES = 160, END_BYTES = 1_600;
        const double ACTUAL_SECONDS = 2, EXPECTED_RATE = 30, EXPECTED_MEGABITS = 0.0024;
        var window = MeasurementWindow.Between(WARMUP_MESSAGES, WARMUP_BYTES, END_MESSAGES, END_BYTES, ACTUAL_SECONDS);
        Assert.Equal(EXPECTED_RATE, window.MessagesPerSecond);
        Assert.Equal(EXPECTED_MEGABITS, window.MegabitsPerSecond);
    }

    /// <summary>Nonpositive or nonfinite elapsed intervals cannot generate published rates.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Between_InvalidElapsedTime_Throws(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementWindow.Between(0, 0, 1, 1, seconds));

    /// <summary>Failed/reset counters cannot appear as valid positive throughput.</summary>
    [Fact]
    public void Between_BackwardCounter_Throws() =>
        Assert.Throws<ArgumentException>(() => MeasurementWindow.Between(2, 2, 1, 1, 1));

    /// <summary>CLI invalid inputs are rejected before a streaming process starts.</summary>
    [Theory]
    [InlineData("--count", "0")]
    [InlineData("--duration", "0")]
    [InlineData("--port", "99999")]
    [InlineData("--unknown", "1")]
    public void Parse_InvalidOptions_Throws(string name, string value) =>
        Assert.ThrowsAny<ArgumentException>(() => Options.Parse([name, value]));

    /// <summary>The restored server emits the original complete text messages, twenty frames per batch.</summary>
    [Fact]
    public async Task LoadServer_OriginalPreparedBatch_ContainsTwentyIdenticalCompleteMessages()
    {
        const int TIMEOUT_SECONDS = 10;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
        await using var server = new LoadServer(payloadBytes: StreamingPayload.ORIGINAL_BYTES);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(server.Uri, timeout.Token);
        var bytes = new byte[LoadServer.Payload.Length];
        for (int i = 0; i < LoadServer.FRAMES_PER_BATCH; i++)
        {
            int offset = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await client.ReceiveAsync(bytes.AsMemory(offset), timeout.Token);
                offset += result.Count;
                Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            } while (!result.EndOfMessage);
            Assert.Equal(LoadServer.Payload.Length, offset);
            Assert.Equal(LoadServer.Payload.ToArray(), bytes);
        }
        Assert.Equal((2 + LoadServer.Payload.Length) * LoadServer.FRAMES_PER_BATCH, server.BatchBytes);
        server.EnsureHealthy();
        client.Abort();
    }

    /// <summary>Exact-size depth profiles remain valid JSON at representative message lengths.</summary>
    [Theory]
    [InlineData(128)]
    [InlineData(1_024)]
    [InlineData(4_096)]
    [InlineData(65_536)]
    public void Create_ConfiguredSize_ProducesExactValidJson(int bytes)
    {
        var payload = StreamingPayload.Create(bytes);
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(bytes, payload.Length);
        Assert.Equal("BTC_USDT", document.RootElement.GetProperty("symbol").GetString());
        Assert.True(document.RootElement.GetProperty("data").GetArrayLength() > 0);
    }

    /// <summary>The new primary profile is one KiB and roles remain explicit.</summary>
    [Fact]
    public void Parse_DefaultAndRemoteProfile_UsesOneKiB()
    {
        const int EXPECTED_BYTES = 1_024;
        var local = Options.Parse([]);
        var remote = Options.Parse(["--mode", "client", "--url", "ws://127.0.0.1:5050", "--batch", "1"]);
        Assert.Equal(EXPECTED_BYTES, local.PayloadBytes);
        Assert.Equal(WorkloadMode.Loopback, local.Mode);
        Assert.Equal(EXPECTED_BYTES, remote.PayloadBytes);
        Assert.Equal(WorkloadMode.Client, remote.Mode);
        Assert.Equal(1, remote.FramesPerBatch);
    }

    /// <summary>Malformed roles, endpoints and bounded-buffer settings fail before I/O.</summary>
    [Theory]
    [InlineData("--payload-bytes", "0")]
    [InlineData("--payload-bytes", "88")]
    [InlineData("--payload-bytes", "1048577")]
    [InlineData("--batch", "0")]
    [InlineData("--batch", "999999")]
    [InlineData("--mode", "unknown")]
    [InlineData("--mode", "client")]
    [InlineData("--url", "ws://127.0.0.1")]
    public void Parse_InvalidProfileOrRole_Throws(string option, string value) =>
        Assert.ThrowsAny<ArgumentException>(() => Options.Parse([option, value]));

    /// <summary>Both clients receive complete one-KiB messages over extended-length server frames.</summary>
    [Theory]
    [InlineData(false, 1_024)]
    [InlineData(true, 1_024)]
    [InlineData(false, 65_535)]
    [InlineData(true, 65_535)]
    [InlineData(false, 65_536)]
    [InlineData(true, 65_536)]
    public async Task LoadServer_OneKiBProfile_DeliversCompleteMessage(bool standard, int payloadBytes)
    {
        const int TIMEOUT_SECONDS = 10;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
        await using var server = new LoadServer(payloadBytes: payloadBytes, framesPerBatch: 1);
        var handler = new FirstMessageHandler();
        await using GapSonar.Core.Exchange.Networking.Core.IFastWebSocket client = standard
            ? new GapSonar.Core.Exchange.Networking.Core.StdWebSocketClient(handler)
            : new GapSonar.Core.Exchange.Networking.Core.FastWebSocketClient(handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        var message = await handler.First.Task.WaitAsync(timeout.Token);
        Assert.Equal(payloadBytes, message.Payload.Length);
        Assert.Equal(server.MessagePayload.ToArray(), message.Payload);
        Assert.Equal(WebSocketMessageType.Text, message.Type);
        server.EnsureHealthy();
    }

    /// <summary>A receiver cannot silently measure a different generator payload/batch profile.</summary>
    [Fact]
    public async Task ConnectAsync_ProfileMismatch_RejectsUpgrade()
    {
        const int TIMEOUT_SECONDS = 10, WRONG_PAYLOAD_BYTES = 4_096;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
        await using var server = new LoadServer(framesPerBatch: 1);
        using var client = new ClientWebSocket();
        var wrong = LoadServer.ProfileEndpoint(server.Uri, WRONG_PAYLOAD_BYTES, 1);
        await Assert.ThrowsAnyAsync<WebSocketException>(() => client.ConnectAsync(wrong, timeout.Token));
        server.EnsureHealthy();
    }
    /// <summary>Copies only the first streaming message and surfaces transport failures immediately.</summary>
    private sealed class FirstMessageHandler : IWebSocketMessageHandler
    {
        /// <summary>First complete payload and its type.</summary>
        public TaskCompletionSource<(byte[] Payload, WebSocketMessageType Type)> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Whether the first callback claimed the capture slot.</summary>
        private int _captured;
        /// <summary>Bounds test allocations while the generator streams continuously.</summary>
        public ValueTask OnMessageAsync(ReadOnlySequence<byte> message, WebSocketMessageType type, bool endOfBatch)
        {
            if (Interlocked.Exchange(ref _captured, 1) == 0) First.TrySetResult((message.ToArray(), type));
            return ValueTask.CompletedTask;
        }
        /// <summary>Receives connection notifications.</summary>
        public void OnConnected() { }
        /// <summary>Fails capture if the connection ends first.</summary>
        public void OnDisconnected(WebSocketCloseStatus? status, string? description) => First.TrySetException(new IOException("Disconnected before capture."));
        /// <summary>Surfaces receive failures without waiting for the test deadline.</summary>
        public void OnError(Exception error) => First.TrySetException(error);
    }
}
