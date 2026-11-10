# Historical GapSonar WebSocket load results

Source: `GapSonar.Core.Exchange/Docs/Networking/README.0.md`, lines 47–68.
These are preserved documentation figures, not a current remeasurement.

## Performance Results

In local load tests (10 clients, ~1.8KB batch size):
- **Throughput**: ~50,000,000 messages/second.
- **Bandwidth**: ~35 Gbps.
- **GC Collections**: 0 (Gen0/1/2).

=== CLIENTWEBSOCKET RESULTS ===
Avg Msg/s: 51,441,484
Max Msg/s: 69,607,657
Min Msg/s: 41,362,750
Total GC Gen0: 8
Total GC Gen1: 7
Total GC Gen2: 6

=== PIPELINE BASED RESULTS ===
Avg Msg/s: 81,676,110
Max Msg/s: 84,332,625
Min Msg/s: 79,236,916
Total GC Gen0: 3
Total GC Gen1: 2
Total GC Gen2: 1
