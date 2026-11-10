# Repository guidance

.NET 10; assembly `Pipelines.WebSockets`. Preserve imported namespaces, APIs, fixtures and algorithms.
Use spans, explicit loops and pooling on hot paths. Name nontrivial constants; document new types and members.
Keep cancellation propagation and memory ownership explicit. No third-party open-source license is granted; preserve NOTICE.

## Validation scope

Choose checks from the complete change, including earlier commits in the push and both paths of a rename. Mixed or unknown changes require full CI. `scripts/ci_changes.py` is the executable routing policy.

- Markdown, `docs/`, `results/`, and the presentation scripts `render-results.py`, `verify-evidence.py`, `verify-docs.py`: run `python scripts/verify-docs.py`. This checks Markdown formatting and local links, Python syntax, generated tables, provenance and evidence hashes without building .NET. When changing rendering, also regenerate from the published run and verify the output. Do not restore, build, run runtime tests/examples/benchmarks, rebuild the Windows package or repeat performance measurements solely to publish documentation.
- Library, tests, support, samples, benchmarks, dependencies or build inputs: locked restore, Release build, all tests, sample and every benchmark case with the Dry job. Run relevant tooling tests. Hosted CI checks Linux and Windows, separate-process streaming and the portable Windows generator.
- Workflow or CI classification changes: lint the workflow, run the classification tests and documentation/evidence checks locally; verify one full hosted matrix. Do not duplicate unchanged .NET checks locally just for CI edits. Manual workflow runs and unavailable Git history use full CI.

Documentation validation runs on every push/PR. The Linux/Windows runtime matrix runs only for full-scope changes. Keep workflow conditions and this contract aligned. Re-run checks after failures or relevant edits, rather than repeating successful checks without new evidence.

## Measurements and review

Real measurements use 5 warmups, 15 iterations, 500 ms, one launch; run sequentially.
Retain full raw evidence and source/binary fingerprints. Generate published tables from measurement JSON; preserve historical bundles.
Require a separate read-only agent acceptance audit for runtime, build, CI logic or measurement-methodology changes and fix HIGH/BLOCKER findings. Ordinary documentation edits require the scoped checks above.
