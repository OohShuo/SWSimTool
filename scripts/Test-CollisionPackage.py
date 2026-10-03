"""Validate the newly generated CAD fixture's portable collision package."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

import mujoco
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'mujoco_backend'))
from convert import export_package

folder = Path(sys.argv[1]).resolve()
if not folder.is_relative_to(ROOT / 'build') or not (folder / 'solidworks-process.txt').is_file():
    raise RuntimeError('Only this task\'s generated CAD fixtures are permitted')
urdf = folder / 'collision_robot/urdf/collision_robot.urdf'
sidecar = urdf.with_suffix('.sim.json')
files = [urdf, sidecar, *sorted((folder / 'collision_robot/meshes').glob('*.stl'))]
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
original = {path: digest(path) for path in files}
output = folder / 'collision_robot_mjcf/collision_robot.xml'
model = export_package(urdf, sidecar, output, {'Enabled': False})
assert all(digest(path) == value for path, value in original.items()), 'Original files changed'
reports = json.loads((output.parent / 'mesh-report.json').read_text(encoding='utf-8'))
for report in reports:
    assert not report['changed']
    original_mesh = folder / 'collision_robot/meshes' / report['source'].replace('\\', '/').split('/')[-1]
    assert digest(output.parent / report['file']) == digest(original_mesh)
config = json.loads(sidecar.read_text(encoding='utf-8-sig'))
for geom in config['collision']['geometries']:
    if config['collision']['link_modes'].get(geom['link']) == 'primitive':
        np.testing.assert_allclose(model.geom(geom['name']).pos, geom['xyz'], atol=1e-9)
data = mujoco.MjData(model)
mujoco.mj_forward(model, data)
assert data.ncon == 0, 'Unexpected internal contacts'
for _ in range(1000):
    mujoco.mj_step(model, data)
assert np.isfinite(data.qpos).all() and np.isfinite(data.qvel).all()
assert model.nexclude == 1
print('PASS: actual CAD URDF/STL/JSON preserved; under-budget STL copies byte-identical; '
      'link-local collision poses retained; internal contacts excluded; 1000 finite simulation steps')
