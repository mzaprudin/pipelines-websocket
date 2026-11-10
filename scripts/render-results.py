#!/usr/bin/env python3
"""Validate local measurements and generate README tables from unrounded reports."""
import argparse,json,math,statistics
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
KIND='websocket'
EXPECTED_CASES=22 if KIND=='json' else 4
ZERO_METHODS={'JsonPopulator_Class_Reused_Interned','JsonPopulator_Struct_Ref','Gate_JsonPopulator_FastList_Reused_Interned','Gate_JsonPopulator_FastList_Reused_Interned_Struct','Standard_Serialize_Writer','JsonPushulator_Class','JsonPushulator_Struct','Manual_Utf8JsonWriter_Direct','Route10kMessages_JsonReader','Route10kMessages_Custom','Route10kMessages_Regex','PopulatorReusedInterned','SystemTextJsonGenerated','PushulatorReusedWriter'}

def reports(directory):
    """Require every case and finite measured statistics, excluding the other library."""
    cases=[]
    for path in sorted((directory/'bdn/results').glob('*-full-compressed.json')):
        data=json.loads(path.read_text())
        for case in data['Benchmarks']:
            if ('WebSocket' in case['Type']) != (KIND=='websocket'):continue
            stats=case['Statistics']
            if not stats or stats['N']<10 or not math.isfinite(stats['Mean']) or stats['Mean']<=0:
                raise ValueError(f'Incomplete measured case: {case["FullName"]}')
            allocated=case['Memory']['BytesAllocatedPerOperation']
            zero=case['Method'] in ZERO_METHODS and not (case['Type']=='JsonParsingBenchmarks' and case['Method']=='SystemTextJsonGenerated')
            if zero and allocated!=0:raise ValueError(f'Expected 0 B/op: {case["FullName"]}: {allocated}')
            cases.append(case)
    if len(cases)!=EXPECTED_CASES:raise ValueError(f'Expected {EXPECTED_CASES} cases; found {len(cases)}')
    return cases

def streaming(directory):
    """Validate healthy profiles, sample arithmetic, elapsed rates and byte counts."""
    groups={}
    for client in ('standard','pipeline'):
        runs=[]
        for n in range(1,4):
            path=directory/f'websocket-{client}-{n}.json'; run=json.loads(path.read_text())
            if not (run['ClientType']==client and run['ClientCount']==10 and run['WarmupSeconds']==5 and run['DurationSeconds']>=30 and run['PayloadBytes']==87 and run['FramesPerBatch']==20 and run['BatchBytes']==1780):raise ValueError(f'Unexpected streaming profile: {path}')
            if run['Messages']<=0 or run['Bytes']!=run['Messages']*87:raise ValueError(f'Invalid message bytes: {path}')
            if sum(s['Messages'] for s in run['Samples'])!=run['Messages'] or sum(s['Bytes'] for s in run['Samples'])!=run['Bytes']:raise ValueError(f'Invalid samples: {path}')
            if not math.isclose(run['Messages']/run['DurationSeconds'],run['MessagesPerSecond'],rel_tol=1e-12):raise ValueError(f'Invalid elapsed rate: {path}')
            log=path.with_suffix('.log').read_text()
            if 'Avg Msg/s:' not in log or 'Streaming benchmark failed:' in log:raise ValueError(f'Failed workload: {path}')
            runs.append(run)
        groups[client]={'messagesPerSecond':statistics.median(r['MessagesPerSecond'] for r in runs),'allocatedBytesPerMillionMessages':statistics.median(r['AllocatedBytes']*1_000_000/r['Messages'] for r in runs),'cpuMsPerMillionMessages':statistics.median(r['CpuMillisecondsPerMillionMessages'] for r in runs),'gcCounts':[[r[f'Gen{g}Collections'] for g in range(3)] for r in runs]}
    return groups

def duration(ns):
    """Display nanoseconds without changing the ratios computed from raw values."""
    scale,unit=(1_000_000,'ms') if ns>=1_000_000 else (1_000,'µs') if ns>=1_000 else (1,'ns')
    return f'{ns/scale:.2f} {unit}'

