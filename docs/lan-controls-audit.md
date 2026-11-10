# Independent acceptance: TCP and batch controls

Date: 2026-11-08. Scope: the control-experiment milestones in [PLAN.md](../PLAN.md).

## One-KiB matrix

**Acceptance: PASS. No remaining BLOCKER/HIGH findings.**

The read-only reviewer independently checked all 27 cases, exact profiles,
healthy completion logs, sample sums, rate/CPU/allocation arithmetic and ten
socket observations per case. All 88 evidence hashes and the balanced execution
order passed. Both 69-file source sets were reconstructed from the pinned Git
revision; the only difference is removal of the receive-buffer assignment.
Source and DLL hashes also matched the live isolated builds before cleanup.

Both isolated variants passed locked restore, Release build and all 122 tests.
All 27 measurements recorded zero collections in every GC generation.

Independent validation used a temporary evidence copy:

```sh
python3 scripts/lan-controls.py summarize /tmp/ws-controls-audit-qf352bee/bundle
```

Exit 0: validated all 27 runs. The reviewer also verified that the strengthened
validator rejected 13 malformed metric, profile, scope, socket, completion-log
and source-control cases.

The findings agree with raw measurements: batch1 automatic sizing increased
the ratio of medians by 10.84%, with positive changes in all three pairs.
Batch4/20 throughput changes are small and mixed across pairs, with overlapping
ranges. Automatic CPU cost is lower in all nine matched comparisons. Automatic
batch1 has the highest throughput median; batch4 has the lowest automatic
CPU-cost median. Whole-interface observations remain separate from receiver
process metrics.

[One-KiB evidence](../results/lan-controls/20261108T194624Z/README.md).

## Stable counter-reader fix

**Acceptance: PASS. No remaining BLOCKER/HIGH findings.**

The reviewer checked the actual single-writer update order and the sampling
proof. Equal atomic message observations surrounding a byte read exclude
completed-message advancement. Bytes may lead by at most one fixed-size
payload per client. For N clients, differencing two such observations has
absolute skew at most N payloads; the retained two-N-payload validator bound
is conservative.

The helper allocates no per-snapshot objects, bounds retries at 1024, and fails
explicitly if no stable observation is possible. The four deterministic tests
cover stable aggregation, torn-pair retry, one in-flight frame and bounded
failure. Full validation passed: Release build with 0 warnings/errors,
126 tests, and 100 ordered demo roundtrips through each implementation.

Restarting every compact condition from the sampler-fix revision preserves the
control design. The change is confined to periodic measurement; receive hot
paths are unchanged. The interrupted diagnostic data is excluded from all
published medians.

## Compact 128-byte matrix

**Acceptance: PASS. No remaining BLOCKER/HIGH findings.**

Independent review checked all 27 profiles, measurement scope and duration,
message/byte/sample arithmetic, CPU/allocation metrics, healthy logs and ten
active socket observations per case. All 90 compact evidence hashes match;
the earlier one-KiB bundle's 88 raw evidence hashes remain valid. Both 71-file
source sets were reconstructed from
`c2c3b1ad1d186073690c540b43b76cad18a54454`; automatic sizing differs only by
the documented receive-buffer assignment removal.

The reviewer independently matched both live resumed source trees and both
DLL hashes per variant against the original manifests. The acquisition pause
followed two complete balanced repetitions; the remaining nine conditions
form the third complete repetition in the original order. Existing raw
receiver reports and TCP snapshots were preserved.

```sh
python3 scripts/lan-controls.py summarize /tmp/zero-alloc-controls128-acceptance.3yerbk17/bundle
git diff --check
git diff --cached --check
```

All commands exited 0. The CLI validated 27 runs and reproduced the report
and summary byte-for-byte. Additional isolated checks rejected 12 malformed
payload, frame-size, arithmetic, GC, scope, active-socket, resume-equivalence,
runner-hash, source-set and patch cases.

Observer/resume tests in `/tmp/ws-controls-resume-audit-n83kjw1s` confirmed
that closing socket states are excluded, binary mismatch stops before any
measurement, matching manifests continue only the remaining nine cases,
raw files stay unchanged and the resume-runner hash matches. Temporary audit
directories were automatically removed.

Findings agree with the data: automatic sizing improves throughput in all
nine fixed/automatic paired comparisons and CPU cost in eight of nine.
The twenty-frame automatic median is 768400 messages/s, 6.99% above the
corresponding native median, with 4.00% lower median CPU cost. All runs record
GC 0/0/0. Both original and resumed source variants passed all 126 tests
and Release builds with 0 warnings/errors. All 29 checked local links resolve.

[Compact evidence](../results/lan-controls/20261108T202415Z/README.md) ·
[Compact findings](lan-controls-128-findings.md).

## Managed allocation comparison

**Acceptance: PASS. No remaining BLOCKER/HIGH findings.**

Independent recomputation covered all eighteen payload/batch/receiver
conditions. B/message, B/s and B/run use separate per-condition medians of
the raw quantities; GC columns use generation maxima. All six native/auto
allocation ratios match. Compact-profile ratios are 1.03x, 80.12x and 140.31x;
the twenty-frame automatic medians are 13960 B/run and 465 B/s versus native
1919472 B/run and 63981 B/s.

```sh
python3 scripts/lan-controls.py summarize /tmp/zero-alloc-controls-memory-audit.xmsouu92/20261108T194624Z
python3 scripts/lan-controls.py summarize /tmp/zero-alloc-controls-memory-audit.xmsouu92/20261108T202415Z
git diff --check
git diff --cached --check
```

All exited 0. Reports and summaries reproduced byte-for-byte; all 88/90 raw
evidence hashes remain valid. Managed allocations and kernel socket-capacity
limits are distinguished. This reporting-only addition required no further
C# builds or throughput measurements.

## Allocation normalization per million messages

**Acceptance: PASS. No remaining BLOCKER/HIGH findings.**

Independent validation checked all twenty conditions against
`median(AllocatedBytes * 1000000 / Messages) / 1000`, without intermediate
rounding. Display units are decimal kB per million messages. Normalized fields,
tables and findings match; ratios, CPU and observed GC meanings are unchanged.
All 193 raw evidence fingerprints (15 + 88 + 90) remain valid.

```sh
python3 scripts/lan-controls.py summarize /tmp/zero-alloc-per-million-audit.r28ezzhv/controls/20261108T194624Z
python3 scripts/lan-controls.py summarize /tmp/zero-alloc-per-million-audit.r28ezzhv/controls/20261108T202415Z
python3 /tmp/zero-alloc-per-million-audit.r28ezzhv/main/scripts/sync-lan-results.py /tmp/zero-alloc-per-million-audit.r28ezzhv/main/results/lan-1kib/20261108T191616Z
git diff --check
git diff --cached --check
```

All exited 0. Both control reports/summaries and the main summary plus both
README languages regenerated byte-for-byte in isolated copies. Compact
twenty-frame allocation is 85.103 versus 0.607 kB per million messages;
the 140.31x ratio uses unrounded values. This changes reporting only; no
C# builds or benchmarks were rerun.
