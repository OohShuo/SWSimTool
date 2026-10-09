"""Compile generated limit fixtures with MuJoCo; no viewer or CAD."""
from pathlib import Path
import sys
import mujoco
import numpy as np

for path in Path(sys.argv[1]).glob('*.xml'):
    model = mujoco.MjModel.from_xml_path(str(path))
    kind, effort = path.stem.split('_')
    assert model.njnt == 1, path
    assert bool(model.jnt_limited[0]) == (kind != 'continuous'), path
    if kind != 'continuous':
        np.testing.assert_allclose(model.jnt_range[0], [-1, 1], rtol=0, atol=0)
    assert bool(model.jnt_actfrclimited[0]) == (effort == '7'), path
    if effort == '7':
        np.testing.assert_allclose(model.jnt_actfrcrange[0], [-7, 7], rtol=0, atol=0)
    mujoco.mj_forward(model, mujoco.MjData(model))
print('PASS: MuJoCo compiled all 9 position/effort fixtures with expected limits')
