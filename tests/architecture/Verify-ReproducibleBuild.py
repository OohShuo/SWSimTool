"""Compare two independently restored, built and tested source archives."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / 'tests/backend'), str(ROOT / 'tests/parity')]
from test_convert import URDF
from semantic_compare import compiled_semantics, dynamics
import mujoco

def versions(folder):
    # No shell interpolation of paths; the script receives a literal argument.
    script = ROOT / 'build/repro-metadata.ps1'
    script.write_text('param([string]$Folder)\nGet-ChildItem -LiteralPath $Folder -Filter *.dll | ForEach-Object { [pscustomobject]@{Name=$_.Name;File=$_.VersionInfo.FileVersion;Product=$_.VersionInfo.ProductVersion} } | ConvertTo-Json\n')
    return json.loads(subprocess.check_output(['powershell', '-NoProfile', '-File', str(script), str(folder)], encoding='utf-8-sig'))

def verify(a, b):
    payloads = [p / 'build/runtime-release' for p in (a, b)]
    files = [{str(p.relative_to(folder)) for p in folder.rglob('*') if p.is_file()} for folder in payloads]
    assert files[0] == files[1], 'Payload file sets differ'
    assert versions(payloads[0]) == versions(payloads[1]), 'Version metadata differs'
    generated = []
    for root in (a, b):
        folder = root / 'build/repro-fixture'
        folder.mkdir()
        urdf, config, output = [folder / name for name in ('robot.urdf', 'config.json', 'robot.xml')]
        urdf.write_text(URDF, encoding='utf-8')
        config.write_text(json.dumps(dict(schema_version=1, attachments=[], actuators=[], sensors=[], equalities=[])), encoding='utf-8')
        runner = root / 'build/bin/CandidateRunner/Release/net48/SWSimTool.CandidateRunner.exe'
        subprocess.run([str(runner), str(urdf), str(config), str(output)], check=True)
        generated.append(output)
    assert generated[0].read_bytes() == generated[1].read_bytes(), 'MJCF is not deterministic'
    models = [mujoco.MjModel.from_xml_path(str(p)) for p in generated]
    compiled_semantics(*models)
    dynamics(*models, steps=(1, 10, 100))
    report = dict(status='PASS', payload_files=len(files[0]), versions_equal=True, deterministic_mjcf=True, compiled_semantics=True, dynamics=True,
                  dll_hash_equal={p.name: hashlib.sha256(p.read_bytes()).digest() == hashlib.sha256((payloads[1] / p.name).read_bytes()).digest() for p in payloads[0].glob('*.dll')})
    (ROOT / 'build/reports/modernization/phase-4-reproducibility.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))

if __name__ == '__main__': verify(*(Path(p).resolve() for p in sys.argv[1:3]))
