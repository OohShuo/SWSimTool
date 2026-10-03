import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from convert import export_package, load_mjcf, main
from simplify_stl import triangles, write_binary
from test_simplify_stl import sphere
from test_convert import URDF


class PackageExportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name) / '原始导出'
        self.folder.mkdir()
        self.urdf = self.folder / 'robot.urdf'
        self.sidecar = self.folder / 'robot.sim.json'
        self.output = Path(self.temp.name) / 'robot_mjcf' / 'robot.xml'
        self.meshes = [self.folder / 'a' / 'same.stl', self.folder / 'b' / 'same.stl']
        for path in self.meshes:
            path.parent.mkdir()
        write_binary(self.meshes[0], *sphere(3))
        write_binary(self.meshes[1], *sphere(0))
        robot = ET.fromstring(URDF)
        for geometry, mesh in [(robot.find('./link/visual/geometry'), self.meshes[0]),
                              (robot.find("./link[@name='arm']/collision/geometry"), self.meshes[1])]:
            geometry.clear()
            ET.SubElement(geometry, 'mesh', filename=mesh.relative_to(self.folder).as_posix())
        root_collision = ET.SubElement(robot.find('link'), 'collision')
        ET.SubElement(ET.SubElement(root_collision, 'geometry'), 'mesh', filename='a/same.stl')
        self.urdf.write_bytes(ET.tostring(robot))
        self.refresh_hash()
        self.settings = dict(Enabled=True, MaximumTriangles=128, Backend='fast-simplification')
    def refresh_hash(self):
        self.sidecar.write_text(json.dumps(dict(schema_version=1, units='m,rad', urdf=self.urdf.name,
            urdf_sha256=hashlib.sha256(self.urdf.read_bytes()).hexdigest(), attachments=[], actuators=[], sensors=[], equalities=[])))
    def tearDown(self):
        self.temp.cleanup()
    def original_bytes(self):
        return [p.read_bytes() for p in [self.urdf, self.sidecar] + self.meshes]
    def test_package_layout_copy_simplify_hash_and_portability(self):
        before = self.original_bytes()
        model = export_package(self.urdf, self.sidecar, self.output, self.settings)
        self.assertEqual(model.nmesh, 2)
        self.assertEqual(self.original_bytes(), before)
        self.assertEqual(sorted(p.name for p in self.output.parent.iterdir()), ['mesh-report.json', 'robot.sim.json', 'robot.urdf', 'robot.xml', 'robot_meshes'])
        robot = ET.parse(self.output.parent / 'robot.urdf')
        references = [m.get('filename') for m in robot.findall('.//mesh')]
        self.assertEqual(len(set(references)), 2)
        self.assertEqual(references[0], references[1])
        for reference in references:
            self.assertTrue(reference.startswith('robot_meshes/'))
            self.assertLessEqual(len(triangles(self.output.parent / reference)), 128)
        self.assertEqual((self.output.parent / references[2]).read_bytes(), before[3])
        retained = self.output.parent / 'robot.urdf'
        config = json.loads((self.output.parent / 'robot.sim.json').read_text())
        self.assertEqual(config['urdf_sha256'], hashlib.sha256(retained.read_bytes()).hexdigest())
        moved = Path(self.temp.name) / '移动后的完整包'
        shutil.copytree(self.output.parent, moved)
        self.assertEqual(load_mjcf(moved / 'robot.xml').nmesh, 2)
    def test_all_three_backends(self):
        for backend in ('pymeshlab', 'fast-simplification', 'blender'):
            with self.subTest(backend=backend):
                settings = dict(self.settings, Backend=backend, Blender=os.environ.get('SW2URDF_TEST_BLENDER'))
                if backend == 'blender' and not settings['Blender']:
                    continue
                export_package(self.urdf, self.sidecar, self.output, settings)
                reports = json.loads((self.output.parent / 'mesh-report.json').read_text())
                self.assertEqual([r['changed'] for r in reports], [True, False])
                self.assertLessEqual(reports[0]['after'], 128)
    def test_failure_retains_previous_package_and_originals(self):
        export_package(self.urdf, self.sidecar, self.output, self.settings)
        (self.output.parent / 'notes.txt').write_text('keep unrelated files')
        before = {str(p.relative_to(self.output.parent)): p.read_bytes() for p in self.output.parent.rglob('*') if p.is_file()}
        originals = self.original_bytes()
        with patch('simplify_stl.simplify', side_effect=RuntimeError('failed preprocessing')):
            with self.assertRaisesRegex(RuntimeError, 'failed preprocessing'):
                export_package(self.urdf, self.sidecar, self.output, self.settings)
        self.assertEqual(before, {str(p.relative_to(self.output.parent)): p.read_bytes() for p in self.output.parent.rglob('*') if p.is_file()})
        self.assertEqual(originals, self.original_bytes())
        export_package(self.urdf, self.sidecar, self.output, self.settings)
        self.assertEqual((self.output.parent / 'notes.txt').read_text(), 'keep unrelated files')
    def test_stale_pair_fails_before_preprocessing(self):
        self.urdf.write_bytes(self.urdf.read_bytes() + b'\n')
        with patch('simplify_stl.simplify', side_effect=AssertionError('must not process')):
            with self.assertRaisesRegex(ValueError, 'changed'):
                export_package(self.urdf, self.sidecar, self.output, self.settings)
        self.assertFalse(self.output.exists())
    def test_cli_conversion_preview_uses_new_package(self):
        preferences = self.folder / 'mesh-settings.json'
        preferences.write_text(json.dumps(self.settings))
        with patch('mujoco.viewer.launch') as viewer:
            main(['--urdf', str(self.urdf), '--config', str(self.sidecar), '--output', str(self.output),
                  '--mesh-settings', str(preferences), '--preview'])
            self.assertEqual(viewer.call_args.args[0].nmesh, 2)
        self.assertTrue((self.output.parent / 'robot.urdf').exists())
    def test_oversized_mesh_processed_before_mujoco_loading(self):
        # 8 * 4**8 = 524288 faces, exceeding the actual STL decoder limit.
        write_binary(self.meshes[0], *sphere(8))
        before = hashlib.sha256(self.meshes[0].read_bytes()).hexdigest()
        settings = dict(self.settings, MaximumTriangles=50000)
        with patch('convert.convert', side_effect=AssertionError('MuJoCo must not load oversized input')):
            with self.assertRaisesRegex(ValueError, '524288 triangles'):
                export_package(self.urdf, self.sidecar, self.output, dict(Enabled=False))
        model = export_package(self.urdf, self.sidecar, self.output, settings)
        self.assertEqual(model.nmesh, 2)
        reports = json.loads((self.output.parent / 'mesh-report.json').read_text())
        self.assertEqual(reports[0]['before'], 524288)
        self.assertLessEqual(reports[0]['after'], 50000)
        self.assertEqual(before, hashlib.sha256(self.meshes[0].read_bytes()).hexdigest())


if __name__ == '__main__':
    unittest.main()
