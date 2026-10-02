import hashlib
import json
import shutil
import struct
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import mujoco
import numpy as np
from convert import convert, load_mjcf, quaternion

URDF = '''<robot name="test">
<link name="base"><visual><geometry><box size="0.1 0.1 0.1"/></geometry></visual></link>
<link name="arm"><inertial><origin xyz="0 0 0"/><mass value="1"/><inertia ixx="0.01" ixy="0" ixz="0" iyy="0.01" iyz="0" izz="0.01"/></inertial><collision><geometry><box size="0.1 0.1 0.1"/></geometry></collision></link>
<joint name="hinge" type="revolute"><parent link="base"/><child link="arm"/><origin xyz="0.2 0.3 0.4" rpy="0 0 1.5707963267948966"/><axis xyz="0 0 1"/><limit lower="-1" upper="1" effort="10" velocity="1"/></joint>
<link name="tool"/>
<joint name="tool_fixed" type="fixed"><parent link="arm"/><child link="tool"/><origin xyz="0.1 0 0"/></joint>
</robot>'''


class ConversionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        self.urdf = self.directory / 'test.urdf'
        self.urdf.write_text(URDF)
        self.sidecar = self.directory / 'test.sim.json'
        self.output = self.directory / 'test.mjcf.xml'
        self.config = dict(schema_version=1, units='m,rad', urdf=self.urdf.name,
            urdf_sha256=hashlib.sha256(self.urdf.read_bytes()).hexdigest(),
            attachments=[dict(name='anchor_base', link='base', type='point', xyz=[.2,.3,.4]),
                         dict(name='anchor_arm', link='arm', type='point', xyz=[0,0,0]),
                         dict(name='imu_mount', link='tool', type='frame', xyz=[.02,0,0], rpy=[0,.2,.3])],
            actuators=[dict(name='drive', joint='hinge', type='position', gain=10, gear=1, ctrl_min=-1, ctrl_max=1, force_min=-10, force_max=10)],
            sensors=[dict(name='imu', type='imu', site='imu_mount'), dict(name='tof', type='tof', site='imu_mount'), dict(name='camera', type='camera', site='imu_mount', fovy=60)],
            equalities=[dict(name='closure', type='connect', site1='anchor_base', site2='anchor_arm')])
    def tearDown(self):
        self.temporary.cleanup()
    def run_convert(self):
        self.sidecar.write_text(json.dumps(self.config))
        return convert(self.urdf, self.sidecar, self.output)
    def test_compile_step_pose_and_unchanged_inputs(self):
        before = self.urdf.read_bytes()
        model = self.run_convert()
        self.assertEqual(self.urdf.read_bytes(), before)
        self.assertEqual((model.nsite,model.nu,model.nsensor,model.neq,model.ncam), (3,1,3,1,1))
        data = mujoco.MjData(model)
        mujoco.mj_forward(model,data)
        site = mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_SITE,'imu_mount')
        np.testing.assert_allclose(data.site_xpos[site], [.2,.42,.4], atol=1e-6)
        root = ET.parse(self.output).getroot()
        np.testing.assert_allclose([float(x) for x in root.find(".//site[@name='imu_mount']").get('quat').split()], quaternion([0,.2,.3]))
        for _ in range(100):
            mujoco.mj_step(model,data)
        self.assertTrue(np.isfinite(data.qpos).all())
    def test_unknown_link_and_directionless_sensor_rejected(self):
        self.config['sensors'][0]['site']='anchor_base'
        with self.assertRaisesRegex(ValueError,'oriented frame'): self.run_convert()
    def test_stale_pair_rejected(self):
        self.urdf.write_text(URDF+'\n')
        with self.assertRaisesRegex(ValueError,'changed'): self.run_convert()
    def test_invalid_range_preserves_previous_output(self):
        self.output.write_text('previous output')
        self.config['actuators'][0]['ctrl_max']=-1
        with self.assertRaisesRegex(ValueError,'range'): self.run_convert()
        self.assertEqual(self.output.read_text(),'previous output')
    def test_points_do_not_allow_orientation(self):
        self.config['attachments'][0]['rpy']=[0,0,0]
        with self.assertRaisesRegex(ValueError,'position only'): self.run_convert()
    def test_unicode_mesh_directory_and_portable_reload(self):
        mesh_directory = self.directory / '中文路径 空格'
        mesh_directory.mkdir()
        mesh = mesh_directory / '测试网格.stl'
        a, b, c, d = (0,0,0), (.05,0,0), (0,.05,0), (0,0,.05)
        data = b'\0' * 80 + struct.pack('<I', 4)
        for triangle in ((a,c,b), (a,b,d), (a,d,c), (b,c,d)):
            data += struct.pack('<12fH', 0,0,0, *(coordinate for vertex in triangle for coordinate in vertex), 0)
        mesh.write_bytes(data)
        robot = ET.fromstring(URDF)
        geometry = robot.find('./link/visual/geometry')
        geometry.clear()
        ET.SubElement(geometry, 'mesh', filename=mesh.relative_to(self.directory).as_posix())
        self.urdf.write_bytes(ET.tostring(robot, encoding='utf-8'))
        self.config['urdf_sha256'] = hashlib.sha256(self.urdf.read_bytes()).hexdigest()
        model = self.run_convert()
        self.assertEqual(model.nmesh, 1)
        self.assertEqual(mesh.read_bytes(), data)
        portable = self.directory / '转移后的目录'
        portable.mkdir()
        shutil.copy(self.output, portable)
        reference = ET.parse(self.output).find('./asset/mesh').get('file')
        self.assertTrue(reference.isascii())
        self.assertFalse(Path(reference).is_absolute())
        shutil.copytree(self.output.parent / Path(reference).parent, portable / Path(reference).parent)
        self.assertEqual(load_mjcf(portable / self.output.name).nmesh, 1)


if __name__ == '__main__':
    unittest.main()
