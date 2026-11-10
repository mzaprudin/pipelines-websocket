#!/usr/bin/env python3
"""Run identical receiver processes against a managed localhost or external generator."""
import argparse
import hashlib
import ipaddress
import json
import math
import os
import platform
import queue
import subprocess
import threading
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROFILE = json.loads((ROOT / 'scripts/streaming-profile.json').read_text(encoding='utf-8'))
ENTRY_POINT = ROOT / 'benchmarks/WebSocketLoadTest/bin/Release/net10.0/WebSocketLoadTest.dll'
MEASUREMENT_PROTOCOL = 'receiver-process-periodic-v1'
STARTUP_TIMEOUT_SECONDS = 15
SHUTDOWN_TIMEOUT_SECONDS = 10
RECEIVER_ALLOWANCE_SECONDS = 30
POLL_INTERVAL_SECONDS = 0.1
READY_PREFIX = 'Listening: '
MAX_PAYLOAD_BYTES = 1_048_576
MAX_BATCH_BYTES = 16_777_216
ORIGINAL_PAYLOAD_BYTES = 87
MIN_DEPTH_BYTES = 128
SHORT_FRAME_MAX_BYTES = 125
EXTENDED_FRAME_MAX_BYTES = 65_535
MESSAGES_PER_MILLION = 1_000_000


def write_json(path, value):
    """Write complete metadata outside the measured receiver process."""
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def sha256(path):
    """Fingerprint source, binaries and raw reports without normalizing bytes."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def frame_header_bytes(payload_bytes):
    """Match the generator's unmasked frame-length encoding."""
    return 2 if payload_bytes <= SHORT_FRAME_MAX_BYTES else 4 if payload_bytes <= EXTENDED_FRAME_MAX_BYTES else 10


