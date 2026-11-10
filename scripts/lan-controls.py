#!/usr/bin/env python3
"""Build one-line TCP variants and measure a balanced physical-LAN control matrix."""

import argparse
import base64
import difflib
import hashlib
import json
import math
import os
import platform
import re
import shutil
import socket
import statistics
import subprocess
import tempfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROFILES = ((1, 5050), (4, 5051), (20, 5052))
VARIANTS = ('standard', 'fixed', 'auto')
RUN_COUNT = 3
GC_GENERATION_COUNT = 3
CLIENT_COUNT = 10
DEFAULT_PAYLOAD_BYTES = 1024
CONTROL_PAYLOAD_SIZES = (128, DEFAULT_PAYLOAD_BYTES)
FRAME_HEADER_BYTES = 4
WARMUP_SECONDS = 5
DURATION_SECONDS = 30
ARITHMETIC_TOLERANCE = 1e-10
MESSAGES_PER_MILLION = 1_000_000
BYTES_PER_KILOBYTE = 1_000
BITS_PER_BYTE = 8
BYTES_PER_MEGABIT = 1_000_000
LINK_MEGABITS_PER_SECOND = 1000
CORE_SOURCE = Path('src/Pipelines.WebSockets/Networking/Core/FastWebSocketClient.cs')
RECEIVE_BUFFER_ASSIGNMENT = '                ReceiveBufferSize = 65536,\n'
REQUESTED_RECEIVE_BUFFER_BYTES = 65_536
LINUX_BUFFER_ACCOUNTING_MULTIPLIER = 2
PROJECT_DIRECTORIES = ('src', 'tests', 'support', 'benchmarks', 'samples')
PROJECT_ROOT_FILES = ('Directory.Build.props', 'global.json', 'Pipelines.WebSockets.sln')
COMPLETION_PREFIX = 'Avg Msg/s:'
FAILURE_PREFIX = 'Streaming benchmark failed:'


