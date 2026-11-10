# Independent acceptance audit: physical Ethernet measurements

Date: 2026-11-08. Scope: the physical Ethernet milestone in [PLAN.md](../PLAN.md).

**Acceptance audit: PASS. No remaining BLOCKER/HIGH findings.**

The read-only reviewer checked all six runs, chronological execution order,
matching 1024-byte/batch1 profiles, ten connections, ten-second warmup and
sixty-second measurement. Sample sums, elapsed-time rates, payload consistency,
CPU/allocation formulas, per-metric medians and all 15 evidence hashes passed.
Receiver source fingerprints and the released generator manifest/package match.
Both README tables agree with the complete raw evidence.

The review also confirmed the direct proxy bypass occurs before native
connection setup and leaves measured receive work unchanged. The corrected
startup banner displays the configured bind address. No public APIs or receive
hot paths changed in this update.

## Independent runtime validation

```sh
dotnet test tests/GapSonar.TransportLab.Tests/GapSonar.TransportLab.Tests.csproj -c Release --no-build --logger 'console;verbosity=minimal'
dotnet run -c Release --no-build --project samples/GapSonar.TransportLab.Demo
```

Both exited 0. Tests: **122 passed, 0 failed, 0 skipped** in eight seconds.
The demo verified 100 ordered JSON roundtrips through each implementation.

## Proxy negative control

In isolated copy `/tmp/ws-proxy-negative-audit-zoy5vdgg`, the reviewer removed
only `_socket.Options.Proxy = null;` and ran:

```sh
dotnet test /tmp/ws-proxy-negative-audit-zoy5vdgg/tests/GapSonar.TransportLab.Tests/GapSonar.TransportLab.Tests.csproj -c Release --filter FullyQualifiedName~LanBenchmarkTests --logger 'console;verbosity=minimal'
```

Expected result: exit 1, native case failed attempting the deliberately
unreachable proxy at `127.0.0.1:1`; the custom-client case passed. Lowercase
ambient proxy variables were absent. This establishes that the positive
independent-process regression exercises the proxy fix. The actual repository
was untouched by the negative control.

## Evidence renderer

Read-only checks rejected **17** altered profile, arithmetic, scope, endpoint
and completion-log cases. All local documentation links resolved.

Published evidence: [six-run Ethernet bundle](../results/lan-1kib/20261108T191616Z/README.md).
The Linux/Windows workflow repeats platform validation on push:
[CI](https://github.com/mzaprudin/zero-alloc-json-ws/actions/workflows/ci.yml).
