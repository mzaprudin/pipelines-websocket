using System.Buffers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using GapSonar.Core.Exchange.Networking.Core;
using GapSonar.Core.Exchange.Networking.Messaging;
using GapSonar.TransportLab.Support;

namespace GapSonar.TransportLab.Tests;

/// <summary>Exercises the extracted clients over real local TCP/TLS connections.</summary>
public sealed class TransportTests
{
    /// <summary>Upper bound for each network scenario.</summary>
    private static readonly TimeSpan TEST_TIMEOUT = TimeSpan.FromSeconds(15);
    /// <summary>Protocol payload boundaries, including data larger than a Pipe's default pause threshold.</summary>
    private const int SHORT_FRAME_MAX = 125, EXTENDED_FRAME_MIN = 126, EXTENDED_FRAME_MAX = 65_535, LARGE_FRAME_MIN = 65_536, MAX_RECEIVED_MESSAGE_BYTES = 1_048_576;

    /// <summary>Both implementations preserve binary payloads across all frame length encodings.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, SHORT_FRAME_MAX)]
    [InlineData(true, SHORT_FRAME_MAX)]
    [InlineData(false, EXTENDED_FRAME_MIN)]
    [InlineData(true, EXTENDED_FRAME_MIN)]
    [InlineData(false, EXTENDED_FRAME_MAX)]
    [InlineData(true, EXTENDED_FRAME_MAX)]
    [InlineData(false, LARGE_FRAME_MIN)]
    [InlineData(true, LARGE_FRAME_MIN)]
    [InlineData(false, MAX_RECEIVED_MESSAGE_BYTES)]
    [InlineData(true, MAX_RECEIVED_MESSAGE_BYTES)]
    public async Task SendAsync_BinaryLengthBoundary_EchoesIdenticalMessage(bool standard, int length)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var peer = server.EchoAsync(timeout.Token);
        var handler = new RecordingHandler();
        await using var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        var payload = new byte[length];
        RandomNumberGenerator.Fill(payload);
        await client.SendAsync(payload, WebSocketMessageType.Binary, token: timeout.Token);
        var message = await handler.Messages.Reader.ReadAsync(timeout.Token);
        Assert.Equal(WebSocketMessageType.Binary, message.Type);
        Assert.Equal(payload, message.Payload);
        Assert.Equal(1, handler.Count);
        await client.DisconnectAsync(token: timeout.Token);
        await peer.WaitAsync(timeout.Token);
    }

    /// <summary>Outbound fragments form one message with correct continuation opcodes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_FragmentedText_EchoesOneCompleteMessage(bool standard)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var peer = server.EchoAsync(timeout.Token);
        var handler = new RecordingHandler();
        await using var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        await client.SendAsync("hello "u8.ToArray(), WebSocketMessageType.Text, false, timeout.Token);
        await client.SendAsync("world"u8.ToArray(), WebSocketMessageType.Text, true, timeout.Token);
        var message = await handler.Messages.Reader.ReadAsync(timeout.Token);
        Assert.Equal("hello world", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(WebSocketMessageType.Text, message.Type);
        Assert.Equal(1, handler.Count);
        await client.DisconnectAsync(token: timeout.Token);
        await peer.WaitAsync(timeout.Token);
    }

    /// <summary>Inbound fragments are dispatched once after reassembly.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveAsync_FragmentedMessage_DispatchesOnce(bool standard)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler();
        await using var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        await using var peer = await accepting;
        await peer.Socket.SendAsync("hello "u8.ToArray().AsMemory(), WebSocketMessageType.Text, false, timeout.Token);
        await peer.Socket.SendAsync("world"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        var message = await handler.Messages.Reader.ReadAsync(timeout.Token);
        Assert.Equal("hello world", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(1, handler.Count);
    }

    /// <summary>Remote shutdown does not make the receive loop await itself.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveAsync_ServerClose_NotifiesAndDisposesWithinDeadline(bool standard)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler();
        var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        await using var peer = await accepting;
        await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "finished", timeout.Token);
        await handler.Disconnected.Task.WaitAsync(timeout.Token);
        await client.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.Equal(0, handler.Count);
    }

    /// <summary>Incorrect challenge responses cannot establish a custom transport connection.</summary>
    [Theory]
    [InlineData("accept")]
    [InlineData("upgrade")]
    [InlineData("status")]
    public async Task ConnectAsync_InvalidUpgrade_RejectsConnection(string fault)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token, response => fault switch
        {
            "accept" => response.Replace("Sec-WebSocket-Accept:", "Invalid-Accept:"),
            "upgrade" => response.Replace("Upgrade: websocket", "Upgrade: invalid"),
            _ => response.Replace("101 Switching", "200 Switching")
        });
        var handler = new RecordingHandler();
        await using var client = new FastWebSocketClient(handler);
        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(server.Uri, timeout.Token));
        await using var peer = await accepting;
        Assert.Equal(WebSocketState.Closed, client.State);
        Assert.Equal(0, handler.Count);
    }

    /// <summary>Invalid frame headers fail promptly without waiting for an attacker-sized body.</summary>
    [Theory]
    [InlineData("reserved")]
    [InlineData("masked")]
    [InlineData("oversized")]
    [InlineData("control")]
    public async Task ReceiveAsync_InvalidFrame_ReportsErrorAndCloses(string fault)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler();
        await using var client = new FastWebSocketClient(handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        await using var peer = await accepting;
        // Wire bytes intentionally represent protocol violations.
        byte[] wire = fault switch
        {
            "reserved" => [0xC1, 0],
            "masked" => [0x81, 0x80],
            "oversized" => [0x82, 0x7F, 0, 0, 0, 0, 0, 0x20, 0, 0],
            _ => [0x09, 0]
        };
        await peer.Stream.WriteAsync(wire, timeout.Token);
        Assert.IsType<WebSocketException>(await handler.Error.Task.WaitAsync(timeout.Token));
        await handler.Disconnected.Task.WaitAsync(timeout.Token);
        Assert.Equal(0, handler.Count);
    }

    /// <summary>Protocol ping payloads are returned unchanged in a masked pong.</summary>
    [Fact]
    public async Task ReceiveAsync_Ping_ReturnsMatchingPong()
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler();
        await using var client = new FastWebSocketClient(handler);
        await client.ConnectAsync(server.Uri, timeout.Token);
        await using var peer = await accepting;
        byte[] ping = [0x89, 2, 1, 2];
        await peer.Stream.WriteAsync(ping, timeout.Token);
        const int PONG_FRAME_BYTES = 8, MASK_OFFSET = 2, PAYLOAD_OFFSET = 6, MASK_BYTES = 4;
        var pong = new byte[PONG_FRAME_BYTES];
        await peer.Stream.ReadExactlyAsync(pong, timeout.Token);
        Assert.Equal(0x8A, pong[0]);
        Assert.Equal(0x82, pong[1]);
        for (int i = 0; i < 2; i++) Assert.Equal(ping[i + 2], (byte)(pong[PAYLOAD_OFFSET + i] ^ pong[MASK_OFFSET + i % MASK_BYTES]));
        Assert.Equal(0, handler.Count);
    }

    /// <summary>Cancellation is propagated to connection establishment.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAsync_CancelledToken_ThrowsCancellation(bool standard)
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        using var server = new LoopbackServer();
        await using var client = CreateClient(standard, new RecordingHandler());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(server.Uri, timeout.Token));
    }

    /// <summary>Default TLS rejects an untrusted certificate; an explicit test callback can trust it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAsync_LocalTls_RequiresExplicitTrust(bool trustLocalCertificate)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var key = RSA.Create();
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var server = new LoopbackServer(certificate);
        var accepting = server.AcceptAsync(timeout.Token);
        RemoteCertificateValidationCallback? trust = trustLocalCertificate ? (_, remote, _, _) => remote?.GetCertHashString() == certificate.GetCertHashString() : null;
        await using var client = new FastWebSocketClient(new RecordingHandler(), trust);
        if (trustLocalCertificate)
        {
            await client.ConnectAsync(server.Uri, timeout.Token);
            await using var peer = await accepting;
            Assert.Equal(WebSocketState.Open, client.State);
        }
        else
        {
            await Assert.ThrowsAsync<AuthenticationException>(() => client.ConnectAsync(server.Uri, timeout.Token));
            try { await using var peer = await accepting; }
            catch (Exception error) when (error is IOException or AuthenticationException) { }
            Assert.Equal(WebSocketState.Closed, client.State);
        }
    }

    /// <summary>Canceling inside OnConnected cannot suppress receive-loop cleanup.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAsync_CancelledInsideConnectedCallback_RunsCleanup(bool standard)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var connectionLifetime = new CancellationTokenSource();
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler(connectionLifetime.Cancel);
        var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, connectionLifetime.Token);
        await using var peer = await accepting;
        await handler.Disconnected.Task.WaitAsync(timeout.Token);
        await client.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.Equal(0, handler.Count);
    }

    /// <summary>Constructs either implementation with the same handler contract.</summary>
    private static IFastWebSocket CreateClient(bool standard, RecordingHandler handler) =>
        standard ? new StdWebSocketClient(handler) : new FastWebSocketClient(handler);

    /// <summary>Cancellation stops idle receive loops and permits bounded disposal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveAsync_IdleConnectionCancelled_StopsAndDisposes(bool standard)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        using var connectionLifetime = new CancellationTokenSource();
        using var server = new LoopbackServer();
        var accepting = server.AcceptAsync(timeout.Token);
        var handler = new RecordingHandler();
        var client = CreateClient(standard, handler);
        await client.ConnectAsync(server.Uri, connectionLifetime.Token);
        await using var peer = await accepting;
        await connectionLifetime.CancelAsync();
        await handler.Disconnected.Task.WaitAsync(timeout.Token);
        await client.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.Equal(0, handler.Count);
    }
}

