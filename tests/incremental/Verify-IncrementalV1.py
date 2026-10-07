"""Strict synthetic backend V1 acceptance, no SolidWorks project access."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import argparse
import json
import sys
from pathlib import Path
from test_incremental_export import IncrementalExportTests,verify_timestep_acceptance
parser=argparse.ArgumentParser()
parser.add_argument('--report',default='build/incremental-v1-backend.json')
args=parser.parse_args()
results=[]
for features in ['site','constant_force','spring','equality','combined']:
    fixture=IncrementalExportTests()
    fixture.setUp()
    try: results.append(verify_timestep_acceptance(fixture,features))
    finally: fixture.tearDown()
report=Path(args.report)
report.parent.mkdir(parents=True,exist_ok=True)
report.write_text(json.dumps(dict(status='passed',scope='synthetic Python backend; no native SW full export',results=results),indent=2),encoding='utf-8')
print('V1 backend acceptance passed: '+str(report))
