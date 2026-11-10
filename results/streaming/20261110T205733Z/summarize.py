#!/usr/bin/env python3
"""Derive the matched-run report from raw sessions and independent allocation traces."""
import importlib.util
import json
from pathlib import Path

DIRECTORY = Path(__file__).resolve().parent
ROOT = DIRECTORY.parents[2]
spec = importlib.util.spec_from_file_location('measurements', ROOT/'scripts/render-results.py')
measurements = importlib.util.module_from_spec(spec)
spec.loader.exec_module(measurements)
summary = measurements.paired_summary(DIRECTORY)
(DIRECTORY/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')

for russian in (False,True):
    title = '# Парный Ethernet/loopback-прогон и аллокации' if russian else '# Matched Ethernet/loopback run and allocations'
    language = '[English](README.md) · [Русский](README.ru.md)'
    context = ('10 ноября 2026 года · AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · Release/x64. JSON 128 байт, 20 фреймов на запись, 10 соединений, 5 с прогрева и 30 с измерения. По три свежих приёмника на клиент и топологию; порядок клиентов чередуется. Все 12 запусков выполнены последовательно. CPU, GC и аллокации — процесс приёмника со сбором метрик.' if russian else
               'November 10, 2026 · AMD Ryzen 9 5950X · Ubuntu 24.04 · .NET 10.0.14 · Release/x64. 128-byte JSON, 20 frames/write, 10 connections, 5 s warmup and 30 s measurement. Three fresh receivers per client and topology, alternating client order. All 12 runs were sequential. CPU, GC and allocations cover the receiver process including sampling.')
    source = ('Ревизия' if russian else 'Source') + f': [`{summary["sourceRevision"][:7]}`](https://github.com/mzaprudin/pipelines-websocket/tree/{summary["sourceRevision"]}). '
    source += ('Один набор DLL и один профиль для обеих серий; хеши исходников и бинарников совпадают. Генератор Windows — текущий переносимый пакет, маршрут `/stream/128/20` проверен перед запуском. Локальный генератор — отдельный процесс. Linux-интерфейс: `enp6s0`, 1000 Мбит/с, full duplex; согласованная скорость Windows не запрошена.' if russian else
               'Both series use the same DLL set and profile; source and binary fingerprints match. Windows generator: the supplied current portable package, with `/stream/128/20` verified before measurement. The local generator runs in a separate process. Linux interface: `enp6s0`, 1000 Mbit/s, full duplex; Windows negotiated speed was not queried.')
    header = ('| Серия | Клиент | Сообщений/с | CPU мс / млн | Аллокации, кБ / млн | Полные аллокации по повторам, байт |' if russian else
              '| Series | Client | Messages/s | CPU ms / 1M | Allocated kB / 1M | Total allocated bytes by repetition |')
    blocks = [title,'',language,'',context,'',source,'',header,'|---|---|---:|---:|---:|---|']
    for topology in ('ethernet','loopback'):
        for client,label in [('standard','ClientWebSocket'),('pipeline','Pipelines')]:
            stats = summary[topology][client]
            totals = ' / '.join(str(n) for n in stats['allocatedBytesByRun'])
            blocks.append(f'| {topology} | {label} | {stats["messagesPerSecond"]:,.0f} | {stats["cpuMsPerMillionMessages"]:,.0f} | {stats["allocatedBytesPerMillionMessages"]/1000:.3f} | {totals} |')
    blocks += ['',('В таблице — медианы неокруглённых значений каждого запуска; 1 кБ = 1000 байт. Все сборки GC 0/1/2 равны 0. Сдвиг байтовых счётчиков проверен отдельно для каждого sample; исходные значения сохранены.' if russian else
                   'Rows show medians of unrounded per-run values; 1 kB = 1000 bytes. Every run reports GC 0/1/2 = 0. Byte-counter skew is validated for every sample; raw values are retained.'),'',
               '## Трассы аллокаций' if russian else '## Allocation traces','',
               ('Три дополнительных диагностических запуска: Ethernet/ClientWebSocket, Ethernet/Pipelines, loopback/ClientWebSocket. EventPipe `gc-verbose` подключён после прогрева и первого sample, на 20 секунд. Эти запуски исключены из таблицы производительности.' if russian else
                'Three additional diagnostic sessions: Ethernet/ClientWebSocket, Ethernet/Pipelines, loopback/ClientWebSocket. EventPipe `gc-verbose` was attached after warmup and the first sample, for 20 seconds. These sessions are excluded from the performance table.'),'',
               ('| Трасса | Сэмплы EnsureBufferContainsAsync | Сэмплы инициализации EventSource | Потерянные события |' if russian else '| Trace | EnsureBufferContainsAsync samples | EventSource initialization samples | Events lost |'),'|---|---:|---:|---:|']
    for name in ('ethernet-standard','ethernet-pipeline','loopback-standard'):
        report=json.loads((DIRECTORY/'diagnostics'/name/'allocations.json').read_text(encoding='utf-8'))
        receive=sum('EnsureBufferContainsAsync' in e['Type'] for e in report['Events'])
        initialization=sum(any('System.Diagnostics.Tracing.EventSource' in frame for frame in e['Stack']) for e in report['Events'])
        blocks.append(f'| {name} | {receive} | {initialization} | {report["EventsLost"]} |')
    findings = ('''В трассе ClientWebSocket выделяются `StateMachineBox` для `ManagedWebSocket.EnsureBufferContainsAsync`. Стек проходит через `PoolingAsyncValueTaskMethodBuilder.GetStateMachineBox`. В [.NET 10.0.14](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/PoolingAsyncValueTaskMethodBuilderT.cs#L264) новый box создаётся при промахе кеша; [EnsureBufferContainsAsync](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Net.WebSockets/src/System/Net/WebSockets/ManagedWebSocket.cs#L1420) использует этот builder. Трасса подтверждает источник дополнительных аллокаций в асинхронном приёмном пути стандартного клиента.

Сэмплы EventSource относятся к включению профилировщика в первые 15 мс трассы. В трассе Pipelines сэмплов приёмного пути нет; это не утверждение о нулевых аллокациях. `GCAllocationTick` — выборка: её веса не дают точного распределения всех байтов по типам. Частота завершений каждого чтения отдельно не измерялась.

У Pipelines полные аллокации основного прогона остаются около 14 КБ при разном числе сообщений. У ClientWebSocket они заметно выше через Ethernet. Следовательно, разница нормированных значений включает и амортизацию затрат измерителя, и реальные дополнительные выделения стандартного приёмного пути.''' if russian else
                '''The ClientWebSocket trace contains `StateMachineBox` allocations for `ManagedWebSocket.EnsureBufferContainsAsync`, through `PoolingAsyncValueTaskMethodBuilder.GetStateMachineBox`. In [.NET 10.0.14](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/PoolingAsyncValueTaskMethodBuilderT.cs#L264), cache misses create a new box; [EnsureBufferContainsAsync](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Net.WebSockets/src/System/Net/WebSockets/ManagedWebSocket.cs#L1420) uses that builder. This confirms a source of additional allocations in the framework client's asynchronous receive path.

EventSource samples belong to profiler initialization within the first 15 ms. No receive-path allocations were sampled for Pipelines; this is not a zero-allocation assertion. `GCAllocationTick` is sampled: its weights do not provide an exact allocation breakdown by type. Individual read completion frequencies were not measured.

Pipelines' main-run allocation totals stay around 14 kB across different message volumes. ClientWebSocket allocates substantially more over Ethernet. Normalized differences therefore include both instrumentation amortization and additional framework receive-path allocations.''')
    blocks += ['',findings,'','## Исходные данные' if russian else '## Evidence','',
               ('- [Сводка](summary.json) · [условия парного сравнения](comparison-contract.json) · [сеть и генератор](network.json).' if russian else '- [Raw summary](summary.json) · [matching contract](comparison-contract.json) · [network and generator metadata](network.json).'),
               ('- [Сеанс Ethernet](ethernet/streaming-session.json) · [сеанс loopback](loopback/streaming-session.json) · [команды](commands.json).' if russian else '- [Ethernet sessions](ethernet/streaming-session.json) · [loopback sessions](loopback/streaming-session.json) · [commands](commands.json).'),
               ('- [Снимок исходников](source-snapshot.json) · [бинарники](binaries.json) · [SHA-256 артефактов](artifacts.sha256.json).' if russian else '- [Source snapshot](source-snapshot.json) · [binaries](binaries.json) · [artifact SHA-256](artifacts.sha256.json).'),
               ('- [Команды трассировки и сеансы](diagnostics/sessions.json) · [исходник анализатора](diagnostics/analyzer/Program.cs) · [сборка анализатора](diagnostics/analysis-commands.json).' if russian else '- [Trace commands and sessions](diagnostics/sessions.json) · [analyzer source](diagnostics/analyzer/Program.cs) · [analyzer build command](diagnostics/analysis-commands.json).'),
               ('- [Трасса Ethernet/стандартный клиент](diagnostics/ethernet-standard/allocations.nettrace) · [декодированные аллокации](diagnostics/ethernet-standard/allocations.json).' if russian else '- [Ethernet/standard trace](diagnostics/ethernet-standard/allocations.nettrace) · [decoded allocations](diagnostics/ethernet-standard/allocations.json).'),
               ('- [Трасса Ethernet/Pipelines](diagnostics/ethernet-pipeline/allocations.nettrace) · [декодированные аллокации](diagnostics/ethernet-pipeline/allocations.json).' if russian else '- [Ethernet/Pipelines trace](diagnostics/ethernet-pipeline/allocations.nettrace) · [decoded allocations](diagnostics/ethernet-pipeline/allocations.json).'),
               ('- [Трасса loopback/стандартный клиент](diagnostics/loopback-standard/allocations.nettrace) · [декодированные аллокации](diagnostics/loopback-standard/allocations.json).' if russian else '- [Loopback/standard trace](diagnostics/loopback-standard/allocations.nettrace) · [decoded allocations](diagnostics/loopback-standard/allocations.json).'),
               ('- [Управление прогонами](runner.py) · [сбор трасс](diagnostics/capture.py) · [расчёт отчёта](summarize.py).' if russian else '- [Generator control](runner.py) · [capture script](diagnostics/capture.py) · [report derivation](summarize.py).'),
               ('- [Историческая компактная Ethernet-матрица](../../lan-controls/20261108T202415Z/README.md) сохранена без изменений.' if russian else '- [Historical compact Ethernet matrix](../../lan-controls/20261108T202415Z/README.md) remains unchanged.'),'']
    (DIRECTORY/('README.ru.md' if russian else 'README.md')).write_text('\n'.join(blocks),encoding='utf-8')
