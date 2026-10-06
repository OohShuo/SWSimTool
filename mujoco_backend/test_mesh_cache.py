import json
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
import os
from mesh_cache import prepare_mesh, cache_folder, file_hash, link_or_copy
from simplify_stl import write_binary, triangles
from test_simplify_stl import sphere

class MeshCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.env = patch.dict(os.environ, {'SW2MUJOCO_MESH_CACHE': str(self.root / 'cache')})
        self.env.start()
        self.source = self.root / 'source.stl'
        write_binary(self.source, *sphere(3))
        self.raw = self.source.read_bytes()
        self.settings = dict(Enabled=True, MaximumTriangles=128, Backend='fast-simplification')
    def tearDown(self):
        self.env.stop()
        self.temp.cleanup()
    def prepare(self, settings=None):
        return prepare_mesh(self.raw, self.root / 'result.stl', settings or self.settings)
    def test_warm_cache_avoids_simplification(self):
        cold = self.prepare()
        with patch('mesh_cache.simplify', side_effect=AssertionError('no repeat simplify')):
            warm = self.prepare()
        self.assertFalse(cold['cache_hit'])
        self.assertTrue(warm['cache_hit'])
        self.assertEqual(cold['after'], warm['after'])
        self.assertEqual(self.raw, self.source.read_bytes())
    def test_budget_and_content_invalidate(self):
        self.prepare()
        from mesh_cache import simplify
        with patch('mesh_cache.simplify', wraps=simplify) as worker:
            self.prepare(dict(self.settings, MaximumTriangles=64))
            self.raw = bytes([1]) + self.raw[1:]
            self.prepare()
            self.assertEqual(worker.call_count, 2)
    def test_corrupt_cache_is_recomputed(self):
        self.prepare()
        cached = next(cache_folder().glob('*.stl'))
        cached.write_bytes(b'broken')
        report = self.prepare()
        self.assertFalse(report['cache_hit'])
        self.assertLessEqual(len(triangles(self.root / 'result.stl')), 128)
    def test_cache_disable_and_unavailable_folder(self):
        self.prepare()
        self.assertFalse(self.prepare(dict(self.settings, CacheEnabled=False))['cache_hit'])
        blocked = self.root / 'blocked'
        blocked.write_text('file')
        with patch.dict(os.environ, {'SW2MUJOCO_MESH_CACHE': str(blocked)}):
            self.assertTrue(self.prepare()['changed'])
    def test_backend_version_and_algorithm_change_invalidate(self):
        self.prepare()
        with patch('mesh_cache.version', return_value='different'):
            self.assertFalse(self.prepare()['cache_hit'])
    def test_staging_link_break_does_not_modify_original(self):
        staged = self.root / 'staged.stl'
        link_or_copy(self.source, staged)
        old_hash = file_hash(self.source)
        staged.unlink()
        staged.write_bytes(b'new')
        self.assertEqual(old_hash, file_hash(self.source))
