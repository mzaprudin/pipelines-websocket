# TCP receive-buffer and batching findings

The complete [27-run Ethernet matrix](../results/lan-controls/20261108T194624Z/README.md)
uses the same 1024-byte JSON workload and three repetitions of every condition.
Percentages below compare per-condition medians from that matrix.

## Automatic receive sizing

With one frame per write, automatic receive sizing raised Pipelines throughput
from **94345 to 104571 messages/s (+10.84%)**. All three matched repetitions
were faster with automatic sizing: **+2.06%, +12.62% and +9.51%**. The native
baseline's median was **103220 messages/s**, placing the automatic custom
receiver at the same throughput level in this series.

For batches of four and twenty frames, the automatic-versus-fixed median
throughput differences were **+1.18%** and **+1.07%**. Matched repetitions moved
in both directions, and their ranges overlap. The clearest throughput effect
in this experiment is the single-frame profile.

The kernel observations confirm the setting changed actual socket capacity:
fixed receivers stayed at **131072 bytes** in Linux's buffer accounting;
automatic single-frame receivers grew to **1588992–5884672 bytes**. These are
capacity limits, with live memory/window details retained in each TCP snapshot.

## CPU and batch sizes

Automatic sizing reduced CPU cost per million received messages in **all nine
matched fixed/automatic comparisons**. Median reductions by batch were
**2.57%, 4.99% and 3.20%** for one, four and twenty frames respectively.

Among automatic receivers, the one-frame profile had the highest throughput
median. Moving from one to four frames lowered CPU cost per million messages
by **5.32%**, with throughput **3.26% lower**. Twenty frames lowered CPU cost by
**4.69%**, with throughput **2.17% lower**. Larger writes did not increase
throughput in this matrix.

All 27 measurements recorded **zero collections in every GC generation**.
The automatic single-frame receiver allocated **4.493 managed kB per 1,000,000 messages**
at process scope, including sampling, versus **93.670 kB per 1,000,000 messages** for the native baseline.
The raw reports retain the exact unrounded values.

## Validation and scope

Both isolated source variants passed locked restore, Release build and all
**122 tests** before measurement. Their 69-source-file sets differ only by
removal of the explicit receive-buffer assignment. The send buffer, application
read size, Pipe options, frame parser and handlers are identical. The executed
runner and both source/binary manifests are retained with the evidence.

The experiment covers plain WebSocket reception over this Windows/Linux
Ethernet link, ten connections and 1-KiB messages. Three fresh processes per
condition and all min/max ranges are reported. Generator CPU and message latency
were outside this receive-throughput experiment. Production code and the
existing portfolio measurement tables retain their published configuration.

[Reproduction guide](lan-controls.md) ·
[Commands and validation](verification.md) ·
[Independent audit](lan-controls-audit.md).
