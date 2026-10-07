"""Reproducible synthetic benchmark; never opens or reads SolidWorks projects."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import time
from test_package_export import PackageExportTests
from test_simplify_stl import sphere
from simplify_stl import write_binary
from convert import export_package

fixture = PackageExportTests()
fixture.setUp()
try:
    write_binary(fixture.meshes[0], *sphere(7))
    fixture.refresh_hash()
    before = fixture.original_bytes()
    fixture.settings['MaximumTriangles'] = 8192
    os.environ['SW2MUJOCO_PROFILE'] = '1'
    results = []
    for label in ('cold', 'warm_solver_change'):
        if label != 'cold':
            config = json.loads(fixture.sidecar.read_text())
            config['solver'] = dict(enabled=True, timestep=.0005)
            fixture.sidecar.write_text(json.dumps(config))
        captured = io.StringIO()
        start = time.perf_counter()
        with contextlib.redirect_stdout(captured):
            export_package(fixture.urdf, fixture.sidecar, fixture.output, fixture.settings)
        lines = captured.getvalue().splitlines()
        results.append(dict(run=label, seconds=time.perf_counter()-start,
            stages=[json.loads(line[6:]) for line in lines if line.startswith('PERF: ')],
            meshes=[json.loads(line[6:]) for line in lines if line.startswith('Mesh: ')]))
    after = fixture.original_bytes()
    assert before[0] == after[0] and before[2:] == after[2:]
    print(json.dumps(dict(faces=131072, budget=8192, source_urdf_and_stl_unchanged=True, results=results), indent=2))
finally:
    fixture.tearDown()
