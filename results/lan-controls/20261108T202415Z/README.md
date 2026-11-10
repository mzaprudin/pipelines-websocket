# Controlled Ethernet measurements: receive buffer and frame batches

Windows generator → 1-Gbit Ethernet → Linux receiver. 128-byte JSON, 10 connections, 3 fresh-process runs per case, 5 s warmup + 30 s measurement.

| Frames/write | Receiver | Messages/s, median [min–max] | Payload Mbit/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---:|---|---:|---:|---:|---:|
| 1 | ClientWebSocket | 261,735 [258,233–263,631] | 268.0 | 17,917 | 8.182 |
| 1 | Pipelines · fixed 64 KiB | 234,477 [222,027–258,137] | 240.1 | 18,570 | 4.052 |
| 1 | Pipelines · automatic receive buffer | 256,816 [256,265–265,660] | 263.0 | 18,398 | 7.934 |
| 4 | ClientWebSocket | 747,085 [739,598–759,743] | 765.0 | 5,139 | 59.394 |
| 4 | Pipelines · fixed 64 KiB | 531,697 [525,948–586,518] | 544.5 | 5,868 | 1.990 |
| 4 | Pipelines · automatic receive buffer | 748,097 [728,106–775,497] | 766.1 | 4,840 | 0.741 |
| 20 | ClientWebSocket | 718,218 [686,924–776,166] | 735.5 | 3,707 | 85.103 |
| 20 | Pipelines · fixed 64 KiB | 743,095 [714,202–743,998] | 760.9 | 3,652 | 0.653 |
| 20 | Pipelines · automatic receive buffer | 768,400 [761,914–786,070] | 786.8 | 3,559 | 0.607 |

## Automatic versus fixed receive sizing

| Frames/write | Throughput change | CPU cost reduction per million messages |
|---:|---:|---:|
| 1 | +9.53% | +0.92% |
| 4 | +40.70% | +17.53% |
| 20 | +3.41% | +2.54% |

## Managed allocation comparison

| Frames/write | Native kB / 1M | Fixed kB / 1M | Auto kB / 1M | Native / auto |
|---:|---:|---:|---:|---:|
| 1 | 8.182 | 4.052 | 7.934 | 1.03× |
| 4 | 59.394 | 1.990 | 0.741 | 80.12× |
| 20 | 85.103 | 0.653 | 0.607 | 140.31× |

| Frames/write | Receiver | CPU ms / 1M messages | Allocated kB / 1M messages | GC 0/1/2, maximum |
|---:|---|---:|---:|---:|
| 1 | ClientWebSocket | 17,917 | 8.182 | 0/0/0 |
| 1 | Pipelines · fixed 64 KiB | 18,570 | 4.052 | 0/0/0 |
| 1 | Pipelines · automatic receive buffer | 18,398 | 7.934 | 0/0/0 |
| 4 | ClientWebSocket | 5,139 | 59.394 | 0/0/0 |
| 4 | Pipelines · fixed 64 KiB | 5,868 | 1.990 | 0/0/0 |
| 4 | Pipelines · automatic receive buffer | 4,840 | 0.741 | 0/0/0 |
| 20 | ClientWebSocket | 3,707 | 85.103 | 0/0/0 |
| 20 | Pipelines · fixed 64 KiB | 3,652 | 0.653 | 0/0/0 |
| 20 | Pipelines · automatic receive buffer | 3,559 | 0.607 | 0/0/0 |

Allocation metrics cover the entire managed receiver process, including periodic measurement. Normalized allocation is `AllocatedBytes / Messages × 1,000,000`; 1 kB = 1000 bytes. Allocation columns are per-condition medians; GC columns show observed maxima. Socket receive-capacity limits are recorded separately below.

## Socket receive-buffer observations

| Frames/write | Receiver | Kernel receive capacity, min–max bytes |
|---:|---|---:|
| 1 | ClientWebSocket | 999,259–16,777,216 |
| 1 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 1 | Pipelines · automatic receive buffer | 1,051,528–10,455,259 |
| 4 | ClientWebSocket | 1,128,937–7,034,852 |
| 4 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 4 | Pipelines · automatic receive buffer | 1,373,888–16,777,216 |
| 20 | ClientWebSocket | 1,321,291–8,565,706 |
| 20 | Pipelines · fixed 64 KiB | 131,072–131,072 |
| 20 | Pipelines · automatic receive buffer | 1,499,387–4,882,657 |

Linux reports 131072 bytes for the explicit 65536-byte request because its socket buffer accounting doubles that setting. The captured `ss` rb values are capacity limits; the complete snapshots also retain live memory and TCP window observations. [Linux socket buffer accounting](https://man7.org/linux/man-pages/man7/socket.7.html).

Only the explicit TCP receive-buffer assignment changes between fixed and automatic Pipelines builds. The send buffer, application read size, Pipe settings and frame-processing code are identical. CPU and allocation metrics cover the Linux receiver process, including sampling.

[Environment, exact case order and socket observations](environment.json) · [Build/source manifests](builds.json) · [Exact source patch](auto-receive.patch) · [Summary JSON](summary.json)
