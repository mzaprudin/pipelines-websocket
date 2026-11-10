#!/usr/bin/env python3
"""Measure echo and receiver-only streaming sequentially, retaining complete evidence."""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import subprocess
from streaming_benchmarks import (ROOT, DEFAULT_PROFILE, capture_context, run_streaming,
                                  validate_session, write_artifact_manifest, write_json)


def main():
    """Use the common streaming profile; only --url changes the generator location."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', help='External streaming generator; omitted starts a separate localhost process.')
    parser.add_argument('--output', type=Path, default=Path('results/local') / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
    args = parser.parse_args()
    output = (ROOT / args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    commands = []

    def run(command, name):
        """Keep exact invocations and stdout, failing before any partial publication."""
        commands.append(command)
        write_json(output / 'commands.json', commands)
        print(datetime.now(timezone.utc).isoformat(), name, flush=True)
        with (output / (name + '.log')).open('w', encoding='utf-8') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)

    run(['dotnet', 'restore', 'Pipelines.WebSockets.sln', '--locked-mode'], 'restore')
    run(['dotnet', 'build', 'Pipelines.WebSockets.sln', '-c', 'Release', '--no-restore'], 'build')
    capture_context(output, 'streaming: receiver-only; echo: both peers and harness')
    run(['dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'benchmarks/Pipelines.WebSockets.Benchmarks',
         '--', '--filter', '*', '--warmupCount', '5', '--iterationCount', '15', '--iterationTime', '500',
         '--launchCount', '1', '--exporters', 'json', '--artifacts', str(output / 'bdn')], 'benchmark')
    run_streaming(output, DEFAULT_PROFILE, args.url)
    validate_session(output)
    write_artifact_manifest(output)
    print(f'COMPLETE: {output}', flush=True)


if __name__ == '__main__':
    main()
