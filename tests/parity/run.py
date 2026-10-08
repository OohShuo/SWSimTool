"""Candidate compiler/parity/analytical gate. Synthetic files only, no CAD access."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import os
import shutil
import unittest
import xml.etree.ElementTree as ET

ROOT = next(p for p in Path(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
import mujoco
import numpy as np
from convert import convert, load_mjcf
from test_convert import URDF
from semantic_compare import compiled_semantics, dynamics, inertia_in_body, mesh_world_vertices, close

EXE = ROOT / 'build/bin/CandidateRunner' / os.getenv('SWSIMTOOL_TEST_CONFIGURATION', 'Release') / 'net48/SWSimTool.CandidateRunner.exe'
FRAMEWORK = os.getenv('SWSIMTOOL_TEST_FRAMEWORK', 'net48')
COMMAND = ['dotnet', str(EXE.parent.parent / 'net8.0/SWSimTool.CandidateRunner.dll')] if FRAMEWORK == 'net8.0' else [str(EXE)]
CROSS_COMMAND = [str(EXE)] if os.getenv('SWSIMTOOL_CROSS_TARGET') == '1' else None
RESULTS = []


class NativeParity(unittest.TestCase):
    def test_nearest_mesh_points_exact_with_and_without_scipy(self):
        from unittest.mock import patch
        from semantic_compare import nearest_distances
        rng = np.random.default_rng(20261007)
        source, target = rng.normal(size=(257, 3)), rng.normal(size=(1030, 3))
        expected = np.linalg.norm(source[:, None, :] - target[None, :, :], axis=2).min(axis=1)
        close(nearest_distances(source, target[::-1]), expected, 'nearest points', atol=1e-12, rtol=1e-12)
        with patch.dict(sys.modules, {'scipy.spatial': None}):
            close(nearest_distances(source, target), expected, 'bounded fallback', atol=1e-12, rtol=1e-12)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name)
        self.urdf = self.folder / 'robot.urdf'
        self.config = dict(schema_version=1, units='m,rad', urdf='robot.urdf',
                           attachments=[], actuators=[], sensors=[], equalities=[])

    def tearDown(self):
        self.temp.cleanup()

    def candidate(self, source, config=None):
        self.urdf.write_text(source, encoding='utf-8')
        sidecar = self.folder / 'robot.sim.json'
        config = self.config if config is None else config
        sidecar.write_text(json.dumps(config), encoding='utf-8')
        output = self.folder / 'candidate/robot.xml'
        result = subprocess.run(COMMAND+[ str(self.urdf), str(sidecar), str(output)], capture_output=True, text=True, encoding='utf-8')
        self.assertEqual(result.returncode, 0, result.stderr)
        model = load_mjcf(output)
        artifacts=os.getenv('SWSIMTOOL_PLATFORM_ARTIFACTS')
        if artifacts:
            self.artifact_index=getattr(self,'artifact_index',0)+1
            target=Path(artifacts)/(self._testMethodName+'_'+str(self.artifact_index))
            shutil.copytree(output.parent,target)
        if CROSS_COMMAND:
            cross_output = self.folder / 'cross/robot.xml'
            result = subprocess.run(CROSS_COMMAND+[str(self.urdf), str(sidecar), str(cross_output)], capture_output=True, text=True, encoding='utf-8')
            self.assertEqual(result.returncode, 0, result.stderr)
            other = load_mjcf(cross_output)
            compiled_semantics(model, other)
            dynamics(model, other, steps=(1, 10, 100))
        return model

    def parity(self, name, source, config, steps=(1, 10, 100), precision=1e-9, dynamic_precision=2e-7):
        b = self.candidate(source, config)
        config = copy.deepcopy(config)
        config['urdf_sha256'] = hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        sidecar = self.folder / 'reference.sim.json'
        sidecar.write_text(json.dumps(config), encoding='utf-8')
        # Each semantic scenario owns its reference output; overwrite/cache lifecycle
        # is covered separately by the production package regression below.
        a = convert(self.urdf, sidecar, self.folder / 'reference' / name / 'robot.xml')
        compiled_semantics(a, b, precision)
        dynamics(a, b, steps, dynamic_precision)
        RESULTS.append(dict(case=name, L1=True, L2B=True, L3=True, steps=list(steps), static_tolerance=precision, dynamic_tolerance=dynamic_precision))
        return a, b

    def test_core_and_extensions(self):
        frame = dict(name='imu_mount', id='imu_id', type='frame', link='tool', xyz=[.02, 0, 0], rpy=[0, .2, .3])
        point = dict(name='anchor_base', type='point', link='base', xyz=[.2, .3, .4])
        other = dict(name='anchor_arm', type='point', link='arm', xyz=[0, 0, 0])
        scenarios = {'core': self.config}
        sites = dict(self.config, attachments=[point, other, frame])
        scenarios['site'] = sites
        scenarios['actuator'] = dict(sites, actuators=[dict(name='drive', joint='hinge', type='position', gain=10, gear=2, ctrl_min=-1, ctrl_max=1, force_min=-10, force_max=10)])
        scenarios['driven_defaults'] = dict(scenarios['actuator'], joint_defaults=True)
        for kind in ('motor', 'velocity'):
            scenarios[kind] = dict(scenarios['actuator'], actuators=[dict(scenarios['actuator']['actuators'][0], type=kind)])
        scenarios['sensor'] = dict(sites, sensors=[dict(name='imu', type='imu', site='imu_mount'), dict(name='tof', type='tof', site='imu_mount'), dict(name='cam', type='camera', site='imu_mount', fovy=60)])
        scenarios['connect'] = dict(sites, equalities=[dict(name='closed', type='connect', site1='anchor_base', site2='anchor_arm')])
        scenarios['joint_equality'] = dict(sites, equalities=[dict(name='lock', type='joint', joint1='hinge', polycoef=[0, 1, 0, 0, 0])])
        scenarios['weld'] = dict(sites, attachments=[dict(point, type='frame', rpy=[0, 0, 0]), dict(other, type='frame', rpy=[0, 0, 0]), frame], equalities=[dict(name='weld', type='weld', site1='anchor_base', site2='anchor_arm', torquescale=.5)])
        solver = dict(enabled=True, timestep=.001, iterations=100, tolerance=1e-9, impratio=10)
        scenarios['solver'] = dict(sites, solver=solver)
        scenarios['joint_physics'] = dict(sites, joint_defaults=True, joints=[dict(joint='hinge', damping=.02, frictionloss=.01, armature=.002, stiffness=2, springref=.1, ref=.03, limit_mode='custom', lower=-.5, upper=.5, limit_solver=dict(timeconst=.003, dmin=.99, dmax=.995))], joint_force_limits=[dict(joint='hinge', lower=-3, upper=3)])
        scenarios['joint_type_override'] = dict(sites, joints=[dict(joint='hinge', type='slide', limit_mode='custom', lower=-.1, upper=.1)])
        force_sites = [point, other, dict(frame, name='endpoint')]
        for kind in ('pull', 'push', 'spring'):
            scenarios[kind] = dict(sites, attachments=force_sites, site_forces=[dict(name='pair', type=kind, site1='anchor_base', site2='endpoint', magnitude=10, stiffness=100, damping=2, length_mode='initial')])
        scenarios['custom_spring'] = dict(scenarios['spring'], site_forces=[dict(scenarios['spring']['site_forces'][0], length_mode='custom', rest_length=.12)])
        scenarios['collision'] = dict(sites, collision=dict(disable_internal=True, link_modes={'base': 'primitive', 'arm': 'primitive', 'tool': 'none'}, geometries=[dict(name='base_box', link='base', type='box', size=[.1, .1, .1], xyz=[0, 0, 0], rpy=[0, 0, 0]), dict(name='arm_sphere', link='arm', type='sphere', size=[.04], xyz=[0, 0, 0], rpy=[0, 0, 0])], allowed_pairs=[dict(link1='base', link2='arm', solver=dict(timeconst=.003))]))
        scenarios['combined'] = dict(scenarios['collision'], solver=solver, sensors=scenarios['sensor']['sensors'], actuators=scenarios['actuator']['actuators'], equalities=scenarios['connect']['equalities'], site_forces=[dict(name='pull', type='pull', site1='anchor_base', site2='imu_mount', magnitude=1), dict(name='spring', type='spring', site1='anchor_base', site2='imu_mount', stiffness=10, damping=.1, length_mode='initial')])
        for name, config in scenarios.items():
            with self.subTest(name=name): self.parity(name, URDF, config)

    def test_rotated_inertia_and_nonprincipal_axis(self):
        source = (Path(__file__).parent / 'fixtures/rotated_inertia.urdf').read_text()
        _, candidate = self.parity('rotated_inertia_nonprincipal_axis', source, self.config, steps=(1, 10, 100), precision=2e-6, dynamic_precision=3e-6)
        # Existing converter's to_xml rounds source poses/inertias to six significant digits.
        # Independently compare the un-serialized MuJoCo URDF model at tight tolerance.
        robot = ET.fromstring(source)
        ET.SubElement(ET.SubElement(robot, 'mujoco'), 'compiler', fusestatic='false', discardvisual='false', strippath='false')
        original = mujoco.MjSpec.from_string(ET.tostring(robot, encoding='unicode')).compile()
        compiled_semantics(original, candidate)
        dynamics(original, candidate, steps=(1, 10, 100, 1000))
        RESULTS.append(dict(case='unrounded_urdf_compile_rotated_inertia', L2B=True, L3=True, steps=[1, 10, 100, 1000], static_tolerance=1e-9, dynamic_tolerance=2e-7))

    def test_slide_and_continuous(self):
        for kind in ('prismatic', 'continuous'):
            with self.subTest(kind=kind):
                source = URDF.replace('type="revolute"', f'type="{kind}"')
                if kind == 'continuous': source = source.replace('lower="-1" upper="1" ', '')
                self.parity(kind, source, self.config)

    def test_real_contact_response(self):
        source = URDF.replace('type="revolute"', 'type="prismatic"').replace('xyz="0.2 0.3 0.4" rpy="0 0 1.5707963267948966"', 'xyz="0 0 0.09"')
        config = dict(self.config, solver=dict(enabled=True, timestep=.001), collision=dict(disable_internal=True, link_modes={'base':'primitive','arm':'primitive','tool':'none'}, geometries=[dict(name='base_sphere',link='base',type='sphere',size=[.05],xyz=[0,0,0],rpy=[0,0,0]), dict(name='arm_sphere',link='arm',type='sphere',size=[.05],xyz=[0,0,0],rpy=[0,0,0])], allowed_pairs=[dict(link1='base',link2='arm')]))
        a, b = self.parity('real_contact_response', source, config)
        data = mujoco.MjData(b); mujoco.mj_forward(b, data)
        self.assertGreater(data.ncon, 0)
        self.assertLess(data.contact[0].dist, 0)
        self.assertGreater(np.linalg.norm(data.qfrc_constraint), 0)

    def test_collision_visual_material_inheritance(self):
        root=ET.fromstring(URDF)
        link=root.find('./link[@name="base"]')
        material=ET.SubElement(link.find('visual'),'material',name='paint')
        ET.SubElement(material,'color',rgba='1 0.2 0.3 1')
        collision=ET.SubElement(link,'collision')
        collision.append(copy.deepcopy(link.find('./visual/geometry')))
        self.parity('collision_visual_material',ET.tostring(root,encoding='unicode'),self.config)

    def test_mimic_and_floating_base(self):
        root=ET.fromstring(URDF)
        tool=root.find('./link[@name="tool"]')
        inertial=ET.SubElement(tool,'inertial')
        ET.SubElement(inertial,'mass',value='1')
        ET.SubElement(inertial,'inertia',ixx='.01',iyy='.01',izz='.01',ixy='0',ixz='0',iyz='0')
        joint=root.find('./joint[@name="tool_fixed"]');joint.set('type','continuous')
        ET.SubElement(joint,'mimic',joint='hinge',multiplier='2',offset='.1')
        self.parity('mimic',ET.tostring(root,encoding='unicode'),self.config)
        base=root.find('./link[@name="base"]');base.append(copy.deepcopy(inertial))
        self.parity('floating_base',ET.tostring(root,encoding='unicode'),dict(self.config,base_mode='floating'))

    def test_comparator_detects_physics_corruption(self):
        a = self.candidate(URDF)
        b = self.candidate(URDF)
        arm = mujoco.mj_name2id(b, mujoco.mjtObj.mjOBJ_BODY, 'arm')
        b.body_inertia[arm, 0] *= 2
        with self.assertRaises(AssertionError): compiled_semantics(a, b)
        b = self.candidate(URDF)
        b.dof_damping[0] = 1
        with self.assertRaises(AssertionError): compiled_semantics(a, b)

    def test_analytical_transform_inertia_and_acceleration(self):
        source = (Path(__file__).parent / 'fixtures/analytic_hinge.urdf').read_text()
        config = dict(self.config, attachments=[dict(name='tip', link='arm', type='frame', xyz=[.2, 0, 0], rpy=[0, 0, 0])])
        model = self.candidate(source, config)
        data = mujoco.MjData(model); mujoco.mj_forward(model, data)
        body = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'arm')
        site = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, 'tip')
        close(data.xpos[body], [1, 2, 3], 'independent body translation')
        close(data.site_xpos[site], [1, 2.2, 3], 'independent site transform')
        close(inertia_in_body(model, body), np.diag([.03, .02, .04]), 'independent rotated inertia')
        close(model.body_ipos[body], [.1, 0, 0], 'independent COM')
        model.opt.gravity[:] = 0; data.qfrc_applied[0] = 1; mujoco.mj_forward(model, data)
        close(data.qacc[0], 1 / (.04 + 2 * .1**2), 'independent parallel-axis acceleration')
        RESULTS.append(dict(case='analytic_transform_inertia_acceleration', L1=True, L2A=True))

    def test_mesh_effective_transform(self):
        from test_simplify_stl import sphere
        from simplify_stl import write_binary
        vertices, faces = sphere(1)
        write_binary(self.folder / 'part.stl', vertices, faces)
        source = URDF.replace('<box size="0.1 0.1 0.1"/>', '<mesh filename="part.stl" scale="1.2 0.8 1.5"/>')
        a, b = self.parity('mesh_effective_transform', source, self.config)
        data = mujoco.MjData(b); mujoco.mj_forward(b, data)
        base = mujoco.mj_name2id(b, mujoco.mjtObj.mjOBJ_BODY, 'base')
        geom = next(i for i in range(b.ngeom) if b.geom_bodyid[i] == base)
        expected = vertices * [1.2, .8, 1.5]
        close(np.linalg.norm(mesh_world_vertices(b, data, geom)[:, None, :] - expected[None, :, :], axis=2).min(axis=1), np.zeros(b.mesh_vertnum[b.geom_dataid[geom]]), 'independent scaled source vertices', atol=2e-7)
        RESULTS.append(dict(case='analytic_mesh_transform', L2A=True))

    def test_production_package_preparation_incremental_and_rollback(self):
        from test_simplify_stl import sphere
        from simplify_stl import write_binary
        from convert import export_package
        vertices,faces=sphere(2)
        mesh=self.folder/'part.stl';write_binary(mesh,vertices,faces)
        source=URDF.replace('<box size="0.1 0.1 0.1"/>','<mesh filename="part.stl"/>')
        self.urdf.write_text(source,encoding='utf-8')
        settings=self.folder/'mesh-settings.json'
        settings.write_text(json.dumps(dict(Enabled=True,MaximumTriangles=64,Backend='fast-simplification',CacheEnabled=True)),encoding='utf-8')
        sidecar=self.folder/'robot.sim.json'
        config=dict(self.config,product='SWSimTool',solver=dict(enabled=True,timestep=.001),attachments=[dict(name='mount',link='arm',type='frame',xyz=[.02,0,0],rpy=[0,.1,.2])],sensors=[dict(name='imu',type='imu',site='mount')])
        output=self.folder/'原生_mjcf'/'robot.xml'
        env=dict(os.environ,SWSIMTOOL_CACHE=str(self.folder/'cache'),SWSIMTOOL_MESH_CACHE=str(self.folder/'mesh-cache'))
        def run():
            sidecar.write_text(json.dumps(config),encoding='utf-8')
            result=subprocess.run(COMMAND+['--package',sys.executable,str(self.urdf),str(sidecar),str(output),str(settings)],env=env,capture_output=True,text=True,encoding='utf-8')
            return result
        first=run();self.assertEqual(first.returncode,0,first.stderr+first.stdout)
        original_inputs=(mesh.read_bytes(),self.urdf.read_bytes())
        model=load_mjcf(output)
        meshfile=output.parent/ET.parse(output).find('./asset/mesh').get('file')
        import struct
        self.assertLessEqual(struct.unpack_from('<I',meshfile.read_bytes(),80)[0],64)
        files=list(output.parent.rglob('*'))
        self.assertTrue(all(p.is_dir() or p.suffix in ('.xml','.stl') for p in files))
        config['solver']['timestep']=.002
        warm=run();self.assertEqual(warm.returncode,0,warm.stderr+warm.stdout)
        metrics=json.loads(next(x[len('Native export metrics: '):] for x in warm.stdout.splitlines() if x.startswith('Native export metrics: ')))
        self.assertEqual({k:metrics['counts'][k] for k in ('mesh_prepare','mesh_simplification','mjcf_generation','mujoco_validation')},dict(mesh_prepare=0,mesh_simplification=0,mjcf_generation=1,mujoco_validation=1))
        warm_model=load_mjcf(output)
        import shutil
        shutil.rmtree(self.folder/'cache')
        cold=run();self.assertEqual(cold.returncode,0,cold.stderr+cold.stdout)
        cold_model=load_mjcf(output);compiled_semantics(warm_model,cold_model);dynamics(warm_model,cold_model)
        from unittest.mock import patch
        with patch.dict(os.environ,env):
            reference=export_package(self.urdf,sidecar,self.folder/'python_mjcf'/'robot.xml',json.loads(settings.read_text()))
        compiled_semantics(reference,cold_model);dynamics(reference,cold_model)
        self.assertEqual(original_inputs,(mesh.read_bytes(),self.urdf.read_bytes()))
        before={str(p.relative_to(output.parent)):p.read_bytes() for p in output.parent.rglob('*') if p.is_file()}
        config['sensors'][0]['site']='missing'
        broken=run();self.assertNotEqual(broken.returncode,0)
        self.assertEqual(before,{str(p.relative_to(output.parent)):p.read_bytes() for p in output.parent.rglob('*') if p.is_file()})
        config['sensors'][0]['site']='mount'
        config['base_mode']='floating' # Root has no inertial: official compilation must reject it.
        compilation_failure=run();self.assertNotEqual(compilation_failure.returncode,0)
        self.assertIn('ERROR:',compilation_failure.stderr+compilation_failure.stdout)
        self.assertEqual(before,{str(p.relative_to(output.parent)):p.read_bytes() for p in output.parent.rglob('*') if p.is_file()})
        config['base_mode']='fixed'
        unrelated=output.parent/'keep.txt';unrelated.write_text('user data')
        refused=run();self.assertNotEqual(refused.returncode,0)
        self.assertEqual(unrelated.read_text(),'user data')
        self.assertEqual(before,{str(p.relative_to(output.parent)):p.read_bytes() for p in output.parent.rglob('*') if p.is_file() and p!=unrelated})
        RESULTS.append(dict(case='native_production_package',incremental=True,cold_parity=True,source_unchanged=True,rollback=True,unrelated_files_preserved=True))

    def test_invalid_models_rejected_without_output(self):
        invalid = [URDF.replace('xyz="0 0 1"', 'xyz="0 0 0"'), URDF.replace('ixx="0.01"', 'ixx="-0.01"'), URDF.replace('parent link="arm"', 'parent link="tool"'), URDF.replace('type="revolute"', 'type="planar"'), URDF.replace('<limit lower=', '<mimic joint="missing"/><limit lower=')]
        for index, source in enumerate(invalid):
            with self.subTest(index=index):
                self.urdf.write_text(source)
                config = self.folder / 'bad.json'; config.write_text(json.dumps(self.config))
                output = self.folder / f'bad_{index}.xml'
                result = subprocess.run(COMMAND+[ str(self.urdf), str(config), str(output)], capture_output=True)
                self.assertNotEqual(result.returncode, 0); self.assertFalse(output.exists())

    def test_local_identity_registry_and_stale_pair(self):
        self.urdf.write_text(URDF,encoding='utf-8')
        sidecar=self.folder/'robot.sim.json'
        settings=self.folder/'settings.json';settings.write_text('{"Enabled":false}')
        config=dict(self.config,product='SWSimTool',identities=dict(links=dict(base='L0',arm='L1',tool='L2'),joints=dict(hinge='J1',tool_fixed='J2')),actuators=[dict(name='drive',joint='hinge',joint_id='J1',type='motor',gear=2,ctrl_min=-1,ctrl_max=1,force_min=-7,force_max=7)])
        output=self.folder/'local_mjcf'/'robot.xml'
        def run():
            sidecar.write_text(json.dumps(config),encoding='utf-8')
            return subprocess.run(COMMAND+['--package',sys.executable,str(self.urdf),str(sidecar),str(output),str(settings)],capture_output=True,text=True,encoding='utf-8')
        result=run();self.assertEqual(result.returncode,0,result.stderr+result.stdout)
        model=load_mjcf(output);self.assertEqual(model.nu,1);self.assertEqual(model.actuator_gear[0,0],2)
        before=output.read_bytes()
        config['actuators'][0]['joint_id']='deleted'
        result=run();self.assertNotEqual(result.returncode,0);self.assertEqual(output.read_bytes(),before)
        config['actuators'][0]['joint_id']='J1'
        config['urdf_sha256']='0'*64
        result=run();self.assertNotEqual(result.returncode,0);self.assertEqual(output.read_bytes(),before)
        RESULTS.append(dict(case='local_stable_identity_and_stale_pair',preserved=True))

    def test_csharp_immutable_and_invariant_checks(self):
        result = subprocess.run(COMMAND+[ '--selftest'], capture_output=True, text=True, encoding='utf-8')
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == '__main__':
    report = ROOT / ('build/native-parity-report.json' if FRAMEWORK == 'net48' else 'build/reports/target-parity-net8.json')
    report.unlink(missing_ok=True)
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(NativeParity))
    report.write_text(json.dumps(dict(status='passed' if result.wasSuccessful() else 'failed', mujoco=mujoco.__version__, scope='synthetic candidate; no production backend switch or native CAD access', tests=result.testsRun, failures=len(result.failures), errors=len(result.errors), results=RESULTS), indent=2), encoding='utf-8')
    sys.exit(0 if result.wasSuccessful() else 1)
