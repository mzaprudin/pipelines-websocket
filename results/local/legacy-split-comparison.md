# Local split comparison / Локальная проверка разделения

Run `20261110T184649Z` · UTC November 10, 2026. Upstream control: `02f7aefda0933e9a719616ec72bf6f11fc5dd9a8`. Split source: `c2b1d96ddc3b82f357317617340018f0fe74acde`.

4 BenchmarkDotNet cases before and after; five warmups and fifteen actual iterations at a 500 ms target, one launch. Measurements ran sequentially on this machine.

Echo allocations are **579–581 B/op before** and **580–582 B/op after**. Complete roundtrips include asynchronous peer work; no zero-allocation echo claim is made. Observed after/before mean ratios range **0.975–1.025**. These are individual local observations with JIT/layout/scheduling effects; raw confidence intervals and all samples are retained.

Все 4 случая измерены до и после. Аллокации полного echo-обмена: 579–581 B/op до и 580–582 B/op после; отношение среднего времени после/до — 0.975–1.025. Это наблюдения одного локального сравнения: учитывайте интервалы, JIT, размещение кода и планировщик.

- [Complete after reports](20261110T184649Z/bdn/results/) and [raw output](20261110T184649Z/benchmark.log)
- [Before reports](20261110T184649Z/baseline/bdn/results/) and [raw output](20261110T184649Z/baseline/benchmark.log)
- [Comparison JSON](20261110T184649Z/summary.json)
- [Commands](20261110T184649Z/commands.json), [environment](20261110T184649Z/environment.json), [source hashes](20261110T184649Z/source-snapshot.json), [binary hashes](20261110T184649Z/binaries.json)
- [Artifact hashes](20261110T184649Z/artifacts.sha256.json), [baseline hashes](20261110T184649Z/baseline/artifacts.sha256.json)
- [Exact clean-check commands](20261110T184649Z/verification/clean-commands.json) and [verification logs](20261110T184649Z/verification/)
- [Coexistence source snapshot](20261110T184649Z/verification/coexistence/) and [passed public API/integrity check](20261110T184649Z/verification/coexistence.log)

| Case | Before | After | After/before | B/op before → after |
|---|---:|---:|---:|---:|
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 128, StandardClient: False)` PayloadBytes=128&StandardClient=False | 33048.85 ns | 33876.99 ns | 1.025× | 581 → 582 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 128, StandardClient: True)` PayloadBytes=128&StandardClient=True | 33288.09 ns | 32650.57 ns | 0.981× | 579 → 580 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 4096, StandardClient: False)` PayloadBytes=4096&StandardClient=False | 39705.35 ns | 38711.86 ns | 0.975× | 581 → 581 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 4096, StandardClient: True)` PayloadBytes=4096&StandardClient=True | 37697.46 ns | 38476.22 ns | 1.021× | 581 → 580 |

Twelve complete streaming reports: six before, six after. Each uses ten connections, 87-byte payload, twenty frames/write, 5 s warmup and 30 s measurement. Process CPU/allocation scope includes the local generator and clients. Raw counter skew is preserved and bounded as documented in the methodology.

| Client | Before messages/s | After messages/s | Before kB / 1M | After kB / 1M | After GC counts per run |
|---|---:|---:|---:|---:|---|
| standard | 42,420,303 | 42,695,264 | 0.058 | 0.054 | [[0, 0, 0], [0, 0, 0], [0, 0, 0]] |
| pipeline | 71,206,979 | 69,954,803 | 0.010 | 0.012 | [[0, 0, 0], [0, 0, 0], [0, 0, 0]] |

Двенадцать полных streaming-отчётов: шесть до и шесть после. CPU/аллокации включают генератор и клиентов; ограниченный сдвиг независимых счётчиков сохранён в сырых данных и summary.json.

The published run uses the Windows-compatible TLS test peer. The [earlier post-split run](20261110T182522Z/) is retained unchanged as historical evidence. Production transport algorithms and test assertions are unchanged.

Опубликованный запуск использует TLS-harness, совместимый с Windows. Предыдущий запуск сохранён без изменений; алгоритмы транспорта и проверки тестов прежние.
