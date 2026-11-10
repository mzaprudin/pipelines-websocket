#!/usr/bin/env python3
"""Validate six physical-LAN runs and render both README tables from their medians."""

import hashlib
import json
import math
import statistics
import sys
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
CLIENTS = ('standard', 'pipeline')
RUN_COUNT = 3
PAYLOAD_BYTES = 1024
FRAME_HEADER_BYTES = 4
CLIENT_COUNT = 10
WARMUP_SECONDS = 10
MEASUREMENT_SECONDS = 60
BITS_PER_BYTE = 8
BYTES_PER_MEGABIT = 1_000_000
MESSAGES_PER_MILLION = 1_000_000
BYTES_PER_KILOBYTE = 1_000
MILLISECONDS_PER_SECOND = 1000
PERCENT_SCALE = 100
LINK_MEGABITS_PER_SECOND = 1000
GC_GENERATION_COUNT = 3
ARITHMETIC_TOLERANCE = 1e-10
START = '<!-- LAN_RESULTS_START -->'
END = '<!-- LAN_RESULTS_END -->'
COMPLETION_LOG_PREFIX = 'Avg Msg/s:'
FAILURE_LOG_PREFIX = 'Streaming benchmark failed:'


def same(actual, expected, label):
    """Reject inconsistent derived metrics rather than silently reformatting them."""
    if not math.isfinite(actual) or not math.isclose(actual, expected, rel_tol=ARITHMETIC_TOLERANCE, abs_tol=ARITHMETIC_TOLERANCE):
        raise ValueError(f'{label}: {actual} != {expected}')


def read_run(path, client):
    """Validate profile, scope, sample deltas and process-metric arithmetic."""
    run = json.loads(path.read_text())
    expected = {
        'ClientType': client, 'ClientCount': CLIENT_COUNT, 'PayloadBytes': PAYLOAD_BYTES,
        'FramesPerBatch': 1, 'BatchBytes': PAYLOAD_BYTES + FRAME_HEADER_BYTES,
        'WarmupSeconds': WARMUP_SECONDS, 'Topology': 'remote-client', 'CpuScope': 'receiver-only',
    }
    for name, value in expected.items():
        if run[name] != value:
            raise ValueError(f'{path.name}: unexpected {name}: {run[name]}')
    if (run['DurationSeconds'] < MEASUREMENT_SECONDS or not run['Samples'] or
            min(run['Messages'], run['Bytes'], run['CpuMilliseconds']) <= 0 or run['AllocatedBytes'] < 0):
        raise ValueError(f'{path.name}: incomplete measurement')
    endpoint = urlparse(run['Endpoint'])
    if endpoint.scheme != 'ws' or endpoint.hostname in ('localhost', '127.0.0.1', '::1'):
        raise ValueError(f'{path.name}: expected a remote plain-WebSocket endpoint')
    if endpoint.path != f'/stream/{PAYLOAD_BYTES}/1':
        raise ValueError(f'{path.name}: mismatched generator profile')
    same(run['MessagesPerSecond'], run['Messages'] / run['DurationSeconds'], 'messages/s')
    same(run['MegabitsPerSecond'], run['Bytes'] * BITS_PER_BYTE / BYTES_PER_MEGABIT / run['DurationSeconds'], 'payload Mbit/s')
    same(run['CpuMillisecondsPerMillionMessages'], run['CpuMilliseconds'] * MESSAGES_PER_MILLION / run['Messages'], 'CPU cost')
    same(run['AllocatedBytesPerMessage'], run['AllocatedBytes'] / run['Messages'], 'allocation volume')
    same(run['CpuCorePercent'], run['CpuMilliseconds'] / run['DurationSeconds'] / MILLISECONDS_PER_SECOND * PERCENT_SCALE, 'core utilization')
    if run['MegabitsPerSecond'] > LINK_MEGABITS_PER_SECOND:
        raise ValueError(f'{path.name}: payload rate exceeds the recorded link speed')
    # Bytes and message counters are read separately while reception continues.
    max_counter_skew_bytes = 2 * CLIENT_COUNT * PAYLOAD_BYTES
    if abs(run['Bytes'] - run['Messages'] * PAYLOAD_BYTES) > max_counter_skew_bytes:
        raise ValueError(f'{path.name}: payload counters disagree')
    previous_seconds = 0
    for sample in run['Samples']:
        elapsed = sample['ElapsedSeconds'] - previous_seconds
        if elapsed <= 0 or min(sample['Messages'], sample['Bytes']) < 0:
            raise ValueError(f'{path.name}: invalid interval')
        same(sample['MessagesPerSecond'], sample['Messages'] / elapsed, 'sample messages/s')
        previous_seconds = sample['ElapsedSeconds']
    same(previous_seconds, run['DurationSeconds'], 'sample elapsed time')
    for field in ('Messages', 'Bytes'):
        if sum(sample[field] for sample in run['Samples']) != run[field]:
            raise ValueError(f'{path.name}: sample {field} do not sum to total')
    return run


