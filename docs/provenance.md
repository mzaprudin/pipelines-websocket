# Provenance and extraction boundary

[English](provenance.md) · [Русский](provenance.ru.md)

[Upstream revision 02f7aef](https://github.com/mzaprudin/zero-alloc-json-ws/tree/02f7aefda0933e9a719616ec72bf6f11fc5dd9a8)

The upstream repository remains the history and measurement archive. The new main branch starts with a clean history. Licensing is unchanged: no open-source license is granted; NOTICE is preserved byte-for-byte. External packages retain their respective licenses.

Complete upstream file inventory with SHA-256: [upstream-inventory.json](upstream-inventory.json). Old/new path mappings and before/after hashes: [split-provenance.json](split-provenance.json). Original GapSonar extraction: [source-provenance.json](upstream/source-provenance.json), [extraction.md](upstream/extraction.md).

Public namespaces and main APIs are retained. The agreed visibility change is internal PoolRental<T> in each assembly. Test .cs files are copied unchanged. JSON routers move from benchmark source to the library with unchanged method bodies. Packaging, imports, friend assemblies and tool paths change; algorithms and the fixed TCP receive buffer are retained.

Archived upstream documents and older network reports describe the old paths and assembly. Their original links can be resolved in the pinned upstream revision. Fresh measurements carry their own source/binary manifests and do not redefine the identity of historical builds.

The test assembly keeps its upstream name, GapSonar.TransportLab.Tests, so the independent-process runtime/deps lookup works without editing any test source. The project and test paths are specific to this repository.

Windows Schannel requires a named private key for the generated local TLS certificate. The loopback test peer imports a temporary PKCS#12 copy on Windows and disposes its owned copy on shutdown. The supplied certificate fingerprint, trust checks and all test assertions remain unchanged. This affects test-peer setup, not production transport code. [Runtime guidance](https://github.com/dotnet/runtime/issues/114640).
