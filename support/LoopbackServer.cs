using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GapSonar.TransportLab.Support;

/// <summary>Local HTTP upgrade server using the .NET WebSocket implementation as the peer.</summary>
internal sealed class LoopbackServer : IDisposable
{
    /// <summary>Maximum upgrade request size accepted by the test server.</summary>
    private const int MAX_REQUEST_BYTES = 16_384;
    /// <summary>WebSocket challenge GUID from RFC 6455.</summary>
    private const string ACCEPT_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    /// <summary>Listener bound to an operating-system-assigned loopback port.</summary>
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    /// <summary>Optional certificate for local TLS verification.</summary>
    private readonly X509Certificate2? _certificate;

    /// <summary>Starts a server accessible only on the local machine.</summary>
    public LoopbackServer(X509Certificate2? certificate = null)
    {
        _certificate = certificate;
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        Uri = new Uri($"{(certificate is null ? "ws" : "wss")}://localhost:{endpoint.Port}/echo");
    }

    /// <summary>Endpoint for the client under test.</summary>
    public Uri Uri { get; }

    /// <summary>Accepts an upgrade, optionally transforming the response for negative tests.</summary>
    public async Task<ServerConnection> AcceptAsync(CancellationToken token, Func<string, string>? transformResponse = null)
    {
        var tcp = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        tcp.NoDelay = true;
        Stream stream = tcp.GetStream();
        try
        {
            if (_certificate is not null)
            {
                var tls = new SslStream(stream);
                stream = tls;
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, token).ConfigureAwait(false);
            }
            var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
            string? key = null;
            foreach (var line in request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                const string KEY_HEADER = "Sec-WebSocket-Key:";
                if (line.StartsWith(KEY_HEADER, StringComparison.OrdinalIgnoreCase)) key = line[KEY_HEADER.Length..].Trim();
            }
            if (key is null) throw new InvalidDataException("Missing WebSocket key.");
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + ACCEPT_GUID)));
            var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(transformResponse?.Invoke(response) ?? response), token).ConfigureAwait(false);
            var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
            return new ServerConnection(tcp, stream, socket);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Echoes logical messages, preserving their message type.</summary>
    public async Task EchoAsync(CancellationToken token)
    {
        await using var connection = await AcceptAsync(token).ConfigureAwait(false);
        const int MAX_MESSAGE_BYTES = 1_048_576;
        var buffer = new byte[MAX_MESSAGE_BYTES];
        int offset = 0;
        while (!token.IsCancellationRequested)
        {
            var result = await connection.Socket.ReceiveAsync(buffer.AsMemory(offset), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token).ConfigureAwait(false);
                return;
            }
            offset += result.Count;
            if (offset == buffer.Length && !result.EndOfMessage) throw new InvalidDataException("Echo message exceeded the limit.");
            if (!result.EndOfMessage) continue;
            await connection.Socket.SendAsync(buffer.AsMemory(0, offset), result.MessageType, true, token).ConfigureAwait(false);
            offset = 0;
        }
    }

    /// <summary>Reads exactly through the HTTP header terminator without consuming frame bytes.</summary>
    private static async Task<string> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var request = new byte[MAX_REQUEST_BYTES];
        const int TERMINATOR_BYTES = 4;
        for (int length = 0; length < request.Length;)
        {
            int read = await stream.ReadAsync(request.AsMemory(length, 1), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Client closed during upgrade.");
            length += read;
            if (length >= TERMINATOR_BYTES && request.AsSpan(length - TERMINATOR_BYTES, TERMINATOR_BYTES).SequenceEqual("\r\n\r\n"u8))
                return Encoding.ASCII.GetString(request, 0, length);
        }
        throw new InvalidDataException("Upgrade request exceeded the limit.");
    }

    /// <summary>Stops accepting new connections.</summary>
    public void Dispose() => _listener.Stop();
}

/// <summary>Owns a local peer socket and its underlying TCP/TLS connection.</summary>
internal sealed class ServerConnection(TcpClient tcp, Stream stream, WebSocket socket) : IAsyncDisposable
{
    /// <summary>Raw wire stream for protocol-level negative tests.</summary>
    public Stream Stream { get; } = stream;
    /// <summary>The framework WebSocket peer.</summary>
    public WebSocket Socket { get; } = socket;

    /// <summary>Closes the peer and releases the network resources.</summary>
    public async ValueTask DisposeAsync()
    {
        Socket.Dispose();
        await Stream.DisposeAsync().ConfigureAwait(false);
        tcp.Dispose();
    }
}
