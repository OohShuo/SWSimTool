"""Finish the generated native SW V1 gate with compiled semantics and dynamics."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import json
from pathlib import Path
import sys

root = next(p for p in Path(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
sys.path.insert(0, str(root / 'tests/parity'))
from semantic_compare import compiled_semantics, dynamics, inertia_in_body, close
from convert import load_mjcf, resolve_meshes, asset_options
import mujoco
import xml.etree.ElementTree as ET

folder = Path((root / 'build/native-incremental-directory.txt').read_text(encoding='utf-8-sig').strip()).resolve()
if not folder.is_relative_to((root / 'build').resolve()) or not folder.name.startswith('native-incremental-'):
    raise ValueError('Only newly generated workspace fixtures are accepted')
report_path = root / 'build/native-incremental-v1.json'
report_path.unlink(missing_ok=True)
report = json.loads((folder / 'native-counts.json').read_text(encoding='utf-8-sig'))
if report['stages'][0].get('nativeOnly'):
    if (folder/'core_direct.xml').is_file():
        direct=load_mjcf(folder/'core_direct.xml')
        reference=load_mjcf(folder/'core_reference.xml')
        compiled_semantics(direct, reference)
        dynamics(direct, reference, steps=(1,10,100))
        report['direct_cad_core_legacy_parity']=True
    warm=load_mjcf(folder/'incremental_native_mjcf/robot.xml')
    cold=load_mjcf(folder/'full_native_mjcf/robot.xml')
    compiled_semantics(warm,cold)
    dynamics(warm,cold,steps=(1,10,100))
    report.update(native_incremental_cold_parity=True,no_intermediate_urdf=True)
    if (folder/'identity-counts.json').is_file():
        identity=json.loads((folder/'identity-counts.json').read_text(encoding='utf-8-sig'))
        identity_warm=load_mjcf(folder/'identity_warm_native_mjcf/robot.xml')
        identity_cold=load_mjcf(folder/'identity_cold_native_mjcf/robot.xml')
        compiled_semantics(identity_warm,identity_cold)
        dynamics(identity_warm,identity_cold,steps=(1,10,100))
        report.update(identity_lifecycle=identity,identity_cold_warm_semantics_and_dynamics=True)
    if (folder/'configuration-reference-counts.json').is_file():
        configuration=json.loads((folder/'configuration-reference-counts.json').read_text(encoding='utf-8-sig'))
        cfg_warm=load_mjcf(folder/'configuration_warm_native_mjcf/robot.xml')
        cfg_cold=load_mjcf(folder/'configuration_cold_native_mjcf/robot.xml')
        compiled_semantics(cfg_warm,cfg_cold)
        dynamics(cfg_warm,cfg_cold,steps=(1,10,100))
        compiled_semantics(cfg_warm,load_mjcf(folder/'identity_warm_native_mjcf/robot.xml'))
        report.update(configuration_lifecycle=configuration,configuration_cold_warm_semantics_and_dynamics=True)
    if (folder/'cad-reference-counts.json').is_file():
        cad_reference=json.loads((folder/'cad-reference-counts.json').read_text(encoding='utf-8-sig'))
        cad_warm=load_mjcf(folder/'cad_reference_warm_native_mjcf/robot.xml')
        cad_cold=load_mjcf(folder/'cad_reference_cold_native_mjcf/robot.xml')
        compiled_semantics(cad_warm,cad_cold)
        dynamics(cad_warm,cad_cold,steps=(1,10,100))
        compiled_semantics(cad_warm,load_mjcf(folder/'identity_warm_native_mjcf/robot.xml'))
        report.update(cad_reference_lifecycle=cad_reference,cad_reference_cold_warm_semantics_and_dynamics=True)
    report_path=root/'build/native-production-v1.json'
    report_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('Native-only production incremental semantics and dynamics: PASS')
    sys.exit(0)

a = load_mjcf(folder / 'incremental_mjcf/robot.xml')
b = load_mjcf(folder / 'full_mjcf/robot.xml')
compiled_semantics(a, b)
dynamics(a, b, steps=(1, 10, 100))
report.update(model_semantics_match=True, forward_and_step_match=True)
for stage in report['stages']:
    candidate=load_mjcf(stage['nativeOutput'])
    reference=load_mjcf(stage['output'])
    # The production reference serializes MjSpec with six significant digits.
    # Limit this tolerance to that serialized reference; cold/native stays strict.
    compiled_semantics(reference,candidate,precision=2e-6)
    dynamics(reference,candidate,steps=(1,10,100),precision=3e-6)
warm=load_mjcf(folder/'incremental_native_mjcf/robot.xml')
cold=load_mjcf(folder/'full_native_mjcf/robot.xml')
compiled_semantics(warm,cold)
dynamics(warm,cold,steps=(1,10,100))
report.update(native_shadow_package_parity=True,native_incremental_cold_parity=True)
source=Path(report['stages'][-1]['sourceUrdf'])
tree=ET.parse(source).getroot()
assets=resolve_meshes(tree,source,folder='meshes')
extension=ET.SubElement(tree,'mujoco')
ET.SubElement(extension,'compiler',fusestatic='false',discardvisual='false',strippath='false')
with asset_options(mujoco,assets) as options:
    unrounded=mujoco.MjModel.from_xml_string(ET.tostring(tree,encoding='unicode'),**options)
for name in ('base','arm','elbow','tool'):
    a=mujoco.mj_name2id(unrounded,mujoco.mjtObj.mjOBJ_BODY,name)
    b=mujoco.mj_name2id(cold,mujoco.mjtObj.mjOBJ_BODY,name)
    for key in ('body_pos','body_quat','body_mass','body_ipos'):
        close(getattr(unrounded,key)[a],getattr(cold,key)[b],name+'/'+key)
    close(inertia_in_body(unrounded,a),inertia_in_body(cold,b),name+'/unrounded inertia')
for name in ('hinge','bend'):
    a=mujoco.mj_name2id(unrounded,mujoco.mjtObj.mjOBJ_JOINT,name)
    b=mujoco.mj_name2id(cold,mujoco.mjtObj.mjOBJ_JOINT,name)
    close(unrounded.jnt_axis[a],cold.jnt_axis[b],name+'/unrounded axis')
report.update(unrounded_core_semantics_match=True,serialized_reference_tolerance=dict(static=2e-6,dynamic=3e-6))
report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('Native incremental semantics and dynamics: PASS')
for stage in report['stages']:
    print(stage['label'], stage['cad'], stage['backend']['counts'])