def validate_profile(profile):
    """Reject unsupported or unbounded profiles before starting child processes."""
    payload = profile['payloadBytes']
    if payload != ORIGINAL_PAYLOAD_BYTES and not MIN_DEPTH_BYTES <= payload <= MAX_PAYLOAD_BYTES:
        raise ValueError('Use 87 bytes or a 128–1048576-byte depth message.')
    if (profile['clientCount'] <= 0 or profile['durationSeconds'] <= 0 or profile['warmupSeconds'] < 0
            or profile['runsPerClient'] <= 0 or profile['framesPerBatch'] <= 0
            or profile['framesPerBatch'] > MAX_BATCH_BYTES // (payload + frame_header_bytes(payload))):
        raise ValueError('Require a positive, bounded profile and nonnegative warmup.')


def receiver_command(endpoint, profile, standard, report, entry_command=None):
    """Use the same receiver role and arguments for local and remote endpoints."""
    command = list(entry_command or ['dotnet', str(ENTRY_POINT)])
    command += ['--mode', 'client', '--url', endpoint,
                '--payload-bytes', str(profile['payloadBytes']), '--batch', str(profile['framesPerBatch']),
                '--count', str(profile['clientCount']), '--warmup', str(profile['warmupSeconds']),
                '--duration', str(profile['durationSeconds']), '--json', str(report)]
    if standard:
        command.append('--std')
    return command


class LocalGenerator:
    """Own an independent generator with bounded readiness and graceful stdin shutdown."""
    def __init__(self, output, profile, entry_command=None):
        self.command = list(entry_command or ['dotnet', str(ENTRY_POINT)])
        self.command += ['--mode', 'server', '--listen', '127.0.0.1', '--port', '0',
                         '--payload-bytes', str(profile['payloadBytes']),
                         '--batch', str(profile['framesPerBatch']), '--control-stdin']
        self.process = None
        self.reader = None
        self.endpoint = None
        self.log = (output / 'generator.log').open('w', encoding='utf-8')
        self.ready = queue.Queue()

    def start(self):
        """Wait for the actual bound endpoint, rejecting startup failures and timeouts."""
        self.process = subprocess.Popen(self.command, cwd=ROOT, stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                        text=True, encoding='utf-8', bufsize=1)
        self.reader = threading.Thread(target=self._read_output, daemon=True)
        self.reader.start()
        try:
            self.endpoint = self.ready.get(timeout=STARTUP_TIMEOUT_SECONDS)
            if self.endpoint is None:
                raise RuntimeError('Generator exited before announcing readiness; see generator.log.')
            self.ensure_running()
            return self.endpoint
        except queue.Empty as error:
            raise TimeoutError("Generator readiness timed out; see generator.log.") from error

    def _read_output(self):
        """Drain generator output so logging cannot block its network writes."""
        try:
            for line in self.process.stdout:
                self.log.write(line)
                self.log.flush()
                if line.startswith(READY_PREFIX):
                    self.ready.put(line[len(READY_PREFIX):].strip())
        finally:
            self.ready.put(None)

    def ensure_running(self):
        """A generator that exits during comparison invalidates the run."""
        if self.process.poll() is not None:
            raise RuntimeError('Local generator stopped during measurement; see generator.log.')

    def stop(self):
        """Observe shutdown; force termination only after the bounded grace period."""
        forced = False
        if self.process is not None:
            if self.process.poll() is None:
                try:
                    self.process.stdin.write('stop\n')
                    self.process.stdin.flush()
                    self.process.wait(timeout=SHUTDOWN_TIMEOUT_SECONDS)
                except (BrokenPipeError, OSError, subprocess.TimeoutExpired):
                    forced = True
                    self.process.kill()
                    self.process.wait(timeout=SHUTDOWN_TIMEOUT_SECONDS)
            if self.reader is not None:
                self.reader.join(timeout=SHUTDOWN_TIMEOUT_SECONDS)
            self.process.stdin.close()
            self.process.stdout.close()
        self.log.close()
        return {'exitCode': None if self.process is None else self.process.returncode,
                'forcedShutdown': forced}


def run_receiver(command, log_path, profile, generator=None):
    """Observe receiver and generator health, cleaning up a failed or timed-out receiver."""
    with log_path.open('w', encoding='utf-8') as log:
        process = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + profile['warmupSeconds'] + profile['durationSeconds'] + RECEIVER_ALLOWANCE_SECONDS
        try:
            while process.poll() is None:
                if generator is not None:
                    generator.ensure_running()
                if time.monotonic() >= deadline:
                    raise TimeoutError('Receiver exceeded its measurement/shutdown deadline.')
                time.sleep(POLL_INTERVAL_SECONDS)
            if process.returncode != 0:
                raise RuntimeError(f'Receiver exit {process.returncode}; see {log_path.name}.')
            if generator is not None:
                generator.ensure_running()
            return process.pid
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=SHUTDOWN_TIMEOUT_SECONDS)