/// <summary>Copies borrowed test messages and exposes asynchronous lifecycle observations.</summary>
internal sealed class RecordingHandler(Action? connected = null) : IWebSocketMessageHandler
{
    /// <summary>Received logical messages.</summary>
    public Channel<(byte[] Payload, WebSocketMessageType Type)> Messages { get; } = Channel.CreateUnbounded<(byte[], WebSocketMessageType)>();
    /// <summary>First reported transport error.</summary>
    public TaskCompletionSource<Exception> Error { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>First observed disconnection.</summary>
    public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Number of delivered logical messages.</summary>
    public int Count;
    /// <summary>Copies the borrowed payload before the callback completes.</summary>
    public ValueTask OnMessageAsync(ReadOnlySequence<byte> message, WebSocketMessageType type, bool endOfBatch)
    {
        Interlocked.Increment(ref Count);
        Messages.Writer.TryWrite((message.ToArray(), type));
        return ValueTask.CompletedTask;
    }
    /// <summary>Records connection establishment.</summary>
    public void OnConnected() => connected?.Invoke();
    /// <summary>Signals disconnect observers.</summary>
    public void OnDisconnected(WebSocketCloseStatus? status, string? description) => Disconnected.TrySetResult();
    /// <summary>Signals the first error.</summary>
    public void OnError(Exception ex) => Error.TrySetResult(ex);
}