def render(groups, relative_dir, russian):
    """Present the same receiver metrics in both language versions."""
    header = ('| Клиент | Сообщений/с | Полезные Мбит/с | CPU мс / млн сообщений | Аллокации, кБ / млн сообщений | GC 0/1/2 |'
        if russian else '| Client | Messages/s | Payload Mbit/s | CPU ms / 1M messages | Allocated kB / 1M messages | GC 0/1/2 |')
    lines = [header, '|---|---:|---:|---:|---:|---:|']
    for client in CLIENTS:
        runs = groups[client]
        median = lambda field: statistics.median(run[field] for run in runs)
        label = '`ClientWebSocket`' if client == 'standard' else '`FastWebSocketClient` · Pipelines'
        gc = '/'.join(f'{median(f"Gen{generation}Collections"):.0f}' for generation in range(GC_GENERATION_COUNT))
        allocated_kb_per_million = statistics.median(run['AllocatedBytes'] * MESSAGES_PER_MILLION / run['Messages'] for run in runs) / BYTES_PER_KILOBYTE
        lines.append(f'| {label} | {median("MessagesPerSecond"):,.0f} | {median("MegabitsPerSecond"):.1f} | {median("CpuMillisecondsPerMillionMessages"):,.0f} | {allocated_kb_per_million:.3f} | {gc} |')
    lines.append('')
    lines.append('Аллокации пересчитаны на 1 000 000 сообщений; 1 кБ = 1000 байт. GC показывает наблюдавшиеся сборки.'
        if russian else 'Allocations are normalized to 1,000,000 messages; 1 kB = 1000 bytes. GC shows observed collections.')
    lines.append('')
    lines.append('Медианы трёх запусков: 10 соединений, 10 с прогрева и 60 с замера; один фрейм на запись. CPU, аллокации и GC относятся к процессу Linux-приёмника, включая сбор метрик.'
        if russian else 'Medians of three runs: 10 connections, 10 s warmup and 60 s measurement; one frame per write. CPU, allocations and GC cover the Linux receiver process, including metric sampling.')
    lines.append('')
    lines.append(f'[{"Полные результаты и условия" if russian else "Full results and conditions"}]({relative_dir}/README.md) · [JSON]({relative_dir}/summary.json)')
    return '\n'.join(lines)


def main():
    """Publish only a complete consistent bundle and preserve its raw reports."""
    if len(sys.argv) != 2:
        raise SystemExit('Usage: python3 scripts/sync-lan-results.py results/lan-1kib/RUN-DIRECTORY')
    directory = Path(sys.argv[1]).resolve()
    relative_dir = directory.relative_to(ROOT).as_posix()
    groups = {client: [read_run(directory / f'{client}-{number}.json', client)
        for number in range(1, RUN_COUNT + 1)] for client in CLIENTS}
    for client in CLIENTS:
        for number in range(1, RUN_COUNT + 1):
            log = (directory / f'{client}-{number}.log').read_text()
            if COMPLETION_LOG_PREFIX not in log or FAILURE_LOG_PREFIX in log:
                raise ValueError(f'{client}-{number}: unsuccessful benchmark log')
    endpoints = {run['Endpoint'] for runs in groups.values() for run in runs}
    if len(endpoints) != 1:
        raise ValueError('All six runs must use the same generator endpoint')
    metrics = ('MessagesPerSecond', 'MegabitsPerSecond', 'CpuMillisecondsPerMillionMessages',
        'AllocatedBytesPerMessage', 'CpuCorePercent', 'Gen0Collections', 'Gen1Collections', 'Gen2Collections')
    summary = {'runsPerClient': RUN_COUNT, 'aggregation': 'Per-metric median across three fresh receiver processes',
        'median': {client: {metric: statistics.median(run[metric] for run in runs) for metric in metrics}
            for client, runs in groups.items()},
        'evidenceSha256': {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(directory.iterdir()) if path.suffix in ('.json', '.log') and path.name != 'summary.json'}}
    for client, runs in groups.items():
        summary['median'][client]['AllocatedBytesPerMillionMessages'] = statistics.median(
            run['AllocatedBytes'] * MESSAGES_PER_MILLION / run['Messages'] for run in runs)
    (directory / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    for filename, russian in (('README.md', False), ('README.ru.md', True)):
        path = ROOT / filename
        before, rest = path.read_text().split(START, 1)
        _, after = rest.split(END, 1)
        path.write_text(before + START + '\n' + render(groups, relative_dir, russian) + '\n' + END + after)
    print(f'Validated all six physical-LAN runs; rendered {relative_dir}/summary.json and both README tables.')


if __name__ == '__main__':
    main()
