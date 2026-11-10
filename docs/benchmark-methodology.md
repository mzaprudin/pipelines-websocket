# Benchmark methodology

[English](benchmark-methodology.md) · [Русский](benchmark-methodology.ru.md)

Streaming compares continuous reception by direct `ClientWebSocket.ReceiveAsync(Memory<byte>)` and `FastWebSocketClient`. Both receive the same prepared JSON frames from the same generator. The standard client reuses its receive array; the Pipelines handler counts borrowed payloads without copying them. This workload measures reception and counting, rather than application JSON parsing.

The shared profile in [streaming-profile.json](../scripts/streaming-profile.json) is **128 payload bytes, 20 frames per write, 10 connections, 5 s warmup, 30 s measurement, three fresh receiver processes per client**. A server write contains 2640 bytes including WebSocket headers. Client order alternates between repetitions. The generator checks the requested payload/batch route during the HTTP upgrade; a mismatch fails the run.

```sh
# Separate generator process on this machine; six fresh receiver processes.
python3 scripts/streaming_benchmarks.py --output results/local/MY_STREAM
# Same receiver, profile, counters and order against another machine.
python3 scripts/streaming_benchmarks.py --url ws://GENERATOR-IP:5050 --output results/network/MY_STREAM
# Also measure all four independent echo cases, sequentially.
python3 scripts/run-benchmarks.py --output results/local/MY_RUN
python3 scripts/render-results.py results/local/MY_RUN
```

For the external run, start a matching generator on the other machine:

```sh
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 128 --batch 20
```

Without `--url`, the runner starts the same server executable on an ephemeral localhost port, waits for its actual listening address, and keeps it alive across all six receivers. It checks server health, bounds startup/measurement/shutdown, sends a graceful stop command, and observes exit status. A failed receiver or server invalidates the session. CPU, managed allocations and GC cover **only the receiver process**; generator and Python runner costs are excluded. Logs retain the commands and distinct process IDs. `--count`, `--warmup`, `--duration`, `--runs`, `--payload-bytes` and `--batch` override the shared streaming profile for controls; use identical settings at both endpoints.

Warmup is excluded. Throughput uses completed messages divided by actual monotonic elapsed time. The unchanged periodic measurement protocol takes counter samples approximately once per second and includes their allocation, task scheduling and console output in receiver process counters. This is not an allocation trace of the transport alone. Fixed instrumentation costs divided by more messages give fewer bytes per message, even with identical transport allocation behavior. Summary files retain total allocated bytes and normalized values for every repetition; kB = 1000 bytes.

## Echo roundtrips

A supplementary benchmark for tracking complete request-response regressions. One request at a time runs over an established connection, with byte-for-byte response validation. Four cases: 128 bytes / 4 KiB × Pipelines / ClientWebSocket adapter.

The measured process includes the client, echo peer and asynchronous harness. The framework adapter uses a Pipe for the callback API. Allocations cover all these components.

<!-- ECHO_RESULTS_START -->
| Client | Payload | Mean | Error | Allocated |
|---|---:|---:|---:|---:|
| Pipelines | 128 B | 32.49 µs | 908.36 ns | 581 B |
| ClientWebSocket adapter | 128 B | 33.29 µs | 1.22 µs | 581 B |
| Pipelines | 4 KiB | 39.23 µs | 1.04 µs | 580 B |
| ClientWebSocket adapter | 4 KiB | 37.76 µs | 909.96 ns | 581 B |

<!-- ECHO_RESULTS_END -->

BenchmarkDotNet 0.15.8 · Release/x64 · five warmup and fifteen measurement iterations at a 500 ms target · one launch per case. Error is the half-width of the 99.9% confidence interval. Allocations are managed bytes per complete roundtrip. Full samples, outlier handling, GC and stdout are retained in the reports; latency percentiles are not measured.

[Raw echo reports](../results/local/20261110T202155Z/bdn/results/) · [Benchmark setup](../benchmarks/Pipelines.WebSockets.Benchmarks/WebSocketBenchmarks.cs) · [Harness](../support/RoundtripSession.cs)

## Evidence and historical measurements

Every fresh run retains environment, source revision and SHA-256, binary hashes, exact child commands, raw samples, logs and artifact hashes. `render-results.py` validates the complete session and arithmetic before generating the README streaming tables and the supplementary echo tables here. `verify-evidence.py` checks the published bundle, measured runtime inputs, source mappings and immutable historical artifacts. Linux and Windows CI exercise the common runner, both receivers, normal server shutdown and failure cleanup.

The November 8 Ethernet matrices retain their original dates, sources and manifests. Their main compact profile is also 128 bytes / 20 frames / 10 connections / 5 s / 30 s, with receiver-only process counters. The library still requests a fixed 64 KiB TCP receive buffer; automatic sizing is a separate historical control. The [November 10 matched rerun](../results/streaming/20261110T205733Z/README.md) contains twelve sequential streaming sessions with identical receiver binaries and three separate allocation traces. The former 87-byte colocated loopback bundles remain available as historical evidence; their generator-inclusive resource counters are not a matched baseline for current streaming.

Byte and message counters update separately, bytes first. Each snapshot can observe one in-progress update per connection. For two snapshots, validation allows conservative skew of `2 × ClientCount × PayloadBytes`, including each interval. Raw counters remain unchanged; throughput uses completed-message counts.
