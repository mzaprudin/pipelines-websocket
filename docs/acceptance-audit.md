# Independent acceptance audit

[English](acceptance-audit.md) · [Русский](acceptance-audit.ru.md)

## Initial extraction audit

On November 10, 2026, two independent read-only reviewers audited source/package separation, CI, provenance, raw evidence and both language versions. No unresolved BLOCKER, HIGH or MEDIUM findings remained. Reviewers did not build, test or benchmark during measurements.

- Confirmed complete source inventory, path/SHA-256 mappings, unchanged NOTICE and all 203 archived Ethernet files.
- All original test sources are unchanged; clean checks partition 126 cases into 56 JSON and 70 transport tests. Samples and coexistence checks are supported by logs.
- Measured runtime inputs match snapshots; measured binary fingerprints are retained.
- Verified complete case sets, 5 raw warmups / 15 raw actual iterations, 500 ms, one launch, summary arithmetic, allocations and both README tables.
- For transport, also checked all 12 streaming reports, scope, sample arithmetic, CPU/allocation normalization and bounded counter skew.
- Reviewed independent scripts and CI for UTF-8 and fail-fast execution. The local Windows package matches its source/binary manifest.

Review fixes restored LAN README markers and historical-path handling; strengthened case/iteration validation; documented unchanged counter-skew behavior; clarified comparison labels and echo allocation differences; identified the standard adapter as internal; and fixed UTF-8/fail-fast behavior on Windows. Final validators also pass with UTF-8 mode disabled under an ASCII locale.

[Verification and commands](verification.md) · [Complete measurements](../results/local/README.md)

A subsequent hosted Windows run exposed the existing ephemeral-key TLS fixture issue. A separate read-only review accepted the Windows-only PKCS#12 test-peer copy and its ownership/cleanup. Production transport and every test assertion remain unchanged. Clean local checks and all four echo/six streaming measurements were repeated using the final peer; the earlier measured bundle remains available unchanged.

## Independent-process streaming acceptance

A separate read-only reviewer accepted measured revision `4b57e71` and [20261110T202155Z](../results/local/20261110T202155Z/README.md). No HIGH/BLOCKER findings or material metadata discrepancies remained.

The review independently recomputed all six streaming rates, CPU/allocation normalization and medians; checked profiles, 30 samples per run, distinct receiver PIDs, alternating order and generator exit 0 without forced termination. All four BDN cases have five raw warmups and fifteen raw actual iterations, one launch. Both README languages match the reports. All 87 source hashes match the measured commit; the portable generator's 24 source fingerprints match the snapshot. All 405 historical files remain byte-identical and production transport is unchanged. Clean-check logs support 73 C# tests, seven Python checks, the sample, four Dry cases and portable build.

Review fixes made external-endpoint report titles follow recorded topology and corrected provenance wording for the documented process-test adaptations.