def validate_report(path, profile, client, receiver_pid, endpoint):
    """Require the same receiver-only contract for either network topology."""
    run = json.loads(path.read_text(encoding='utf-8'))
    host = urlsplit(endpoint).hostname
    try:
        local = ipaddress.ip_address(host).is_loopback
    except ValueError:
        local = host == 'localhost'
    expected = {'ClientType': client, 'PayloadBytes': profile['payloadBytes'],
                'FramesPerBatch': profile['framesPerBatch'], 'ClientCount': profile['clientCount'],
                'WarmupSeconds': profile['warmupSeconds'], 'CpuScope': 'receiver-only',
                'ReceiverProcessId': receiver_pid, 'MeasurementProtocol': MEASUREMENT_PROTOCOL,
                'Topology': 'separate-process-loopback' if local else 'remote-client',
                'BatchBytes': (profile['payloadBytes'] + frame_header_bytes(profile['payloadBytes'])) * profile['framesPerBatch']}
    for field, value in expected.items():
        if run[field] != value:
            raise ValueError(f'{path.name}: {field} differs from the shared profile.')
    actual_endpoint = urlsplit(run['Endpoint'])
    requested_endpoint = urlsplit(endpoint)
    if (actual_endpoint.scheme != requested_endpoint.scheme or actual_endpoint.hostname != requested_endpoint.hostname
            or (actual_endpoint.port or 80) != (requested_endpoint.port or 80)
            or actual_endpoint.path != f'/stream/{profile["payloadBytes"]}/{profile["framesPerBatch"]}'
            or actual_endpoint.query or actual_endpoint.fragment):
        raise ValueError(f'{path.name}: receiver endpoint differs from generator profile.')
    if (run['DurationSeconds'] < profile['durationSeconds'] or run['Messages'] <= 0
            or any(not math.isfinite(run[field]) or run[field] < 0 for field in
                   ('AllocatedBytes', 'CpuMilliseconds', 'Gen0Collections', 'Gen1Collections', 'Gen2Collections'))):
        raise ValueError(f'{path.name}: incomplete measurement.')
    tolerance = 1e-10
    derived = {'MessagesPerSecond': run['Messages'] / run['DurationSeconds'],
               'MegabitsPerSecond': run['Bytes'] * 8 / 1_000_000 / run['DurationSeconds'],
               'CpuMillisecondsPerMillionMessages': run['CpuMilliseconds'] * MESSAGES_PER_MILLION / run['Messages'],
               'AllocatedBytesPerMessage': run['AllocatedBytes'] / run['Messages'],
               'CpuCorePercent': run['CpuMilliseconds'] / (run['DurationSeconds'] * 1_000) * 100}
    for field, value in derived.items():
        if not math.isclose(run[field], value, rel_tol=tolerance, abs_tol=tolerance):
            raise ValueError(f'{path.name}: invalid {field}.')
    skew_limit = 2 * profile['clientCount'] * profile['payloadBytes']
    if abs(run['Bytes'] - run['Messages'] * profile['payloadBytes']) > skew_limit:
        raise ValueError(f'{path.name}: inconsistent message/byte counters.')
    previous = 0
    if not run['Samples']:
        raise ValueError(f'{path.name}: missing samples.')
    for sample in run['Samples']:
        interval = sample['ElapsedSeconds'] - previous
        if (not math.isfinite(interval) or interval <= 0 or min(sample['Messages'], sample['Bytes']) < 0
                or abs(sample['Bytes'] - sample['Messages'] * profile['payloadBytes']) > skew_limit):
            raise ValueError(f'{path.name}: invalid sample.')
        if not math.isclose(sample['MessagesPerSecond'], sample['Messages'] / interval, rel_tol=tolerance):
            raise ValueError(f'{path.name}: invalid sample rate.')
        previous = sample['ElapsedSeconds']
    if not math.isclose(previous, run['DurationSeconds'], rel_tol=tolerance):
        raise ValueError(f'{path.name}: inconsistent final sample time.')
    for field in ('Messages', 'Bytes'):
        if sum(sample[field] for sample in run['Samples']) != run[field]:
            raise ValueError(f'{path.name}: sample totals differ.')
    log = path.with_suffix('.log').read_text(encoding='utf-8')
    if 'Avg Msg/s:' not in log or 'Streaming benchmark failed:' in log:
        raise ValueError(f'{path.name}: unsuccessful workload log.')
    return run


