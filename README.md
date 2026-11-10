# pipelines-websocket

[English](README.md) · [Русский](README.ru.md) · [CI](https://github.com/mzaprudin/pipelines-websocket/actions/workflows/ci.yml)

WebSocket clients for C# / .NET 10: direct TCP/TLS with System.IO.Pipelines and pooled buffers, alongside a ClientWebSocket adapter. Borrowed message memory and reusable buffers reduce allocation pressure during continuous reception.

## WebSocket over physical Ethernet

Measured November 8, 2026: Windows generator → Linux receiver, 1-Gbit Ethernet, 128-byte JSON, 20 frames/write, 10 connections. Medians of three runs: 5 s warmup, 30 s measurement.

| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---|---:|---:|---:|
| ClientWebSocket | 718,218 | 3,707 | 85.103 |
| Pipelines · fixed 64 KiB receive buffer | 743,095 | 3,652 | 0.653 |
| Pipelines · automatic buffer, separate build | 768,400 | 3,559 | 0.607 |

CPU and allocations cover the receiver including sampling; 1 kB = 1000 bytes. The library retains its fixed 64 KiB TCP receive buffer. Automatic sizing was measured in a separate build with a [one-line patch](results/lan-controls/20261108T202415Z/auto-receive.patch).

[Measured revision c2c3b1a](https://github.com/mzaprudin/zero-alloc-json-ws/tree/c2c3b1ad1d186073690c540b43b76cad18a54454) · [128-byte matrix and raw reports](results/lan-controls/20261108T202415Z/README.md) · [1-KiB matrix](results/lan-controls/20261108T194624Z/README.md) · [1-KiB Ethernet runs](results/lan-1kib/20261108T191616Z/README.md)

All original Ethernet bundles, dates, patches and manifests are preserved byte-for-byte. They measure the earlier combined assembly. Fresh results for the split assembly below use loopback; new physical measurements are a separate stage.

## Run it

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

Requires the .NET 10 SDK. All **70 original tests** are retained. The example verifies contents and ordering of 100 messages through each client.

## Fresh local measurements

AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · BenchmarkDotNet 0.15.8. Release/x64; 5 warmups, 15 measurement iterations with a 500 ms target, one launch.

Echo measures a verified roundtrip through a local peer. Streaming: 87-byte JSON, 20 frames/write, 10 connections, three fresh processes per client; 5 s warmup and 30 s measurement. Streaming CPU and allocations include the local server, clients and sampling.

<!-- LOCAL_RESULTS_START -->
Measurements are being prepared.
<!-- LOCAL_RESULTS_END -->

[Complete reports, raw samples and before/after comparison](results/local/README.md) · [Methodology](docs/benchmark-methodology.md)

```sh
python3 scripts/run-benchmarks.py
python3 scripts/render-results.py results/local/RUN_DIRECTORY
```

## Architecture and verification

[Memory ownership and limits](docs/architecture.md) · [Build and CI](docs/development.md) · [Verification](docs/verification.md) · [Provenance](docs/provenance.md)

The standalone `Pipelines.WebSockets` assembly preserves GapSonar namespaces. It has no dependency on [zero-alloc-json](https://github.com/mzaprudin/zero-alloc-json); both libraries can be referenced together.

Published for portfolio review without an open-source license. See [NOTICE](NOTICE).
