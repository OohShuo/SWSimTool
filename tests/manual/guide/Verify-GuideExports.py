"""Validate the authorized demo outputs; never open CAD or change source files."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = next(p for p in Path(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
sys.path.insert(0, str(ROOT / 'tests/parity'))
from convert import export_package, load_mjcf, resolve_meshes, asset_options
from semantic_compare import compiled_semantics, dynamics, close, inertia_in_body, rotation
import mujoco

folder = ROOT / 'build/guide-demo'
urdf = folder / 'export_sidecar/balance2026_gimbal/urdf/balance2026_gimbal.urdf'
sidecar = urdf.with_suffix('.sim.json')
inputs = [urdf, sidecar, *urdf.parent.parent.joinpath('meshes').glob('*.STL')]
before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
reference = folder / 'reference_mjcf/balance2026_gimbal.xml'
export_package(urdf, sidecar, reference, dict(Enabled=True, MaximumTriangles=100000,
    Backend='fast-simplification', Python=r'D:\Softwaves\python\python.exe'))
a = load_mjcf(reference)
results = []
for name in ('local_mjcf', 'export_mjcf'):
    path = folder / name / 'balance2026_gimbal.xml'
    b = load_mjcf(path)
    # The Python reference serializes through mj_saveLastXML (six significant
    # digits). Keep the same declared tolerance as the native CAD shadow gate.
    compiled_semantics(a, b, 2e-6)
    files = sorted(str(p.relative_to(path.parent)) for p in path.parent.rglob('*') if p.is_file())
    assert all(p == path.name or p.startswith('meshes') and p.lower().endswith('.stl') for p in files), files
    results.append(dict(path=name, L1=True, reference_L2=True,
        static_tolerance=2e-6,files=files))
local = load_mjcf(folder / 'local_mjcf/balance2026_gimbal.xml')
project = load_mjcf(folder / 'export_mjcf/balance2026_gimbal.xml')
compiled_semantics(local, project)
dynamics(local, project, (1, 10, 100))
# Independent official URDF import, without the reference's lossy XML step.
tree = ET.parse(urdf).getroot()
names = list(dict.fromkeys(mesh.get('filename') for mesh in tree.findall('.//mesh')))
for mesh in tree.findall('.//mesh'):
    mesh.set('filename', str(reference.parent / 'meshes' / ('mesh_%04d.stl' % names.index(mesh.get('filename')))))
extension = tree.find('mujoco')
if extension is None:
    extension = ET.SubElement(tree, 'mujoco')
compiler = extension.find('compiler')
if compiler is None:
    compiler = ET.SubElement(extension, 'compiler')
compiler.attrib.update(fusestatic='false', discardvisual='false', strippath='false')
assets = resolve_meshes(tree, urdf)
with asset_options(mujoco, assets) as options:
    core = mujoco.MjModel.from_xml_string(ET.tostring(tree, encoding='unicode'), **options)
for i in range(1, project.nbody):
    name = mujoco.mj_id2name(project, mujoco.mjtObj.mjOBJ_BODY, i)
    j = mujoco.mj_name2id(core, mujoco.mjtObj.mjOBJ_BODY, name)
    assert j >= 0, name
    for key in ('body_pos', 'body_mass', 'body_ipos'):
        close(getattr(core, key)[j], getattr(project, key)[i], name + '/' + key)
    close(rotation(core.body_quat[j]), rotation(project.body_quat[i]), name + '/rotation')
    close(inertia_in_body(core, j), inertia_in_body(project, i), name + '/inertia')
for i in range(project.njnt):
    name = mujoco.mj_id2name(project, mujoco.mjtObj.mjOBJ_JOINT, i)
    j = mujoco.mj_name2id(core, mujoco.mjtObj.mjOBJ_JOINT, name)
    for key in ('jnt_type', 'jnt_axis', 'jnt_pos', 'jnt_range'):
        close(getattr(core, key)[j], getattr(project, key)[i], name + '/' + key)
assert before == {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}, 'Input modified'
report = dict(results=results, native_project_local_L2_L3=True,steps=[1,10,100],
    official_unrounded_urdf_core=True,
    reference_dynamics_note='Six-digit Python serialization amplified closed-loop acceleration error; reference L3 is not claimed for this demo.',
    input_unchanged=True, bodies=a.nbody,joints=a.njnt,
    sites=a.nsite,actuators=a.nu,sensors=a.nsensor,equalities=a.neq)
(folder / 'parity-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