def run_streaming(output, profile, endpoint=None):
    """Alternate fresh receiver processes against one persistent local or remote generator."""
    validate_profile(profile)
    output.mkdir(parents=True, exist_ok=True)
    if (output / 'streaming-session.json').exists():
        raise ValueError('Refusing to overwrite a streaming session.')
    generator = None if endpoint else LocalGenerator(output, profile)
    session = {'status': 'running', 'protocol': MEASUREMENT_PROTOCOL, 'scope': 'receiver-only',
               'profile': profile, 'runnerProcessId': os.getpid(), 'receivers': []}
    write_json(output / 'streaming-profile.json', profile)
    try:
        if generator is not None:
            endpoint = generator.start()
            session['generator'] = {'command': generator.command, 'processId': generator.process.pid}
        else:
            parsed = urlsplit(endpoint)
            if (parsed.scheme != 'ws' or not parsed.hostname or parsed.username or parsed.password
                    or parsed.query or parsed.fragment or not 0 < (parsed.port or 80) <= 65_535):
                raise ValueError('Use a direct ws://HOST:PORT generator endpoint without credentials.')
            session['generator'] = {'externalEndpoint': endpoint}
        session['endpoint'] = endpoint
        write_json(output / 'streaming-session.json', session)
        for repetition in range(1, profile['runsPerClient'] + 1):
            clients = ('standard', 'pipeline') if repetition % 2 else ('pipeline', 'standard')
            for client in clients:
                stem = f'websocket-{client}-{repetition}'
                report = output / (stem + '.json')
                command = receiver_command(endpoint, profile, client == 'standard', report)
                print(f'Measuring {client} {repetition}/{profile["runsPerClient"]}, receiver-only.', flush=True)
                pid = run_receiver(command, output / (stem + '.log'), profile, generator)
                if generator is not None and pid == generator.process.pid:
                    raise ValueError('Receiver must not share the generator process.')
                validate_report(report, profile, client, pid, endpoint)
                session['receivers'].append({'client': client, 'repetition': repetition,
                                             'processId': pid, 'command': command, 'exitCode': 0,
                                             'report': report.name})
                write_json(output / 'streaming-session.json', session)
        session['status'] = 'completed'
    except BaseException as error:
        session['status'] = 'failed'
        session['error'] = str(error)
        raise
    finally:
        if generator is not None:
            shutdown = generator.stop()
            session.setdefault('generator', {}).update(shutdown)
            if session['status'] == 'completed' and (shutdown['exitCode'] != 0 or shutdown['forcedShutdown']):
                session['status'] = 'failed'
                write_json(output / 'streaming-session.json', session)
                raise RuntimeError('Local generator did not stop cleanly.')
        write_json(output / 'streaming-session.json', session)


def validate_session(output):
    """Reject partial runs, shared roles, changed commands and failed child shutdown."""
    session = json.loads((output / 'streaming-session.json').read_text(encoding='utf-8'))
    profile = json.loads((output / 'streaming-profile.json').read_text(encoding='utf-8'))
    validate_profile(profile)
    if (session['status'] != 'completed' or session['profile'] != profile
            or session['protocol'] != MEASUREMENT_PROTOCOL or session['scope'] != 'receiver-only'):
        raise ValueError('Incomplete or inconsistent streaming session.')
    generator = session['generator']
    local = 'processId' in generator
    if not local and generator['externalEndpoint'] != session['endpoint']:
        raise ValueError('External generator endpoint differs from receiver endpoint.')
    if local and (generator['processId'] <= 0 or generator['processId'] == session['runnerProcessId']
                  or generator['exitCode'] != 0 or generator['forcedShutdown']):
        raise ValueError('Generator did not shut down cleanly.')
    expected = [(client, repetition) for repetition in range(1, profile['runsPerClient'] + 1)
                for client in (('standard', 'pipeline') if repetition % 2 else ('pipeline', 'standard'))]
    actual = [(item['client'], item['repetition']) for item in session['receivers']]
    if actual != expected:
        raise ValueError('Missing, duplicate or reordered streaming receivers.')
    groups = {'standard': [], 'pipeline': []}
    for item in session['receivers']:
        client, repetition = item['client'], item['repetition']
        report = output / f'websocket-{client}-{repetition}.json'
        command = receiver_command(session['endpoint'], profile, client == 'standard', report)
        recorded = item['command']
        if len(recorded) != len(command) or not recorded[1].replace('\\', '/').endswith('benchmarks/WebSocketLoadTest/bin/Release/net10.0/WebSocketLoadTest.dll'):
            raise ValueError('Unexpected receiver entry point.')
        command[1] = recorded[1]
        # Output directories can move after publication: the recorded report path remains absolute.
        if (item['report'] != report.name or item['exitCode'] != 0 or item['processId'] <= 0
                or (local and item['processId'] in (generator['processId'], session['runnerProcessId']))
                or item['command'][:-2 if client == 'standard' else -1] != command[:-2 if client == 'standard' else -1]
                or Path(item['command'][-2 if client == 'standard' else -1]).name != report.name
                or (client == 'standard' and item['command'][-1] != '--std')):
            raise ValueError('Invalid receiver process or command provenance.')
        groups[client].append(validate_report(report, profile, client, item['processId'], session['endpoint']))
    return profile, groups


