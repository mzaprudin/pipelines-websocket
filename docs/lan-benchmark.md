# Local and Ethernet streaming benchmark

[English](lan-benchmark.md) · [Русский](lan-benchmark.ru.md)

One receiver executable measures both clients against a generator in another process. Without an address the runner hosts that generator on localhost; with `--url` it connects to another machine. The default is 128-byte JSON, 20 frames/write, 10 connections, 5 s warmup, 30 s measurement, three fresh processes per client.

```sh
python3 scripts/streaming_benchmarks.py
python3 scripts/streaming_benchmarks.py --url ws://GENERATOR-IP:5050
```

CPU, GC and allocations cover only the receiver, including periodic metric sampling. The local generator has a distinct PID and shares the host's resources. See [methodology and limitations](benchmark-methodology.md).

## External generator

On the server machine, using the current source:

```sh
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 128 --batch 20
```

On Windows, build the portable x64 package with `python scripts/build-windows-generator.py`, copy `artifacts/lan-generator-win-x64/pipelines-websocket-generator-win-x64.zip` to the generator PC, extract it and run `START-GENERATOR.cmd`. The package includes its .NET runtime. The launcher uses the same compact profile. Stop the external generator with Ctrl+C after all runs. If prompted, allow access on the private Ethernet network. Use the server's Ethernet address, rather than `0.0.0.0`, in `--url`.

The ZIP includes source/binary fingerprints, the exact publish command, profile and executable SHA-256. Publishing uses an isolated source copy without lock files and `RestorePackagesWithLockFile=false`; normal development lock files remain unchanged. Linux and Windows CI exercise the common process runner; Windows also launches the unpacked portable executable.

## Controls

Use the same explicit payload/batch on the external generator and receiver. The profile route is verified during HTTP upgrade. For the historic one-KiB profile:

```sh
# Generator machine:
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 1024 --batch 1
# Receiver machine:
python3 scripts/streaming_benchmarks.py --url ws://GENERATOR-IP:5050 \
  --payload-bytes 1024 --batch 1 --count 10 --warmup 10 --duration 60
```

`bash scripts/run-lan-benchmarks.sh ws://GENERATOR-IP:5050` is a wrapper for the same runner and accepts its profile options. Both receiver implementations bypass ambient HTTP proxies. Valid payload sizes are 87 (original trade fixture) or 128 through 1 MiB (depth JSON). `--batch` controls complete frames per write, not TCP packet boundaries.

The [November 8 portable release](https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib) is historical and its launcher selects 1024 bytes / one frame. Its original startup banner may display loopback while binding all IPv4 interfaces. Preserve its identity when reproducing archived data; build the current package for the default compact profile.

The [buffer and batch controls](lan-controls.md) document the historical fixed/automatic TCP receive-buffer matrix. Current production code still requests 64 KiB.
