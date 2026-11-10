#!/usr/bin/env python3
"""Build and zip the portable generator from isolated sources, preserving repository restore state."""

import hashlib
import json
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'artifacts' / 'lan-generator-win-x64'
PROFILE = json.loads((ROOT / 'scripts/streaming-profile.json').read_text(encoding='utf-8'))


def sha256(path):
    """Hash exact build inputs or binary bytes."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    """Publish a self-contained Windows executable and a ready-to-run launcher."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='zero-alloc-lan-build-') as temporary:
        isolated = Path(temporary)
        for filename in ('Directory.Build.props', 'global.json'):
            shutil.copyfile(ROOT / filename, isolated / filename)
        for relative in ('src/Pipelines.WebSockets', 'benchmarks/WebSocketLoadTest'):
            shutil.copytree(ROOT / relative, isolated / relative,
                            ignore=shutil.ignore_patterns('bin', 'obj', 'packages.lock.json'))
        sources = {str(path.relative_to(isolated)): sha256(path) for path in sorted(isolated.rglob('*')) if path.is_file()}
        command = ['dotnet', 'publish', str(isolated / 'benchmarks/WebSocketLoadTest/WebSocketLoadTest.csproj'),
                   '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
                   '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishTrimmed=false',
                   '-p:RestorePackagesWithLockFile=false', '-o', str(OUTPUT / 'publish')]
        subprocess.run(command, check=True, cwd=ROOT)
        executable = OUTPUT / 'publish' / 'WebSocketLoadTest.exe'
        manifest = {'runtime': 'win-x64', 'selfContained': True, 'nativeLibrariesBundled': True,
                    'dotnetSdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(), 'payloadBytes': PROFILE['payloadBytes'], 'framesPerBatch': PROFILE['framesPerBatch'], 'streamingProfile': PROFILE,
                    'executableSha256': sha256(executable), 'sourceSha256': sources, 'publishCommand': command}
        launcher = f'@echo off\r\n"%~dp0WebSocketLoadTest.exe" --mode server --listen 0.0.0.0 --port 5050 --payload-bytes {PROFILE["payloadBytes"]} --batch {PROFILE["framesPerBatch"]}\r\npause\r\n'
        readme = ('Run START-GENERATOR.cmd. The executable includes .NET; no SDK install is required.\r\n'
                  f'Generator: TCP port 5050, {PROFILE["payloadBytes"]}-byte JSON messages, {PROFILE["framesPerBatch"]} frames per write.\r\n'
                  'Allow access on the private Ethernet network if Windows asks. Stop with Ctrl+C.\r\n'
                  'Keep it running while the Linux receiver benchmarks both implementations.\r\n')
        archive = OUTPUT / 'pipelines-websocket-generator-win-x64.zip'
        with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
            bundle.write(executable, 'WebSocketLoadTest.exe')
            bundle.writestr('START-GENERATOR.cmd', launcher)
            bundle.writestr('README.txt', readme)
            bundle.writestr('manifest.json', json.dumps(manifest, indent=2) + '\n')
        (OUTPUT / 'package-sha256.txt').write_text(f'{sha256(archive)}  {archive.name}\n', encoding='utf-8')
        print(archive)


if __name__ == '__main__':
    main()
