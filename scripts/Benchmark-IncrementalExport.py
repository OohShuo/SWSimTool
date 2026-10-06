"""Reproducible synthetic benchmark; never opens SolidWorks or existing CAD."""
import argparse
import json
import shutil
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'mujoco_backend'))
from test_package_export import PackageExportTests
from test_incremental_export import IncrementalExportTests
from convert import export_package
from incremental import LAST_METRICS, cache_root

parser=argparse.ArgumentParser()
parser.add_argument('--report',default='build/incremental-benchmark.json')
args=parser.parse_args()
fixture=PackageExportTests()
fixture.setUp()
results=[]
try:
    config=json.loads(fixture.sidecar.read_text())
    config['solver']=dict(enabled=True,timestep=.001)
    def run(label):
        fixture.sidecar.write_text(json.dumps(config))
        model=export_package(fixture.urdf,fixture.sidecar,fixture.output,fixture.settings)
        results.append(dict(scenario=label,**json.loads(json.dumps(LAST_METRICS))))
        return model
    run('cold')
    run('warm')
    config['solver']['timestep']=.002
    run('solver-only')
    config['solver']['contact']=dict(timeconst=.005,dmin=.98,dmax=.995,width=.001,midpoint=.5,power=2,dampratio=1,margin=.002,condim=3)
    incremental=run('contact-only')
    shutil.rmtree(cache_root())
    from mesh_cache import cache_folder
    if cache_folder().exists(): shutil.rmtree(cache_folder())
    full=run('cache-deleted-full')
    IncrementalExportTests().assert_semantics(incremental,full)
    for row in results[1:4]:
        assert row['counts']==dict(mesh_prepare=0,mesh_simplification=0,mjcf_generation=1,mujoco_validation=1,base_generation=0), row
    report=Path(args.report)
    report.parent.mkdir(parents=True,exist_ok=True)
    report.write_text(json.dumps(dict(kind='synthetic',semantic_match=True,results=results),indent=2),encoding='utf-8')
    print('Benchmark verified: '+str(report))
finally:
    fixture.tearDown()