def digest(path):
    """Hash exact source, binary or evidence bytes."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    """Store human-readable evidence outside measured receiver processes."""
    path.write_text(json.dumps(value, indent=2) + '\n')


def verify_generator(host, batch, port, payload_bytes=DEFAULT_PAYLOAD_BYTES):
    """Require a successful upgrade for every exact generator profile."""
    network_timeout_seconds = 5
    max_header_bytes = 16_384
    error_preview_bytes = 80
    nonce_bytes = 16
    nonce = base64.b64encode(os.urandom(nonce_bytes)).decode()
    request = (f'GET /stream/{payload_bytes}/{batch} HTTP/1.1\r\nHost: {host}:{port}\r\n'
        f'Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {nonce}\r\nSec-WebSocket-Version: 13\r\n\r\n')
    with socket.create_connection((host, port), timeout=network_timeout_seconds) as peer:
        peer.sendall(request.encode('ascii'))
        header = b''
        while b'\r\n\r\n' not in header and len(header) < max_header_bytes:
            chunk = peer.recv(max_header_bytes)
            if not chunk:
                break
            header += chunk
    if not header.startswith(b'HTTP/1.1 101 '):
        raise RuntimeError(f'Generator {host}:{port} does not serve batch {batch}: {header[:error_preview_bytes]!r}')


def prepare_variant(directory, automatic, output, log_suffix=''):
    """Build/test an isolated source copy with at most the receive-size assignment removed."""
    for name in PROJECT_ROOT_FILES:
        shutil.copyfile(ROOT / name, directory / name)
    for name in PROJECT_DIRECTORIES:
        shutil.copytree(ROOT / name, directory / name, ignore=shutil.ignore_patterns('bin', 'obj'))
    if automatic:
        path = directory / CORE_SOURCE
        before = path.read_text()
        if before.count(RECEIVE_BUFFER_ASSIGNMENT) != 1:
            raise RuntimeError('Expected exactly one original receive-buffer assignment')
        after = before.replace(RECEIVE_BUFFER_ASSIGNMENT, '', 1)
        path.write_text(after)
        patch = ''.join(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
            fromfile=f'a/{CORE_SOURCE}', tofile=f'b/{CORE_SOURCE}'))
        (output / 'auto-receive.patch').write_text(patch)
    sources = {str(path.relative_to(directory)): digest(path)
        for path in sorted(directory.rglob('*')) if path.is_file()}
    commands = [
        ['dotnet', 'restore', 'Pipelines.WebSockets.sln', '--locked-mode'],
        ['dotnet', 'build', 'Pipelines.WebSockets.sln', '-c', 'Release', '--no-restore'],
        ['dotnet', 'test', 'tests/Pipelines.WebSockets.Tests/Pipelines.WebSockets.Tests.csproj',
            '-c', 'Release', '--no-build', '--logger', 'console;verbosity=minimal'],
    ]
    variant = 'auto' if automatic else 'fixed'
    print(f'Preparing {variant}: locked restore, Release build and complete tests.', flush=True)
    with (output / f'{variant}{log_suffix}-setup.log').open('w') as log:
        for command in commands:
            log.write(json.dumps(command) + '\n'); log.flush()
            subprocess.run(command, cwd=directory, stdout=log, stderr=subprocess.STDOUT, check=True)
    binary_dir = directory / 'benchmarks/WebSocketLoadTest/bin/Release/net10.0'
    return {'sourceSha256': sources, 'commands': commands, 'exitCodes': [0] * len(commands),
        'binarySha256': {name: digest(binary_dir / name) for name in ('WebSocketLoadTest.dll', 'Pipelines.WebSockets.dll')},
        'entryPoint': str(binary_dir / 'WebSocketLoadTest.dll')}


def established_receive_capacities(snapshot):
    """Select active connections and exclude old FIN-WAIT/TIME-WAIT socket observations."""
    capacities = []
    established = False
    for line in snapshot.splitlines():
        if line and not line[0].isspace():
            established = line.split(maxsplit=1)[0] == 'ESTAB'
        elif established:
            match = re.search(r'skmem:\(r\d+,rb(\d+),', line)
            if match:
                capacities.append(int(match.group(1)))
    return capacities


def measure_case(host, batch, port, variant, repetition, builds, output, payload_bytes=DEFAULT_PAYLOAD_BYTES):
    """Run one receiver and capture its independent TCP socket state mid-measurement."""
    stem = f'batch-{batch}-{variant}-{repetition}'
    entry = builds['auto' if variant == 'auto' else 'fixed']['entryPoint']
    command = ['dotnet', entry, '--mode', 'client', '--url', f'ws://{host}:{port}',
        '--payload-bytes', str(payload_bytes), '--batch', str(batch), '--count', str(CLIENT_COUNT),
        '--duration', str(DURATION_SECONDS), '--warmup', str(WARMUP_SECONDS), '--json', str(output / f'{stem}.json')]
    if variant == 'standard':
        command.append('--std')
    print(f'CONTROL: batch={batch}, receiver={variant}, repetition={repetition}/{RUN_COUNT}', flush=True)
    snapshot_command = ['ss', '-tinm', 'dst', host, 'dport', '=', f':{port}']
    with (output / f'{stem}.log').open('w') as log:
        receiver = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
        try:
            try:
                receiver.wait(timeout=WARMUP_SECONDS + DURATION_SECONDS / 2)
                raise RuntimeError(f'{stem}: receiver exited before the socket snapshot')
            except subprocess.TimeoutExpired:
                snapshot = subprocess.check_output(snapshot_command, text=True)
                (output / f'{stem}.tcp.txt').write_text(snapshot)
            lifecycle_allowance_seconds = 30
            code = receiver.wait(timeout=WARMUP_SECONDS + DURATION_SECONDS + lifecycle_allowance_seconds)
            if code:
                raise RuntimeError(f'{stem}: receiver exit {code}; see its log')
        finally:
            if receiver.poll() is None:
                receiver.kill(); receiver.wait()
    return {'name': stem, 'variant': variant, 'batch': batch, 'port': port, 'repetition': repetition,
        'command': command, 'exitCode': code, 'socketCommand': snapshot_command,
        'observedReceiveBufferBytes': established_receive_capacities(snapshot)}


def equal(actual, expected, label):
    """Reject inconsistent derived metrics with floating-point tolerance."""
    if not math.isfinite(actual) or not math.isclose(actual, expected, rel_tol=ARITHMETIC_TOLERANCE, abs_tol=ARITHMETIC_TOLERANCE):
        raise ValueError(f'{label}: {actual} != {expected}')


def validate_case(output, case, host, payload_bytes=DEFAULT_PAYLOAD_BYTES):
    """Verify one completed profile, metric scope, raw samples and derived arithmetic."""
    path = output / f'{case["name"]}.json'
    run = json.loads(path.read_text())
    if dict(PROFILES).get(case['batch']) != case['port']:
        raise ValueError(f'{path.name}: mismatched generator port/profile')
    expected_name = f'batch-{case["batch"]}-{case["variant"]}-{case["repetition"]}'
    if case['name'] != expected_name:
        raise ValueError(f'{path.name}: mislabeled receiver or repetition')
    expected = {'ClientType': 'standard' if case['variant'] == 'standard' else 'pipeline',
        'ClientCount': CLIENT_COUNT, 'PayloadBytes': payload_bytes, 'FramesPerBatch': case['batch'],
        'BatchBytes': (payload_bytes + FRAME_HEADER_BYTES) * case['batch'], 'WarmupSeconds': WARMUP_SECONDS,
        'Topology': 'remote-client', 'CpuScope': 'receiver-only',
        'Endpoint': f'ws://{host}:{case["port"]}/stream/{payload_bytes}/{case["batch"]}'}
    for key, value in expected.items():
        if run[key] != value:
            raise ValueError(f'{path.name}: unexpected {key}')
    if (case['exitCode'] != 0 or run['DurationSeconds'] < DURATION_SECONDS or
            min(run['Messages'], run['Bytes'], run['CpuMilliseconds']) <= 0 or run['AllocatedBytes'] < 0):
        raise ValueError(f'{path.name}: incomplete run')
    equal(run['MessagesPerSecond'], run['Messages'] / run['DurationSeconds'], 'messages/s')
    equal(run['MegabitsPerSecond'], run['Bytes'] * BITS_PER_BYTE / BYTES_PER_MEGABIT / run['DurationSeconds'], 'Mbit/s')
    equal(run['CpuMillisecondsPerMillionMessages'], run['CpuMilliseconds'] * MESSAGES_PER_MILLION / run['Messages'], 'CPU cost')
    equal(run['AllocatedBytesPerMessage'], run['AllocatedBytes'] / run['Messages'], 'allocations')
    percent_scale = 100
    milliseconds_per_second = 1000
    equal(run['CpuCorePercent'], run['CpuMilliseconds'] / run['DurationSeconds'] / milliseconds_per_second * percent_scale, 'core utilization')
    if run['MegabitsPerSecond'] > LINK_MEGABITS_PER_SECOND:
        raise ValueError(f'{path.name}: exceeds recorded link speed')
    max_snapshot_skew_bytes = 2 * CLIENT_COUNT * payload_bytes
    if abs(run['Bytes'] - run['Messages'] * payload_bytes) > max_snapshot_skew_bytes:
        raise ValueError(f'{path.name}: inconsistent payload counters')
    previous_seconds = 0
    for sample in run['Samples']:
        seconds = sample['ElapsedSeconds'] - previous_seconds
        if seconds <= 0 or min(sample['Messages'], sample['Bytes']) < 0:
            raise ValueError(f'{path.name}: invalid interval')
        equal(sample['MessagesPerSecond'], sample['Messages'] / seconds, 'sample rate')
        previous_seconds = sample['ElapsedSeconds']
    equal(previous_seconds, run['DurationSeconds'], 'elapsed time')
    for field in ('Messages', 'Bytes'):
        if sum(sample[field] for sample in run['Samples']) != run[field]:
            raise ValueError(f'{path.name}: sample {field} do not sum to total')
    log = (output / f'{case["name"]}.log').read_text()
    if COMPLETION_PREFIX not in log or FAILURE_PREFIX in log:
        raise ValueError(f'{path.name}: unsuccessful log')
    if len(case['observedReceiveBufferBytes']) != CLIENT_COUNT:
        raise ValueError(f'{path.name}: missing TCP socket observations')
    snapshot = (output / f'{case["name"]}.tcp.txt').read_text()
    observed = established_receive_capacities(snapshot)
    if observed != case['observedReceiveBufferBytes']:
        raise ValueError(f'{path.name}: socket snapshot disagrees with metadata')
    if case['variant'] == 'fixed' and any(size != REQUESTED_RECEIVE_BUFFER_BYTES * LINUX_BUFFER_ACCOUNTING_MULTIPLIER for size in observed):
        raise ValueError(f'{path.name}: fixed receive-buffer setting was not observed')
    for generation in range(GC_GENERATION_COUNT):
        collections = run[f'Gen{generation}Collections']
        if not isinstance(collections, int) or collections < 0:
            raise ValueError(f'{path.name}: invalid collection count')
    return run


def validate_builds(output, environment):
    """Require matched source sets, exactly one removed assignment, and successful full tests."""
    builds = json.loads((output / 'builds.json').read_text())
    if digest(output / 'runner-source.py') != environment['runnerSha256']:
        raise ValueError('Executed runner snapshot does not match its fingerprint')
    for resumption in environment.get('resumptions', []):
        if digest(output / resumption['runnerSnapshot']) != resumption['runnerSha256']:
            raise ValueError('Resume runner snapshot does not match its fingerprint')
        if not resumption['sourceAndBinaryHashesMatch']:
            raise ValueError('Resumed receiver build differs from the original')
    source_sets = [set(builds[variant]['sourceSha256']) for variant in ('fixed', 'auto')]
    if not source_sets[0] or source_sets[0] != source_sets[1]:
        raise ValueError('Receiver variants must use identical source-file sets')
    changed = [name for name in source_sets[0]
        if builds['fixed']['sourceSha256'][name] != builds['auto']['sourceSha256'][name]]
    if changed != [str(CORE_SOURCE)]:
        raise ValueError(f'Unexpected source differences: {changed}')
    patch = (output / 'auto-receive.patch').read_text().splitlines(True)
    removed = [line[1:] for line in patch if line.startswith('-') and not line.startswith('---')]
    added = [line for line in patch if line.startswith('+') and not line.startswith('+++')]
    if removed != [RECEIVE_BUFFER_ASSIGNMENT] or added:
        raise ValueError('Expected only removal of the explicit TCP receive-buffer assignment')
    counts = {}
    for variant in ('fixed', 'auto'):
        if builds[variant]['exitCodes'] != [0] * len(builds[variant]['commands']):
            raise ValueError(f'{variant}: unsuccessful build/test command')
        log = (output / f'{variant}-setup.log').read_text()
        result = re.search(r'Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+(\d+),\s+Skipped:\s+0,', log)
        if result is None or int(result.group(1)) <= 0 or 'Build succeeded.' not in log:
            raise ValueError(f'{variant}: successful full test/build result missing')
        counts[variant] = int(result.group(1))
    if counts['fixed'] != counts['auto']:
        raise ValueError('Receiver variants did not pass the same test suite')
    return builds, {'testsPassed': counts, 'sourceFilesPerVariant': len(source_sets[0]),
        'onlyChangedSource': str(CORE_SOURCE), 'executedRunnerSha256': environment['runnerSha256']}


def summarize(output):
    """Validate the full matrix and render every receiver/batch combination."""
    environment = json.loads((output / 'environment.json').read_text())
    payload_bytes = environment['profile']['payloadBytes']
    if payload_bytes not in CONTROL_PAYLOAD_SIZES:
        raise ValueError('Unsupported control payload size')
    builds, build_validation = validate_builds(output, environment)
    groups = {(batch, variant): [] for batch, _ in PROFILES for variant in VARIANTS}
    seen = set()
    for case in environment['cases']:
        key = (case['batch'], case['variant'], case['repetition'])
        if key in seen or case['repetition'] not in range(1, RUN_COUNT + 1):
            raise ValueError('Duplicate or invalid repetition')
        seen.add(key)
        build = builds['auto' if case['variant'] == 'auto' else 'fixed']
        if case['command'][:2] != ['dotnet', build['entryPoint']]:
            raise ValueError(f'{case["name"]}: incorrect receiver binary')
        groups[key[:2]].append(validate_case(output, case, environment['host'], payload_bytes))
    expected_count = len(PROFILES) * len(VARIANTS) * RUN_COUNT
    if len(seen) != expected_count or any(len(runs) != RUN_COUNT for runs in groups.values()):
        raise ValueError(f'Expected exactly {expected_count} complete control cases')
    metrics = ('MessagesPerSecond', 'MegabitsPerSecond', 'CpuMillisecondsPerMillionMessages', 'AllocatedBytesPerMessage')
    medians = {f'batch-{batch}-{variant}': {metric: statistics.median(run[metric] for run in runs) for metric in metrics}
        for (batch, variant), runs in groups.items()}
    for (batch, variant), runs in groups.items():
        medians[f'batch-{batch}-{variant}']['AllocatedBytesPerMillionMessages'] = statistics.median(
            run['AllocatedBytes'] * MESSAGES_PER_MILLION / run['Messages'] for run in runs)
    socket_capacities = {f'batch-{batch}-{variant}': [size for case in environment['cases']
        if case['batch'] == batch and case['variant'] == variant for size in case['observedReceiveBufferBytes']]
        for batch, _ in PROFILES for variant in VARIANTS}
    percent_scale = 100
    effects = {}
    for batch, _ in PROFILES:
        standard, fixed, automatic = [medians[f'batch-{batch}-{variant}'] for variant in VARIANTS]
        effects[str(batch)] = {
            'automaticVsFixedThroughputPercent': (automatic['MessagesPerSecond'] / fixed['MessagesPerSecond'] - 1) * percent_scale,
            'automaticVsFixedCpuReductionPercent': (1 - automatic['CpuMillisecondsPerMillionMessages'] / fixed['CpuMillisecondsPerMillionMessages']) * percent_scale,
            'automaticVsStandardThroughputPercent': (automatic['MessagesPerSecond'] / standard['MessagesPerSecond'] - 1) * percent_scale,
        }
    lines = ['# Controlled Ethernet measurements: receive buffer and frame batches', '',
        f'Windows generator → 1-Gbit Ethernet → Linux receiver. {payload_bytes}-byte JSON, {CLIENT_COUNT} connections, '
        f'{RUN_COUNT} fresh-process runs per case, {WARMUP_SECONDS} s warmup + {DURATION_SECONDS} s measurement.', '',
        '| Frames/write | Receiver | Messages/s, median [min–max] | Payload Mbit/s | CPU ms / 1M messages | Allocated kB / 1M messages |',
        '|---:|---|---:|---:|---:|---:|']
    labels = {'standard': 'ClientWebSocket', 'fixed': 'Pipelines · fixed 64 KiB', 'auto': 'Pipelines · automatic receive buffer'}
    for (batch, variant), runs in groups.items():
        median = medians[f'batch-{batch}-{variant}']
        rates = [run['MessagesPerSecond'] for run in runs]
        lines.append(f'| {batch} | {labels[variant]} | {median["MessagesPerSecond"]:,.0f} [{min(rates):,.0f}–{max(rates):,.0f}] | '
            f'{median["MegabitsPerSecond"]:.1f} | {median["CpuMillisecondsPerMillionMessages"]:,.0f} | {median["AllocatedBytesPerMillionMessages"] / BYTES_PER_KILOBYTE:.3f} |')
    lines.extend(['', '## Automatic versus fixed receive sizing', '',
        '| Frames/write | Throughput change | CPU cost reduction per million messages |', '|---:|---:|---:|'])
    for batch, _ in PROFILES:
        effect = effects[str(batch)]
        lines.append(f'| {batch} | {effect["automaticVsFixedThroughputPercent"]:+.2f}% | {effect["automaticVsFixedCpuReductionPercent"]:+.2f}% |')
    allocation_comparisons = {}
    lines.extend(['', '## Managed allocation comparison', '',
        '| Frames/write | Native kB / 1M | Fixed kB / 1M | Auto kB / 1M | Native / auto |', '|---:|---:|---:|---:|---:|'])
    for batch, _ in PROFILES:
        allocated = [medians[f'batch-{batch}-{variant}']['AllocatedBytesPerMessage'] for variant in VARIANTS]
        ratio = allocated[0] / allocated[2] if allocated[2] > 0 else None
        allocation_comparisons[str(batch)] = {'nativeOverAutomaticBytesPerMessageRatio': ratio,
            'medianAllocatedBytesPerMillionMessages': {variant: medians[f'batch-{batch}-{variant}']['AllocatedBytesPerMillionMessages'] for variant in VARIANTS},
            'medianAllocatedBytesPerSecond': {variant: statistics.median(run['AllocatedBytes'] / run['DurationSeconds']
                for run in groups[(batch, variant)]) for variant in VARIANTS},
            'medianAllocatedBytesPerRun': {variant: statistics.median(run['AllocatedBytes']
                for run in groups[(batch, variant)]) for variant in VARIANTS},
            'maxGcCollections': {variant: [max(run[f'Gen{generation}Collections'] for run in groups[(batch, variant)])
                for generation in range(GC_GENERATION_COUNT)] for variant in VARIANTS}}
        ratio_text = f'{ratio:.2f}×' if ratio is not None else 'zero auto allocation'
        normalized = [medians[f'batch-{batch}-{variant}']['AllocatedBytesPerMillionMessages'] / BYTES_PER_KILOBYTE for variant in VARIANTS]
        lines.append(f'| {batch} | {normalized[0]:.3f} | {normalized[1]:.3f} | {normalized[2]:.3f} | {ratio_text} |')
    lines.extend(['', '| Frames/write | Receiver | CPU ms / 1M messages | Allocated kB / 1M messages | GC 0/1/2, maximum |', '|---:|---|---:|---:|---:|'])
    for batch, _ in PROFILES:
        comparison = allocation_comparisons[str(batch)]
        for variant in VARIANTS:
            gc = '/'.join(str(count) for count in comparison['maxGcCollections'][variant])
            median = medians[f'batch-{batch}-{variant}']
            lines.append(f'| {batch} | {labels[variant]} | {median["CpuMillisecondsPerMillionMessages"]:,.0f} | '
                f'{median["AllocatedBytesPerMillionMessages"] / BYTES_PER_KILOBYTE:.3f} | {gc} |')
    lines.extend(['', 'Allocation metrics cover the entire managed receiver process, including periodic measurement. '
        'Normalized allocation is `AllocatedBytes / Messages × 1,000,000`; 1 kB = 1000 bytes. '
        'Allocation columns are per-condition medians; GC columns show observed maxima. Socket receive-capacity limits are recorded separately below.'])
    lines.extend(['', '## Socket receive-buffer observations', '',
        '| Frames/write | Receiver | Kernel receive capacity, min–max bytes |', '|---:|---|---:|'])
    for batch, _ in PROFILES:
        for variant in VARIANTS:
            capacities = socket_capacities[f'batch-{batch}-{variant}']
            lines.append(f'| {batch} | {labels[variant]} | {min(capacities):,}–{max(capacities):,} |')
    lines.extend(['', 'Linux reports 131072 bytes for the explicit 65536-byte request because its socket '
        'buffer accounting doubles that setting. The captured `ss` rb values are capacity limits; '
        'the complete snapshots also retain live memory and TCP window observations. '
        '[Linux socket buffer accounting](https://man7.org/linux/man-pages/man7/socket.7.html).', '',
        'Only the explicit TCP receive-buffer assignment changes between fixed and automatic Pipelines builds. '
        'The send buffer, application read size, Pipe settings and frame-processing code are identical. '
        'CPU and allocation metrics cover the Linux receiver process, including sampling.', '',
        '[Environment, exact case order and socket observations](environment.json) · '
        '[Build/source manifests](builds.json) · [Exact source patch](auto-receive.patch) · [Summary JSON](summary.json)', ''])
    (output / 'README.md').write_text('\n'.join(lines))
    write_json(output / 'summary.json', {'cases': expected_count, 'runsPerCase': RUN_COUNT, 'medians': medians,
        'automaticVsFixed': effects,
        'allocationComparisons': allocation_comparisons,
        'buildValidation': build_validation,
        'socketReceiveCapacityBytes': {name: {'min': min(values), 'max': max(values)} for name, values in socket_capacities.items()},
        'evidenceSha256': {path.name: digest(path) for path in sorted(output.iterdir())
            if path.is_file() and path.name not in ('summary.json', 'README.md')}})
    print(f'Validated {expected_count} runs. Report: {output / "README.md"}', flush=True)


def run(host, payload_bytes):
    """Prepare both validated builds before measuring a balanced 27-case matrix."""
    for batch, port in PROFILES:
        verify_generator(host, batch, port, payload_bytes)
    output = ROOT / 'results/lan-controls' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    output.mkdir(parents=True)
    shutil.copyfile(Path(__file__), output / 'runner-source.py')
    environment = {'startedAtUtc': datetime.now(timezone.utc).isoformat(), 'host': host,
        'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'runnerSha256': digest(Path(__file__)), 'platform': platform.platform(),
        'cpu': next(line.split(':', 1)[1].strip() for line in Path('/proc/cpuinfo').read_text().splitlines() if line.startswith('model name')),
        'dotnetInfo': subprocess.check_output(['dotnet', '--info'], text=True),
        'route': subprocess.check_output(['ip', 'route', 'get', host], text=True),
        'sysctls': {name: Path('/proc/sys/' + name.replace('.', '/')).read_text().strip()
            for name in ('net.ipv4.tcp_moderate_rcvbuf', 'net.ipv4.tcp_rmem', 'net.core.rmem_max')},
        'profile': {'payloadBytes': payload_bytes, 'connections': CLIENT_COUNT, 'repetitions': RUN_COUNT,
            'warmupSeconds': WARMUP_SECONDS, 'measurementSeconds': DURATION_SECONDS, 'batchPorts': PROFILES},
        'generatorRelease': 'https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib',
        'generatorPackageSha256': 'bfdbf103d6040904cccd61fa77698c89b5038f37ecd59125abdc227ac628285b',
        'cases': []}
    interface = re.search(r'\bdev (\S+)', environment['route']).group(1)
    environment['interface'] = {'name': interface,
        'linkMbps': int(Path(f'/sys/class/net/{interface}/speed').read_text()),
        'duplex': Path(f'/sys/class/net/{interface}/duplex').read_text().strip()}
    write_json(output / 'environment.json', environment)
    print(f'Control evidence: {output}', flush=True)
    with tempfile.TemporaryDirectory(prefix='zero-alloc-lan-controls-') as temporary:
        builds = {}
        for variant in ('fixed', 'auto'):
            directory = Path(temporary) / variant; directory.mkdir()
            builds[variant] = prepare_variant(directory, variant == 'auto', output)
        changed = [name for name, value in builds['fixed']['sourceSha256'].items()
            if builds['auto']['sourceSha256'].get(name) != value]
        if changed != [str(CORE_SOURCE)]:
            raise RuntimeError(f'Unexpected source differences: {changed}')
        write_json(output / 'builds.json', builds)
        for repetition_index in range(RUN_COUNT):
            ordered_profiles = PROFILES[repetition_index:] + PROFILES[:repetition_index]
            for batch, port in ordered_profiles:
                offset = repetition_index % len(VARIANTS)
                order = VARIANTS[offset:] + VARIANTS[:offset]
                for variant in order:
                    case = measure_case(host, batch, port, variant, repetition_index + 1, builds, output, payload_bytes)
                    environment['cases'].append(case)
                    write_json(output / 'environment.json', environment)
                    validate_case(output, case, host, payload_bytes)
        environment['finishedAtUtc'] = datetime.now(timezone.utc).isoformat()
        write_json(output / 'environment.json', environment)
    summarize(output)


def resume(output):
    """Continue an interrupted observer run only after reproducing identical source and binary hashes."""
    environment = json.loads((output / 'environment.json').read_text())
    builds = json.loads((output / 'builds.json').read_text())
    payload_bytes = environment['profile']['payloadBytes']
    for batch, port in PROFILES:
        verify_generator(environment['host'], batch, port, payload_bytes)
    original_cases = len(environment['cases'])
    for case in environment['cases']:
        raw_snapshot = (output / f'{case["name"]}.tcp.txt').read_text()
        case['observedReceiveBufferBytes'] = established_receive_capacities(raw_snapshot)
        validate_case(output, case, environment['host'], payload_bytes)
    locations = {variant: Path(build['entryPoint']).parents[5] for variant, build in builds.items()}
    for variant, directory in locations.items():
        if directory.exists():
            raise RuntimeError(f'Refusing to replace existing isolated build: {directory}')
        directory.mkdir(parents=True)
        actual = prepare_variant(directory, variant == 'auto', output, log_suffix='-resume')
        if actual != builds[variant]:
            raise RuntimeError(f'{variant}: resumed source/binary hashes differ; measurement remains stopped')
    snapshot_name = 'resume-runner-source.py'
    shutil.copyfile(Path(__file__), output / snapshot_name)
    environment.setdefault('resumptions', []).append({'atUtc': datetime.now(timezone.utc).isoformat(),
        'completedCasesBeforeResume': original_cases, 'sourceAndBinaryHashesMatch': True,
        'reason': 'TCP observer now selects ESTAB entries and excludes lingering FIN-WAIT sockets.',
        'runnerSnapshot': snapshot_name, 'runnerSha256': digest(output / snapshot_name)})
    write_json(output / 'environment.json', environment)
    completed = {case['name'] for case in environment['cases']}
    try:
        for repetition_index in range(RUN_COUNT):
            ordered_profiles = PROFILES[repetition_index:] + PROFILES[:repetition_index]
            for batch, port in ordered_profiles:
                offset = repetition_index % len(VARIANTS)
                for variant in VARIANTS[offset:] + VARIANTS[:offset]:
                    stem = f'batch-{batch}-{variant}-{repetition_index + 1}'
                    if stem in completed:
                        continue
                    case = measure_case(environment['host'], batch, port, variant,
                        repetition_index + 1, builds, output, payload_bytes)
                    environment['cases'].append(case)
                    write_json(output / 'environment.json', environment)
                    validate_case(output, case, environment['host'], payload_bytes)
        environment['finishedAtUtc'] = datetime.now(timezone.utc).isoformat()
        write_json(output / 'environment.json', environment)
    finally:
        for directory in locations.values():
            shutil.rmtree(directory)
    summarize(output)


def main():
    """Run the experiment or independently validate/render a stored control bundle."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='mode', required=True)
    run_parser = commands.add_parser('run')
    run_parser.add_argument('host')
    run_parser.add_argument('--payload-bytes', type=int, choices=CONTROL_PAYLOAD_SIZES, default=DEFAULT_PAYLOAD_BYTES)
    commands.add_parser('summarize').add_argument('directory', type=Path)
    commands.add_parser('resume').add_argument('directory', type=Path)
    args = parser.parse_args()
    if args.mode == 'run':
        run(args.host, args.payload_bytes)
    elif args.mode == 'summarize':
        summarize(args.directory.resolve())
    else:
        resume(args.directory.resolve())


if __name__ == '__main__':
    main()
