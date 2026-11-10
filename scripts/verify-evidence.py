#!/usr/bin/env python3
"""Read-only acceptance checks for imported files, licenses and measured artifacts."""
import hashlib,importlib.util,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def verify_paired_run(directory, module):
    """Require complete matched sessions, frozen binaries, sources and artifact hashes."""
    manifests = [directory/'artifacts.sha256.json', directory/'ethernet/artifacts.sha256.json',
                 directory/'loopback/artifacts.sha256.json']
    for path in manifests:
        for relative, expected in json.loads(path.read_text(encoding='utf-8')).items():
            if digest(path.parent/relative) != expected:
                raise ValueError(f'Changed paired artifact: {path.parent/relative}')
    binaries = [json.loads((directory/subdir/'binaries.json').read_text(encoding='utf-8'))
                for subdir in ('', 'ethernet', 'loopback')]
    if not binaries[0] or any(value != binaries[0] for value in binaries[1:]):
        raise ValueError('Matched streaming binaries differ')
    snapshots = [json.loads((directory/subdir/'source-snapshot.json').read_text(encoding='utf-8'))
                 for subdir in ('', 'ethernet', 'loopback')]
    if any(value != snapshots[0] for value in snapshots[1:]) or snapshots[0]['status']:
        raise ValueError('Matched source identities differ or contain uncommitted inputs')
    for relative, expected in snapshots[0]['sha256'].items():
        if relative.endswith(('.cs', '.csproj', '.props', 'packages.lock.json')) and digest(ROOT/relative) != expected:
            raise ValueError(f'Runtime input differs from paired source: {relative}')
    if module.paired_summary(directory) != json.loads((directory/'summary.json').read_text(encoding='utf-8')):
        raise ValueError('Paired summary differs from raw reports')


def main():
    """Fail on changed provenance, historical evidence or incomplete published runs."""
    provenance=json.loads((ROOT/'docs/split-provenance.json').read_text(encoding='utf-8'))
    inventory=json.loads((ROOT/'docs/upstream-inventory.json').read_text(encoding='utf-8'))['sha256']
    for item in provenance['files']:
        if item['sourceSha256']!=inventory[item['source']]:raise ValueError(f'Invalid upstream hash: {item["source"]}')
        if digest(ROOT/item['destination'])!=item['destinationSha256']:raise ValueError(f'Changed imported file: {item["destination"]}')
        if item['source'].startswith('tests/') and item['destinationSha256']!=item['sourceSha256'] and not item.get('adaptation'):
            raise ValueError(f'Undocumented test adaptation: {item["destination"]}')
        if item['source'].startswith('results/') or item['source']=='NOTICE':
            if item['destinationSha256']!=item['sourceSha256']:raise ValueError(f'Changed tests/evidence/license: {item["destination"]}')
    for relative, expected in json.loads((ROOT/'results/history.sha256.json').read_text(encoding='utf-8')).items():
        if digest(ROOT/relative)!=expected:raise ValueError(f'Changed historical evidence: {relative}')
    projects=list((ROOT/'src').rglob('*.csproj'))
    for p in projects:
        if 'ProjectReference' in p.read_text(encoding='utf-8'):raise ValueError(f'Library depends on another project: {p}')
    helpers=list((ROOT/'src').rglob('PoolRental.cs'))
    if len(helpers)!=1 or 'internal struct PoolRental' not in helpers[0].read_text(encoding='utf-8'):raise ValueError('PoolRental must be internal')
    spec=importlib.util.spec_from_file_location('measurements',ROOT/'scripts/render-results.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    pointer=ROOT/'results/local/published-run.json'
    if not pointer.exists():raise ValueError('Missing published local run')
    run=ROOT/json.loads(pointer.read_text(encoding='utf-8'))['directory']
    manifest_paths=[run/'artifacts.sha256.json']
    if (run/'baseline').exists():manifest_paths.append(run/'baseline/artifacts.sha256.json')
    for path in manifest_paths:
        manifest=json.loads(path.read_text(encoding='utf-8'))
        for rel,expected in manifest.items():
            if digest(path.parent/rel)!=expected:raise ValueError(f'Changed measured artifact: {path.parent/rel}')
    snapshot=json.loads((run/'source-snapshot.json').read_text(encoding='utf-8'))['sha256']
    for relative,expected in snapshot.items():
        if relative.endswith(('.cs','.csproj','.props','packages.lock.json')) and digest(ROOT/relative)!=expected:
            raise ValueError(f'Runtime build input differs from measured sources: {relative}')
    after=module.reports(run);before=module.reports(run/'baseline') if (run/'baseline').exists() else []
    streams=module.streaming(run) if module.KIND=='websocket' else None
    paired=module.paired_directory()
    if paired:verify_paired_run(paired,module)
    streams=module.displayed_streaming(streams)
    for filename,russian in [('README.md',False),('README.ru.md',True)]:
        section=(ROOT/filename).read_text(encoding='utf-8').split('<!-- LOCAL_RESULTS_START -->')[1].split('<!-- LOCAL_RESULTS_END -->')[0].strip()
        if section!=module.render_local(streams,russian).strip():
            raise ValueError(f'Generated benchmark table differs from measured reports: {filename}')
        if module.KIND=='websocket':
            methodology=ROOT/'docs'/('benchmark-methodology.ru.md' if russian else 'benchmark-methodology.md')
            echo=methodology.read_text(encoding='utf-8').split('<!-- ECHO_RESULTS_START -->')[1].split('<!-- ECHO_RESULTS_END -->')[0].strip()
            if echo!=module.render_echo(after,russian).strip():
                raise ValueError(f'Generated echo table differs from measured reports: {methodology.name}')
            ethernet=(ROOT/filename).read_text(encoding='utf-8').split('<!-- ETHERNET_RESULTS_START -->')[1].split('<!-- ETHERNET_RESULTS_END -->')[0].strip()
            if ethernet!=module.render_ethernet(russian).strip():
                raise ValueError(f'Generated Ethernet table differs from measured reports: {filename}')
    if module.KIND=='websocket' and (run/'baseline').exists():module.streaming(run/'baseline')
    print(f'PASS: {len(provenance["files"])} mapped files, documented test adaptations and unchanged historical reports, {len(before)} + {len(after)} measured cases; independent library, internal pool helper.')
if __name__=='__main__':main()
