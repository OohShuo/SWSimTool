"""Candidate compiler/parity/analytical gate. Synthetic files only, no CAD access."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'mujoco_backend'))
import mujoco
import numpy as np
from convert import convert, load_mjcf
from test_convert import URDF
from semantic_compare import compiled_semantics, dynamics, inertia_in_body, mesh_world_vertices, close

EXE = ROOT / 'build/native-parity/SW2MuJoCo.CandidateRunner.exe'
RESULTS = []


class NativeParity(unittest.TestCase):
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
        result = subprocess.run([str(EXE), str(self.urdf), str(sidecar), str(output)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        return load_mjcf(output)

    def parity(self, name, source, config, steps=(1, 10, 100), precision=1e-9, dynamic_precision=2e-7):
        b = self.candidate(source, config)
        config = copy.deepcopy(config)
        config['urdf_sha256'] = hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        sidecar = self.folder / 'reference.sim.json'
        sidecar.write_text(json.dumps(config), encoding='utf-8')
        a = convert(self.urdf, sidecar, self.folder / 'reference/robot.xml')
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

    def test_invalid_models_rejected_without_output(self):
        invalid = [URDF.replace('xyz="0 0 1"', 'xyz="0 0 0"'), URDF.replace('ixx="0.01"', 'ixx="-0.01"'), URDF.replace('parent link="arm"', 'parent link="tool"'), URDF.replace('type="revolute"', 'type="planar"'), URDF.replace('<limit lower=', '<mimic joint="missing"/><limit lower=')]
        for index, source in enumerate(invalid):
            with self.subTest(index=index):
                self.urdf.write_text(source)
                config = self.folder / 'bad.json'; config.write_text(json.dumps(self.config))
                output = self.folder / f'bad_{index}.xml'
                result = subprocess.run([str(EXE), str(self.urdf), str(config), str(output)], capture_output=True)
                self.assertNotEqual(result.returncode, 0); self.assertFalse(output.exists())

    def test_csharp_immutable_and_invariant_checks(self):
        result = subprocess.run([str(EXE), '--selftest'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == '__main__':
    report = ROOT / 'build/native-parity-report.json'
    report.unlink(missing_ok=True)
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(NativeParity))
    report.write_text(json.dumps(dict(status='passed' if result.wasSuccessful() else 'failed', mujoco=mujoco.__version__, scope='synthetic candidate; no production backend switch or native CAD access', tests=result.testsRun, failures=len(result.failures), errors=len(result.errors), results=RESULTS), indent=2), encoding='utf-8')
    sys.exit(0 if result.wasSuccessful() else 1)
