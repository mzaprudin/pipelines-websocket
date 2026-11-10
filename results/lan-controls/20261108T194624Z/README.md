# Controlled Ethernet measurements: receive buffer and frame batches

Windows generator → 1-Gbit Ethernet → Linux receiver. 1024-byte JSON, 10 connections, 3 fresh-process runs per case, 5 s warmup + 30 s measurement.

| Frames/write | Receiver | Messages/s, median [min–max] | Payload Mbit/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---:|---|---:|---:|---:|---:|
| 1 | ClientWebSocket | 103,220 [101,964–107,315] | 845.6 | 29,749 | 93.670 |
| 1 | Pipelines · fixed 64 KiB | 94,345 [92,853–105,121] | 772.9 | 29,503 | 5.000 |
| 1 | Pipelines · automatic receive buffer | 104,571 [103,322–107,290] | 856.6 | 28,745 | 4.493 |
| 4 | ClientWebSocket | 103,053 [101,203–105,528] | 844.2 | 28,193 | 716.587 |
| 4 | Pipelines · fixed 64 KiB | 99,984 [98,060–105,672] | 819.1 | 28,645 | 4.686 |
| 4 | Pipelines · automatic receive buffer | 101,161 [95,921–105,373] | 828.7 | 27,215 | 4.589 |
| 20 | ClientWebSocket | 100,605 [97,507–104,653] | 824.2 | 27,767 | 371.877 |
| 20 | Pipelines · fixed 64 KiB | 101,211 [98,374–104,475] | 829.1 | 28,302 | 4.719 |
| 20 | Pipelines · automatic receive buffer | 102,297 [100,447–102,735] | 838.0 | 27,398 | 4.601 |

## Automatic versus fixed receive sizing

| Frames/write | Throughput change | CPU cost reduction per million messages |
|---:|---:|---:|
| 1 | +10.84% | +2.57% |
| 4 | +1.18% | +4.99% |
| 20 | +1.07% | +3.20% |

## Managed allocation comparison

| Frames/write | Native kB / 1M | Fixed kB / 1M | Auto kB / 1M | Native / auto |
|---:|---:|---:|---:|---:|
| 1 | 93.670 | 5.000 | 4.493 | 20.85× |
| 4 | 716.587 | 4.686 | 4.589 | 156.14× |
| 20 | 371.877 | 4.719 | 4.601 | 80.83× |

| Frames/write | Receiver | CPU ms / 1M messages | Allocated kB / 1M messages | GC 0/1/2, maximum |
|---:|---|---:|---:|---:|
| 1 | ClientWebSocket | 29,749 | 93.670 | 0/0/0 |
| 1 | Pipelines · fixed 64 KiB | 29,503 | 5.000 | 0/0/0 |
| 1 | Pipelines · automatic receive buffer | 28,745 | 4.493 | 0/0/0 |
| 4 | ClientWebSocket | 28,193 | 716.587 | 0/0/0 |
| 4 | Pipelines · fixed 64 KiB | 28,645 | 4.686 | 0/0/0 |
| 4 | Pipelines · automatic receive buffer | 27,215 | 4.589 | 0/0/0 |
| 20 | ClientWebSocket | 27,767 | 371.877 | 0/0/0 |
| 20 | Pipelines · fixed 64 KiB | 28,302 | 4.719 | 0/0/0 |
| 20 | Pipelines · automatic receive buffer | 27,398 | 4.601 | 0/0/0 |

Allocation metrics cover the entire managed receiver process, including periodic measurement. Normalized allocation is `AllocatedBytes / Messages × 1,000,000`; 1 kB = 1000 bytes. Allocation columns are per-condition medians; GC columns show observed maxima. Socket receive-capacity limits are recorded separately below.

## Socket receive-buffer observations

| Frames/write | Receiver | Kernel receive capacity, min–max bytes |
|---:|---|---:|
| 1 | ClientWebSocket | 1,048,114–4,560,054 |
| 1 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 1 | Pipelines · automatic receive buffer | 1,588,992–5,884,672 |
| 4 | ClientWebSocket | 995,611–4,594,074 |
| 4 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 4 | Pipelines · automatic receive buffer | 1,112,517–4,902,354 |
| 20 | ClientWebSocket | 1,386,758–4,561,344 |
| 20 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 20 | Pipelines · automatic receive buffer | 1,468,809–6,655,445 |

Linux reports 131072 bytes for the explicit 65536-byte request because its socket buffer accounting doubles that setting. The captured `ss` rb values are capacity limits; the complete snapshots also retain live memory and TCP window observations. [Linux socket buffer accounting](https://man7.org/linux/man-pages/man7/socket.7.html).

Only the explicit TCP receive-buffer assignment changes between fixed and automatic Pipelines builds. The send buffer, application read size, Pipe settings and frame-processing code are identical. CPU and allocation metrics cover the Linux receiver process, including sampling.

[Environment, exact case order and socket observations](environment.json) · [Build/source manifests](builds.json) · [Exact source patch](auto-receive.patch) · [Summary JSON](summary.json)