def render(cases,russian):
    """Show all cases, workload-specific standard baselines and actual allocated bytes."""
    blocks=[]
    for typename in dict.fromkeys(c['Type'] for c in cases):
        group=[c for c in cases if c['Type']==typename]; means={c['Method']:c['Statistics']['Mean'] for c in group}
        title={'PopulatorBenchmarks':('JSON population','Разбор JSON'),'PushulatorBenchmarks':('JSON writing','Запись JSON'),'RouterBenchmarks':('UTF-8 routing','Маршрутизация UTF-8'),'JsonParsingBenchmarks':('Equal-model parsing','Разбор одинаковой модели'),'JsonWritingBenchmarks':('Equal-model writing','Запись одинаковой модели'),'WebSocketRoundtripBenchmarks':('Local echo roundtrips','Локальный echo')}[typename][russian]
        blocks+=['### '+title,'','| Method | Mean | Error (99.9% CI) | Allocated B/op | Speedup |','|---|---:|---:|---:|---:|']
        for c in group:
            method=c['Method'];stats=c['Statistics'];baseline=None
            if typename=='PopulatorBenchmarks':baseline='Gate_Standard_Deserialize_List' if method.startswith('Gate_') else 'Standard_Deserialize_Class'
            elif typename=='PushulatorBenchmarks':baseline='Standard_Serialize_Class'
            elif typename=='RouterBenchmarks':baseline='Route10kMessages_JsonReader'
            elif typename in ('JsonParsingBenchmarks','JsonWritingBenchmarks'):baseline='SystemTextJson'
            ratio=f'{means[baseline]/stats["Mean"]:.2f}×' if baseline else '—'
            label=method+(f' ({c["Parameters"]})' if c['Parameters'] else '')
            blocks.append(f'| `{label}` | {duration(stats["Mean"])} | {duration(stats["ConfidenceInterval"]["Margin"])} | {c["Memory"]["BytesAllocatedPerOperation"]:g} B | {ratio} |')
        blocks.append('')
    return '\n'.join(blocks)

def main():
    """Regenerate both languages and retain comparison statistics in machine-readable form."""
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);args=parser.parse_args();directory=args.directory.resolve()
    cases=reports(directory);before=reports(directory/'baseline') if (directory/'baseline').exists() else []
    key=lambda c:(c['FullName'],c['Parameters'])
    old={key(c):c for c in before}
    summary={'caseCount':len(cases),'comparison':[{'name':c['FullName'],'parameters':c['Parameters'],'beforeMeanNs':old[key(c)]['Statistics']['Mean'],'afterMeanNs':c['Statistics']['Mean'],'afterToBefore':c['Statistics']['Mean']/old[key(c)]['Statistics']['Mean'],'beforeAllocatedBytes':old[key(c)]['Memory']['BytesAllocatedPerOperation'],'afterAllocatedBytes':c['Memory']['BytesAllocatedPerOperation']} for c in cases if key(c) in old]}
    if KIND=='websocket':summary.update(streaming=streaming(directory));summary.update(baselineStreaming=streaming(directory/'baseline')) if before and KIND=='websocket' else None
    (directory/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    for filename,russian in [('README.md',False),('README.ru.md',True)]:
        p=ROOT/filename;s=p.read_text();start='<!-- LOCAL_RESULTS_START -->';end='<!-- LOCAL_RESULTS_END -->';head,rest=s.split(start);_,tail=rest.split(end)
        table=render(cases,russian)
        if KIND=='websocket':
            table+='\n| Client | Messages/s | CPU ms / 1M | Allocated kB / 1M |\n|---|---:|---:|---:|\n'
            for client,stats in summary['streaming'].items():table+=f'| {client} | {stats["messagesPerSecond"]:,.0f} | {stats["cpuMsPerMillionMessages"]:,.0f} | {stats["allocatedBytesPerMillionMessages"]/1_000:.3f} |\n'
        p.write_text(head+start+'\n'+table+'\n'+end+tail)
    print(f'Validated {len(cases)} before/after cases'+(' and twelve streaming reports' if KIND=='websocket' else '')+'; generated both READMEs.')
if __name__=='__main__':main()
