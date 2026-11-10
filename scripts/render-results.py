#!/usr/bin/env python3
"""Validate local measurements and generate README tables from unrounded reports."""
import argparse,json,math,statistics,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from streaming_benchmarks import validate_session
KIND='websocket'
EXPECTED_CASES=22 if KIND=='json' else 4
ZERO_METHODS={'JsonPopulator_Class_Reused_Interned','JsonPopulator_Struct_Ref','Gate_JsonPopulator_FastList_Reused_Interned','Gate_JsonPopulator_FastList_Reused_Interned_Struct','Standard_Serialize_Writer','JsonPushulator_Class','JsonPushulator_Struct','Manual_Utf8JsonWriter_Direct','Route10kMessages_JsonReader','Route10kMessages_Custom','Route10kMessages_Regex','PopulatorReusedInterned','SystemTextJsonGenerated','PushulatorReusedWriter'}

MEASUREMENT_ITERATIONS=15
WARMUP_ITERATIONS=5
METHOD_SETS={
    'PopulatorBenchmarks': {'Standard_Deserialize_Class','JsonPopulator_Class_Reused_Interned','JsonPopulator_Struct_Ref','Gate_Standard_Deserialize_List','Gate_JsonPopulator_FastList_Reused_Interned','Gate_JsonPopulator_FastList_Reused_Interned_Struct'},
    'PushulatorBenchmarks': {'Standard_Serialize_Class','Standard_Serialize_Writer','JsonPushulator_Class','JsonPushulator_Struct','Manual_Utf8JsonWriter_Direct'},
    'RouterBenchmarks': {'Route10kMessages_JsonReader','Route10kMessages_Custom','Route10kMessages_Regex'},
    'JsonParsingBenchmarks': {'SystemTextJson','SystemTextJsonGenerated','PopulatorNewObject','PopulatorReusedObject','PopulatorReusedInterned'},
    'JsonWritingBenchmarks': {'SystemTextJson','SystemTextJsonGenerated','PushulatorReusedWriter'},
}
EXPECTED_IDENTITIES=({(typename,method,'') for typename,methods in METHOD_SETS.items() for method in methods} if KIND=='json' else
    {('WebSocketRoundtripBenchmarks','EchoRoundtrip',f'PayloadBytes={size}&StandardClient={standard}') for size in (128,4096) for standard in ('False','True')})

def reports(directory):
    """Require every case and finite measured statistics, excluding the other library."""
    cases=[]
    for path in sorted((directory/'bdn/results').glob('*-full-compressed.json')):
        data=json.loads(path.read_text(encoding='utf-8'))
        for case in data['Benchmarks']:
            if ('WebSocket' in case['Type']) != (KIND=='websocket'):continue
            required_job=('IterationCount=15','IterationTime=500ms','LaunchCount=1','WarmupCount=5')
            if any(parameter not in case['DisplayInfo'] for parameter in required_job):
                raise ValueError(f'Wrong benchmark configuration: {case["DisplayInfo"]}')
            workload=[m for m in case['Measurements'] if m['IterationMode']=='Workload']
            actual=[m for m in workload if m['IterationStage']=='Actual']
            warmup=[m for m in workload if m['IterationStage']=='Warmup']
            if len(actual)!=MEASUREMENT_ITERATIONS or len(warmup)!=WARMUP_ITERATIONS or {m['LaunchIndex'] for m in actual}!={1}:
                raise ValueError(f'Incomplete warmup/measurement iterations: {case["FullName"]}')
            stats=case['Statistics']
            if not stats or stats['N']<10 or not math.isfinite(stats['Mean']) or stats['Mean']<=0:
                raise ValueError(f'Incomplete measured case: {case["FullName"]}')
            allocated=case['Memory']['BytesAllocatedPerOperation']
            zero=case['Method'] in ZERO_METHODS and not (case['Type']=='JsonParsingBenchmarks' and case['Method']=='SystemTextJsonGenerated')
            if zero and allocated!=0:raise ValueError(f'Expected 0 B/op: {case["FullName"]}: {allocated}')
            cases.append(case)
    if len(cases)!=EXPECTED_CASES:raise ValueError(f'Expected {EXPECTED_CASES} cases; found {len(cases)}')
    identities=[(c['Type'],c['Method'],c['Parameters']) for c in cases]
    if len(set(identities))!=len(identities) or set(identities)!=EXPECTED_IDENTITIES:
        raise ValueError('Missing, duplicate or unexpected benchmark identities')
    return cases

