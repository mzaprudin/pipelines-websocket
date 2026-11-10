# Benchmark methodology

[English](benchmark-methodology.md) · [Русский](benchmark-methodology.ru.md)

The pinned upstream revision and split assemblies are measured sequentially on the same machine: Release/x64, 5 warmup and 15 measurement iterations targeting 500 ms, one launch. Error is the half-width of the BenchmarkDotNet 99.9% confidence interval. BenchmarkDotNet handles outliers; full samples, intervals, GC and allocations are retained in JSON. Iteration time is a target rather than a hard duration.

Four echo cases: 128/4096 bytes × Pipelines/ClientWebSocket, verifying every returned byte. Six streaming runs: two clients, three fresh processes each, 10 connections, 87-byte JSON, 20 frames/write, 5 s warmup and 30 s measurement. Client order alternates across repetitions.

The server writes prepared 1780-byte batches. Warmup is excluded; rates use messages / actual monotonic elapsed time. Validation checks profiles, healthy completion, sample sums and bounded message/byte snapshot skew. Loopback CPU/GC/allocations cover the whole process including the generator and sampling; Ethernet archives measure only the receiver. kB = 1000 bytes; allocation per million uses unrounded AllocatedBytes / Messages.

Original Ethernet matrices are preserved in full. Automatic TCP buffer sizing is a separate one-assignment control; the current library requests a fixed 64 KiB buffer. No new physical-network measurements were made during the split.

```sh
python3 scripts/run-benchmarks.py --output results/local/MY_RUN
python3 scripts/render-results.py results/local/MY_RUN
```

For before/after comparison, copy the matching upstream control reports into MY_RUN/baseline/. The runner retains commands, environment, source/binary SHA-256, full BDN stdout and JSON/CSV/Markdown. render-results validates inputs and writes summary.json plus both README tables. verify-evidence checks copied files and published evidence.

A timing difference after extraction does not by itself establish an algorithm change: JIT, code layout, scheduling and background processes affect results. Compare intervals and raw samples. Verification establishes correctness and retained zero-allocation scenarios; tail latency was not measured separately.

Byte and message counters update separately, bytes first. Each snapshot may observe one in-progress update per connection. For the delta between two snapshots, validation uses the conservative bound `2 × ClientCount × PayloadBytes`, matching the upstream LAN validator; raw values are unchanged. Throughput uses complete-message counts. summary.json retains each run's observed byte skew. Baseline pipeline-2 has −87 bytes of total skew; this reflects counter observation rather than transport message loss.
