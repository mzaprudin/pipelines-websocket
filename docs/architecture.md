# Architecture and memory ownership

[English](architecture.md) · [Русский](architecture.ru.md)

FastWebSocketClient connects TcpClient, optional SslStream, a producer writing to a Pipe, and a bounded frame parser reading that Pipe. StdWebSocketClient implements the same IFastWebSocket surface using the framework client.

An unfragmented message is a borrowed ReadOnlySequence<byte>. The callback may use its bytes until its returned ValueTask completes; the parser then advances the Pipe and returns storage to the pool. Copy any data retained beyond callback completion. Fragment assembly uses a separate bounded copy/allocation path, with a 1 MiB message limit. Pipe backpressure thresholds exceed the maximum frame so waiting for a complete frame cannot deadlock the producer.

Outgoing frames use scoped ArrayPool rentals, cryptographic mask keys and serialized sends. A separate data-message gate maintains continuation state; callers own serialization of entire fragmented messages. Control frames may interleave. One lifecycle owner manages the receive loop, connection and shutdown; cancellation propagates to both loops and shutdown observes their tasks.

The upgrade validates HTTP 101, required tokens and the accept challenge. TLS uses normal certificate validation unless the caller explicitly supplies a callback. Tests trust only an ephemeral local certificate. Data-activity timers ignore control-only traffic. Automatic reconnect and exchange subscriptions belong to the application. StdWebSocketClient.SendPingAsync throws NotSupportedException; framework keepalive is configured with KeepAliveInterval.

The main implementation retains ReceiveBufferSize = 65536. The automatic TCP receive-buffer variant removes only that assignment in an isolated experimental build; it is neither the main library nor a runtime option. Send-buffer and receive algorithms remain the same.

PoolRental<T> is private to this assembly and also privately copied in ZeroAlloc.Json. No JSON implementation, model, interner or fixture is required by the transport. LoopbackServer and RoundtripSession are test/sample support, not library APIs.

StdWebSocketClient is internal, for tests, the sample and performance comparison. The public transport is FastWebSocketClient through IFastWebSocket and IWebSocketMessageHandler.
