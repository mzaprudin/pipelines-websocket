# Парный Ethernet/loopback-прогон и аллокации

[English](README.md) · [Русский](README.ru.md)

10 ноября 2026 года · AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · Release/x64. JSON 128 байт, 20 фреймов на запись, 10 соединений, 5 с прогрева и 30 с измерения. По три свежих приёмника на клиент и топологию; порядок клиентов чередуется. Все 12 запусков выполнены последовательно. CPU, GC и аллокации — процесс приёмника со сбором метрик.

Ревизия: [`d35c490`](https://github.com/mzaprudin/pipelines-websocket/tree/d35c490b8dd6121a471b6ee06d63d73c4c12bbef). Один набор DLL и один профиль для обеих серий; хеши исходников и бинарников совпадают. Генератор Windows — текущий переносимый пакет, маршрут `/stream/128/20` проверен перед запуском. Локальный генератор — отдельный процесс. Linux-интерфейс: `enp6s0`, 1000 Мбит/с, full duplex; согласованная скорость Windows не запрошена.

| Серия | Клиент | Сообщений/с | CPU мс / млн | Аллокации, кБ / млн | Полные аллокации по повторам, байт |
|---|---|---:|---:|---:|---|
| ethernet | ClientWebSocket | 802,665 | 3,732 | 95.628 | 2886976 / 2302776 / 2285672 |
| ethernet | Pipelines | 785,592 | 3,706 | 0.592 | 13976 / 13880 / 13944 |
| loopback | ClientWebSocket | 29,421,536 | 427 | 0.524 | 403960 / 463368 / 467304 |
| loopback | Pipelines | 54,595,868 | 237 | 0.009 | 23968 / 14208 / 14240 |

В таблице — медианы неокруглённых значений каждого запуска; 1 кБ = 1000 байт. Все сборки GC 0/1/2 равны 0. Сдвиг байтовых счётчиков проверен отдельно для каждого sample; исходные значения сохранены.

## Трассы аллокаций

Три дополнительных диагностических запуска: Ethernet/ClientWebSocket, Ethernet/Pipelines, loopback/ClientWebSocket. EventPipe `gc-verbose` подключён после прогрева и первого sample, на 20 секунд. Эти запуски исключены из таблицы производительности.

| Трасса | Сэмплы EnsureBufferContainsAsync | Сэмплы инициализации EventSource | Потерянные события |
|---|---:|---:|---:|
| ethernet-standard | 18 | 5 | 0 |
| ethernet-pipeline | 0 | 6 | 0 |
| loopback-standard | 2 | 6 | 0 |

В трассе ClientWebSocket выделяются `StateMachineBox` для `ManagedWebSocket.EnsureBufferContainsAsync`. Стек проходит через `PoolingAsyncValueTaskMethodBuilder.GetStateMachineBox`. В [.NET 10.0.14](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/PoolingAsyncValueTaskMethodBuilderT.cs#L264) новый box создаётся при промахе кеша; [EnsureBufferContainsAsync](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Net.WebSockets/src/System/Net/WebSockets/ManagedWebSocket.cs#L1420) использует этот builder. Трасса подтверждает источник дополнительных аллокаций в асинхронном приёмном пути стандартного клиента.

Сэмплы EventSource относятся к включению профилировщика в первые 15 мс трассы. В трассе Pipelines сэмплов приёмного пути нет; это не утверждение о нулевых аллокациях. `GCAllocationTick` — выборка: её веса не дают точного распределения всех байтов по типам. Частота завершений каждого чтения отдельно не измерялась.

У Pipelines полные аллокации основного прогона остаются около 14 КБ при разном числе сообщений. У ClientWebSocket они заметно выше через Ethernet. Следовательно, разница нормированных значений включает и амортизацию затрат измерителя, и реальные дополнительные выделения стандартного приёмного пути.

## Исходные данные

- [Сводка](summary.json) · [условия парного сравнения](comparison-contract.json) · [сеть и генератор](network.json).
- [Сеанс Ethernet](ethernet/streaming-session.json) · [сеанс loopback](loopback/streaming-session.json) · [команды](commands.json).
- [Снимок исходников](source-snapshot.json) · [бинарники](binaries.json) · [SHA-256 артефактов](artifacts.sha256.json).
- [Команды трассировки и сеансы](diagnostics/sessions.json) · [исходник анализатора](diagnostics/analyzer/Program.cs) · [сборка анализатора](diagnostics/analysis-commands.json).
- [Трасса Ethernet/стандартный клиент](diagnostics/ethernet-standard/allocations.nettrace) · [декодированные аллокации](diagnostics/ethernet-standard/allocations.json).
- [Трасса Ethernet/Pipelines](diagnostics/ethernet-pipeline/allocations.nettrace) · [декодированные аллокации](diagnostics/ethernet-pipeline/allocations.json).
- [Трасса loopback/стандартный клиент](diagnostics/loopback-standard/allocations.nettrace) · [декодированные аллокации](diagnostics/loopback-standard/allocations.json).
- [Управление прогонами](runner.py) · [сбор трасс](diagnostics/capture.py) · [расчёт отчёта](summarize.py).
- [Историческая компактная Ethernet-матрица](../../lan-controls/20261108T202415Z/README.md) сохранена без изменений.
