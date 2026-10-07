
from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import numpy as np
from simplify_stl import indexed_mesh, simplify, triangles, write_binary


def sphere(level=4):
    vertices = [np.array(point, dtype=float) for point in [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]]
    faces = [(0, 2, 4), (2, 1, 4), (1, 3, 4), (3, 0, 4), (2, 0, 5), (1, 2, 5), (3, 1, 5), (0, 3, 5)]
    for _ in range(level):
        cache, refined = {}, []
        def midpoint(a, b):
            key = tuple(sorted((a, b)))
            if key not in cache:
                point = vertices[a] + vertices[b]
                cache[key] = len(vertices)
                vertices.append(point / np.linalg.norm(point))
            return cache[key]
        for a, b, c in faces:
            ab, bc, ca = midpoint(a, b), midpoint(b, c), midpoint(c, a)
            refined.extend([(a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)])
        faces = refined
    return np.asarray(vertices) * [.04, .03, .02] + [.123, -.2, .08], np.asarray(faces)


class MeshSimplificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / '中文 空格.stl'
        self.vertices, self.faces = sphere()
        write_binary(self.path, self.vertices, self.faces)
    def tearDown(self):
        self.temp.cleanup()
    def test_under_limit_and_equal_limit_are_byte_identical(self):
        data = self.path.read_bytes()
        for maximum in (len(self.faces), len(self.faces) + 10):
            for backend in ('pymeshlab', 'fast-simplification', 'blender'):
                with patch('simplify_stl.reduce_python', side_effect=AssertionError('Should not run')):
                    self.assertFalse(simplify(self.path, maximum, backend)['changed'])
                self.assertEqual(self.path.read_bytes(), data)
    def test_reduction_meets_budget_and_preserves_coordinate_frame(self):
        for backend in ('pymeshlab', 'fast-simplification', 'blender'):
            with self.subTest(backend=backend):
                write_binary(self.path, self.vertices, self.faces)
                blender = os.environ.get('SW2URDF_TEST_BLENDER')
                if backend == 'blender' and not blender:
                    continue
                report = simplify(self.path, 500, backend, blender)
                self.assertTrue(report['changed'])
                self.assertLessEqual(report['after'], 500)
                points = triangles(self.path).reshape(-1, 3)
                np.testing.assert_allclose((points.min(axis=0) + points.max(axis=0)) / 2, [.123, -.2, .08], atol=.002)
                np.testing.assert_allclose(points.max(axis=0) - points.min(axis=0), [.08, .06, .04], atol=.002)
                self.assertTrue(np.isfinite(points).all())
    def test_unattainable_budget_preserves_original(self):
        before = self.path.read_bytes()
        with patch('simplify_stl.reduce_python', return_value=(self.vertices, self.faces)):
            with self.assertRaisesRegex(ValueError, 'original STL retained'):
                simplify(self.path, 500, 'fast-simplification')
        self.assertEqual(self.path.read_bytes(), before)
        self.assertFalse(list(self.path.parent.glob('.sw2urdf-mesh-*')))
    def test_backend_error_preserves_original(self):
        before = self.path.read_bytes()
        with patch('simplify_stl.reduce_python', side_effect=ImportError('missing backend')):
            with self.assertRaises(ImportError): simplify(self.path, 500, 'pymeshlab')
        self.assertEqual(self.path.read_bytes(), before)
    def test_invalid_geometry_preserves_original(self):
        before = self.path.read_bytes()
        with patch('simplify_stl.reduce_python', return_value=(np.zeros((3, 3)), np.array([[0, 1, 2]]))):
            with self.assertRaisesRegex(ValueError, 'degenerate'): simplify(self.path, 500, 'pymeshlab')
        self.assertEqual(self.path.read_bytes(), before)
    def test_ascii_input_is_supported(self):
        self.path.write_text('solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test\n', encoding='ascii')
        self.assertEqual(triangles(self.path).shape, (1, 3, 3))
    def test_non_finite_input_rejected(self):
        self.path.write_text('solid test\nvertex nan 0 0\nvertex 1 0 0\nvertex 0 1 0\nendsolid test', encoding='ascii')
        with self.assertRaisesRegex(ValueError, 'non-finite'): indexed_mesh(self.path)


if __name__ == '__main__':
    unittest.main()
