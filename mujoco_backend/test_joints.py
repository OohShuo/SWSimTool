import hashlib
import copy
import unittest
import xml.etree.ElementTree as ET
import mujoco
import numpy as np
import test_convert
from joints import validate_joints


class JointTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def configure(self, **values):
        self.config['joints'] = [dict(joint='hinge', **values)]

    def test_scalar_physics_and_independent_joint_settings(self):
        self.configure(damping=.02, frictionloss=.05, armature=.001,
                       stiffness=10, springref=.2, ref=.1)
        before = self.urdf.read_bytes()
        model = self.run_convert()
        j = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_JOINT, 'hinge')
        dof = model.jnt_dofadr[j]
        self.assertAlmostEqual(model.dof_damping[dof], .02)
        self.assertAlmostEqual(model.dof_frictionloss[dof], .05)
        self.assertAlmostEqual(model.dof_armature[dof], .001)
        self.assertAlmostEqual(model.jnt_stiffness[j], 10)
        self.assertAlmostEqual(model.qpos0[model.jnt_qposadr[j]], .1)
        self.assertEqual(self.urdf.read_bytes(), before)
        self.assertEqual(self.config['actuators'][0]['gain'], 10)

    def test_limit_defaults_axis_and_local_position(self):
        self.configure(pos=[.01,.02,.03], axis=[1,0,0], limit_mode='custom', lower=-.4, upper=.6,
                       margin=.01, limit_solver=dict(timeconst=.003,dmin=.99,dmax=.995))
        model = self.run_convert()
        j = mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_JOINT,'hinge')
        np.testing.assert_allclose(model.jnt_pos[j], [.01,.02,.03])
        np.testing.assert_allclose(model.jnt_axis[j], [1,0,0])
        np.testing.assert_allclose(model.jnt_range[j], [-.4,.6])
        np.testing.assert_allclose(model.jnt_solref[j], [.003,1])
        np.testing.assert_allclose(model.jnt_solimp[j], [.99,.995,.001,.5,2])
        self.assertAlmostEqual(model.jnt_margin[j], .01)

    def test_slide_override_and_explicit_no_limits(self):
        self.config['equalities']=[]
        self.configure(type='slide', limit_mode='none', damping=0)
        model=self.run_convert()
        j=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_JOINT,'hinge')
        self.assertEqual(model.jnt_type[j],mujoco.mjtJoint.mjJNT_SLIDE)
        self.assertFalse(model.jnt_limited[j])
        self.configure(type='slide')
        with self.assertRaisesRegex(ValueError,'explicit limits'):self.run_convert()

    def test_floating_and_fixed_base_preserve_sites_and_bodies(self):
        self.urdf.write_text(test_convert.URDF.replace('<link name="base">','<link name="base"><inertial><mass value="2"/><inertia ixx=".01" ixy="0" ixz="0" iyy=".01" iyz="0" izz=".01"/></inertial>'))
        self.config['urdf_sha256']=hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        self.config['base_mode']='floating'
        model=self.run_convert()
        self.assertEqual(model.nv,7)
        j=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_JOINT,'__sw2mujoco_base_free')
        self.assertEqual(model.jnt_type[j],mujoco.mjtJoint.mjJNT_FREE)
        self.assertEqual(model.nsite,3)
        self.config['base_mode']='fixed'
        model=self.run_convert()
        self.assertEqual(model.nv,1)

    def test_missing_floating_base_inertia_is_not_guessed(self):
        self.config['base_mode']='floating'
        with self.assertRaisesRegex(ValueError,'mass and inertia'):self.run_convert()

    def test_other_joint_is_not_modified(self):
        extra='<link name="second"><inertial><mass value="1"/><inertia ixx=".01" ixy="0" ixz="0" iyy=".01" iyz="0" izz=".01"/></inertial></link><joint name="other" type="continuous"><parent link="base"/><child link="second"/><axis xyz="1 0 0"/><dynamics damping=".07"/></joint>'
        self.urdf.write_text(test_convert.URDF.replace('</robot>',extra+'</robot>'))
        self.config['urdf_sha256']=hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        self.configure(damping=.02,armature=.001)
        model=self.run_convert()
        j=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_JOINT,'other')
        self.assertAlmostEqual(model.dof_damping[model.jnt_dofadr[j]],.07)
        self.assertEqual(model.dof_armature[model.jnt_dofadr[j]],0)

    def test_joint_force_limit_is_after_gear(self):
        self.config['equalities']=[]
        self.config['actuators']=[dict(name='one',joint='hinge',type='motor',gear=10,ctrl_min=-10,ctrl_max=10,force_min=-10,force_max=10),
                                  dict(name='two',joint='hinge',type='motor',gear=1,ctrl_min=-10,ctrl_max=10,force_min=-10,force_max=10)]
        self.config['joint_force_limits']=[dict(joint='hinge',lower=-7,upper=7)]
        model=self.run_convert();data=mujoco.MjData(model);data.ctrl[:]=1;mujoco.mj_forward(model,data)
        self.assertAlmostEqual(data.qfrc_actuator[0],7)
        np.testing.assert_allclose(data.actuator_force,[1,1])

    def test_invalid_overrides_preserve_previous_output(self):
        for settings in [dict(damping=-1),dict(armature=float('nan')),dict(axis=[0,0,0]),dict(type='ball'),
                         dict(limit_mode='custom',lower=1,upper=0),dict(pos=[1,2]),dict(limit_solver=dict(timeconst=0))]:
            with self.subTest(settings=settings):
                self.output.write_text('previous');self.configure(**settings)
                with self.assertRaises(ValueError):self.run_convert()
                self.assertEqual(self.output.read_text(),'previous')

    def test_fixed_unknown_duplicate_and_shared_force_limits_rejected(self):
        robot=ET.fromstring(test_convert.URDF)
        for values in [[dict(joint='tool_fixed')],[dict(joint='missing')],[dict(joint='hinge'),dict(joint='hinge')]]:
            with self.assertRaises(ValueError):validate_joints(dict(joints=values),robot)
        with self.assertRaises(ValueError):validate_joints(dict(joint_force_limits=[dict(joint='hinge',lower=1,upper=0)]),robot)

    def test_legacy_null_and_explicit_zero(self):
        old=self.run_convert()
        self.configure(damping=None,armature=None,frictionloss=None,stiffness=None,ref=None)
        model=self.run_convert()
        np.testing.assert_allclose(model.dof_damping,old.dof_damping)
        self.configure(damping=0,armature=0,frictionloss=0,limit_mode='none',stiffness=0)
        model=self.run_convert()
        self.assertEqual(model.dof_damping[0],0)


if __name__=='__main__':unittest.main()
