# Repository guidance

.NET 10; assembly `Pipelines.WebSockets`. Preserve imported namespaces, APIs, fixtures and algorithms.
Use spans, explicit loops and pooling on hot paths. Name nontrivial constants; document new types and members.
Keep cancellation propagation and memory ownership explicit. No third-party open-source license is granted; preserve NOTICE.
Before publishing run locked restore, Release build, every test, the sample and all benchmark cases with the dry job.
Real measurements use 5 warmups, 15 iterations, 500 ms, one launch; run sequentially.
Retain full raw evidence and source/binary fingerprints. Generate README tables from measurement JSON.
Require a separate read-only agent acceptance audit before completion and fix HIGH/BLOCKER findings.
