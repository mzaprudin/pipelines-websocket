"""Exercise independent-process ownership and reject corrupted measurement evidence."""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import streaming_benchmarks as runner


class ProcessLifecycleTests(unittest.TestCase):
    """Readiness failures and receiver failures must leave no child running."""
    def test_generator_clean_shutdown(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            fake = output / 'fake.py'
            fake.write_text("print('Listening: ws://127.0.0.1:5050', flush=True)\ninput()\n", encoding='utf-8')
            generator = runner.LocalGenerator(output, runner.DEFAULT_PROFILE, [sys.executable, str(fake)])
            try:
                self.assertEqual('ws://127.0.0.1:5050', generator.start())
            finally:
                shutdown = generator.stop()
            self.assertEqual({'exitCode': 0, 'forcedShutdown': False}, shutdown)

    def test_missing_readiness_stops_child(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            fake = output / 'fake.py'
            fake.write_text('input()\n', encoding='utf-8')
            generator = runner.LocalGenerator(output, runner.DEFAULT_PROFILE, [sys.executable, str(fake)])
            try:
                with patch.object(runner, 'STARTUP_TIMEOUT_SECONDS', 0.2):
                    with self.assertRaises(TimeoutError):
                        generator.start()
            finally:
                generator.stop()
            self.assertIsNotNone(generator.process.poll())

    def test_generator_failure_kills_active_receiver(self):
        children = []
        real_popen = subprocess.Popen
        def start(*args, **kwargs):
            process = real_popen(*args, **kwargs)
            children.append(process)
            return process
        class FailedGenerator:
            def ensure_running(self):
                raise RuntimeError('generator failed')
        with tempfile.TemporaryDirectory() as directory, patch.object(runner.subprocess, 'Popen', start):
            with self.assertRaisesRegex(RuntimeError, 'generator failed'):
                runner.run_receiver([sys.executable, '-c', 'import time; time.sleep(60)'],
                                    Path(directory) / 'receiver.log', runner.DEFAULT_PROFILE, FailedGenerator())
        self.assertEqual(1, len(children))
        self.assertIsNotNone(children[0].poll())


class EvidenceTests(unittest.TestCase):
    """Validate actual receiver reports, then prove damaged sessions are rejected."""
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.output = Path(cls.temporary.name)
        cls.profile = dict(runner.DEFAULT_PROFILE, clientCount=1, warmupSeconds=0, durationSeconds=1, runsPerClient=1)
        runner.run_streaming(cls.output, cls.profile)
        cls.originals = {path: path.read_bytes() for path in cls.output.glob('*.json')}

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def tearDown(self):
        for path, content in self.originals.items():
            path.write_bytes(content)

    def test_valid_local_session(self):
        profile, groups = runner.validate_session(self.output)
        self.assertEqual(self.profile, profile)
        self.assertEqual({'standard', 'pipeline'}, set(groups))

    def test_explicit_endpoint_uses_same_receiver_profile(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            generator = runner.LocalGenerator(output, self.profile)
            try:
                endpoint = generator.start()
                runner.run_streaming(output, self.profile, endpoint)
                profile, groups = runner.validate_session(output)
                self.assertEqual(self.profile, profile)
                self.assertTrue(all(run['CpuScope'] == 'receiver-only' for values in groups.values() for run in values))
            finally:
                self.assertEqual(0, generator.stop()['exitCode'])

    def test_scope_pid_endpoint_and_sample_corruption_rejected(self):
        path = self.output / 'websocket-standard-1.json'
        original = json.loads(path.read_text(encoding='utf-8'))
        corruptions = [('CpuScope', 'generator-and-clients'), ('ReceiverProcessId', 0),
                       ('Endpoint', 'ws://127.0.0.1:1/stream/128/20'), ('MessagesPerSecond', 1),
                       ('AllocatedBytes', -1), ('DurationSeconds', float('nan'))]
        for field, value in corruptions:
            with self.subTest(field=field):
                damaged = copy.deepcopy(original)
                damaged[field] = value
                runner.write_json(path, damaged)
                with self.assertRaises(ValueError):
                    runner.validate_session(self.output)
        damaged = copy.deepcopy(original)
        damaged['Samples'][0]['Bytes'] += 100_000
        runner.write_json(path, damaged)
        with self.assertRaises(ValueError):
            runner.validate_session(self.output)

    def test_incomplete_session_or_failed_shutdown_rejected(self):
        path = self.output / 'streaming-session.json'
        original = json.loads(path.read_text(encoding='utf-8'))
        for damage in ('missing receiver', 'forced shutdown', 'failed session'):
            with self.subTest(damage=damage):
                value = copy.deepcopy(original)
                if damage == 'missing receiver':
                    value['receivers'].pop()
                elif damage == 'forced shutdown':
                    value['generator']['forcedShutdown'] = True
                else:
                    value['status'] = 'failed'
                runner.write_json(path, value)
                with self.assertRaises(ValueError):
                    runner.validate_session(self.output)


if __name__ == '__main__':
    unittest.main()
