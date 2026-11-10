# Local split comparison / Локальная проверка разделения

Run `20261110T182522Z` · UTC November 10, 2026. Upstream control: `02f7aefda0933e9a719616ec72bf6f11fc5dd9a8`. Split source: `1600ce6d79d206e784801d448a62c59ec119e687`.

4 BenchmarkDotNet cases before and after; five warmups and fifteen actual iterations at a 500 ms target, one launch. Measurements ran sequentially on this machine.

Echo allocations are **579–581 B/op before** and **575–581 B/op after**. These complete roundtrips include asynchronous peer work; no zero-allocation echo claim is made. Observed after/before mean ratios range **0.949–1.029**. These are individual local observations with JIT/layout/scheduling effects; raw confidence intervals and all samples are retained.

Все 4 случая измерены до и после. Аллокации полного echo-обмена составляют 579–581 B/op до и 575–581 B/op после; zero-allocation для этого сценария не заявляется; отношение среднего времени после/до — 0.949–1.029. Это наблюдения одного локального сравнения: учитывайте интервалы, JIT, размещение кода и планировщик.

- [Complete after reports](20261110T182522Z/bdn/results/) and [raw output](20261110T182522Z/benchmark.log)
- [Before reports](20261110T182522Z/baseline/bdn/results/) and [raw output](20261110T182522Z/baseline/benchmark.log)
- [Comparison JSON](20261110T182522Z/summary.json)
- [Commands](20261110T182522Z/commands.json), [environment](20261110T182522Z/environment.json), [source hashes](20261110T182522Z/source-snapshot.json), [binary hashes](20261110T182522Z/binaries.json)
- [Artifact hashes](20261110T182522Z/artifacts.sha256.json), [baseline hashes](20261110T182522Z/baseline/artifacts.sha256.json)
- [Exact clean-check commands](20261110T182522Z/verification/clean-commands.json) and [verification logs](20261110T182522Z/verification/)
- [Coexistence source snapshot](20261110T182522Z/verification/coexistence/) and [passed public API/integrity check](20261110T182522Z/verification/coexistence.log)

| Case | Before | After | After/before | B/op before → after |
|---|---:|---:|---:|---:|
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 128, StandardClient: False)` PayloadBytes=128&StandardClient=False | 33048.85 ns | 31610.24 ns | 0.956× | 581 → 575 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 128, StandardClient: True)` PayloadBytes=128&StandardClient=True | 33288.09 ns | 32611.34 ns | 0.980× | 579 → 579 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 4096, StandardClient: False)` PayloadBytes=4096&StandardClient=False | 39705.35 ns | 37683.56 ns | 0.949× | 581 → 580 |
| `WebSocketRoundtripBenchmarks.EchoRoundtrip(PayloadBytes: 4096, StandardClient: True)` PayloadBytes=4096&StandardClient=True | 37697.46 ns | 38797.36 ns | 1.029× | 581 → 581 |

Twelve complete streaming reports: six before, six after. Each uses ten connections, 87-byte payload, twenty frames/write, 5 s warmup and 30 s measurement. Process CPU/allocation scope includes the local generator and clients. Raw counter skew is preserved and bounded as documented in the methodology.

| Client | Before messages/s | After messages/s | Before kB / 1M | After kB / 1M | After GC counts per run |
|---|---:|---:|---:|---:|---|
| standard | 42,420,303 | 45,332,015 | 0.058 | 0.053 | [[0, 0, 0], [0, 0, 0], [0, 0, 0]] |
| pipeline | 71,206,979 | 70,983,348 | 0.010 | 0.012 | [[0, 0, 0], [0, 0, 0], [0, 0, 0]] |

Двенадцать полных streaming-отчётов: шесть до и шесть после. CPU/аллокации включают генератор и клиентов; ограниченный сдвиг независимых счётчиков сохранён в сырых данных и summary.json.
