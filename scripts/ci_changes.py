#!/usr/bin/env python3
"""Select lightweight documentation checks or the complete runtime matrix."""
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PRESENTATION_SCRIPTS = frozenset(('scripts/render-results.py', 'scripts/verify-evidence.py', 'scripts/verify-docs.py'))
SHA_PATTERN = re.compile(r'[0-9a-f]{40,64}')


def needs_full_checks(paths):
    """An explicit documentation allowlist leaves new or unknown inputs on full CI."""
    for name in paths:
        path = PurePosixPath(name)
        if (path.suffix == '.md' or name.startswith(('docs/', 'results/'))
                or name in PRESENTATION_SCRIPTS):
            continue
        return True
    return False


def changed_paths(event_name, event, root=ROOT):
    """Include the entire push or PR diff, treating renames as deletion plus addition."""
    if event_name == 'push':
        base, head = event.get('before', ''), event.get('after', '')
    elif event_name == 'pull_request':
        request = event['pull_request']
        base, head = request['base']['sha'], request['head']['sha']
    else:
        return None
    if any(not SHA_PATTERN.fullmatch(sha) or set(sha) == {'0'} for sha in (base, head)):
        return None
    try:
        if event_name == 'pull_request':
            base = subprocess.check_output(['git', 'merge-base', base, head], cwd=root, text=True).strip()
        output = subprocess.check_output(['git', 'diff', '--no-renames', '--name-only', '-z', base, head, '--'], cwd=root)
    except subprocess.CalledProcessError:
        return None
    return [name.decode('utf-8', errors='surrogateescape') for name in output.split(b'\0') if name]


def main():
    """Manual runs and unavailable history require full validation rather than guessing."""
    event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text(encoding='utf-8'))
    paths = changed_paths(os.environ['GITHUB_EVENT_NAME'], event)
    full = paths is None or needs_full_checks(paths)
    print(json.dumps({'full': full, 'paths': paths, 'fallback': paths is None}, indent=2))
    with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
        output.write(f'full={str(full).lower()}\n')


if __name__ == '__main__':
    main()
