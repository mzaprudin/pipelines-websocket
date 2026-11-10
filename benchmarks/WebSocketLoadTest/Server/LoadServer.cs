using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WebSocketLoadTest.Server;

/// <summary>Prepared JSON WebSocket streaming generator for local and LAN measurements.</summary>
public sealed class LoadServer : IAsyncDisposable
{
    /// <summary>Original number of complete WebSocket frames per TCP write.</summary>
    public const int FRAMES_PER_BATCH = 20;
    /// <summary>Upgrade challenge suffix.</summary>
    private const string ACCEPT_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    /// <summary>Maximum accepted HTTP request header length.</summary>
    private const int MAX_REQUEST_BYTES = 16_384;
    /// <summary>Original unencoded application message, retained for the archived workload.</summary>
    private static readonly byte[] PAYLOAD = StreamingPayload.Create(StreamingPayload.ORIGINAL_BYTES);
    /// <summary>Current immutable application message.</summary>
    private readonly byte[] _payload;
    /// <summary>One prepared batch shared by all connection writers.</summary>
    private readonly byte[] _preparedBatch;
    /// <summary>Loopback listener; a zero port requests an unused local port.</summary>
    private readonly TcpListener _listener;
    /// <summary>Cancellation observed by accept/handshake/write operations.</summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>Connection tasks owned exclusively by the accept loop until shutdown.</summary>
    private readonly List<Task> _connections = [];
    /// <summary>Observed accept loop.</summary>
    private readonly Task _acceptTask;
    /// <summary>First unexpected server failure.</summary>
    private Exception? _error;

    /// <summary>Prepares a bounded message profile and starts accepting on the selected interface.</summary>
    public LoadServer(int port = 0, int payloadBytes = StreamingPayload.DEFAULT_BYTES,
        int framesPerBatch = FRAMES_PER_BATCH, IPAddress? listenAddress = null)
    {
        _payload = StreamingPayload.Create(payloadBytes);
        var frame = StreamingPayload.CreateFrame(_payload);
        if (framesPerBatch <= 0 || framesPerBatch > StreamingPayload.MAX_BATCH_BYTES / frame.Length)
            throw new ArgumentOutOfRangeException(nameof(framesPerBatch));
        FramesPerBatch = framesPerBatch;
        _preparedBatch = new byte[frame.Length * framesPerBatch];
        for (int i = 0; i < framesPerBatch; i++) frame.CopyTo(_preparedBatch, i * frame.Length);
        var address = listenAddress ?? IPAddress.Loopback;
        _listener = new TcpListener(address, port);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        var advertised = address.Equals(IPAddress.Any) ? IPAddress.Loopback : address;
        Uri = ProfileEndpoint(new UriBuilder("ws", advertised.ToString(), endpoint.Port).Uri, payloadBytes, framesPerBatch);
        _acceptTask = AcceptLoopAsync(_shutdown.Token);
    }

    /// <summary>Local endpoint used by both client implementations.</summary>
    public Uri Uri { get; }
    /// <summary>Application bytes in each logical message.</summary>
    public static ReadOnlyMemory<byte> Payload => PAYLOAD;
    /// <summary>Application payload used by this generator instance.</summary>
    public ReadOnlyMemory<byte> MessagePayload => _payload;
    /// <summary>Complete frames emitted by each prepared write.</summary>
    public int FramesPerBatch { get; }
    /// <summary>Wire bytes per original twenty-frame batch.</summary>
    public int BatchBytes => _preparedBatch.Length;

    /// <summary>Builds the explicit profile route shared by generator and receiver.</summary>
    public static Uri ProfileEndpoint(Uri origin, int payloadBytes, int framesPerBatch) =>
        new Uri(origin, $"/stream/{payloadBytes}/{framesPerBatch}");

    /// <summary>Fails the run when a server connection stopped unexpectedly.</summary>
    public void EnsureHealthy()
    {
        if (Volatile.Read(ref _error) is { } error) throw new IOException("Streaming server failed.", error);
    }

    /// <summary>Accepts connections and retains every task for later observation.</summary>
    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                // Completed peers are observed and removed between comparison runs.
                for (int i = _connections.Count - 1; i >= 0; i--)
                {
                    if (!_connections[i].IsCompleted) continue;
                    await _connections[i].ConfigureAwait(false);
                    _connections.RemoveAt(i);
                }
                _connections.Add(ProcessClientAsync(client, token));
            }
        }
        catch (Exception error) when (token.IsCancellationRequested && error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    /// <summary>Performs the cold upgrade, then continuously writes the unchanged prepared batch.</summary>
    private async Task ProcessClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
                const string KEY_HEADER = "Sec-WebSocket-Key:";
                string? key = null;
                foreach (var line in request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    if (line.StartsWith(KEY_HEADER, StringComparison.OrdinalIgnoreCase)) key = line[KEY_HEADER.Length..].Trim();
                if (!request.StartsWith($"GET {Uri.PathAndQuery} HTTP/1.1\r\n", StringComparison.Ordinal))
                {
                    await stream.WriteAsync("HTTP/1.1 400 Profile mismatch\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
                    return;
                }
                if (key is null) throw new InvalidDataException("Missing upgrade key.");
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + ACCEPT_GUID)));
                var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), token).ConfigureAwait(false);
                // Original measured workload: continuously send the prepared batch, without a delay.
                while (!token.IsCancellationRequested)
                    await stream.WriteAsync(_preparedBatch, token).ConfigureAwait(false);
            }
            catch (Exception error) when (token.IsCancellationRequested && error is OperationCanceledException or IOException or SocketException) { }
            catch (Exception error) when (error is IOException or SocketException)
            {
                // Remote receivers disconnect between independent comparison runs.
            }
            catch (Exception error) { Interlocked.CompareExchange(ref _error, error, null); }
        }
    }


    /// <summary>Reads complete bounded headers without assuming one TCP read contains the request.</summary>
    private static async Task<string> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[MAX_REQUEST_BYTES];
        const int TERMINATOR_BYTES = 4;
        for (int length = 0; length < bytes.Length;)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length, 1), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Connection ended during upgrade.");
            length += read;
            if (length >= TERMINATOR_BYTES && bytes.AsSpan(length - TERMINATOR_BYTES, TERMINATOR_BYTES).SequenceEqual("\r\n\r\n"u8))
                return Encoding.ASCII.GetString(bytes, 0, length);
        }
        throw new InvalidDataException("Upgrade request exceeds the bound.");
    }

    /// <summary>Cancels all writers and observes every accepted connection task.</summary>
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _acceptTask.ConfigureAwait(false);
        await Task.WhenAll(_connections).ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
