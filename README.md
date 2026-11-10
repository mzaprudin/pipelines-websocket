# pipelines-websocket

[English](README.md) · [Русский](README.ru.md) · [CI](https://github.com/mzaprudin/pipelines-websocket/actions/workflows/ci.yml)

A TCP/TLS WebSocket client for continuous streams of small messages, such as market trades and quotes, in C# / .NET 10. System.IO.Pipelines carries received bytes in pooled memory and hands message payloads directly to the application.

The receive path follows a **zero-allocation approach**: a warmed, unfragmented message needs no separate payload array. The handler borrows its bytes until its callback completes. Connection setup, pool growth and fragment assembly have separate allocation costs.

## Engineering decisions

- **Make memory lifetime explicit.** An unfragmented payload is a borrowed `ReadOnlySequence<byte>`; the Pipe releases storage after the handler completes. Data kept longer is copied by the application.
- **Bound incoming work.** Fragmented messages use a separate assembly path with a 1 MiB limit. Pipe backpressure thresholds allow a complete frame to arrive without stalling the producer.
- **Own connection shutdown.** Cancellable fill/read loops and serialized sends have a single lifecycle owner. Shutdown observes both loop tasks.
- **Validate the protocol.** The HTTP upgrade, frame boundaries and control traffic are checked; TLS uses normal certificate validation. ClientWebSocket provides the benchmark reference.

## WebSocket over physical Ethernet

**128-byte JSON · 20 frames/write · 10 connections · 1-Gbit Ethernet.** Windows generator → Linux receiver. Medians of three runs, with 5 s warmup and 30 s measurement.

<!-- ETHERNET_RESULTS_START -->
**Main implementation: 743,095 messages/s and 130.32× less managed memory allocated per message than ClientWebSocket.**

| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---|---:|---:|---:|
| ClientWebSocket | 718,218 | 3,707 | 85.103 |
| Pipelines · fixed 64 KiB buffer | 743,095 | 3,652 | 0.653 |
| Pipelines · automatic buffer (control) | 768,400 | 3,559 | 0.607 |

CPU and allocations cover the receiver process including sampling; 1 kB = 1000 bytes. The main client uses a fixed 64 KiB TCP receive buffer. Automatic sizing is a separate control variant.

<!-- ETHERNET_RESULTS_END -->

Measured November 8, 2026: [conditions and raw data](results/lan-controls/20261108T202415Z/README.md), [measured source revision](https://github.com/mzaprudin/zero-alloc-json-ws/tree/c2c3b1ad1d186073690c540b43b76cad18a54454). The [one-KiB control matrix](results/lan-controls/20261108T194624Z/README.md) covers the same receive-buffer and batch-size choices with larger messages.

<details>
<summary>Ethernet: 1 KiB JSON, one frame per write</summary>

<!-- LAN_RESULTS_START -->
| Client | Messages/s | Payload Mbit/s | CPU ms / 1M messages | Allocated kB / 1M messages | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|
| `ClientWebSocket` | 101,373 | 830.5 | 30,981 | 114.793 | 0/0/0 |
| `FastWebSocketClient` · Pipelines | 96,201 | 788.1 | 30,210 | 7.715 | 0/0/0 |

Allocations are normalized to 1,000,000 messages; 1 kB = 1000 bytes. GC shows observed collections.

Medians of three runs: 10 connections, 10 s warmup and 60 s measurement; one frame per write. CPU, allocations and GC cover the Linux receiver process, including metric sampling.

[Full results and conditions](results/lan-1kib/20261108T191616Z/README.md) · [JSON](results/lan-1kib/20261108T191616Z/summary.json)
<!-- LAN_RESULTS_END -->

</details>

## Local reception and echo benchmarks

AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · Release/x64. Streaming measures continuous reception; echo measures a complete verified roundtrip. These are different workloads with different allocation scopes.

<!-- LOCAL_RESULTS_START -->
### Continuous reception on loopback

87-byte JSON, 20 frames/write, 10 connections. Medians of three runs: 5 s warmup and 30 s measurement. CPU and allocations include the generator, clients and sampling.

| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---|---:|---:|---:|
| ClientWebSocket | 42,695,264 | 380 | 0.054 |
| Pipelines | 69,954,803 | 277 | 0.012 |

This is a local streaming workload. Its rates and CPU costs apply to this setup and differ from the physical-network measurements.

### Complete local echo roundtrips

Each message is sent, echoed and checked byte-for-byte. Time and allocation include both peers and asynchronous scheduling.

| Client | Payload | Mean | Error | Allocated |
|---|---:|---:|---:|---:|
| Pipelines | 128 B | 33.88 µs | 1.89 µs | 582 B |
| ClientWebSocket adapter | 128 B | 32.65 µs | 857.37 ns | 580 B |
| Pipelines | 4 KiB | 38.71 µs | 1.01 µs | 581 B |
| ClientWebSocket adapter | 4 KiB | 38.48 µs | 1.34 µs | 580 B |

Echo measures a complete roundtrip with its own allocation profile. Continuous reception from pooled memory is measured separately.

<!-- LOCAL_RESULTS_END -->

Echo uses BenchmarkDotNet 0.15.8: five warmup and fifteen measurement iterations at a 500 ms target, one launch per case. Allocations are managed bytes per roundtrip; error is the half-width of the 99.9% confidence interval.

[Raw local reports and samples](results/local/20261110T184649Z/) · [Measurement methodology and reproduction](docs/benchmark-methodology.md)

## Build and run the example

Requires the .NET 10 SDK:

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

The [example](samples/Pipelines.WebSockets.Demo/Program.cs) sends 100 numbered messages through Pipelines and the ClientWebSocket adapter, checking every byte and message order. Tests use real local TCP/TLS peers to exercise frame sizes, fragmentation, invalid upgrades, cancellation and certificate trust. Linux and Windows CI also check benchmark setups and streaming; Windows builds and runs the portable generator.

[Memory ownership and limits](docs/architecture.md) · [Development](docs/development.md) · [Verification](docs/verification.md) · [Windows LAN generator](docs/lan-benchmark.md) · [Source and measurement provenance](docs/provenance.md)

Published for portfolio review without an open-source license. See [NOTICE](NOTICE).
