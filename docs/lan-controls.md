# Controlled TCP receive-buffer and batching experiment

This experiment measures two independent factors over the same physical
Ethernet network: the custom receiver's TCP receive-buffer policy and the
generator's number of complete WebSocket frames per application write.
Each matrix holds the JSON payload size constant: 1024 bytes or the compact
128-byte profile. Both profiles contain valid JSON depth data and use a
four-byte unmasked WebSocket frame header.

## Receiver controls

Three receivers are measured for every batch size:

- Direct `ClientWebSocket`, with the existing reusable receive buffer and proxy bypass.
- Pipelines with its original explicit 65536-byte socket receive-buffer setting.
- Pipelines with automatic socket receive sizing, built by removing only that explicit assignment.

The fixed and automatic variants are built in isolated source directories from
the same repository revision. The automatic variant keeps the fixed send
buffer, application read size, Pipe options, frame parser and callbacks. The
runner verifies that exactly one source file differs, saves the one-line patch,
and fingerprints all source and both transport/application assemblies. Locked
restore, Release build and the complete test suite must pass for each variant
before any timing begins.

## Windows generators

Use the executable from the existing
[Windows generator release](https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib).
Run these commands in PowerShell from its extracted directory:

```powershell
Start-Process .\WebSocketLoadTest.exe -ArgumentList '--mode server --listen 0.0.0.0 --port 5050 --payload-bytes 1024 --batch 1'
Start-Process .\WebSocketLoadTest.exe -ArgumentList '--mode server --listen 0.0.0.0 --port 5051 --payload-bytes 1024 --batch 4'
Start-Process .\WebSocketLoadTest.exe -ArgumentList '--mode server --listen 0.0.0.0 --port 5052 --payload-bytes 1024 --batch 20'
```

An already running batch1 generator on port5050 can keep serving that profile.
All generators remain available, while the runner loads only one at a time.
The HTTP upgrade path checks the exact payload/batch combination on each port.

## Linux experiment

```sh
python3 scripts/lan-controls.py run 192.168.1.75
```

For the compact profile, replace `--payload-bytes 1024` with
`--payload-bytes 128` in all three generator commands, then run:

```sh
python3 scripts/lan-controls.py run 192.168.1.75 --payload-bytes 128
```

The original 87-byte message is the length of a prepared trade JSON document.
The compact control profile uses exactly 128 application bytes and therefore
occupies 132 bytes including its WebSocket header.

The full matrix has **27 fresh-process runs**: three receiver variants × three
batch sizes × three repetitions. Each uses ten connections, five seconds of
warmup and thirty seconds of measured reception. Profile order and receiver
order rotate independently between repetitions; each receiver occupies each
position once within every profile. Measurement takes approximately sixteen
minutes after both source builds have passed validation.

The generator prepares frames before streaming. Frames per write is an
application-level setting; TCP manages packet boundaries. Throughput counts
completed received messages and application bytes. CPU, managed allocations
and GC counters cover only the Linux receiver process, including sampling.

At the middle of each measurement, the external runner captures `ss -tinm`
for that generator port. This records the socket receive-buffer capacity and
TCP window observations for all ten connections. The snapshot runs outside
the measured process. The report retains the complete command and output.

## Evidence and validation

Every result directory contains:

- All 27 unmodified receiver JSON reports and completion logs.
- Corresponding TCP socket snapshots and observed receive-buffer capacities.
- Environment, kernel settings, negotiated Linux link, exact commands and execution order.
- Fixed/automatic source and binary fingerprints, build/test logs and the exact patch.
- A complete median/range table and an evidence hash manifest.

To independently validate and render a completed bundle:

```sh
python3 scripts/lan-controls.py summarize results/lan-controls/RUN-DIRECTORY
```

The validator requires every receiver/profile/repetition, valid completion,
matching workload and scope, consistent sample sums and derived rate/CPU/
allocation arithmetic. Comparisons use all three runs for each condition.

The compact run uses a stable counter reader: it observes each client's message
count before and after reading its byte count, retrying if the message count
advanced. Since the single writer adds bytes before incrementing completed
messages, a stable pair has at most one in-flight payload per client. The
existing strict byte-consistency bound therefore remains conservative. Retries
are bounded and fail explicitly if no stable observation is available. This
changes only periodic sampling, with four deterministic regression tests;
receiving code and callbacks are unchanged. The earlier interrupted compact
diagnostic run is excluded from all published medians.

Measured results: [complete matrix](../results/lan-controls/20261108T194624Z/README.md)
and [findings](lan-controls-findings.md).

## Resuming an observer-only interruption

TCP snapshots retain all returned socket states. Capacity extraction selects
only `ESTAB` entries, excluding lingering sockets from earlier closed clients.
The validator still requires exactly ten active observations for each case.

```sh
python3 scripts/lan-controls.py resume results/lan-controls/RUN-DIRECTORY
```

Resume validates every completed receiver report, reconstructs both isolated
builds at their original paths, and requires identical source and binary
fingerprints before measuring any missing conditions. It records the resume
runner, validation logs, reason and timestamp. Completed raw receiver reports
and socket snapshots are preserved. Measurement continues in the original
balanced order, with a fresh process for each remaining condition.
