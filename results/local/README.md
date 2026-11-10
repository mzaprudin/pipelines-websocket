# Local streaming and echo measurements / Локальные измерения

Current: [20261110T202155Z](20261110T202155Z/README.md), measured from [4b57e71](https://github.com/mzaprudin/pipelines-websocket/tree/4b57e71c3e086f06bbc5116199434a98559d26f2).

Streaming uses an independent local generator and six fresh receiver processes. Profile: 128 bytes, 20 frames/write, 10 connections, 5 s warmup, 30 s measurement, three repetitions per client. CPU, GC and allocations cover the receiver process, including periodic sampling. All four echo cases were measured separately with 5 warmup / 15 actual iterations, 500 ms target and one launch.

Streaming: отдельный локальный генератор и шесть свежих процессов приёмника. Профиль: 128 байт, 20 фреймов на запись, 10 соединений, 5 с прогрева, 30 с измерения, три повтора на клиент. CPU, GC и аллокации относятся к приёмнику, включая метрики. Все четыре echo-сценария измерены отдельно: 5 прогревочных / 15 измерительных итераций, целевое время 500 мс, один запуск.

- [Current summary](20261110T202155Z/summary.json), [session and commands](20261110T202155Z/streaming-session.json), [echo reports](20261110T202155Z/bdn/results/).
- [Environment](20261110T202155Z/environment.json), [source snapshot](20261110T202155Z/source-snapshot.json), [binaries](20261110T202155Z/binaries.json), [artifact hashes](20261110T202155Z/artifacts.sha256.json).
- Historical colocated 87-byte loopback and extraction comparison: [184649Z](20261110T184649Z/), [182522Z](20261110T182522Z/), [original comparison index](legacy-split-comparison.md). Their raw data is unchanged; the workload and process scope differ from the current profile.
- [Historical artifact hashes](../history.sha256.json) · [Methodology](../../docs/benchmark-methodology.md) · [Методика](../../docs/benchmark-methodology.ru.md).
