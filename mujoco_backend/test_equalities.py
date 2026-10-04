import hashlib
import unittest
import xml.etree.ElementTree as ET
import mujoco
import numpy as np
import test_convert


class EqualityTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def test_legacy_sites_and_disabled_constraint(self):
        self.config['equalities'][0]['active'] = False
        model = self.run_convert()
        self.assertFalse(model.eq_active0[0])
        self.assertEqual(model.neq, 1)

    def test_joint_fixed_offset_and_solver(self):
        self.config['equalities'] = [dict(name='lock', type='joint', joint1='hinge',
                                        polycoef=[.2, 1, 0, 0, 0], solver=dict(timeconst=.003))]
        model = self.run_convert()
        self.assertEqual(model.eq_type[0], mujoco.mjtEq.mjEQ_JOINT)
        self.assertAlmostEqual(model.eq_data[0, 0], .2)
        np.testing.assert_allclose(model.eq_solref[0], [.003, 1])
        self.assertEqual(model.eq_obj2id[0], -1)

    def test_joint_coupling_and_explicit_coefficients(self):
        extra = '<link name="second"><inertial><mass value="1"/><inertia ixx=".01" ixy="0" ixz="0" iyy=".01" iyz="0" izz=".01"/></inertial></link><joint name="other" type="continuous"><parent link="base"/><child link="second"/><axis xyz="1 0 0"/></joint>'
        self.urdf.write_text(test_convert.URDF.replace('</robot>', extra + '</robot>'))
        self.config['urdf_sha256'] = hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        self.config['equalities'] = [dict(name='couple', type='joint', joint1='hinge',
                                        joint2='other', polycoef=[0, -2, .3, 0, 0])]
        model = self.run_convert()
        np.testing.assert_allclose(model.eq_data[0, :5], [0, -2, .3, 0, 0])
        self.assertGreaterEqual(model.eq_obj2id[0], 0)

    def test_body_connect_static_root_and_world(self):
        self.config['equalities'] = [dict(name='body_closure', type='connect', binding='body',
                                        body1='base', body2='arm', anchor=[0, 0, .1])]
        model = self.run_convert()
        self.assertGreaterEqual(mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'base'), 0)
        self.assertEqual(model.eq_type[0], mujoco.mjtEq.mjEQ_CONNECT)
        self.config['equalities'][0].update(body1='arm', body2='')
        model = self.run_convert()
        self.assertEqual(model.eq_obj2id[0], 0)

    def test_body_weld_pose_and_torquescale(self):
        self.config['equalities'] = [dict(name='weld_body', type='weld', binding='body',
                                        body1='base', body2='arm', pose_mode='custom',
                                        position=[0, 0, .1], orientation=[0, 0, .2], torquescale=.05)]
        model = self.run_convert()
        self.assertEqual(model.eq_type[0], mujoco.mjtEq.mjEQ_WELD)
        xml = ET.parse(self.output).find('equality/weld')
        self.assertAlmostEqual(float(xml.get('torquescale')), .05)
        pose = [float(x) for x in xml.get('relpose').split()]
        np.testing.assert_allclose(pose[:3], [0, 0, .1])
        self.assertAlmostEqual(pose[3], np.cos(.1))

    def test_site_weld_preserves_frame_orientation(self):
        for site in self.config['attachments'][:2]:
            site.update(type='frame', rpy=[.1, .2, .3])
        self.config['equalities'] = [dict(name='frame_weld', type='weld', site1='anchor_base',
                                        site2='anchor_arm', torquescale=.02)]
        model = self.run_convert()
        self.assertEqual(model.eq_type[0], mujoco.mjtEq.mjEQ_WELD)
        self.assertEqual(ET.parse(self.output).find('equality/weld').get('torquescale'), '0.02')

    def test_converted_equalities_are_retained(self):
        from equalities import apply_equalities
        from solver import effective
        root = ET.fromstring('<mujoco><equality><joint name="original" joint1="a"/></equality></mujoco>')
        config = dict(equalities=[dict(name='added', type='joint', joint1='b')])
        apply_equalities(root, config, effective)
        self.assertEqual(len(root.findall('equality')), 1)
        self.assertEqual([e.get('name') for e in root.find('equality')], ['original', 'added'])

    def test_invalid_constraints_keep_previous_output(self):
        for equality in [dict(type='joint', joint1='missing'),
                         dict(type='joint', joint1='hinge', joint2='hinge'),
                         dict(type='joint', joint1='hinge', polycoef=[0, 1]),
                         dict(type='weld', binding='body', body1='base', body2='arm', torquescale=-1),
                         dict(type='connect', binding='body', body1='missing'),
                         dict(type='connect', site1='a', site2='b', active='false')]:
            with self.subTest(equality=equality):
                self.output.write_text('existing output')
                self.config['equalities'] = [dict(name='bad', **equality)]
                with self.assertRaises(ValueError):
                    self.run_convert()
                self.assertEqual(self.output.read_text(), 'existing output')

    def test_driven_defaults_manual_zero_and_actuator_removal(self):
        self.config['joint_defaults'] = True
        model = self.run_convert()
        np.testing.assert_allclose([model.dof_damping[0], model.dof_frictionloss[0], model.dof_armature[0]], [.01, .01, .001])
        self.config['joints'] = [dict(joint='hinge', damping=0, armature=.02)]
        model = self.run_convert()
        np.testing.assert_allclose([model.dof_damping[0], model.dof_frictionloss[0], model.dof_armature[0]], [0, .01, .02])
        self.config['actuators'] = []
        model = self.run_convert()
        np.testing.assert_allclose([model.dof_damping[0], model.dof_frictionloss[0], model.dof_armature[0]], [0, .001, .02])
        self.config['joints'] = []
        model = self.run_convert()
        np.testing.assert_allclose([model.dof_damping[0], model.dof_frictionloss[0], model.dof_armature[0]], [.001, .001, 0])