def streaming(directory):
    """Validate healthy profiles, sample arithmetic, elapsed rates and byte counts."""
    if (directory / 'streaming-session.json').exists():
        profile, runs = validate_session(directory)
        topologies = {run['Topology'] for values in runs.values() for run in values}
        if len(topologies) != 1:raise ValueError('Mixed streaming topologies')
        groups = {'_profile': profile, '_topology': topologies.pop()}
        for client, values in runs.items():
            groups[client] = summarize_streams(values)
        return groups
    groups={}
    for client in ('standard','pipeline'):
        runs=[]
        for n in range(1,4):
            path=directory/f'websocket-{client}-{n}.json'; run=json.loads(path.read_text(encoding='utf-8'))
            if not (run['ClientType']==client and run['ClientCount']==10 and run['WarmupSeconds']==5 and run['DurationSeconds']>=30 and run['PayloadBytes']==87 and run['FramesPerBatch']==20 and run['BatchBytes']==1780):raise ValueError(f'Unexpected streaming profile: {path}')
            max_snapshot_skew_bytes=2*run['ClientCount']*run['PayloadBytes']
            if run['Messages']<=0 or abs(run['Bytes']-run['Messages']*run['PayloadBytes'])>max_snapshot_skew_bytes:raise ValueError(f'Invalid message bytes: {path}')
            if sum(s['Messages'] for s in run['Samples'])!=run['Messages'] or sum(s['Bytes'] for s in run['Samples'])!=run['Bytes']:raise ValueError(f'Invalid samples: {path}')
            if not math.isclose(run['Messages']/run['DurationSeconds'],run['MessagesPerSecond'],rel_tol=1e-12):raise ValueError(f'Invalid elapsed rate: {path}')
            log=path.with_suffix('.log').read_text(encoding='utf-8')
            if 'Avg Msg/s:' not in log or 'Streaming benchmark failed:' in log:raise ValueError(f'Failed workload: {path}')
            previous_elapsed=0
            for sample in run['Samples']:
                interval=sample['ElapsedSeconds']-previous_elapsed
                if interval<=0 or not math.isclose(sample['Messages']/interval,sample['MessagesPerSecond'],rel_tol=1e-10):
                    raise ValueError(f'Invalid sample elapsed rate: {path}')
                if abs(sample['Bytes']-sample['Messages']*run['PayloadBytes'])>max_snapshot_skew_bytes:raise ValueError(f'Invalid sample payload counters: {path}')
                previous_elapsed=sample['ElapsedSeconds']
            if not math.isclose(previous_elapsed,run['DurationSeconds'],rel_tol=1e-12):raise ValueError(f'Incomplete sample duration: {path}')
            if run['Topology']!='loopback' or run['CpuScope']!='generator-and-clients':raise ValueError(f'Invalid loopback measurement scope: {path}')
            if not math.isclose(run['CpuMilliseconds']*1_000_000/run['Messages'],run['CpuMillisecondsPerMillionMessages'],rel_tol=1e-12):raise ValueError(f'Invalid CPU normalization: {path}')
            runs.append(run)
        groups[client]=summarize_streams(runs)
    return groups

def summarize_streams(runs):
    """Retain per-run dispersion and total allocations alongside median rates."""
    return {'messagesPerSecond': statistics.median(r['MessagesPerSecond'] for r in runs),
            'allocatedBytesPerMillionMessages': statistics.median(r['AllocatedBytes'] * 1_000_000 / r['Messages'] for r in runs),
            'cpuMsPerMillionMessages': statistics.median(r['CpuMillisecondsPerMillionMessages'] for r in runs),
            'messagesPerSecondByRun': [r['MessagesPerSecond'] for r in runs],
            'allocatedBytesByRun': [r['AllocatedBytes'] for r in runs],
            'allocatedBytesPerMillionMessagesByRun': [r['AllocatedBytes'] * 1_000_000 / r['Messages'] for r in runs],
            'gcCounts': [[r[f'Gen{g}Collections'] for g in range(3)] for r in runs],
            'payloadCounterSkewBytes': [r['Bytes'] - r['Messages'] * r['PayloadBytes'] for r in runs]}


