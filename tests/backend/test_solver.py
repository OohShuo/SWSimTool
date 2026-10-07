
from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import contextlib
import io
import copy
import json
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import mujoco
import numpy as np
import test_convert
from solver import EQUALITY, CONTACT, validate_solver, diagnostic_sample, preview_model

class SolverTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def test_legacy_and_disabled_settings_preserve_defaults(self):
        old = self.run_convert()
        self.config['solver'] = dict(enabled=False)
        disabled = self.run_convert()
        self.assertEqual(old.opt.timestep, disabled.opt.timestep)
        np.testing.assert_array_equal(old.eq_solref, disabled.eq_solref)
        np.testing.assert_array_equal(old.geom_solref, disabled.geom_solref)

    def test_preset_and_environment_geom_parameters_compile(self):
        self.config['solver'] = dict(enabled=True)
        m = self.run_convert()
        self.assertEqual(m.opt.timestep, .001)
        self.assertEqual(m.opt.solver, mujoco.mjtSolver.mjSOL_NEWTON)
        self.assertEqual(m.opt.cone, mujoco.mjtCone.mjCONE_ELLIPTIC)
        self.assertEqual(m.opt.iterations, 100)
        self.assertEqual(m.opt.noslip_iterations, 0)
        np.testing.assert_allclose(m.eq_solref[0], [.005, 1])
        collision = np.where(m.geom_contype | m.geom_conaffinity)[0]
        np.testing.assert_allclose(m.geom_solref[collision], np.tile([.003, 1], (len(collision), 1)))
        np.testing.assert_allclose(m.geom_margin[collision], .001)
        d = mujoco.MjData(m)
        for _ in range(100):
            mujoco.mj_step(m, d)
        self.assertTrue(diagnostic_sample(m, d)['finite'])

    def test_explicit_pair_overrides_and_equality_override(self):
        self.config['solver'] = dict(enabled=True)
        self.config['equalities'][0]['solver'] = dict(EQUALITY, timeconst=.002)
        self.config['collision'] = dict(disable_internal=True, link_modes=dict(base='primitive', arm='primitive'),
            geometries=[dict(name='base_proxy', link='base', type='box', size=[.1]*3, xyz=[0]*3, rpy=[0]*3),
                        dict(name='arm_proxy', link='arm', type='box', size=[.1]*3, xyz=[0]*3, rpy=[0]*3)],
            allowed_pairs=[dict(link1='base', link2='arm', solver=dict(CONTACT, margin=.002))])
        m = self.run_convert()
        self.assertEqual(m.npair, 1)
        np.testing.assert_allclose(m.pair_solref[0], [.003, 1])
        np.testing.assert_allclose(m.pair_solimp[0], [.99, .995, .001, .5, 2])
        self.assertEqual(m.pair_margin[0], .002)
        np.testing.assert_allclose(m.eq_solref[0], [.002, 1])
        self.assertEqual(m.geom('arm_proxy').margin, .001)
        self.config['collision']['allowed_pairs'][0].pop('solver')
        inherited = self.run_convert()
        self.assertEqual(inherited.pair_margin[0], .001)

    def test_local_override_without_global_preset(self):
        self.config['equalities'][0]['solver'] = dict(EQUALITY, timeconst=.003)
        m = self.run_convert()
        self.assertEqual(m.opt.timestep, .002)
        np.testing.assert_allclose(m.eq_solref[0], [.003, 1])

    def test_invalid_numbers_ranges_and_integer_options_rejected(self):
        for key, value in [('timestep', 0), ('iterations', 1.5), ('noslip_iterations', -1), ('impratio', True), ('tolerance', float('nan'))]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_solver(dict(solver=dict(enabled=True, **{key:value})))
        for key, value in [('dmin', 1), ('dmax', .8), ('timeconst', -.003), ('midpoint', 0), ('width', 0), ('power', .5), ('margin', -1), ('condim', 2)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_solver(dict(collision=dict(allowed_pairs=[dict(solver=dict(CONTACT, **{key:value}))])))

    def test_diagnostics_distances_and_restores_callback_on_viewer_failure(self):
        self.config["attachments"][0]["xyz"] = [.2, .4, .4]
        self.config["attachments"][1]["xyz"] = [.1, 0, 0]
        m = self.run_convert(); d = mujoco.MjData(m); mujoco.mj_forward(m,d)
        sample = diagnostic_sample(m,d)
        self.assertLess(sample['site_separation_m'], 1e-6)
        d.qpos[0] = .5; mujoco.mj_forward(m,d)
        self.assertGreater(diagnostic_sample(m,d)['site_separation_m'], 0)
        previous = mujoco.get_mjcb_control()
        def run_viewer(model):
            state = mujoco.MjData(model)
            for _ in range(10):
                mujoco.mj_step(model, state)
            self.assertTrue(np.isfinite(state.qpos).all())
        with patch('mujoco.viewer.launch', side_effect=run_viewer), contextlib.redirect_stdout(io.StringIO()) as output:
            preview_model(m)
        self.assertEqual(output.getvalue(), '')
        self.assertIs(mujoco.get_mjcb_control(), previous)

        with patch('mujoco.viewer.launch', side_effect=RuntimeError('test close')), self.assertRaises(RuntimeError):
            preview_model(m)
        self.assertIs(mujoco.get_mjcb_control(), previous)

    def test_diagnostics_measures_physical_penetration(self):
        model = mujoco.MjModel.from_xml_string('''<mujoco><worldbody>
          <geom type="plane" size="1 1 .1" margin=".002"/>
          <body pos="0 0 .05"><freejoint/><geom type="sphere" size=".1"/></body>
        </worldbody></mujoco>''')
        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        sample = diagnostic_sample(model, data)
        self.assertEqual(sample['contacts'], 1)
        self.assertAlmostEqual(sample['penetration_m'], .05)
        self.assertTrue(sample['finite'])
