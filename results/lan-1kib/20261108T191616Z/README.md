# One-KiB WebSocket reception over physical Ethernet

Measured on 2026-11-08. Windows x64 generator → Ethernet switch → Linux receiver.
The Linux `enp6s0` interface negotiated **1000 Mbit/s, full duplex**; the route to
the generator uses that interface. The user reports both NICs and the switch
are rated for 1 Gbit/s. The Windows negotiated link was not queried.

Receiver: AMD Ryzen 9 5950X, Ubuntu 24.04, .NET 10.0.14, SDK 10.0.114, Release/x64.
The generator runs the self-contained Windows package from the
[LAN tooling release](https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib).

## Shared workload

- Ten connections per implementation; implementations run sequentially in fresh processes.
- Exactly **1024 application bytes** of prepared UTF-8 JSON depth data per message.
- One complete WebSocket text frame per generator write, including its four-byte frame header.
- Plain `ws://` TCP connections directly to the generator; native proxy use is disabled.
- Ten seconds of warmup, then sixty seconds of measured reception per process.
- Three runs per implementation, in this order: standard1, pipeline1, pipeline2, standard2, standard3, pipeline3.
- Both receive callbacks count complete messages and application bytes.

## Measurement scope

Throughput counts completed application messages received by Linux. Payload
Mbit/s excludes WebSocket, TCP/IP and Ethernet headers. CPU time covers the
Linux receiver process, including kernel time charged to it. CPU cost is
normalized per million completed messages. Allocation volume includes the
receiver, transport, timers and periodic measurement sampling; GC columns
record collections during the measured interval. The Windows generator runs
in a separate process on another machine.

The tables in both READMEs use the per-metric median across all three runs.
Each report retains actual elapsed time, command-line arguments, byte/message
deltas, receiver-process metrics and every one-second sample. Short two-client
connectivity checks in the parent directory are excluded from these medians.

## Reproduce and verify

On Windows, run `START-GENERATOR.cmd` from the released ZIP. On Linux:

```sh
bash scripts/run-lan-benchmarks.sh ws://192.168.1.75:5050
python3 scripts/sync-lan-results.py results/lan-1kib/RUN-DIRECTORY
```

The validator requires all six reports with the same endpoint and workload,
checks whole-run and sample arithmetic, and derives the published tables.
Message and byte counters are read separately while reception continues;
payload validation allows the bounded snapshot skew across ten clients.

## Evidence

- [Median summary and evidence hashes](summary.json).
- [Receiver environment, route, commands, and measurement scope](environment.json).
- [Exact receiver source hashes](source-snapshot.json).
- [Released generator manifest and source hashes](generator-manifest.json).
- [Standard 1](standard-1.json) · [log](standard-1.log).
- [Standard 2](standard-2.json) · [log](standard-2.log).
- [Standard 3](standard-3.json) · [log](standard-3.log).
- [Pipelines 1](pipeline-1.json) · [log](pipeline-1.log).
- [Pipelines 2](pipeline-2.json) · [log](pipeline-2.log).
- [Pipelines 3](pipeline-3.json) · [log](pipeline-3.log).

The released generator is pinned to `25f242543616cc05a3f2d35274943a1483bf2893`.
The receiver snapshot also includes the direct-proxy setting and corrected bind
address display. The source hashes identify the exact measured receiver code.
