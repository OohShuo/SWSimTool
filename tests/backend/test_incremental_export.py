
from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import json
import shutil
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
import numpy as np
import mujoco
from convert import export_package, load_mjcf
from incremental import LAST_METRICS, canonical, cache_root
import test_package_export

class IncrementalExportTests(unittest.TestCase):
    setUp=test_package_export.PackageExportTests.setUp
    tearDown=test_package_export.PackageExportTests.tearDown
    refresh_hash=test_package_export.PackageExportTests.refresh_hash

    def run_export(self):
        return export_package(self.urdf,self.sidecar,self.output,self.settings)

    def config(self, **values):
        config=json.loads(self.sidecar.read_text())
        config.update(values)
        self.sidecar.write_text(json.dumps(config))

    def assert_semantics(self, first, second):
        for name in ['nbody','njnt','ngeom','nmesh','nsite','nu','nsensor','neq','ntendon','nq','nv']:
            self.assertEqual(getattr(first,name),getattr(second,name),name)
        for name in ['names','body_parentid','body_pos','body_quat','body_ipos','body_iquat','body_mass','body_inertia',
                     'jnt_type','jnt_pos','jnt_axis','jnt_range','dof_damping','dof_armature',
                     'geom_type','geom_bodyid','geom_pos','geom_quat','geom_size','geom_friction',
                     'geom_contype','geom_conaffinity','geom_solref','geom_solimp','geom_margin','site_pos','site_quat','site_bodyid',
                     'actuator_trnid','actuator_gainprm','actuator_biasprm','actuator_gear','actuator_ctrlrange','actuator_forcerange','sensor_type','sensor_objid',
                     'eq_type','eq_obj1id','eq_obj2id','eq_data','eq_solref','eq_solimp',
                     'tendon_stiffness','tendon_damping','tendon_lengthspring','qpos0']:
            a,b=getattr(first,name),getattr(second,name)
            if isinstance(a,bytes): self.assertEqual(a,b,name)
            else: np.testing.assert_allclose(a,b,rtol=1e-12,atol=1e-12,err_msg=name)
        for name in ['timestep','solver','integrator','iterations','noslip_iterations','tolerance','impratio','gravity']:
            np.testing.assert_allclose(getattr(first.opt,name),getattr(second.opt,name),rtol=1e-12,atol=1e-12,err_msg='option '+name)
        for position,velocity in [(0,0),(.15,.2),(-.1,-.3)]:
            a,b=mujoco.MjData(first),mujoco.MjData(second)
            if first.nq:
                a.qpos[0]+=position;b.qpos[0]+=position
                a.qvel[0]=velocity;b.qvel[0]=velocity
            for model,data in [(first,a),(second,b)]: mujoco.mj_forward(model,data)
            for name in ['xpos','xquat','site_xpos','site_xmat','ten_length','ten_velocity','qfrc_passive','qfrc_actuator','qfrc_constraint','sensordata']:
                np.testing.assert_allclose(getattr(a,name),getattr(b,name),rtol=1e-9,atol=1e-9,err_msg=name)
            for _ in range(10):
                mujoco.mj_step(first,a);mujoco.mj_step(second,b)
            for name in ['qpos','qvel']:
                self.assertTrue(np.isfinite(getattr(a,name)).all())
                np.testing.assert_allclose(getattr(a,name),getattr(b,name),rtol=1e-9,atol=1e-9,err_msg='stepped '+name)

    def test_solver_only_zero_mesh_work_one_generation_one_validation(self):
        self.config(solver=dict(enabled=True,timestep=.001))
        self.run_export()
        self.config(solver=dict(enabled=True,timestep=.002))
        with patch('mesh_cache.prepare_mesh',side_effect=AssertionError('mesh pipeline forbidden')), patch('mesh_cache.simplify',side_effect=AssertionError('simplification forbidden')):
            incremental=self.run_export()
        self.assertEqual(LAST_METRICS['counts'],dict(mesh_prepare=0,mesh_simplification=0,mjcf_generation=1,mujoco_validation=1,base_generation=0))
        self.assertEqual(incremental.opt.timestep,.002)
        xml=self.output.read_bytes()
        clear_fixture_caches(self)
        full=self.run_export()
        self.assert_semantics(incremental,full)
        self.assertEqual(ET.canonicalize(xml_data=xml.decode()),ET.canonicalize(xml_data=self.output.read_text(encoding='utf-8')))
        self.assertEqual(sorted(p.name for p in self.output.parent.iterdir()),['meshes','robot.xml'])

    def test_overlays_deleted_without_stale_nodes_and_match_full(self):
        self.run_export()
        attachments=[dict(id='A',name='A',type='frame',link='base',xyz=[0,0,0],rpy=[0,0,0]),dict(id='B',name='B',type='frame',link='arm',xyz=[.1,0,0],rpy=[0,0,0])]
        self.config(attachments=attachments,sensors=[dict(name='imu',type='imu',site='A',site_id='A')],
                    actuators=[dict(name='drive',type='motor',joint='hinge',ctrl_min=-1,ctrl_max=1,force_min=-7,force_max=7)],
                    equalities=[dict(name='closed',type='connect',binding='site',site1='A',site2='B',site1_id='A',site2_id='B')],
                    site_forces=[dict(name='spring',type='spring',stiffness=100,damping=2,length_mode='initial',site1='A',site2='B',site1_id='A',site2_id='B')])
        incremental=self.run_export()
        self.assertEqual(LAST_METRICS['counts']['mesh_prepare'],0)
        full=load_mjcf(self.output)
        self.assert_semantics(incremental,full)
        self.config(attachments=[],sensors=[],actuators=[],equalities=[],site_forces=[])
        empty=self.run_export()
        self.assertEqual((empty.nsite,empty.nsensor,empty.nu,empty.neq,empty.ntendon),(0,0,0,0,0))
        shutil.rmtree(cache_root())
        self.assert_semantics(empty,self.run_export())

    def test_corrupt_cache_and_deleted_cache_fall_back(self):
        self.run_export()
        next((cache_root()/'projects').glob('*/.internal/base.xml')).write_text('corrupt')
        model=self.run_export()
        self.assertEqual(LAST_METRICS['counts']['base_generation'],1)
        shutil.rmtree(cache_root())
        self.assert_semantics(model,self.run_export())

    def test_source_mutation_and_budget_invalidate(self):
        self.run_export()
        self.settings['MaximumTriangles']=64
        self.run_export()
        self.assertGreater(LAST_METRICS['counts']['mesh_prepare'],0)
        self.urdf.write_bytes(self.urdf.read_bytes()+b'\n')
        self.refresh_hash()
        self.run_export()
        self.assertGreater(LAST_METRICS['counts']['base_generation'],0)
        test_package_export.write_binary(self.meshes[0], *test_package_export.sphere(1))
        self.run_export()
        self.assertEqual(LAST_METRICS['counts']['mesh_prepare'],1)

    def test_validation_failure_retains_package_and_cache(self):
        self.run_export()
        before=self.output.read_bytes()
        files={str(p):p.read_bytes() for p in cache_root().rglob('*') if p.is_file()}
        self.config(solver=dict(enabled=True,timestep=.002))
        with patch('convert.load_mjcf',side_effect=RuntimeError('failure')):
            with self.assertRaisesRegex(RuntimeError,'failure'): self.run_export()
        self.assertEqual(self.output.read_bytes(),before)
        self.assertEqual(files,{str(p):p.read_bytes() for p in cache_root().rglob('*') if p.is_file()})

    def test_disabled_cache_and_reserved_output_filename(self):
        self.output=self.output.with_name('base.xml')
        self.run_export()
        self.config(solver=dict(enabled=True,timestep=.003))
        incremental=self.run_export()
        self.settings['CacheEnabled']=False
        full=self.run_export()
        self.assert_semantics(incremental,full)
        self.assertGreater(LAST_METRICS['counts']['mesh_prepare'],0)

    def test_canonical_dictionary_order(self):
        self.assertEqual(canonical(dict(b=[1,2],a=0)),canonical(dict(a=0,b=[1,2])))
        self.assertNotEqual(canonical(dict(a=[1,2])),canonical(dict(a=[2,1])))

    def test_recreated_source_directory_reuses_content_fingerprints(self):
        self.run_export()
        next_folder=self.folder.with_name('recreated_source')
        shutil.copytree(self.folder,next_folder)
        self.urdf=next_folder/self.urdf.name
        self.sidecar=next_folder/self.sidecar.name
        with patch('mesh_cache.prepare_mesh',side_effect=AssertionError('must reuse')):
            self.run_export()
        self.assertEqual(LAST_METRICS['counts']['base_generation'],0)

    def test_source_change_during_export_retains_previous_output(self):
        self.run_export()
        previous=self.output.read_bytes()
        original_loader=load_mjcf
        def mutate(path):
            model=original_loader(path)
            self.meshes[0].write_bytes(self.meshes[1].read_bytes())
            return model
        with patch('convert.load_mjcf',side_effect=mutate):
            with self.assertRaisesRegex(ValueError,'changed during export'):
                self.run_export()
        self.assertEqual(previous,self.output.read_bytes())

    def test_malformed_cache_manifest_is_optional(self):
        self.run_export()
        state=next((cache_root()/'projects').glob('*/state.json'))
        state.write_text(json.dumps(dict(key=state.parent.name,schema=1,files=[],reports=[])))
        self.run_export()
        self.assertEqual(LAST_METRICS['counts']['base_generation'],1)

    def test_v1_timestep_acceptance_with_existing_features(self):
        for features in ['site','constant_force','spring','equality','combined']:
            with self.subTest(features=features):
                # Each scenario begins with its own full, isolated export.
                fixture=IncrementalExportTests()
                fixture.setUp()
                try:
                    verify_timestep_acceptance(fixture,features)
                finally:
                    fixture.tearDown()


