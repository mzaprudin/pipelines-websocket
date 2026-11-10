"""Guard CI routing against mixed changes, full push ranges and disguised renames."""
import subprocess
from pathlib import Path
import tempfile
import unittest
from ci_changes import changed_paths, needs_full_checks


class ScopeTests(unittest.TestCase):
    """Documentation is cheap to validate; executable and unknown inputs retain full CI."""
    def test_documentation_and_rendering(self):
        for name in ('README.md', 'AGENTS.md', 'docs/benchmark-methodology.ru.md',
                     'docs/split-provenance.json', 'results/local/run/report.json',
                     'scripts/render-results.py', 'scripts/verify-evidence.py', 'scripts/verify-docs.py'):
            with self.subTest(path=name):
                self.assertFalse(needs_full_checks([name]))

    def test_runtime_build_workflows_and_unknown_paths(self):
        for name in ('src/Pipelines.WebSockets/Client.cs', 'tests/Tests.cs', 'support/Peer.cs',
                     'samples/Demo.cs', 'benchmarks/Load.cs', 'global.json', 'Directory.Build.props',
                     'Pipelines.WebSockets.sln', 'src/packages.lock.json', '.github/workflows/ci.yml',
                     'scripts/ci_changes.py', 'scripts/streaming_benchmarks.py', 'new-input.dat'):
            with self.subTest(path=name):
                self.assertTrue(needs_full_checks(['README.md', name]))

    def test_empty_diff(self):
        self.assertFalse(needs_full_checks([]))

    def test_manual_and_new_branch_require_full(self):
        self.assertIsNone(changed_paths('workflow_dispatch', {}))
        self.assertIsNone(changed_paths('push', {'before': '0' * 40, 'after': 'a' * 40}))


class GitDiffTests(unittest.TestCase):
    """Use real commits to verify event-range semantics independently of the classifier."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.git('init', '-q')
        self.git('config', 'user.name', 'CI routing test')
        self.git('config', 'user.email', 'ci@example.invalid')
        (self.root / 'src').mkdir()
        (self.root / 'src/Client.cs').write_text('original\n', encoding='utf-8')
        self.base = self.commit()

    def tearDown(self):
        self.temporary.cleanup()

    def git(self, *arguments):
        return subprocess.check_output(['git', *arguments], cwd=self.root, stderr=subprocess.STDOUT, text=True).strip()

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-qm', 'test fixture')
        return self.git('rev-parse', 'HEAD')

    def event(self, head):
        return {'before': self.base, 'after': head}

    def test_complete_push_range_includes_earlier_code_change(self):
        (self.root / 'src/Client.cs').write_text('changed\n', encoding='utf-8')
        self.commit()
        (self.root / 'README.md').write_text('docs\n', encoding='utf-8')
        head = self.commit()
        paths = changed_paths('push', self.event(head), self.root)
        self.assertEqual({'src/Client.cs', 'README.md'}, set(paths))
        self.assertTrue(needs_full_checks(paths))

    def test_rename_into_docs_keeps_deleted_runtime_path(self):
        (self.root / 'docs').mkdir()
        self.git('mv', 'src/Client.cs', 'docs/Client.md')
        head = self.commit()
        paths = changed_paths('push', self.event(head), self.root)
        self.assertEqual({'src/Client.cs', 'docs/Client.md'}, set(paths))
        self.assertTrue(needs_full_checks(paths))

    def test_pr_uses_merge_base(self):
        self.git('checkout', '-qb', 'feature')
        (self.root / 'README.md').write_text('docs\n', encoding='utf-8')
        head = self.commit()
        self.git('checkout', '-q', '--detach', self.base)
        (self.root / 'src/Client.cs').write_text('base branch change\n', encoding='utf-8')
        base_tip = self.commit()
        event = {'pull_request': {'base': {'sha': base_tip}, 'head': {'sha': head}}}
        self.assertEqual(['README.md'], changed_paths('pull_request', event, self.root))


if __name__ == '__main__':
    unittest.main()
