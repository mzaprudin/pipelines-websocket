# Physical Ethernet WebSocket measurements

The Windows generator sends exact 1024-byte JSON depth messages to the Linux
receiver through a 1-Gbit Ethernet network. Both WebSocket implementations use
ten connections and the same generator profile, with one frame per application
write.

Measured bundle: [20261108T191616Z](20261108T191616Z/README.md).

Reproduce the workload with the Windows generator running:

```sh
bash scripts/run-lan-benchmarks.sh ws://WINDOWS-IP:5050
python3 scripts/sync-lan-results.py results/lan-1kib/RUN-DIRECTORY
```

The runner alternates three fresh-process measurements per receiver. Every run
has ten seconds of warmup followed by sixty seconds of measured reception.
The renderer validates all six reports before deriving the README tables.
CPU, managed allocation volume and GC counts cover the receiver process;
raw reports retain each one-second message/byte sample.

The `check-*.json` reports are short connectivity checks with two connections.
They are retained separately and do not contribute to the published medians.