def clear_fixture_caches(fixture):
    from mesh_cache import cache_folder
    from pathlib import Path
    for folder in (cache_root(),cache_folder()):
        if not folder.resolve().is_relative_to(Path(fixture.temp.name).resolve()):
            raise AssertionError('Refusing to clear a non-fixture cache')
        if folder.exists(): shutil.rmtree(folder)


def verify_timestep_acceptance(fixture,features):
    attachments=[dict(id='A',name='A',type='frame',link='base',xyz=[0,0,0],rpy=[0,0,0]),
                 dict(id='B',name='B',type='frame',link='arm',xyz=[.1,0,0],rpy=[0,0,0])]
    config=json.loads(fixture.sidecar.read_text())
    config.update(attachments=attachments,solver=dict(enabled=True,timestep=.001))
    if features in ('constant_force','combined'):
        config['site_forces']=[dict(name='constant',type='pull',magnitude=10,site1='A',site2='B',site1_id='A',site2_id='B')]
    if features in ('spring','combined'):
        config.setdefault('site_forces',[]).append(dict(name='spring',type='spring',stiffness=100,damping=2,
            length_mode='initial',site1='A',site2='B',site1_id='A',site2_id='B'))
    if features in ('equality','combined'):
        config['equalities']=[dict(name='closed',type='connect',binding='site',site1='A',site2='B',site1_id='A',site2_id='B')]
    fixture.sidecar.write_text(json.dumps(config))
    fixture.run_export()
    initial=json.loads(json.dumps(LAST_METRICS))
    fixture.assertEqual(initial['counts']['base_generation'],1)
    fixture.assertGreater(initial['counts']['mesh_simplification'],0)
    before=json.loads(json.dumps(config))
    config['solver']['timestep']=.002
    fixture.sidecar.write_text(json.dumps(config))
    # Prove the only changed configuration value is timestep.
    before['solver']['timestep']=.002
    fixture.assertEqual(before,config)
    with patch('mesh_cache.prepare_mesh',side_effect=AssertionError('mesh preparation forbidden')),patch('mesh_cache.simplify',side_effect=AssertionError('simplification forbidden')):
        incremental=fixture.run_export()
    warm=json.loads(json.dumps(LAST_METRICS))
    fixture.assertEqual(warm['counts'],dict(mesh_prepare=0,mesh_simplification=0,mjcf_generation=1,mujoco_validation=1,base_generation=0))
    incremental_xml=fixture.output.read_text(encoding='utf-8')
    clear_fixture_caches(fixture)
    full=fixture.run_export()
    cold=json.loads(json.dumps(LAST_METRICS))
    fixture.assertEqual(cold['counts']['base_generation'],1)
    fixture.assertGreater(cold['counts']['mesh_prepare'],0)
    fixture.assertGreater(cold['counts']['mesh_simplification'],0)
    fixture.assert_semantics(incremental,full)
    fixture.assertEqual(ET.canonicalize(xml_data=incremental_xml),ET.canonicalize(xml_data=fixture.output.read_text(encoding='utf-8')))
    return dict(features=features,initial=initial,incremental=warm,cache_cleared_full=cold,
                only_timestep_changed=True,model_semantics_match=True,forward_and_step_match=True,canonical_xml_match=True)
