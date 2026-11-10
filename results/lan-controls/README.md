# Controlled TCP and frame-batch measurements

The control matrix measures direct `ClientWebSocket`, original fixed-buffer
Pipelines, and automatic-receive-buffer Pipelines over the same physical
Windows/Linux Ethernet link. Each matrix fixes the JSON depth payload size.

- [One-KiB 27-run matrix](20261108T194624Z/README.md).
- [Compact 128-byte 27-run matrix](20261108T202415Z/README.md).
- [Design and reproduction commands](../../docs/lan-controls.md).
- [Findings](../../docs/lan-controls-findings.md).
- [Compact-profile findings](../../docs/lan-controls-128-findings.md).

Each combination has three fresh receiver processes, ten connections, five
seconds of warmup and thirty seconds of measurement. Receiver and profile order
rotate across repetitions. Both source variants pass the full test suite
before measurement; their sole source difference is preserved as an exact patch.

The report contains every combination, raw receiver logs/samples, process CPU,
managed allocation volume, TCP socket observations, source/binary hashes and
build/test logs. The existing LAN portfolio results remain in
[lan-1kib/](../lan-1kib/).
