# Compact 128-byte WebSocket controls

This profile uses exact **128-byte JSON payloads**, or **132 bytes per complete
unmasked WebSocket frame**. It repeats the fixed/automatic receive-buffer
controls with batches of one, four and twenty frames per application write.
There are ten connections, five seconds of warmup, thirty seconds of measured
reception and three fresh processes for every condition.

<!-- COMPACT_FINDINGS_START -->
| Frames/write | ClientWebSocket, msg/s | Pipelines 64 KiB, msg/s | Pipelines auto, msg/s |
|---:|---:|---:|---:|
| 1 | 261,735 | 234,477 | 256,816 |
| 4 | 747,085 | 531,697 | 748,097 |
| 20 | 718,218 | 743,095 | 768,400 |

The highest automatic-receiver median is **768,400 messages/s / 786.8 payload Mbit/s** with twenty frames per write. This is **6.99% above the native baseline** in the same profile. CPU cost is **3,559 versus 3,707 ms per million messages**, a **4.00% reduction** by per-condition medians.

Automatic sizing increased the custom receiver's throughput versus its fixed-buffer control in **all nine matched repetitions**. Ratios of medians by batch size are:
- **1 frame(s)/write:** throughput **+9.53%**, CPU cost reduction **0.92%**.
- **4 frame(s)/write:** throughput **+40.70%**, CPU cost reduction **17.53%**.
- **20 frame(s)/write:** throughput **+3.41%**, CPU cost reduction **2.54%**.

The four-frame fixed/automatic throughput ranges are **525,948–586,518** and **728,106–775,497 messages/s**, respectively. With twenty frames they are **714,202–743,998** and **761,914–786,070**. The one-frame ranges overlap. CPU cost is lower with automatic sizing in eight of nine matched repetitions; the table above uses per-condition medians.

All **27 runs recorded GC 0/0/0**. The twenty-frame automatic receiver allocated **0.607 managed kB per 1,000,000 messages** at process scope, including sampling, versus **85.103 kB per 1,000,000 messages** for the native baseline. All raw values and min/max throughput ranges are retained in the complete matrix.
<!-- COMPACT_FINDINGS_END -->

The original 87-byte profile is the length of a prepared trade JSON document,
not the memory size of a C# structure. The 128-byte control profile uses the
same extended-header format as the one-KiB control profile.

## Managed memory allocations

| Frames/write | Native kB / 1M | Fixed kB / 1M | Auto kB / 1M | Native / auto |
|---:|---:|---:|---:|---:|
| 1 | 8.182 | 4.052 | 7.934 | 1.03× |
| 4 | 59.394 | 1.990 | 0.741 | 80.12× |
| 20 | 85.103 | 0.653 | 0.607 | 140.31× |

By per-condition medians, with twenty frames/write the automatic receiver allocates **0.607 kB per 1,000,000 messages**, versus **85.103 kB** for the native baseline: **140.31× less**. Normalization uses unrounded raw `AllocatedBytes / Messages × 1,000,000`; 1 kB = 1000 bytes. Ratios also use unrounded values.

For one frame/write, native and automatic managed allocation intensity is close. The larger reduction appears with four and twenty frames/write. All conditions have GC 0/0/0. These are whole managed receiver-process measurements including sampling; TCP receive-capacity limits describe kernel socket buffering and are reported separately.

## Sampling and provenance

Both compact receiver variants include the same benchmark-only stable counter
reader. It retries a message/byte observation if the message counter advances
around the byte read, without modifying receiving algorithms or callbacks.
The strict byte-consistency check remains in place. Four deterministic tests
cover the interleavings and bounded failure behavior; each isolated variant
passes the full 126-test suite before timing.

The earlier compact diagnostic run is excluded from all published medians.
The published matrix restarts every condition from the source revision with
the sampling fix. The one-KiB evidence keeps its recorded sources and all raw
measurements. Every fixed/automatic pair still differs only by removal of the
explicit socket receive-buffer assignment.

CPU and managed allocations cover the Linux receiver process, including metric
sampling. Generator CPU and message latency are outside this throughput study.

[Complete compact matrix](../results/lan-controls/20261108T202415Z/README.md) ·
[Reproduction](lan-controls.md) · [Independent audit](lan-controls-audit.md).
