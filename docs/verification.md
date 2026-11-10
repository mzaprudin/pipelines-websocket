# Verification record

[English](verification.md) · [Русский](verification.ru.md)

## Initial extraction checks

November 10, 2026: a separate clean git clone passed locked restore, Release build, all **70 tests** (0 failures/skips), and the standalone sample. Builds completed without warnings. The pinned upstream control passed all 126 tests.

The coexistence application referenced both libraries, verified JSON parsing/writing, 100 ordered JSON roundtrips through the public transport, the exported router, and 41 unique public types. Exported types and declared public members were compared with the upstream assembly; the agreed exception is internal PoolRental<T>.

All 4 BenchmarkDotNet cases were measured before and after, each with 5 raw warmup and 15 raw actual iterations. Echo allocations measured 579–581 B/op before and 580–582 B/op after; exact differences are retained in the reports. Complete roundtrips are not zero-allocation scenarios. Six streaming runs were completed before and six after. The self-contained win-x64 generator was built locally; Windows CI verifies executable launch. All 203 upstream Ethernet files are byte-identical; no fresh physical-network measurement was made.

[Commands, reports and logs](../results/local/README.md) · [Provenance](provenance.md)

[Linux / Windows CI](https://github.com/mzaprudin/pipelines-websocket/actions/workflows/ci.yml)

The generated local TLS certificate is normalized to a temporary named key on Windows in the test peer. Certificate identity, explicit-trust/rejection checks and production transport are unchanged.

[Independent acceptance audit](acceptance-audit.md)


## Independent-process streaming

The current receiver CLI supports only separate server/client roles. Local and external runs use the same 128-byte / 20-frame / 10-connection profile and periodic receiver-only counters. The local runner records a distinct server PID, six fresh receiver invocations, validated reports and clean server exit.

Locked restore, a warning-free Release build, all **73 C# tests**, **7 Python lifecycle/evidence tests**, the ordered-message sample and all four BenchmarkDotNet Dry cases passed locally. The tests exercise both automatic local-server hosting and an explicitly supplied endpoint, startup timeout cleanup, generator failure cleanup, damaged-report rejection and graceful shutdown. A current portable win-x64 generator package was built successfully; hosted Windows CI checks executable launch.

Fresh full measurements: [20261110T202155Z](../results/local/20261110T202155Z/README.md), measured source `4b57e71`. All four echo cases and six receiver-only streaming processes completed successfully. The raw evidence retains distinct PIDs, per-run allocation totals, samples and clean generator exit.
