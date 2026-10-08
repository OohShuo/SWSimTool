"""Actual net8 platform/culture gate. All generated files remain under build/."""
from pathlib import Path
import argparse
import json
import os
import platform
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--culture',choices=('en-US','zh-CN','de-DE'))
parser.add_argument('--no-build',action='store_true')
parser.add_argument('--skip-backend',action='store_true')
args=parser.parse_args()
system=platform.system().lower()
base=ROOT/'build/reports/platform'/system
base.mkdir(parents=True,exist_ok=True)
work=ROOT/'build/test-work';work.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1',PYTHONIOENCODING='utf-8',SWSIMTOOL_TEST_FRAMEWORK='net8.0',SWSIMTOOL_CROSS_TARGET='0',SWSIMTOOL_STORAGE_ARTIFACT=str(base/'storage.json'),SWSIMTOOL_CACHE=str(work/('platform-'+system+'-cache')),SWSIMTOOL_MESH_CACHE=str(work/('platform-'+system+'-mesh-cache')),TMP=str(work),TEMP=str(work),TMPDIR=str(work))
def run(name,command,environment=env):
    with (base/(name+'.log')).open('w',encoding='utf-8') as log:
        result=subprocess.run(command,cwd=ROOT,env=environment,stdout=log,stderr=subprocess.STDOUT)
    if result.returncode:
        print((base/(name+'.log')).read_text(encoding='utf-8'),file=sys.stderr)
        raise SystemExit(result.returncode)
    print('PASS:',name,flush=True)
if not args.no_build:
    for name,path in [('candidate','tests/parity/CandidateRunner.csproj')]:
        command=['dotnet','build',path,'-c','Release']
        if os.name!='nt':command+=['-f','net8.0']
        run('build-'+name,command)
run('modern-host',[sys.executable,'tests/targets/Test-ModernHost.py'])
run('platform-paths',[sys.executable,'tests/platform/test_paths.py'])
if not args.skip_backend:
    run('backend',[sys.executable,'-m','unittest','discover','-s','tests/backend','-p','test_*.py'])
cultures=[args.culture] if args.culture else ['en-US','zh-CN','de-DE']
for culture in cultures:
    target=base/culture
    if target.exists():raise SystemExit('Refusing stale platform artifacts: '+str(target))
    target.mkdir()
    run('parity-'+culture,[sys.executable,'tests/parity/run.py'],dict(env,SWSIMTOOL_TEST_CULTURE=culture,SWSIMTOOL_PLATFORM_ARTIFACTS=str(target)))
import mujoco,numpy
(base/'environment.json').write_text(json.dumps(dict(system=system,python=platform.python_version(),mujoco=mujoco.__version__,numpy=numpy.__version__,cultures=cultures,status='passed'),indent=2),encoding='utf-8')
print('Platform and culture gate passed:',base)