def capture_context(output, scope='receiver-only'):
    """Keep exact commands, host and source/binary identities for reproduction."""
    paths = subprocess.check_output(['git', 'ls-files'], cwd=ROOT, text=True, encoding='utf-8').splitlines()
    write_json(output / 'source-snapshot.json', {
        'revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'status': subprocess.check_output(['git', 'status', '--porcelain', '--', '.', ':(exclude)results', ':(exclude)artifacts'], cwd=ROOT, text=True).strip(),
        'statusExcludes': ['results/', 'artifacts/'],
        'sha256': {name: sha256(ROOT / name) for name in paths if (ROOT / name).is_file() and not name.startswith('results/')}})
    write_json(output / 'environment.json', {'utc': datetime.now(timezone.utc).isoformat(),
        'platform': platform.platform(), 'cpu': Path('/proc/cpuinfo').read_text(encoding='utf-8') if Path('/proc/cpuinfo').exists() else platform.processor(),
        'dotnet': subprocess.check_output(['dotnet', '--info'], cwd=ROOT, text=True, encoding='utf-8'),
        'scope': scope, 'measurementProtocol': MEASUREMENT_PROTOCOL})
    write_json(output / 'binaries.json', {str(path.relative_to(ROOT)): sha256(path)
        for path in sorted(ROOT.glob('**/bin/Release/net10.0/*')) if path.is_file()})


def write_artifact_manifest(output):
    """Seal the raw evidence after all child processes have stopped."""
    write_json(output / 'artifacts.sha256.json', {str(path.relative_to(output)): sha256(path)
        for path in sorted(output.rglob('*')) if path.is_file() and path.name != 'artifacts.sha256.json'})


def main():
    """Only the endpoint differs between the default local comparison and a LAN run."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url')
    parser.add_argument('--output', type=Path, default=Path('results/local') / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
    parser.add_argument('--no-build', action='store_true')
    for flag, key in [('payload-bytes', 'payloadBytes'), ('batch', 'framesPerBatch'), ('count', 'clientCount'),
                      ('warmup', 'warmupSeconds'), ('duration', 'durationSeconds'), ('runs', 'runsPerClient')]:
        parser.add_argument('--' + flag, type=int, default=DEFAULT_PROFILE[key], dest=key)
    args = parser.parse_args()
    profile = {key: getattr(args, key) for key in DEFAULT_PROFILE}
    validate_profile(profile)
    output = (ROOT / args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    commands = []
    if not args.no_build:
        commands = [['dotnet', 'restore', 'Pipelines.WebSockets.sln', '--locked-mode'],
                    ['dotnet', 'build', 'Pipelines.WebSockets.sln', '-c', 'Release', '--no-restore']]
        for command in commands:
            subprocess.run(command, cwd=ROOT, check=True)
    write_json(output / 'commands.json', commands)
    capture_context(output)
    run_streaming(output, profile, args.url)
    validate_session(output)
    write_artifact_manifest(output)
    print(f'COMPLETE: {output}', flush=True)


if __name__ == '__main__':
    main()