def duration(ns):
    """Display nanoseconds without changing the ratios computed from raw values."""
    scale,unit=(1_000_000,'ms') if ns>=1_000_000 else (1_000,'µs') if ns>=1_000 else (1,'ns')
    return f'{ns/scale:.2f} {unit}'

BYTES_PER_KILOBYTE = 1_000
ETHERNET_BUNDLE = 'results/lan-controls/20261108T202415Z'

def render(cases, russian):
    """Describe all echo cases by client and payload rather than benchmark parameters."""
    title = '### Полный обмен с локальным echo-сервером' if russian else '### Complete local echo roundtrips'
    explanation = ('Полный обмен с побайтной проверкой; счётчики процесса включают echo-сервер и асинхронный измеритель.' if russian else
                   'Complete roundtrips with byte-for-byte validation; process counters include the echo peer and asynchronous harness.')
    heading = '| Клиент | Сообщение | Время | Погрешность | Аллокации |' if russian else '| Client | Payload | Mean | Error | Allocated |'
    blocks = [title, '', explanation, '', heading, '|---|---:|---:|---:|---:|']
    for case in cases:
        parameters = dict(field.split('=', 1) for field in case['Parameters'].split('&'))
        client = 'ClientWebSocket adapter' if parameters['StandardClient'] == 'True' else 'Pipelines'
        if russian and parameters['StandardClient'] == 'True':
            client = 'Адаптер ClientWebSocket'
        payload = '128 B' if parameters['PayloadBytes'] == '128' else '4 KiB'
        stats = case['Statistics']
        allocated = case['Memory']['BytesAllocatedPerOperation']
        blocks.append(f'| {client} | {payload} | {duration(stats["Mean"])} | {duration(stats["ConfidenceInterval"]["Margin"])} | {allocated:g} B |')
    blocks.append('')
    return '\n'.join(blocks)

def render_streaming(groups, russian):
    """Render continuous-reception results with explicit workload and process scope."""
    local = groups.get('_topology', 'separate-process-loopback') == 'separate-process-loopback'
    title = (('### Непрерывный приём через loopback' if russian else '### Continuous reception on loopback') if local else
             ('### Непрерывный приём с внешнего адреса' if russian else '### Continuous reception from an external endpoint'))
    profile = groups.get('_profile')
    if profile:
        server = (('Сервер работает в отдельном локальном процессе.' if russian else 'The server runs in a separate local process.') if local else
                  ('Использован внешний адрес генератора.' if russian else 'The generator uses the supplied external endpoint.'))
        explanation = (f"JSON {profile['payloadBytes']} байт · {profile['framesPerBatch']} фреймов на запись · {profile['clientCount']} соединений · {profile['warmupSeconds']} с прогрева / {profile['durationSeconds']} с измерения. Медианы {profile['runsPerClient']} свежих процессов на клиент. {server} CPU, GC и аллокации — процесс приёмника, включая сбор метрик." if russian else
                       f"{profile['payloadBytes']}-byte JSON · {profile['framesPerBatch']} frames/write · {profile['clientCount']} connections · {profile['warmupSeconds']} s warmup / {profile['durationSeconds']} s measurement. Medians of {profile['runsPerClient']} fresh processes per client. {server} CPU, GC and allocations: receiver process, including sampling.")
    else:
        explanation = ('Исторический профиль: 87 байт, 20 фреймов на запись, 10 соединений; 3 запуска, 5 с прогрева и 30 с измерения. CPU и аллокации включают генератор и клиентов в одном процессе.' if russian else
                       'Historical profile: 87 bytes, 20 frames/write, 10 connections; 3 runs, 5 s warmup and 30 s measurement. CPU and allocations include generator and clients in one process.')
    heading = '| Клиент | Сообщений/с | CPU мс / млн сообщений | Аллокации, кБ / млн сообщений |' if russian else '| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |'
    blocks = [title, '', explanation, '', heading, '|---|---:|---:|---:|']
    for key, label in [('standard', 'ClientWebSocket'), ('pipeline', 'Pipelines')]:
        stats = groups[key]
        blocks.append(f'| {label} | {stats["messagesPerSecond"]:,.0f} | {stats["cpuMsPerMillionMessages"]:,.0f} | {stats["allocatedBytesPerMillionMessages"] / BYTES_PER_KILOBYTE:.3f} |')
    blocks.append('')
    return '\n'.join(blocks)

