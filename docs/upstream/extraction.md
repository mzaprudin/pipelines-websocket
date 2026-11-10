# Extraction boundary and provenance

Source snapshot: GapSonar commit
`238dc8bd7a7835872d94f89e109dec692e19f1d2`.

The extraction keeps the original `GapSonar.Core.Shared` and
`GapSonar.Core.Exchange.Networking` namespaces. It includes the serializer's
small `SymbolId` dependency and its supporting value types, rather than
introducing a public contract change to remove it.

The [source manifest](source-manifest.txt) maps copied source/test files to
their standalone paths. The generated `source-provenance.json` records source
and extracted file hashes, so copied code and local changes are distinguishable.

## Included

- JSON population/writing, attributes, fixed-size string types.
- `FastList`, `PoolRental`, timestamp helpers, optional interning.
- Low-level custom and framework WebSocket clients and raw message handler.
- The transport-only metrics subset, with an opt-in sink and disabled emission
  by default. Exchange/shard/subscription metrics were left in the parent.
- Four existing parser/serializer test suites, plus focused extraction tests.
  The order-direction enum used by serializer tests is test-local.

## Excluded

Exchange connectors and credentials, routing/pipelines, strategies, execution,
managed reconnect/shard supervision, Protobuf contracts, database/history
infrastructure, deployment configuration, and external git submodules.

## Corrections made during extraction

1. **TLS validation:** removed the unconditional certificate-accept callback;
   propagated cancellation through TLS authentication.
2. **Upgrade validation:** validate the server accept challenge and HTTP upgrade
   tokens; cap response headers; reject unsolicited extensions/subprotocols;
   preserve bytes following the upgrade response.
3. **Server close:** avoid awaiting the receive task from itself, return the
   close payload, preserve the reported close status/reason, and observe loop
   completion during disposal.
4. **Outbound fragmentation:** use continuation opcodes after the first fragment
   and serialize data-frame state.
5. **Resource bounds:** cap received frame/message size at 1 MiB and upgrade headers at
   16 KiB; reject oversized/negative payload lengths, unsupported opcodes,
   reserved bits, masked server frames, and invalid control-frame shapes.
6. **Pipe progress:** set pause thresholds above maximum message size. Tests
   reproduced a 65536-byte hang in the original framework adapter and verified
   both implementations after the fix.
7. **Empty messages:** dispatch an empty framework message directly. The original
   adapter waited indefinitely for an empty Pipe flush.
8. **Interned escapes:** decode JSON string escapes before interning, including
   a pooled fallback for larger escaped strings.
9. **Explicit ping semantics:** the framework adapter reports explicit protocol
   ping as unsupported instead of silently sending binary application data.
10. **Reproducible measurements:** use identical seven-field models, add generated
    framework baselines, match writer/buffer lifecycles, and check equivalence
    before measuring. Benchmark execution returns a nonzero exit code if a case fails.
11. **Cancellation during connection callbacks:** queue receive loops without a scheduling cancellation token so cancellation
    in `OnConnected` cannot suppress loop startup and cleanup. Continuous buffered
    input also cannot monopolize connection setup. A deterministic test covers this ordering for both clients.
12. **Interrupted close response:** finalize custom close state and notify the
    handler even if sending the close reply fails; propagate receive cancellation
    into pong writes.

The original root solution and source files were not modified. Legacy coding
style in unchanged imported blocks is retained apart from whitespace cleanup to keep the extraction diff
reviewable. New implementation paths use named resource/protocol bounds.
