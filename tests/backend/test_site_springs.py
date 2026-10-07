
from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import unittest
import xml.etree.ElementTree as ET
import mujoco
import numpy as np
import test_convert
from convert import load_mjcf
from site_forces import apply_site_forces, resolve_initial_spring_lengths

class SiteSpringTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def setup_spring(self):
        self.config['equalities'] = []
        self.config['actuators'] = []
        self.config['sensors'] = []
        self.config['attachments'][0]['xyz'] = [0, 0, 0]
        self.config['attachments'][1]['xyz'] = [.1, 0, 0]
        self.config['site_forces'] = [dict(name='pair', type='spring', stiffness=100, damping=0, length_mode='initial', site1='anchor_base', site2='anchor_arm')]

    def test_initial_length_matches_qpos0_and_no_control_slot(self):
        self.setup_spring()
        self.config['joints'] = [dict(joint='hinge', ref=.5)]
        original = self.urdf.read_bytes()
        model = self.run_convert()
        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        np.testing.assert_allclose(model.tendon_lengthspring[0], data.ten_length[0], atol=1e-12)
        np.testing.assert_allclose(data.qfrc_passive, 0, atol=1e-10)
        self.assertEqual((model.nu, model.ntendon, model.neq), (0, 1, 0))
        self.assertEqual(original, self.urdf.read_bytes())
        self.assertAlmostEqual(float(ET.parse(self.output).find('tendon/spatial').get('springlength')), data.ten_length[0])
        loaded = load_mjcf(self.output)
        np.testing.assert_allclose(loaded.tendon_lengthspring, model.tendon_lengthspring)

    def free_model(self, stiffness, damping, rest):
        root = ET.fromstring('<mujoco><option gravity="0 0 0"/><worldbody><body name="a"><freejoint/><geom type="sphere" size=".05" mass="1"/><site name="A"/></body><body name="b" pos="1 0 0"><freejoint/><geom type="sphere" size=".05" mass="1"/><site name="B"/></body></worldbody></mujoco>')
        config = dict(site_forces=[dict(name='pair', site1='A', site2='B', type='spring', stiffness=stiffness, damping=damping, length_mode='custom', rest_length=rest)])
        apply_site_forces(root, config)
        return mujoco.MjModel.from_xml_string(ET.tostring(root, encoding='unicode'))

    def test_hooke_extension_compression_and_rest(self):
        model = self.free_model(100, 0, .5)
        data = mujoco.MjData(model)
        for length, force_on_b in [(1, -50), (.3, 20), (.5, 0)]:
            data.qpos[7] = length
            mujoco.mj_forward(model, data)
            np.testing.assert_allclose(data.qfrc_passive[:3], [-force_on_b, 0, 0], atol=1e-10)
            np.testing.assert_allclose(data.qfrc_passive[6:9], [force_on_b, 0, 0], atol=1e-10)

    def test_damping_opposes_relative_length_velocity(self):
        model = self.free_model(0, 2, 1)
        data = mujoco.MjData(model)
        for velocity in [-2, 0, 2]:
            data.qvel[6] = velocity
            mujoco.mj_forward(model, data)
            np.testing.assert_allclose(data.qfrc_passive[6:9], [-2*velocity, 0, 0], atol=1e-10)
            np.testing.assert_allclose(data.qfrc_passive[:3], [2*velocity, 0, 0], atol=1e-10)

    def test_damped_spring_steps_are_finite(self):
        model = self.free_model(100, 2, .8)
        data = mujoco.MjData(model)
        for _ in range(200):
            mujoco.mj_step(model, data)
        self.assertTrue(np.isfinite(data.qpos).all())
        self.assertEqual(model.nu, 0)

    def test_invalid_active_fields_preserve_output(self):
        for fields in [dict(stiffness=-1), dict(damping=-1), dict(stiffness=float('nan')),
                       dict(length_mode='wrong'), dict(length_mode='custom', rest_length=-1)]:
            self.setup_spring()
            self.config['site_forces'][0].update(fields)
            self.output.write_text('previous output')
            with self.assertRaises(ValueError):
                self.run_convert()
            self.assertEqual(self.output.read_text(), 'previous output')

    def test_disabled_and_irrelevant_force_values(self):
        self.setup_spring()
        self.config['site_forces'][0].update(magnitude=-20, enabled=False)
        model = self.run_convert()
        self.assertEqual((model.ntendon, model.nu), (0, 0))
        self.config['site_forces'][0]['enabled'] = True
        model = self.run_convert()
        self.assertEqual((model.ntendon, model.nu), (1, 0))

    def test_coincident_active_spring_is_rejected(self):
        self.setup_spring()
        self.config['attachments'][0]['xyz'] = [.2, .3, .4]
        self.config['attachments'][1]['xyz'] = [0, 0, 0]
        with self.assertRaisesRegex(ValueError, 'coincide initially'):
            self.run_convert()
