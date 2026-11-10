# Independent acceptance audit

[English](acceptance-audit.md) · [Русский](acceptance-audit.ru.md)

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
