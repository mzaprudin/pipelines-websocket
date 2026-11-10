#!/usr/bin/env python3
"""Validate documentation, generated measurements and evidence without a .NET build."""
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
# These imported audit records retain links to the upstream plan in their measured revision.
HISTORICAL_LINKS = frozenset((f'docs/{name}', '../PLAN.md') for name in
                            ('lan-acceptance-audit.md', 'lan-controls-audit.md', 'lan-tooling-audit.md'))
LINK_PATTERN = re.compile(r'!?\[[^\]]*\]\((<[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\)')


def main():
    """Check active Markdown and Python tooling; leave archived reports byte-identical."""
    for path in sorted((ROOT / 'scripts').glob('*.py')):
        compile(path.read_text(encoding='utf-8'), str(path), 'exec')
    documents = sorted(ROOT.glob('*.md')) + sorted((ROOT / 'docs').glob('*.md'))
    for path in documents:
        relative = path.relative_to(ROOT).as_posix()
        content = path.read_text(encoding='utf-8')
        if not content.endswith('\n'):
            raise ValueError(f'{relative}: missing final newline')
        for number, line in enumerate(content.splitlines(), 1):
            # Markdown's two-space hard break is intentional formatting.
            if line.endswith('\t') or (line.endswith(' ') and not line.endswith('  ')):
                raise ValueError(f'{relative}:{number}: trailing whitespace')
        for target in LINK_PATTERN.findall(content):
            target = target.strip('<>')
            if (relative, target) in HISTORICAL_LINKS:
                continue
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            if not (path.parent / unquote(parsed.path)).exists():
                raise ValueError(f'{relative}: missing local link {target}')
    subprocess.run([sys.executable, str(ROOT / 'scripts/verify-evidence.py')], cwd=ROOT, check=True)
    print(f'PASS: {len(documents)} Markdown files, local links, Python syntax and generated evidence.')


if __name__ == '__main__':
    main()
