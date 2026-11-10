# Receiver-only loopback and echo / Loopback и echo

Measured November 10, 2026 on AMD Ryzen 9 5950X, Ubuntu 24.04, .NET 10.0.14, Release/x64. Source: [4b57e71c3e086f06bbc5116199434a98559d26f2](https://github.com/mzaprudin/pipelines-websocket/tree/4b57e71c3e086f06bbc5116199434a98559d26f2), clean runtime inputs. All measurements ran sequentially.

## Streaming

Independent local generator, three fresh receiver processes per client; order: standard/pipeline, pipeline/standard, standard/pipeline. JSON 128 bytes, 20 frames per write (2640 wire bytes), 10 connections, 5 s warmup and 30 s measurement. Process CPU, GC and allocations cover the receiver including periodic sampling. The same runner and receiver arguments are used for an external generator through `--url`.

| Client | Median messages/s | CPU ms / 1M | Allocated kB / 1M | Total allocated bytes by repetition | GC 0/1/2 by repetition |
|---|---:|---:|---:|---|---|
| ClientWebSocket | 30,651,369 | 404 | 0.411 | 453208 / 378216 / 288440 | 0/0/0 · 0/0/0 · 0/0/0 |
| Pipelines | 54,427,112 | 235 | 0.013 | 20680 / 14184 / 20584 | 0/0/0 · 0/0/0 · 0/0/0 |

kB = 1000 bytes. Summary medians are calculated from unrounded per-run values. Byte/message snapshot skew: standard 0/0/0 bytes; pipeline 0/−128/0 bytes, within the 2560-byte bound. All six receivers exited successfully; generator PID 2501009 stopped with exit code 0 without forced termination. The session records each receiver PID and command.

Локальный генератор работает в отдельном процессе; по три свежих приёмника на клиент. В таблице — медианы исходных значений. CPU, GC и аллокации включают процесс приёмника со сбором метрик. Профиль и команды приёмника едины с запуском через `--url`. Все шесть приёмников и сервер завершились штатно; команды и PID сохранены в сеансе.

## Echo

Four complete roundtrip cases, 128/4096 bytes × Pipelines/ClientWebSocket adapter. Each has five raw warmup and fifteen raw actual iterations at a 500 ms target, one launch. Allocations: 580–581 B/op. Both peers and the asynchronous harness are in the measured process. This is a separate workload from continuous reception.

Все четыре echo-сценария имеют 5 исходных прогревочных и 15 измерительных итераций с целевым временем 500 мс и одним запуском. Аллокации: 580–581 B/op; учтены оба сетевых узла и асинхронный измеритель.

## Evidence

- [Summary and per-run statistics](summary.json), [streaming session and exact child commands](streaming-session.json), [shared profile](streaming-profile.json), [generator log](generator.log).
- [Full echo exports](bdn/results/), [stdout](benchmark.log), [restore/build/echo commands](commands.json).
- [Environment](environment.json), [source SHA-256](source-snapshot.json), [binary SHA-256](binaries.json), [artifact SHA-256](artifacts.sha256.json).
- [Clean-check commands](verification/clean-commands.json), [verification logs](verification/), [portable generator manifest](verification/windows-generator-manifest.json).
- [Methodology](../../../docs/benchmark-methodology.md) · [Методика](../../../docs/benchmark-methodology.ru.md).

Historical Ethernet and colocated loopback bundles were not changed. No new physical Ethernet run was performed.
