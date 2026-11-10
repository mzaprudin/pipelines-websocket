# One-KiB WebSocket benchmark over Ethernet

The generator and receiver run on separate computers:

```text
Windows PC                         Ethernet                      Linux PC
JSON depth generator  ─────────────────────────────────────────► receiver
1024 payload bytes / message                         ClientWebSocket or Pipelines
```

## Windows generator

Download the portable Windows x64 package from
[LAN benchmark tools](https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib),
extract it, and run `START-GENERATOR.cmd`. The package includes its .NET runtime.
The generator listens on TCP port 5050 and stays available for successive runs.
Stop it with Ctrl+C.

Equivalent command:

```powershell
.\WebSocketLoadTest.exe --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 1024 --batch 1
```

Use the PC's Ethernet IPv4 address for the Linux client. If Windows displays a
firewall prompt, allow the generator on the private network. To check negotiated
link speed:

```powershell
Get-NetAdapter -Physical | Select-Object Name, Status, LinkSpeed
```

## Linux receiver

```sh
bash scripts/run-lan-benchmarks.sh ws://WINDOWS-IP:5050
```

This builds the receiver and alternates three fresh-process runs per client.
Each run uses ten connections, ten seconds of warmup and sixty seconds of
measurement. The generator profile is checked during the HTTP upgrade, so
payload and batch settings agree at both ends.

For a shorter first check:

```sh
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode client --url ws://WINDOWS-IP:5050 --payload-bytes 1024 --batch 1 \
  --count 2 --warmup 1 --duration 3 --json results/lan-1kib/check.json
```

Add `--std` to select the direct `ClientWebSocket` baseline. Without it the
receiver uses `FastWebSocketClient`.

Both receivers connect directly to the specified PC. The native baseline sets
`ClientWebSocket.Options.Proxy = null`, so ambient HTTP proxy settings cannot
redirect the Ethernet workload.

The first Windows package prints a loopback address in its startup banner even
when launched with `--listen 0.0.0.0`. Its listener still binds to all IPv4
interfaces as requested by the launcher. Current source displays the configured
bind address. Connect the Linux receiver to the Windows PC's Ethernet address.

## Payload and results

The default profile is exactly 1024 bytes of valid UTF-8 JSON containing depth
levels. `--payload-bytes` supports 128 bytes through one MiB; `87` selects the
original compact trade message. `--batch` configures complete frames per write.
The LAN profile uses one frame per write; TCP packet boundaries are managed by
the operating system.

Each JSON report includes:

- Actual completed messages/s and application payload Mbit/s.
- Receiver-process CPU milliseconds per million completed messages.
- Logical-core utilization, managed bytes per message, and GC counts.
- Payload size, frame batch size, endpoint, execution topology, and every sample.

At a one-Gbit link, the frame-only calculation for this profile is
`1,000,000,000 / ((1024 + 4) × 8)`, approximately 122 thousand messages/s before
TCP/Ethernet overhead. CPU cost at the same delivered traffic rate makes the
receiver implementation's efficiency visible alongside throughput.

`loopback` tags a combined local process. `separate-process-loopback` tags a
receiver connected to a loopback address in another process. `remote-client`
tags the remote receiver role; actual physical-LAN runs use the other PC's address.

## Build from source

```sh
python3 scripts/build-windows-generator.py
```

The builder publishes a self-contained `win-x64` executable, including native
runtime libraries through `IncludeNativeLibrariesForSelfExtract=true`, from an isolated
source copy without lock files, using `RestorePackagesWithLockFile=false` for
that copy. Normal development restore files remain unchanged. The ZIP and its
SHA-256 file are saved to `artifacts/lan-generator-win-x64/`. The ZIP includes
the launcher and a manifest of source/binary hashes and the exact publish command.

## Controlled tuning experiment

To compare automatic TCP receive sizing and batches of 1, 4 and 20 frames,
use the [controlled experiment guide](lan-controls.md). It builds isolated
fixed/automatic receiver variants and measures a balanced 27-run matrix.
