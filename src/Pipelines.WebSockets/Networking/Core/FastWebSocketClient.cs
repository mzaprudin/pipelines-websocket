#pragma warning disable CA1805

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using GapSonar.Core.Exchange.Infrastructure.Diagnostics;
using GapSonar.Core.Exchange.Networking.Messaging;
using GapSonar.Core.Exchange.Networking.Protocol;

using Microsoft.Extensions.Logging;

namespace GapSonar.Core.Exchange.Networking.Core;

/// <summary>TCP/TLS WebSocket client that dispatches borrowed Pipe buffers to a message handler.</summary>
public sealed class FastWebSocketClient(
    IWebSocketMessageHandler handler,
    RemoteCertificateValidationCallback? validationCallback = null,
    ILogger? log = null,
    WebSocketMetricContext metricContext = default,
    TimeSpan? serverActivityTimeout = null) : IFastWebSocket
{
    private const int DISPOSE_DISCONNECT_TIMEOUT_SECONDS = 5;
    private static readonly TimeSpan DISPOSE_DISCONNECT_TIMEOUT = TimeSpan.FromSeconds(DISPOSE_DISCONNECT_TIMEOUT_SECONDS);

    public static readonly IDictionary<string, string[]> HostHeaders = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["gateio.ws"] = ["X-Gate-Size-Decimal: 1"],
    };

    private readonly IWebSocketMessageHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly RemoteCertificateValidationCallback? _validationCallback = validationCallback;
    private readonly WebSocketMetricContext _metricContext = metricContext;
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private Pipe? _pipe;
    private volatile bool _isRunning;
    private CancellationTokenSource? _internalCts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    /// <summary>Serializes data frames and their continuation state.</summary>
    private readonly SemaphoreSlim _dataSendLock = new(1, 1);
    /// <summary>The data message currently being sent in fragments.</summary>
    private WebSocketMessageType? _outboundMessageType;
    private Task? _fillPipeTask = null;
    private Task? _processPipeTask = null;
    private MemoryStream? _fragmentedMessageBuffer;
    private WebSocketMessageType _fragmentedMessageType;

    // Keep-alive mechanism
    private Timer? _keepAliveTimer;
    private Timer? _pingMonitorTimer;
    private DateTime _lastServerActivityTime = DateTime.UtcNow;
    private readonly TimeSpan _keepAliveInterval = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _serverActivityTimeout = serverActivityTimeout ?? TimeSpan.FromMinutes(5);

    /// <summary>
    /// Configured server activity timeout. Exposed for test assertions
    /// (InternalsVisibleTo "Tests").
    /// </summary>
    internal TimeSpan ServerActivityTimeout => _serverActivityTimeout;

    public WebSocketState State { get; private set; } = WebSocketState.None;

    public string? ProxyAddress { get; set; }

    /// <summary>
    /// Optional caller-provided predicate that reports whether this client is
    /// expected to receive data frames. When set and returning <see langword="false"/>,
    /// <see cref="CheckServerPingTimeout"/> skips the configured activity timeout.
    /// A host can disable that watchdog while the connection has no expected data.
    /// Null preserves the default behavior of always checking activity.
    /// </summary>
    public Func<bool>? HasExpectedActivity { get; set; }

    /// <summary>Connects and validates the HTTP upgrade; TLS validates certificates by default.</summary>
    public async Task ConnectAsync(Uri uri, CancellationToken token = default)
    {
        if (uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("Expected a ws or wss URI.", nameof(uri));
        if (State == WebSocketState.Open || State == WebSocketState.Connecting)
            throw new InvalidOperationException("Socket is already connected or connecting.");

        State = WebSocketState.Connecting;
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            _tcpClient = new TcpClient
            {
                // TCP NoDelay is critical for HFT to disable Nagle's algorithm
                NoDelay = true,
                ReceiveBufferSize = 65536,
                SendBufferSize = 65536,
            };

            // Enable TCP Keep-Alive to detect dead connections early
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, optionValue: true);

            // Set TCP Keep-Alive parameters (where supported)
            // These values help detect dead connections within ~90 seconds
            try
            {
                // TCP_KEEPIDLE: time before starting keepalive probes (60 seconds)
                _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
                // TCP_KEEPINTVL: interval between keepalive probes (10 seconds)
                _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                // TCP_KEEPCNT: number of keepalive probes before declaring dead (3 probes)
                _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch (SocketException)
            {
                // Some platforms may not support these options, that's ok
                log?.LogDebug("[FastWebSocketClient] TCP keep-alive fine-tuning not supported on this platform");
            }

            if (!string.IsNullOrEmpty(ProxyAddress))
            {
                // Handle proxy addresses without scheme (e.g. "1.2.3.4:8080")
                string proxyUrl = ProxyAddress;
                if (!proxyUrl.Contains("://", StringComparison.Ordinal))
                {
                    proxyUrl = "http://" + proxyUrl;
                }

                var proxyUri = new Uri(proxyUrl);
                await _tcpClient.ConnectAsync(proxyUri.Host, proxyUri.Port, token).ConfigureAwait(false);

                Stream proxyStream = _tcpClient.GetStream();

                // HTTP CONNECT Tunneling
                int port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
                string connectRequest = $"CONNECT {uri.Host}:{port} HTTP/1.1\r\nHost: {uri.Host}:{port}\r\n\r\n";
                await proxyStream.WriteAsync(Encoding.ASCII.GetBytes(connectRequest), token).ConfigureAwait(false);

                // Simple response parsing for CONNECT
                using var reader = new StreamReader(proxyStream, Encoding.ASCII, leaveOpen: true);
                string? response = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (response is null || !response.Contains("200"))
                    throw new WebSocketException($"Proxy tunnel failed: {response}");

                // Consume headers
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token).ConfigureAwait(false))) { }

                _stream = proxyStream;
            }
            else
            {
                await _tcpClient.ConnectAsync(uri.Host, uri.Port, token).ConfigureAwait(false);
                _stream = _tcpClient.GetStream();
            }

            if (uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                var sslStream = new SslStream(
                    innerStream: _stream!,
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: _validationCallback,
                    userCertificateSelectionCallback: null);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host }, token).ConfigureAwait(false);
                _stream = sslStream;
            }

            // Setup Pipe with Inline Scheduling for maximum responsiveness
            var pipeOptions = new PipeOptions(
                pool: MemoryPool<byte>.Shared,
                readerScheduler: PipeScheduler.Inline,
                writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES + WebSocketFrameHeader.MaxHeaderSize,
                resumeWriterThreshold: WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES / 2,
                useSynchronizationContext: false
            );

            _pipe = new Pipe(pipeOptions);
            _isRunning = true;

            // Start filling the pipe immediately to capture Handshake response + any immediate data
            _fillPipeTask = FillPipeAsync(_pipe.Writer, _internalCts.Token);

            // Perform Handshake using the Pipe for reading
            await PerformHandshakeAsync(uri, _stream, _pipe.Reader, token).ConfigureAwait(false);

            State = WebSocketState.Open;
            _handler.OnConnected();

            // Initialize keep-alive tracking
            _lastServerActivityTime = DateTime.UtcNow;

            // Start proactive keep-alive timer (send unsolicited pong every 2 minutes)
            _keepAliveTimer = new Timer(
                callback: _ => _ = SendKeepAlivePongAsync(),
                state: null,
                dueTime: _keepAliveInterval,
                period: _keepAliveInterval);

            // Start ping monitor timer (check if server ping timeout every minute)
            _pingMonitorTimer = new Timer(
                callback: _ => CheckServerPingTimeout(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1));

            // Start Processing Loop (consuming the rest of the pipe)
            // Keep readiness independent of continuously buffered receive work.
            // Do not cancel task scheduling: the loop must always execute its cleanup.
            var receiveReader = _pipe.Reader;
            var receiveToken = _internalCts.Token;
            _processPipeTask = Task.Run(() => ProcessPipeAsync(receiveReader, receiveToken));
        }
        catch (Exception ex)
        {
            State = WebSocketState.Closed;
            Cleanup();
            if (_fillPipeTask is not null) await _fillPipeTask.ConfigureAwait(false);
            if (_pipe is not null) await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
            _handler.OnError(ex);
            throw;
        }
    }

    /// <summary>Builds the HTTP upgrade request and validates its bounded response.</summary>
    private async Task PerformHandshakeAsync(Uri uri, Stream stream, PipeReader reader, CancellationToken token)
    {
        // 1. Generate Key (Zero-Alloc). RFC 6455 §4.1 requires a freshly-
        // chosen 16-byte nonce; cryptographically secure RandomNumberGenerator
        // satisfies "selected randomly" (audit LO-10).
        Span<byte> keyBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(keyBytes);

        Span<char> keyBase64Chars = stackalloc char[24];
        Convert.TryToBase64Chars(keyBytes, keyBase64Chars, out int charsWritten);

        // 2. Build Request (Zero-Alloc using PoolRental)
        // Estimating size: ~300-400 bytes usually. 2048 is safe.
        using var rent = new PoolRental<byte>(2048);

        int offset = 0;
        // Use Span overload: GetBytes(ReadOnlySpan<char>, Span<byte>)
        void Append(string s) => offset += Encoding.ASCII.GetBytes(s, rent.Span.Slice(offset));

        Append("GET ");
        Append(uri.PathAndQuery);
        Append(" HTTP/1.1\r\n");
        Append("Host: ");
        Append(uri.IsDefaultPort ? uri.Host : uri.Authority);
        Append("\r\n");
        Append("Upgrade: websocket\r\n");
        Append("Connection: Upgrade\r\n");
        Append("Sec-WebSocket-Key: ");
        offset += Encoding.ASCII.GetBytes(keyBase64Chars.Slice(0, charsWritten), rent.Span.Slice(offset));
        Append("\r\n");
        Append("Sec-WebSocket-Version: 13\r\n");

        foreach (var kvp in HostHeaders)
        {
            if (!uri.Host.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var header in kvp.Value)
            {
                Append(header);
                Append("\r\n");
            }
        }

        Append("\r\n");

        var nonce = new string(keyBase64Chars[..charsWritten]);
        // Write Request
        await stream.WriteAsync(rent.Memory.Slice(0, offset), token).ConfigureAwait(false);

        // 3. Read Response from PIPE to preserve extra data
        while (!token.IsCancellationRequested)
        {
            ReadResult result = await reader.ReadAsync(token).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (result.IsCanceled)
            {
                throw new OperationCanceledException("Handshake read was canceled.");
            }

            // Search for double newline \r\n\r\n indicating end of headers
            var position = FindEndOfHeaders(buffer);

            if (position is not null)
            {
                if (buffer.Slice(0, position.Value).Length > WebSocketProtocol.MAX_HANDSHAKE_HEADER_BYTES)
                    throw new WebSocketException("Upgrade response exceeds the header limit.");
                var response = Encoding.ASCII.GetString(buffer.Slice(0, position.Value).ToArray());
                ValidateHandshake(response, nonce);

                // Consume the headers from the pipe
                reader.AdvanceTo(position.Value);
                return; // Handshake success
            }

            if (buffer.Length > WebSocketProtocol.MAX_HANDSHAKE_HEADER_BYTES)
                throw new WebSocketException("Upgrade response exceeds the header limit.");

            // Need more data
            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                throw new WebSocketException("Connection closed before handshake completion.");
            }
        }

        token.ThrowIfCancellationRequested();
    }

    /// <summary>Validates the server upgrade challenge and required HTTP tokens.</summary>
    private static void ValidateHandshake(string response, string nonce)
    {
        var lines = response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !(lines[0] == "HTTP/1.1 101" || lines[0].StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal)))
            throw new WebSocketException("Handshake failed: expected HTTP 101.");
        var expected = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(nonce + WebSocketProtocol.ACCEPT_GUID)));
        bool upgrade = false, connection = false, accept = false;
        foreach (var line in lines.AsSpan(1))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var name = line[..separator];
            var value = line[(separator + 1)..].Trim();
            if (name.Equals("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase)) accept = value == expected;
            if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) upgrade = value.Equals("websocket", StringComparison.OrdinalIgnoreCase);
            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                connection = value.Split(',').Any(static item => item.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase));
            if (name.Equals("Sec-WebSocket-Extensions", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase))
                throw new WebSocketException("Server selected an extension or subprotocol that was not requested.");
        }
        if (!upgrade || !connection || !accept) throw new WebSocketException("Handshake failed: invalid upgrade headers or accept challenge.");
    }

    /// <summary>Finds the HTTP terminator without consuming any subsequent frame bytes.</summary>
    private static SequencePosition? FindEndOfHeaders(ReadOnlySequence<byte> buffer)
    {
        // Looking for \r\n\r\n (13, 10, 13, 10)
        var reader = new SequenceReader<byte>(buffer);
        while (reader.TryReadTo(out ReadOnlySequence<byte> _, "\r\n\r\n"u8, advancePastDelimiter: true))
        {
            return reader.Position;
        }
        return null;
    }

    /// <summary>Reads TCP/TLS bytes into pooled Pipe segments until cancellation or EOF.</summary>
    private async Task FillPipeAsync(PipeWriter writer, CancellationToken token)
    {
        const int bufferSize = 8192;
        try
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                Memory<byte> memory = writer.GetMemory(bufferSize);
                int bytesRead = await _stream!.ReadAsync(memory, token).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break; // End of stream
                }

                writer.Advance(bytesRead);

                // FlushAsync forces the reader to process data immediately due to InlineScheduler
                FlushResult result = await writer.FlushAsync(token).ConfigureAwait(false);

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log?.LogError($"[FastWebSocketClient] FillPipeAsync Error: {ex.GetType().Name}: {ex.Message}");
            if (_isRunning) _handler.OnError(ex);
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Dispatches complete data messages and handles protocol control frames.</summary>
    private async Task ProcessPipeAsync(PipeReader reader, CancellationToken token)
    {
        try
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(token).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadFrame(ref buffer, out var payload, out var opCode, out bool fin))
                {
                    // Refresh server-activity watchdog ONLY on data frames (continuation/text/
                    // binary). WS-protocol Ping (9), Pong (10), and Close (8) MUST NOT bump the
                    // timer — the server's pong responses to our own KeepAliveLoop pings would
                    // otherwise mask a half-open business channel (mirror of commit e0d4e84 at
                    // the FastWebSocketClient layer). Metric emission stays unconditional to
                    // preserve full-frame observability.
                    if (IsDataFrameOpCode((byte)opCode))
                    {
                        _lastServerActivityTime = DateTime.UtcNow;
                    }
                    WebSocketMetrics.AddFrameReceived(_metricContext, opCode, payload.Length);

                    // Control Frames
                    if (opCode == 8) // Close
                    {
                        State = WebSocketState.CloseReceived;
                        // This is the receive task: awaiting DisconnectAsync here would await itself.
                        var closePayload = payload.ToArray();
                        var closeStatus = closePayload.Length >= sizeof(ushort)
                            ? (WebSocketCloseStatus?)BinaryPrimitives.ReadUInt16BigEndian(closePayload)
                            : null;
                        var closeDescription = closePayload.Length > sizeof(ushort)
                            ? Encoding.UTF8.GetString(closePayload.AsSpan(sizeof(ushort)))
                            : null;
                        try
                        {
                            await SendFrameAsync(closePayload, WebSocketProtocol.CLOSE_OPCODE, true, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            State = WebSocketState.Closed;
                            _handler.OnDisconnected(closeStatus, closeDescription);
                        }
                        return;
                    }

                    if (opCode == 9) // Ping
                    {
                        if (log is not null && log.IsEnabled(LogLevel.Debug))
                        {
                            if (payload.Length > 0)
                            {
                                var payloadStr = DecodeControlFramePayload(payload);
                                log?.LogDebug("[FastWebSocketClient] Protocol Ping received, sending Pong. Payload: {payloadStr}", payloadStr);
                            }
                            else
                            {
                                log?.LogDebug("[FastWebSocketClient] Protocol Ping received (empty), sending Pong.");
                            }
                        }

                        _handler.OnPing(payload);
                        WebSocketMetrics.IncrementPingReceived(_metricContext);
                        await SendPongAsync(payload, token).ConfigureAwait(false);
                    }
                    else if (opCode == 10) // Pong
                    {
                        if (log is not null && log.IsEnabled(LogLevel.Debug))
                        {
                            if (payload.Length > 0)
                            {
                                var payloadStr = DecodeControlFramePayload(payload);
                                log?.LogDebug("[FastWebSocketClient] Protocol Pong received. Payload: {payloadStr}", payloadStr);
                            }
                            else
                            {
                                log?.LogDebug("[FastWebSocketClient] Protocol Pong received (empty).");
                            }
                        }

                        _handler.OnPong(payload);
                        WebSocketMetrics.IncrementPongReceived(_metricContext);
                    }
                    // Data Frames and continuations. RFC 6455 permits a logical
                    // WebSocket message to be split into an initial text/binary frame
                    // followed by one or more continuation frames. Exchange payloads
                    // such as Phemex batched trades can cross this boundary; dispatching
                    // the first frame directly would hand parsers truncated JSON.
                    else if (opCode == 0 || opCode == 1 || opCode == 2)
                    {
                        if (opCode == 0)
                        {
                            if (_fragmentedMessageBuffer is null)
                            {
                                throw new InvalidOperationException("Continuation frame received before a fragmented data frame.");
                            }

                            if (_fragmentedMessageBuffer.Length + payload.Length > WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES)
                                throw new WebSocketException("Fragmented message exceeds the payload limit.");
                            CopySequenceToStream(payload, _fragmentedMessageBuffer);
                            if (fin)
                            {
                                await DispatchFragmentedMessageAsync(buffer.Length == 0 && !result.IsCompleted).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            var msgType = opCode == 1 ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
                            if (fin)
                            {
                                if (_fragmentedMessageBuffer is not null)
                                    throw new WebSocketException("A new data message interrupted an unfinished message.");
                                // Check if buffer is empty after this frame to hint "EndOfBatch"
                                bool endOfBatch = buffer.Length == 0 && !result.IsCompleted;
                                await _handler.OnMessageAsync(payload, msgType, endOfBatch).ConfigureAwait(false);
                            }
                            else
                            {
                                if (_fragmentedMessageBuffer is not null)
                                    throw new WebSocketException("A new data message interrupted an unfinished message.");
                                _fragmentedMessageBuffer = new MemoryStream(payload.Length <= int.MaxValue ? (int)payload.Length : 0);
                                _fragmentedMessageType = msgType;
                                if (_fragmentedMessageBuffer.Length + payload.Length > WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES)
                                throw new WebSocketException("Fragmented message exceeds the payload limit.");
                            CopySequenceToStream(payload, _fragmentedMessageBuffer);
                            }
                        }
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log?.LogError(ex, "[FastWebSocketClient] ProcessPipeAsync Error in shard");
            _handler.OnError(ex);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            if (State == WebSocketState.Open)
            {
                State = WebSocketState.Closed;
                _handler.OnDisconnected(WebSocketCloseStatus.ProtocolError, "Connection dropped");
            }
            Cleanup();
        }
    }

    /// <summary>Copies a completed fragmented message for callback delivery and releases its assembly buffer.</summary>
    private async ValueTask DispatchFragmentedMessageAsync(bool endOfBatch)
    {
        if (_fragmentedMessageBuffer is null)
        {
            return;
        }

        try
        {
            var payload = _fragmentedMessageBuffer.ToArray();
            await _handler.OnMessageAsync(new ReadOnlySequence<byte>(payload), _fragmentedMessageType, endOfBatch).ConfigureAwait(false);
        }
        finally
        {
            await _fragmentedMessageBuffer.DisposeAsync().ConfigureAwait(false);
            _fragmentedMessageBuffer = null;
        }
    }

    /// <summary>Copies segmented payload bytes into the bounded fragmented-message buffer.</summary>
    private static void CopySequenceToStream(ReadOnlySequence<byte> sequence, Stream stream)
    {
        foreach (var segment in sequence)
        {
            stream.Write(segment.Span);
        }
    }

    /// <summary>
    /// Decodes a UTF-8 control-frame payload (Ping/Pong) to string for diagnostic logging.
    /// Control frames are <= 125 bytes per RFC 6455, so a fixed stackalloc buffer is used
    /// for multi-segment cases to avoid heap allocation via <c>ReadOnlySequence.ToArray()</c>.
    /// </summary>
    private static string DecodeControlFramePayload(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }

        // RFC 6455 control-frame limit is 125 bytes; round up for safety.
        Span<byte> buf = stackalloc byte[128];
        int len = (int)payload.Length;
        if (len > buf.Length)
        {
            // Defensive fallback: oversized control frame, decode first segment only.
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }
        payload.CopyTo(buf);
        return Encoding.UTF8.GetString(buf.Slice(0, len));
    }

    /// <summary>Parses a bounded frame header and borrows its complete payload from the input sequence.</summary>
    private bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload, out int opCode, out bool fin)
    {
        payload = default;
        opCode = 0;
        fin = false;

        if (buffer.Length < 2) return false;

        // Peek first 2 bytes
        // To avoid allocs, we use a small stack buffer if sequence is fragmented
        Span<byte> header = stackalloc byte[14]; // Max header size

        // Fast path: if the first segment is large enough for basic header
        if (buffer.FirstSpan.Length >= 2)
        {
            header[0] = buffer.FirstSpan[0];
            header[1] = buffer.FirstSpan[1];
        }
        else
        {
            buffer.Slice(0, 2).CopyTo(header.Slice(0, 2));
        }

        fin = (header[0] & 0x80) != 0;
        opCode = header[0] & 0x0F;
        bool masked = (header[1] & 0x80) != 0;
        long payloadLen = header[1] & 0x7F;
        int headerLen = 2;

        if (payloadLen == 126)
        {
            if (buffer.Length < 4) return false;
            if (buffer.FirstSpan.Length >= 4)
            {
                payloadLen = BinaryPrimitives.ReadUInt16BigEndian(buffer.FirstSpan.Slice(2));
            }
            else
            {
                buffer.Slice(2, 2).CopyTo(header.Slice(2, 2));
                payloadLen = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2));
            }
            headerLen += 2;
        }
        else if (payloadLen == 127)
        {
            if (buffer.Length < 10) return false;
            if (buffer.FirstSpan.Length >= 10)
            {
                payloadLen = BinaryPrimitives.ReadInt64BigEndian(buffer.FirstSpan.Slice(2));
            }
            else
            {
                buffer.Slice(2, 8).CopyTo(header.Slice(2, 8));
                payloadLen = BinaryPrimitives.ReadInt64BigEndian(header.Slice(2));
            }
            headerLen += 8;
        }

        if ((header[0] & WebSocketProtocol.RESERVED_BITS_MASK) != 0 || opCode is not (0 or 1 or 2 or WebSocketProtocol.CLOSE_OPCODE or WebSocketProtocol.PING_OPCODE or WebSocketProtocol.PONG_OPCODE))
            throw new WebSocketException("Reserved bits or unsupported opcode.");
        if (payloadLen < 0 || payloadLen > WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES)
            throw new WebSocketException("Frame exceeds the payload limit.");
        if (opCode >= WebSocketProtocol.CLOSE_OPCODE && (!fin || payloadLen > WebSocketProtocol.MAX_CONTROL_PAYLOAD_BYTES))
            throw new WebSocketException("Invalid control frame.");
        if (opCode == WebSocketProtocol.CLOSE_OPCODE && payloadLen == 1)
            throw new WebSocketException("Invalid close payload.");
        if (masked) throw new WebSocketException("Server frames must not be masked.");

        if (buffer.Length < headerLen + payloadLen) return false;

        payload = buffer.Slice(headerLen, payloadLen);
        buffer = buffer.Slice(headerLen + payloadLen);
        return true;
    }

    /// <summary>Sends text or binary data, using continuation frames after an unfinished fragment.</summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type, bool endOfMessage = true, CancellationToken token = default)
    {
        if (type is not (WebSocketMessageType.Text or WebSocketMessageType.Binary))
            throw new ArgumentException("Use DisconnectAsync for close frames.", nameof(type));
        await _dataSendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_outboundMessageType is not null && _outboundMessageType != type)
                throw new InvalidOperationException("An unfinished message must keep its original type.");
            int opcode = _outboundMessageType is not null ? 0 : type == WebSocketMessageType.Text ? 1 : 2;
            await SendFrameAsync(message, opcode, endOfMessage, token).ConfigureAwait(false);
            _outboundMessageType = endOfMessage ? null : type;
        }
        finally { _dataSendLock.Release(); }
    }

    /// <summary>Masks one frame in a pooled buffer and serializes access to the transport stream.</summary>
    private async ValueTask SendFrameAsync(ReadOnlyMemory<byte> message, int opCode, bool endOfMessage, CancellationToken token)
    {
        if (message.Length > WebSocketProtocol.MAX_MESSAGE_PAYLOAD_BYTES) throw new ArgumentException("Payload exceeds the configured limit.", nameof(message));
        if (State != WebSocketState.Open && opCode != 8) throw new InvalidOperationException("Socket not open");

        // 14 header + payload
        int totalLen = 14 + message.Length;
        using var rent = new PoolRental<byte>(totalLen);

        var bufferSpan = rent.Span;
        // Generate mask. RFC 6455 §5.3 mandates an unpredictable 32-bit
        // masking key per frame to defeat cache-poisoning attacks; use a
        // cryptographic RNG, not System.Random (audit LO-10).
        Span<byte> mask = stackalloc byte[4];
        RandomNumberGenerator.Fill(mask);

        // Write Header at start of buffer
        int headerLen = WebSocketFrameHeader.WriteHeader(
            destination: bufferSpan,
            payloadLength: message.Length,
            masked: true,
            fin: endOfMessage,
            opCode: opCode);

        // Write Mask Key into header
        mask.CopyTo(bufferSpan.Slice(headerLen, 4));
        headerLen += 4;

        // Copy payload
        message.Span.CopyTo(bufferSpan.Slice(headerLen));

        // Mask payload in-place
        WebSocketFrameHeader.ApplyMask(bufferSpan.Slice(headerLen, message.Length), mask);

        // Send Everything
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Check if stream is still valid
            if (_stream is null) throw new InvalidOperationException("Transport stream is closed.");
            var mem = rent.Memory.Slice(0, headerLen + message.Length);
            await _stream.WriteAsync(mem, token).ConfigureAwait(false);
            WebSocketMetrics.AddFrameSent(_metricContext, opCode, message.Length);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Echoes a protocol ping payload as a masked pong.</summary>
    private async ValueTask SendPongAsync(ReadOnlySequence<byte> payload, CancellationToken token)
    {
        // Pong must contain the same payload as Ping
        // It must be masked
        // 125 bytes max for control frames.
        if (payload.Length > 125) return;

        int len = (int)payload.Length;
        using var rent = new PoolRental<byte>(len);

        payload.CopyTo(rent.Span);
        // OpCode 10 = Pong
        // Note: SendFrameAsync will Rent another buffer for Header+Masking.
        // For Control Frames this double copy is negligible (max 125 bytes).
        await SendFrameAsync(message: rent.Memory, opCode: 10, endOfMessage: true, token: token).ConfigureAwait(false);
    }

    /// <summary>Sends an explicit protocol ping with a bounded payload.</summary>
    public async ValueTask SendPingAsync(ReadOnlyMemory<byte> payload, CancellationToken token = default)
    {
        if (payload.Length > 125) throw new ArgumentException("Ping payload too large", nameof(payload));
        // OpCode 9 = Ping
        await SendFrameAsync(message: payload, opCode: 9, endOfMessage: true, token: token).ConfigureAwait(false);
        WebSocketMetrics.IncrementPingSent(_metricContext);
    }

    /// <summary>Sends a close frame, cancels the loops, and waits for them within the caller deadline.</summary>
    public async Task DisconnectAsync(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string? statusDescription = null, CancellationToken token = default)
    {
        if (State == WebSocketState.Closed || State == WebSocketState.None) return;

        _isRunning = false;
        // Send Close Frame if possible
        try
        {
            // OpCode 8 = Close
            await SendFrameAsync(
                message: CreateClosePayload(status, statusDescription),
                opCode: 8,
                endOfMessage: true,
                token: token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, $"[FastWebSocketClient] DisconnectAsync: Failed to send close frame");
        }

        State = WebSocketState.Closed;
        Cleanup();

        if (_fillPipeTask is not null) await _fillPipeTask.WaitAsync(token).ConfigureAwait(false);
        if (_processPipeTask is not null) await _processPipeTask.WaitAsync(token).ConfigureAwait(false);
    }

    /// <summary>Encodes a bounded close status and UTF-8 reason on the shutdown path.</summary>
    private static byte[] CreateClosePayload(WebSocketCloseStatus status, string? description)
    {
        var reason = Encoding.UTF8.GetBytes(description ?? string.Empty);
        if (reason.Length > WebSocketProtocol.MAX_CONTROL_PAYLOAD_BYTES - sizeof(ushort))
            throw new ArgumentException("Close description exceeds the control frame limit.", nameof(description));
        var payload = new byte[sizeof(ushort) + reason.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)status);
        reason.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>Closes the connection and observes both loops with a bounded shutdown deadline.</summary>
    public async ValueTask DisposeAsync()
    {
        using var disconnectCts = new CancellationTokenSource(DISPOSE_DISCONNECT_TIMEOUT);
        try
        {
            await DisconnectAsync(token: disconnectCts.Token).ConfigureAwait(false);
            if (_fillPipeTask is not null) await _fillPipeTask.WaitAsync(disconnectCts.Token).ConfigureAwait(false);
            if (_processPipeTask is not null) await _processPipeTask.WaitAsync(disconnectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            log?.LogWarning(
                "[FastWebSocketClient] Dispose disconnect exceeded {TimeoutSeconds}s; forcing cleanup",
                DISPOSE_DISCONNECT_TIMEOUT_SECONDS);
            State = WebSocketState.Closed;
            Cleanup();
        }
        finally { _internalCts?.Dispose(); }
    }

    /// <summary>
    /// Sends an unsolicited pong frame to keep the connection alive.
    /// This is proactive keep-alive to prevent server from closing the connection.
    /// </summary>
    private async Task SendKeepAlivePongAsync()
    {
        try
        {
            if (State == WebSocketState.Open)
            {
                // Send empty pong frame
                await SendFrameAsync(
                    message: ReadOnlyMemory<byte>.Empty,
                    opCode: 10, // Pong
                    endOfMessage: true,
                    token: CancellationToken.None).ConfigureAwait(false);

                log?.LogDebug("[FastWebSocketClient] Proactive pong sent");
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "[FastWebSocketClient] Failed to send proactive pong");
        }
    }

    /// <summary>
    /// Checks if we haven't received any data from the server within the expected timeout.
    /// If timeout is exceeded, triggers connection error handling.
    /// </summary>
    private void CheckServerPingTimeout()
    {
        var nowUtc = DateTime.UtcNow;
        if (!ShouldFireConnectionTimeout(State, _lastServerActivityTime, nowUtc,
                                          _serverActivityTimeout, HasExpectedActivity))
        {
            return;
        }

        if (log is not null && log.IsEnabled(LogLevel.Error))
            log?.LogError("[FastWebSocketClient] Server activity timeout detected. Last activity: {LastActivityTime:HH:mm:ss.fff}, Elapsed: {ElapsedMs}ms",
                _lastServerActivityTime, (nowUtc - _lastServerActivityTime).TotalMilliseconds);

        // Trigger connection error - this will cause the connection to be dropped
        // and the upper layer will handle reconnection
        _ = HandleConnectionTimeoutAsync();
    }

    /// <summary>
    /// Handles connection timeout by closing the connection gracefully.
    /// A host application can observe the disconnect and apply its reconnect policy.
    /// </summary>
    private async Task HandleConnectionTimeoutAsync()
    {
        try
        {
            // Notify handler about the disconnection and error
            _handler.OnError(new TimeoutException("Server ping timeout - no ping received within expected interval"));

            await DisconnectAsync(WebSocketCloseStatus.ProtocolError, "Server ping timeout", CancellationToken.None).ConfigureAwait(false);
            _handler.OnDisconnected(WebSocketCloseStatus.ProtocolError, "Server ping timeout");
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[FastWebSocketClient] Error during connection timeout handling");
        }
    }

    /// <summary>
    /// RFC 6455 data-frame opcodes: 0 = continuation, 1 = text, 2 = binary.
    /// Control frames (Close=8, Ping=9, Pong=10) are excluded because their
    /// delivery does not prove the business channel is alive (incident
    /// 2026-05-12 / commit e0d4e84). Internal-via-InternalsVisibleTo so the
    /// GapSonar.Core/Tests assembly can pin the invariant.
    /// </summary>
    internal static bool IsDataFrameOpCode(byte opCode) =>
        opCode is 0 or 1 or 2;

    /// <summary>
    /// Pure predicate for the connection-timeout watchdog. Single source of truth
    /// shared by <see cref="CheckServerPingTimeout"/> and the test suite. Returns
    /// true when the watchdog should fire a TimeoutException + reconnect.
    /// </summary>
    internal static bool ShouldFireConnectionTimeout(
        WebSocketState state,
        DateTime lastServerActivity,
        DateTime nowUtc,
        TimeSpan serverActivityTimeout,
        Func<bool>? hasExpectedActivity)
    {
        if (state != WebSocketState.Open) return false;
        if (hasExpectedActivity is not null && !hasExpectedActivity()) return false;
        return (nowUtc - lastServerActivity) > serverActivityTimeout;
    }

    /// <summary>Cancels background activity and releases transport and fragmentation resources.</summary>
    private void Cleanup()
    {
        _isRunning = false;

        // Dispose timers
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        _pingMonitorTimer?.Dispose();
        _pingMonitorTimer = null;

        _fragmentedMessageBuffer?.Dispose();
        _fragmentedMessageBuffer = null;
        _outboundMessageType = null;

        _internalCts?.Cancel();
        _tcpClient?.Dispose();
        _stream?.Dispose();
        _stream = null;
    }

}
