# Matched Ethernet/loopback run and allocations

[English](README.md) · [Русский](README.ru.md)

November 10, 2026 · AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · Release/x64. 128-byte JSON, 20 frames/write, 10 connections, 5 s warmup and 30 s measurement. Three fresh receivers per client and topology, alternating client order. All 12 runs were sequential. CPU, GC and allocations cover the receiver process including sampling.

Source: [`d35c490`](https://github.com/mzaprudin/pipelines-websocket/tree/d35c490b8dd6121a471b6ee06d63d73c4c12bbef). Both series use the same DLL set and profile; source and binary fingerprints match. Windows generator: the supplied current portable package, with `/stream/128/20` verified before measurement. The local generator runs in a separate process. Linux interface: `enp6s0`, 1000 Mbit/s, full duplex; Windows negotiated speed was not queried.

| Series | Client | Messages/s | CPU ms / 1M | Allocated kB / 1M | Total allocated bytes by repetition |
|---|---|---:|---:|---:|---|
| ethernet | ClientWebSocket | 802,665 | 3,732 | 95.628 | 2886976 / 2302776 / 2285672 |
| ethernet | Pipelines | 785,592 | 3,706 | 0.592 | 13976 / 13880 / 13944 |
| loopback | ClientWebSocket | 29,421,536 | 427 | 0.524 | 403960 / 463368 / 467304 |
| loopback | Pipelines | 54,595,868 | 237 | 0.009 | 23968 / 14208 / 14240 |

Rows show medians of unrounded per-run values; 1 kB = 1000 bytes. Every run reports GC 0/1/2 = 0. Byte-counter skew is validated for every sample; raw values are retained.

## Allocation traces

Three additional diagnostic sessions: Ethernet/ClientWebSocket, Ethernet/Pipelines, loopback/ClientWebSocket. EventPipe `gc-verbose` was attached after warmup and the first sample, for 20 seconds. These sessions are excluded from the performance table.

| Trace | EnsureBufferContainsAsync samples | EventSource initialization samples | Events lost |
|---|---:|---:|---:|
| ethernet-standard | 18 | 5 | 0 |
| ethernet-pipeline | 0 | 6 | 0 |
| loopback-standard | 2 | 6 | 0 |

The ClientWebSocket trace contains `StateMachineBox` allocations for `ManagedWebSocket.EnsureBufferContainsAsync`, through `PoolingAsyncValueTaskMethodBuilder.GetStateMachineBox`. In [.NET 10.0.14](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/PoolingAsyncValueTaskMethodBuilderT.cs#L264), cache misses create a new box; [EnsureBufferContainsAsync](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Net.WebSockets/src/System/Net/WebSockets/ManagedWebSocket.cs#L1420) uses that builder. This confirms a source of additional allocations in the framework client's asynchronous receive path.

EventSource samples belong to profiler initialization within the first 15 ms. No receive-path allocations were sampled for Pipelines; this is not a zero-allocation assertion. `GCAllocationTick` is sampled: its weights do not provide an exact allocation breakdown by type. Individual read completion frequencies were not measured.

Pipelines' main-run allocation totals stay around 14 kB across different message volumes. ClientWebSocket allocates substantially more over Ethernet. Normalized differences therefore include both instrumentation amortization and additional framework receive-path allocations.

## Evidence

- [Raw summary](summary.json) · [matching contract](comparison-contract.json) · [network and generator metadata](network.json).
- [Ethernet sessions](ethernet/streaming-session.json) · [loopback sessions](loopback/streaming-session.json) · [commands](commands.json).
- [Source snapshot](source-snapshot.json) · [binaries](binaries.json) · [artifact SHA-256](artifacts.sha256.json).
- [Trace commands and sessions](diagnostics/sessions.json) · [analyzer source](diagnostics/analyzer/Program.cs) · [analyzer build command](diagnostics/analysis-commands.json).
- [Ethernet/standard trace](diagnostics/ethernet-standard/allocations.nettrace) · [decoded allocations](diagnostics/ethernet-standard/allocations.json).
- [Ethernet/Pipelines trace](diagnostics/ethernet-pipeline/allocations.nettrace) · [decoded allocations](diagnostics/ethernet-pipeline/allocations.json).
- [Loopback/standard trace](diagnostics/loopback-standard/allocations.nettrace) · [decoded allocations](diagnostics/loopback-standard/allocations.json).
- [Generator control](runner.py) · [capture script](diagnostics/capture.py) · [report derivation](summarize.py).
- [Historical compact Ethernet matrix](../../lan-controls/20261108T202415Z/README.md) remains unchanged.
