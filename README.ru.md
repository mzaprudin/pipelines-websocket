# pipelines-websocket

[English](README.md) · [Русский](README.ru.md) · [CI](https://github.com/mzaprudin/pipelines-websocket/actions/workflows/ci.yml)

WebSocket-клиенты на C# / .NET 10: прямой TCP/TLS через System.IO.Pipelines и буферы из пула, а также адаптер ClientWebSocket. Заимствование памяти сообщения и повторное использование буферов снижают нагрузку на GC при непрерывном приёме данных.

Адаптер StdWebSocketClient сохранён внутренним для сравнений; публичные точки входа — FastWebSocketClient, IFastWebSocket и обработчик сообщений.

## WebSocket по физической сети Ethernet

Измерения 8 ноября 2026 года: Windows-генератор → Linux-приёмник, Ethernet 1 Гбит/с, JSON 128 байт, 20 фреймов на запись, 10 соединений. Медианы трёх запусков: 5 секунд прогрева, 30 секунд измерения.

| Клиент | Сообщений/с | CPU мс / 1 млн сообщений | Память kB / 1 млн сообщений |
|---|---:|---:|---:|
| ClientWebSocket | 718,218 | 3,707 | 85.103 |
| Pipelines · фиксированный приёмный буфер 64 KiB | 743,095 | 3,652 | 0.653 |
| Pipelines · автоматический буфер, отдельная сборка | 768,400 | 3,559 | 0.607 |

CPU и аллокации относятся к приёмнику, включая сбор метрик; 1 kB = 1000 байт. Основная библиотека сохраняет фиксированный TCP-буфер 64 KiB. Автоматический размер измерен в отдельной сборке с [однострочным патчем](results/lan-controls/20261108T202415Z/auto-receive.patch).

[Measured revision c2c3b1a](https://github.com/mzaprudin/zero-alloc-json-ws/tree/c2c3b1ad1d186073690c540b43b76cad18a54454) · [128-byte matrix and raw reports](results/lan-controls/20261108T202415Z/README.md) · [1-KiB matrix](results/lan-controls/20261108T194624Z/README.md) · [1-KiB Ethernet runs](results/lan-1kib/20261108T191616Z/README.md)

Все исходные Ethernet-наборы, даты, патчи и манифесты сохранены побайтно. Это измерения прежней объединённой сборки. Свежие результаты новых сборок приведены ниже для loopback; повторные физические измерения — отдельный этап.

<details>
<summary>Дополнительные Ethernet-измерения: JSON 1 KiB</summary>

<!-- LAN_RESULTS_START -->
| Клиент | Сообщений/с | Полезные Мбит/с | CPU мс / млн сообщений | Аллокации, кБ / млн сообщений | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|
| `ClientWebSocket` | 101,373 | 830.5 | 30,981 | 114.793 | 0/0/0 |
| `FastWebSocketClient` · Pipelines | 96,201 | 788.1 | 30,210 | 7.715 | 0/0/0 |

Аллокации пересчитаны на 1 000 000 сообщений; 1 кБ = 1000 байт. GC показывает наблюдавшиеся сборки.

Медианы трёх запусков: 10 соединений, 10 с прогрева и 60 с замера; один фрейм на запись. CPU, аллокации и GC относятся к процессу Linux-приёмника, включая сбор метрик.

[Полные результаты и условия](results/lan-1kib/20261108T191616Z/README.md) · [JSON](results/lan-1kib/20261108T191616Z/summary.json)
<!-- LAN_RESULTS_END -->

</details>

## Запуск

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

Требуется .NET 10 SDK. Перенесены все **70 тестов**. Пример проверяет содержимое и порядок 100 сообщений через каждый клиент.

## Локальные измерения новых сборок

AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · BenchmarkDotNet 0.15.8. Release/x64; 5 прогревочных и 15 измерительных итераций по 500 мс, один запуск процесса на случай.

Echo проверяет полный обмен с локальным сервером. Streaming: JSON 87 байт, 20 фреймов на запись, 10 соединений, по три свежих процесса; 5 секунд прогрева и 30 секунд измерения. В streaming CPU и аллокации включают локальный сервер, клиентов и сбор метрик.

<!-- LOCAL_RESULTS_START -->
### Локальный echo

| Метод | Среднее | Погрешность (99,9% ДИ) | Память B/op | Ускорение |
|---|---:|---:|---:|---:|
| `EchoRoundtrip (PayloadBytes=128&StandardClient=False)` | 31.61 µs | 856.69 ns | 575 B | — |
| `EchoRoundtrip (PayloadBytes=128&StandardClient=True)` | 32.61 µs | 950.02 ns | 579 B | — |
| `EchoRoundtrip (PayloadBytes=4096&StandardClient=False)` | 37.68 µs | 687.36 ns | 580 B | — |
| `EchoRoundtrip (PayloadBytes=4096&StandardClient=True)` | 38.80 µs | 1.64 µs | 581 B | — |

| Клиент | Сообщений/с | CPU мс / 1 млн | Память kB / 1 млн |
|---|---:|---:|---:|
| standard | 45,332,015 | 356 | 0.053 |
| pipeline | 70,983,348 | 276 | 0.012 |

<!-- LOCAL_RESULTS_END -->

[Полные отчёты, исходные значения и сравнение до/после](results/local/README.md) · [Методика](docs/benchmark-methodology.ru.md)

```sh
python3 scripts/run-benchmarks.py
python3 scripts/render-results.py results/local/RUN_DIRECTORY
```

## Устройство и проверка

[Владение памятью и ограничения](docs/architecture.ru.md) · [Сборка и CI](docs/development.ru.md) · [Проверки](docs/verification.ru.md) · [Происхождение](docs/provenance.ru.md)

Самостоятельная сборка `Pipelines.WebSockets` сохраняет пространства имён GapSonar. Зависимости от [zero-alloc-json](https://github.com/mzaprudin/zero-alloc-json) нет; обе библиотеки можно подключить одновременно.

Опубликовано для портфолио без открытой лицензии. См. [NOTICE](NOTICE).
