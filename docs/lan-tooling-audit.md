# Independent readiness audit: one-KiB LAN tooling

Date: 2026-11-08. Scope: the one-KiB LAN tooling milestone in [PLAN.md](../PLAN.md).

**Tool-readiness acceptance: PASS. No remaining BLOCKER/HIGH findings.**

## Resolved findings

- **HIGH — startup liveness:** continuously buffered receive work could occupy
  connection setup. Receive loops are now queued without a task-scheduling
  cancellation token, while the token still reaches the loop. Canceled lifetimes
  continue to execute cleanup.
- **HIGH — portable runtime packaging:** native runtime inclusion is explicit
  through `IncludeNativeLibrariesForSelfExtract=true`. The Windows CI job
  extracts the delivered ZIP to a clean directory and starts that executable.

The generator observes/prunes completed connection tasks, and the streaming
test captures only its first message before allocating a copy.

## Independent runtime checks

```sh
dotnet test tests/GapSonar.TransportLab.Tests/GapSonar.TransportLab.Tests.csproj -c Release --no-build --logger 'console;verbosity=minimal'
dotnet run -c Release --no-build --project samples/GapSonar.TransportLab.Demo
```

Both exited 0: **122 tests passed**, 0 failed/skipped; the demo verified 100 ordered
messages per client. A persistent generator also served sequential native and
Pipelines receiver processes using 1024-byte messages and batch1. Both completed
with valid rate/CPU arithmetic and `separate-process-loopback` / `receiver-only`
metadata.

The clean source copy `/tmp/zero-alloc-lan-verify.OjOcmF` passed locked restore,
Release build (0 warnings/errors), and all 122 tests. Frame boundaries at
65535/65536 bytes, profile mismatches, startup cancellation, payload lengths,
and bounded configuration are covered.

## Portable package

The package contains a valid Windows AMD64 executable, launcher, instructions,
and manifest. All 36 runtime-source hashes match the current sources and clean
copy. The executable hash matches the manifest; the launcher selects server
mode, 1024 bytes, batch1 and port5050.

- ZIP bytes: `32037876`.
- SHA256: `bfdbf103d6040904cccd61fa77698c89b5038f37ecd59125abdc227ac628285b`.

The Windows workflow verifies the extracted package. Physical Ethernet
measurements are collected after the external Windows generator starts; this
readiness audit does not record a physical-LAN performance result.
