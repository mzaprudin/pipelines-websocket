"""Separate diagnostic sessions; EventPipe results are excluded from performance medians."""
from pathlib import Path
import json
import subprocess
import sys
import threading
import time

ROOT=Path('/home/m/projects/pipelines-websocket')
sys.path.insert(0,str(ROOT/'scripts'))
from streaming_benchmarks import (DEFAULT_PROFILE,LocalGenerator,receiver_command,validate_report,
                                  write_json,write_artifact_manifest)
output=Path(Path('/home/m/projects/zero-split-work/ethernet-repeat-20261110/directory.txt').read_text().strip())/'diagnostics'
output.mkdir()
profile=dict(DEFAULT_PROFILE,runsPerClient=1)
sessions=[]
for topology,client in [('ethernet','standard'),('ethernet','pipeline'),('loopback','standard')]:
    case=output/f'{topology}-{client}';case.mkdir()
    generator=None
    process=None
    tracer=None
    thread=None
    try:
        if topology=='loopback':
            generator=LocalGenerator(case,profile);endpoint=generator.start()
        else:endpoint='ws://192.168.1.75:5050'
        report=case/'receiver.json'
        command=receiver_command(endpoint,profile,client=='standard',report)
        ready=threading.Event()
        sample=[]
        with (case/'receiver.log').open('w',encoding='utf-8') as log:
            process=subprocess.Popen(command,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',bufsize=1)
            def drain():
                for line in process.stdout:
                    log.write(line);log.flush()
                    if 's: ' in line and 'msg/s' in line and not ready.is_set():
                        sample.append(line.strip());ready.set()
            thread=threading.Thread(target=drain,daemon=True);thread.start()
            if not ready.wait(timeout=20):raise RuntimeError('Receiver failed to reach post-warmup sampling.')
            trace_command=['/home/m/.dotnet/tools/dotnet-trace','collect','--process-id',str(process.pid),
                           '--profile','gc-verbose','--duration','00:00:00:20','--output',str(case/'allocations.nettrace')]
            print('DIAGNOSTIC:',topology,client,'PID',process.pid,flush=True)
            with (case/'trace.log').open('w',encoding='utf-8') as trace_log:
                tracer=subprocess.Popen(trace_command,cwd=ROOT,stdout=trace_log,stderr=subprocess.STDOUT)
                if tracer.wait(timeout=50)!=0:raise RuntimeError('Allocation tracing failed.')
            if process.wait(timeout=30)!=0:raise RuntimeError('Diagnostic receiver failed.')
            thread.join(timeout=5)
            if thread.is_alive():raise RuntimeError('Receiver log drain failed to terminate.')
        validate_report(report,profile,client,process.pid,endpoint)
        sessions.append({'topology':topology,'client':client,'receiverProcessId':process.pid,
                         'command':command,'traceCommand':trace_command,'firstSampleBeforeAttach':sample[0],
                         'diagnosticOnly':True,'includeInPerformanceMedians':False})
        write_json(output/'sessions.json',sessions)
    finally:
        if tracer is not None and tracer.poll() is None:tracer.kill();tracer.wait(timeout=10)
        if process is not None:
            if process.poll() is None:process.kill();process.wait(timeout=10)
            if thread is not None:thread.join(timeout=5)
            process.stdout.close()
        if generator is not None:
            result=generator.stop()
            if result['exitCode']!=0 or result['forcedShutdown']:raise RuntimeError('Diagnostic generator shutdown failed.')
write_artifact_manifest(output)
print('DIAGNOSTICS COMPLETE:',output,flush=True)
