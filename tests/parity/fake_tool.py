"""Controlled process/protocol faults; never launches a viewer or touches CAD."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

parser = argparse.ArgumentParser()
for key in ('prepare', 'mesh-settings', 'validate', 'preview', 'child'):
    parser.add_argument('--' + key)
args = parser.parse_args()
if args.child:
    time.sleep(60)
elif args.prepare:
    data = json.loads(Path(args.prepare).read_text(encoding='utf-8-sig'))
    assets = []
    mode = data['meshes'][0]['id'] if data['meshes'] else ''
    for entry in data['meshes']:
        target = Path(data['staging']) / entry['relative']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(entry['source'], target)
        assets.append(dict(id=entry['id'], relative=entry['relative'],
                           sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    if mode == 'source-change':
        Path(data['meshes'][0]['source']).write_bytes(b'changed')
    if mode == 'settings-change':
        Path(args.mesh_settings).write_text('{"Enabled": false}', encoding='utf-8')
    if mode == 'bad-id':
        assets[0]['id'] = 'unknown'
    if mode == 'bad-hash':
        assets[0]['sha256'] = 'invalid'
    result = dict(assets=assets, counts=dict(mesh_prepare=len(assets), mesh_simplification=0))
    if mode == 'bad-count':
        result['counts']['mesh_prepare'] = -1
    Path(data['result']).write_text('bad json' if mode == 'bad-json' else json.dumps(result), encoding='utf-8')
else:
    path = Path(args.validate or args.preview)
    mode = path.read_text(encoding='utf-8')
    print('工具日志: 中文路径 ✓', flush=True)
    if mode == 'exit':
        print('expected error', file=sys.stderr, flush=True)
        sys.exit(7)
    if mode == 'bad-error-json':
        print('SW2MUJOCO_TOOL_ERROR:invalid json', file=sys.stderr, flush=True)
        sys.exit(1)
    if mode == 'modify':
        path.write_text('rewritten', encoding='utf-8')
    if mode == 'sleep':
        child = subprocess.Popen([sys.executable, __file__, '--child', 'yes'])
        (path.parent / 'child.pid').write_text(str(child.pid), encoding='utf-8')
        print('CHILD_READY', flush=True)
        time.sleep(60)
    if args.preview:
        print('FAKE_PREVIEW_CALL', flush=True)
