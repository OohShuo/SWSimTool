
from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import hashlib
import json
import unittest
import xml.etree.ElementTree as ET
import mujoco
import numpy as np
import test_convert
from convert import load_mjcf, export_package

class SiteForceTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def force_config(self, kind='pull'):
        self.config['equalities'] = []
        self.config['actuators'] = []
        self.config['sensors'] = []
        self.config['attachments'][0]['xyz'] = [0, 0, 0]
        self.config['attachments'][1]['xyz'] = [.1, .02, 0]
        self.config['site_forces'] = [dict(name='pair', type=kind, magnitude=10,
            site1='anchor_base', site2='anchor_arm', enabled=True)]

    def expected(self, model, data, force):
        ids = [mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, name) for name in ['anchor_base', 'anchor_arm']]
        delta = data.site_xpos[ids[1]] - data.site_xpos[ids[0]]
        unit = delta / np.linalg.norm(delta)
        total = np.zeros(model.nv)
        for site, sign in zip(ids, [1, -1]):
            jac = np.zeros((3, model.nv))
            rot = np.zeros((3, model.nv))
            mujoco.mj_jacSite(model, data, jac, rot, site)
            total += jac.T @ (unit * force * sign)
        return total

    def test_pull_and_push_direction_and_constant_magnitude(self):
        for kind, value in [('pull', 10), ('push', -10)]:
            self.force_config(kind)
            model = self.run_convert()
            data = mujoco.MjData(model)
            for q in [-.3, 0, .4]:
                data.qpos[0] = q
                data.ctrl[:] = 12345
                mujoco.mj_forward(model, data)
                np.testing.assert_allclose(data.qfrc_actuator, self.expected(model, data, value), atol=1e-10)
                self.assertAlmostEqual(data.actuator_force[0], -value)
                self.assertEqual(model.ntendon, 1)
                self.assertEqual(model.neq, 0)
                self.assertEqual(model.nv, 1)

    def test_free_bodies_have_equal_opposite_force_and_offset_torque(self):
        from site_forces import apply_site_forces
        root = ET.fromstring('<mujoco><option gravity="0 0 0"/><worldbody><body name="a"><freejoint/><geom type="sphere" size=".05" mass="1"/><site name="A" pos="0 .1 0"/></body><body name="b" pos="1 0 0"><freejoint/><geom type="sphere" size=".05" mass="1"/><site name="B" pos="0 .1 0"/></body></worldbody></mujoco>')
        config = dict(site_forces=[dict(name='pair', site1='A', site2='B', type='pull', magnitude=10)])
        apply_site_forces(root, config)
        model = mujoco.MjModel.from_xml_string(ET.tostring(root, encoding='unicode'))
        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        np.testing.assert_allclose(data.qfrc_actuator[:3], [10, 0, 0], atol=1e-12)
        np.testing.assert_allclose(data.qfrc_actuator[6:9], [-10, 0, 0], atol=1e-12)
        np.testing.assert_allclose(data.qfrc_actuator[3:6], [0, 0, -1], atol=1e-12)
        np.testing.assert_allclose(data.qfrc_actuator[9:12], [0, 0, 1], atol=1e-12)
        for _ in range(50):
            mujoco.mj_step(model, data)
            np.testing.assert_allclose(data.qfrc_actuator[:3], -data.qfrc_actuator[6:9], atol=1e-10)
            self.assertAlmostEqual(np.linalg.norm(data.qfrc_actuator[:3]), 10)
        self.assertTrue(np.isfinite(data.qpos).all())


    def test_disabled_and_zero(self):
        self.force_config()
        self.config['site_forces'][0]['enabled'] = False
        model = self.run_convert()
        self.assertEqual((model.ntendon, model.nu), (0, 0))
        self.config['site_forces'][0].update(enabled=True, magnitude=0)
        model = self.run_convert()
        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        np.testing.assert_allclose(data.qfrc_actuator, 0)

    def test_coincident_endpoints_fail_before_publishing(self):
        self.force_config()
        self.config['attachments'][0]['xyz'] = [.2, .3, .4]
        self.config['attachments'][1]['xyz'] = [0, 0, 0]
        self.output.write_text('previous output')
        with self.assertRaisesRegex(ValueError, 'coincide initially'):
            self.run_convert()
        self.assertEqual(self.output.read_text(), 'previous output')

    def test_invalid_parameters_and_sites(self):
        for invalid, message in [({'magnitude': -1}, 'nonnegative'),
            ({'magnitude': float('nan')}, 'finite'), ({'type': 'unknown'}, 'pull or push'),
            ({'enabled': 1}, 'boolean'), ({'site2': 'missing'}, 'different existing sites'),
            ({'site2': 'anchor_base'}, 'different existing sites'),
            ({'site1': 'imu_mount'}, 'different links')]:
            self.force_config()
            if invalid.get('site1') == 'imu_mount':
                self.config['attachments'][2]['link'] = 'arm'
            self.config['site_forces'][0].update(invalid)
            self.output.write_text('previous output')
            with self.assertRaisesRegex(ValueError, message):
                self.run_convert()
            self.assertEqual(self.output.read_text(), 'previous output')

    def test_rename_and_deleted_identity(self):
        self.force_config()
        for i, site in enumerate(self.config['attachments']):
            site['id'] = str(i)
        force = self.config['site_forces'][0]
        force.update(site1_id='0', site2_id='1')
        self.config['attachments'][0]['name'] = 'renamed'
        model = self.run_convert()
        self.assertGreaterEqual(mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, 'renamed'), 0)
        self.config['attachments'][0]['id'] = 'replacement'
        with self.assertRaisesRegex(ValueError, 'deleted or is missing'):
            self.run_convert()

    def test_standalone_load_and_unchanged_urdf(self):
        self.force_config()
        original = self.urdf.read_bytes()
        model = self.run_convert()
        self.assertEqual(original, self.urdf.read_bytes())
        self.urdf.unlink()
        self.sidecar.unlink()
        loaded = load_mjcf(self.output)
        data = mujoco.MjData(loaded)
        mujoco.mj_forward(loaded, data)
        self.assertAlmostEqual(data.actuator_force[0], -10)
        root = ET.parse(self.output)
        self.assertEqual(root.find('actuator/general').get('gainprm'), '0')
        self.assertEqual(model.neq, 0)

    def test_clean_package_contains_only_xml_and_meshes(self):
        self.force_config()
        self.sidecar.write_text(json.dumps(self.config))
        original = self.urdf.read_bytes()
        model = export_package(self.urdf, self.sidecar, self.directory / 'robot_mjcf' / 'robot.xml')
        self.assertEqual(model.ntendon, 1)
        self.assertEqual(original, self.urdf.read_bytes())
        self.assertEqual(sorted(p.name for p in (self.directory / 'robot_mjcf').iterdir()), ['robot.xml'])
