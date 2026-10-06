"""Finish the generated native SW V1 gate with compiled semantics and dynamics."""
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'tests/parity'))
sys.path.insert(0, str(root / 'mujoco_backend'))
from semantic_compare import compiled_semantics, dynamics
from convert import load_mjcf

folder = Path((root / 'build/native-incremental-directory.txt').read_text(encoding='utf-8-sig').strip()).resolve()
if not folder.is_relative_to((root / 'build').resolve()) or not folder.name.startswith('native-incremental-'):
    raise ValueError('Only newly generated workspace fixtures are accepted')
report_path = root / 'build/native-incremental-v1.json'
report_path.unlink(missing_ok=True)
report = json.loads((folder / 'native-counts.json').read_text(encoding='utf-8-sig'))
a = load_mjcf(folder / 'incremental_mjcf/robot.xml')
b = load_mjcf(folder / 'full_mjcf/robot.xml')
compiled_semantics(a, b)
dynamics(a, b, steps=(1, 10, 100))
report.update(model_semantics_match=True, forward_and_step_match=True)
report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('Native incremental semantics and dynamics: PASS')
for stage in report['stages']:
    print(stage['label'], stage['cad'], stage['backend']['counts'])