def render_local(cases, streams, russian):
    """Keep streaming and echo clearly separated in the reproducible README region."""
    return render_streaming(streams, russian) + '\n' + render(cases, russian)

def render_ethernet(russian):
    """Render the recorded network comparison and its current-versus-control labels."""
    medians = json.loads((ROOT / ETHERNET_BUNDLE / 'summary.json').read_text(encoding='utf-8'))['medians']
    heading = '| Клиент | Сообщений/с | CPU мс / млн сообщений | Аллокации, кБ / млн сообщений |' if russian else '| Client | Messages/s | CPU ms / 1M messages | Allocated kB / 1M messages |'
    blocks = [heading, '|---|---:|---:|---:|']
    labels = [('standard', 'ClientWebSocket', 'ClientWebSocket'),
              ('fixed', 'Pipelines · fixed 64 KiB buffer', 'Pipelines · фиксированный буфер 64 KiB'),
              ('auto', 'Pipelines · automatic buffer (control)', 'Pipelines · автоматический буфер (контроль)')]
    for variant, english, russian_label in labels:
        stats = medians['batch-20-' + variant]
        blocks.append(f'| {russian_label if russian else english} | {stats["MessagesPerSecond"]:,.0f} | {stats["CpuMillisecondsPerMillionMessages"]:,.0f} | {stats["AllocatedBytesPerMillionMessages"] / BYTES_PER_KILOBYTE:.3f} |')
    blocks.extend(['', 'CPU и аллокации относятся ко всему процессу приёмника, включая сбор метрик; 1 кБ = 1000 байт. Основной клиент использует фиксированный приёмный TCP-буфер 64 KiB. Автоматический размер — отдельный контрольный вариант.' if russian else
                   'CPU and allocations cover the receiver process including sampling; 1 kB = 1000 bytes. The main client uses a fixed 64 KiB TCP receive buffer. Automatic sizing is a separate control variant.', ''])
    return '\n'.join(blocks)

def main():
    """Regenerate both languages and retain comparison statistics in machine-readable form."""
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);args=parser.parse_args();directory=args.directory.resolve()
    cases=reports(directory);before=reports(directory/'baseline') if (directory/'baseline').exists() else []
    key=lambda c:(c['FullName'],c['Parameters'])
    old={key(c):c for c in before}
    if before and set(old)!={key(c) for c in cases}:raise ValueError('Before/after case identities differ')
    summary={'caseCount':len(cases),'comparison':[{'name':c['FullName'],'parameters':c['Parameters'],'beforeMeanNs':old[key(c)]['Statistics']['Mean'],'afterMeanNs':c['Statistics']['Mean'],'afterToBefore':c['Statistics']['Mean']/old[key(c)]['Statistics']['Mean'],'beforeAllocatedBytes':old[key(c)]['Memory']['BytesAllocatedPerOperation'],'afterAllocatedBytes':c['Memory']['BytesAllocatedPerOperation']} for c in cases if key(c) in old]}
    if KIND=='websocket':
        summary['streaming']=streaming(directory)
        if before:summary['baselineStreaming']=streaming(directory/'baseline')
    (directory/'summary.json').write_text(json.dumps(summary,indent=2)+'\n', encoding='utf-8')
    for filename,russian in [('README.md',False),('README.ru.md',True)]:
        p=ROOT/filename;s=p.read_text(encoding='utf-8');start='<!-- LOCAL_RESULTS_START -->';end='<!-- LOCAL_RESULTS_END -->';head,rest=s.split(start);_,tail=rest.split(end)
        table=render_local(cases,summary.get('streaming'),russian)
        rendered=head+start+'\n'+table+'\n'+end+tail
        if KIND=='websocket':
            eth_start='<!-- ETHERNET_RESULTS_START -->';eth_end='<!-- ETHERNET_RESULTS_END -->'
            before_eth,eth_rest=rendered.split(eth_start);_,after_eth=eth_rest.split(eth_end)
            rendered=before_eth+eth_start+'\n'+render_ethernet(russian)+'\n'+eth_end+after_eth
        p.write_text(rendered, encoding='utf-8')
    print(f'Validated {len(cases)} measured cases'+(' and before/after comparison' if before else '')+(' plus streaming' if KIND=='websocket' else '')+'; generated both READMEs.')
if __name__=='__main__':main()
