"""Run matched physical-Ethernet and loopback sessions with one frozen binary set."""
from pathlib import Path
from datetime import datetime, timezone
import json
import shutil
import subprocess
import sys
import zipfile

ROOT = Path('/home/m/projects/pipelines-websocket')
sys.path.insert(0,str(ROOT/'scripts'))
from streaming_benchmarks import capture_context, sha256, validate_session, write_artifact_manifest, write_json

output = ROOT/'results/streaming'/datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
output.mkdir(parents=True)
Path('/home/m/projects/zero-split-work/ethernet-repeat-20261110/directory.txt').write_text(str(output)+'\n')
shutil.copyfile(__file__,output/'runner.py')
commands = []
def run(command,name):
    commands.append(command)
    write_json(output/'commands.json',commands)
    print(datetime.now(timezone.utc).isoformat(),name,flush=True)
    with (output/(name+'.log')).open('w',encoding='utf-8') as log:
        subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,check=True)

run(['dotnet','restore','Pipelines.WebSockets.sln','--locked-mode'],'restore')
run(['dotnet','build','Pipelines.WebSockets.sln','-c','Release','--no-restore'],'build')
capture_context(output)
package=ROOT/'artifacts/lan-generator-win-x64/pipelines-websocket-generator-win-x64.zip'
with zipfile.ZipFile(package) as archive:
    generator_manifest=json.loads(archive.read('manifest.json'))
write_json(output/'network.json',{'endpoint':'ws://192.168.1.75:5050',
    'route':subprocess.check_output(['ip','route','get','192.168.1.75'],text=True),
    'receiverInterface':{name:Path('/sys/class/net/enp6s0'/Path(name)).read_text().strip() for name in ('speed','duplex','mtu')},
    'providedGeneratorPackageSha256':sha256(package),'providedGeneratorManifest':generator_manifest,
    'generatorProfileVerified':'/stream/128/20 HTTP 101 before measurement',
    'windowsNegotiatedLinkSpeed':'not queried',
    'sysctls':subprocess.check_output(['sysctl','net.ipv4.tcp_rmem','net.ipv4.tcp_wmem','net.core.rmem_max','net.core.wmem_max'],text=True)})
for topology in ('ethernet','loopback'):
    command=[sys.executable,'scripts/streaming_benchmarks.py','--no-build','--output',str(output/topology)]
    if topology=='ethernet':command+=['--url','ws://192.168.1.75:5050']
    run(command,topology)
    validate_session(output/topology)
first=json.loads((output/'ethernet/binaries.json').read_text())
second=json.loads((output/'loopback/binaries.json').read_text())
if first!=second:raise ValueError('Receiver binaries differ between topologies')
write_json(output/'comparison-contract.json',{'sameReceiverBinaries':True,'sameReceiverProfile':True,
    'ethernet':'ethernet/streaming-session.json','loopback':'loopback/streaming-session.json',
    'scope':'receiver-only','protocol':'receiver-process-periodic-v1'})
write_artifact_manifest(output)
print('COMPLETE:',output,flush=True)
