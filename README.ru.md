# pipelines-websocket

[English](README.md) · [Русский](README.ru.md) · [CI](https://github.com/mzaprudin/pipelines-websocket/actions/workflows/ci.yml)

WebSocket-клиенты на C# / .NET 10: прямой TCP/TLS через System.IO.Pipelines и буферы из пула, а также адаптер ClientWebSocket. Заимствование памяти сообщения и повторное использование буферов снижают нагрузку на GC при непрерывном приёме данных.

## WebSocket по физической сети Ethernet

Измерения 8 ноября 2026 года: Windows-генератор → Linux-приёмник, Ethernet 1 Гбит/с, JSON 128 байт, 20 фреймов на запись, 10 соединений. Медианы трёх запусков: 5 секунд прогрева, 30 секунд измерения.

| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |
|---|---:|---:|---:|
| ClientWebSocket | 718,218 | 3,707 | 85.103 |
| Pipelines · fixed 64 KiB receive buffer | 743,095 | 3,652 | 0.653 |
| Pipelines · automatic buffer, separate build | 768,400 | 3,559 | 0.607 |

CPU и аллокации относятся к приёмнику, включая сбор метрик; 1 kB = 1000 байт. Основная библиотека сохраняет фиксированный TCP-буфер 64 KiB. Автоматический размер измерен в отдельной сборке с [однострочным патчем](results/lan-controls/20261108T202415Z/auto-receive.patch).

[Measured revision c2c3b1a](https://github.com/mzaprudin/zero-alloc-json-ws/tree/c2c3b1ad1d186073690c540b43b76cad18a54454) · [128-byte matrix and raw reports](results/lan-controls/20261108T202415Z/README.md) · [1-KiB matrix](results/lan-controls/20261108T194624Z/README.md) · [1-KiB Ethernet runs](results/lan-1kib/20261108T191616Z/README.md)

Все исходные Ethernet-наборы, даты, патчи и манифесты сохранены побайтно. Это измерения прежней объединённой сборки. Свежие результаты новых сборок приведены ниже для loopback; повторные физические измерения — отдельный этап.

## Запуск

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

Требуется .NET 10 SDK. Перенесены все **70 тестов**. Пример проверяет содержимое и порядок 100 сообщений через каждый клиент.

## Локальные измерения новых сборок

AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · BenchmarkDotNet 0.15.8. Release/x64; 5 прогревочных и 15 измерительных итераций по 500 мс, один процесс.

Echo проверяет полный обмен с локальным сервером. Streaming: JSON 87 байт, 20 фреймов на запись, 10 соединений, по три свежих процесса; 5 секунд прогрева и 30 секунд измерения. В streaming CPU и аллокации включают локальный сервер, клиентов и сбор метрик.

<!-- LOCAL_RESULTS_START -->
Measurements are being prepared.
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
