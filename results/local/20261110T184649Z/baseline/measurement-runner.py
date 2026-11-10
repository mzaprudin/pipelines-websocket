#!/usr/bin/env python3
"""Sequential, fail-fast split comparison with exact inputs and complete artifacts."""
import hashlib,json,os,platform,subprocess,sys
from datetime import datetime,timezone
from pathlib import Path
root=Path(sys.argv[1]).resolve(); out=Path(sys.argv[2]).resolve(); kind=sys.argv[3]
out.mkdir(parents=True,exist_ok=False)
commands=[]
def run(command, name):
    commands.append(command)
    (out/'commands.json').write_text(json.dumps(commands,indent=2)+'\n')
    print(datetime.now(timezone.utc).isoformat(), name,flush=True)
    with (out/(name+'.log')).open('w') as log:
        subprocess.run(command,cwd=root,stdout=log,stderr=subprocess.STDOUT,check=True)
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
paths=subprocess.check_output(['git','ls-files'],cwd=root,text=True).splitlines()
sources={s:sha(root/s) for s in paths if (root/s).is_file() and not s.startswith('results/')}
(root_files:=out/'source-snapshot.json').write_text(json.dumps({'revision':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'status':subprocess.check_output(['git','status','--porcelain'],cwd=root,text=True),'sha256':sources},indent=2)+'\n')
(out/'environment.json').write_text(json.dumps({'utc':datetime.now(timezone.utc).isoformat(),'platform':platform.platform(),'cpu':Path('/proc/cpuinfo').read_text(),'dotnet':subprocess.check_output(['dotnet','--info'],cwd=root,text=True),'parameters':{'warmupCount':5,'iterationCount':15,'iterationTimeMs':500,'launchCount':1},'scope':'local loopback; generator CPU included for streaming'},indent=2)+'\n')
binaries={str(p.relative_to(root)):sha(p) for p in sorted(root.glob('**/bin/Release/net10.0/*.dll'))}
(out/'binaries.json').write_text(json.dumps(binaries,indent=2)+'\n')
project={'source':'benchmarks/GapSonar.TransportLab.Benchmarks','json':'benchmarks/ZeroAlloc.Json.Benchmarks','websocket':'benchmarks/Pipelines.WebSockets.Benchmarks'}[kind]
run(['dotnet','run','-c','Release','--no-build','--project',project,'--','--filter','*','--warmupCount','5','--iterationCount','15','--iterationTime','500','--launchCount','1','--exporters','json','--artifacts',str(out/'bdn')],'benchmark')
if kind!='json':
    for n in range(1,4):
        for client in (['standard','pipeline'] if n%2 else ['pipeline','standard']):
            command=['dotnet','run','-c','Release','--no-build','--project','benchmarks/WebSocketLoadTest','--','--count','10','--duration','30','--warmup','5','--payload-bytes','87','--batch','20','--json',str(out/f'websocket-{client}-{n}.json')]
            if client=='standard':command+=['--std']
            run(command,f'websocket-{client}-{n}')
(out/'artifacts.sha256.json').write_text(json.dumps({str(p.relative_to(out)):sha(p) for p in sorted(out.rglob('*')) if p.is_file() and p.name!='artifacts.sha256.json'},indent=2)+'\n')
print('COMPLETE',flush=True)
