"""Exercise the production converter on the newly generated CAD export only."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import hashlib
import json
import sys
from pathlib import Path
import numpy as np
import mujoco

from convert import convert, load_mjcf

directory = Path(sys.argv[1])
urdf = directory / "cad_robot/urdf/cad_robot.urdf"
before = urdf.read_bytes()
config = json.loads(urdf.with_suffix(".sim.json").read_text(encoding="utf-8"))
config["attachments"].append({"name": "closure_base", "link": "base_link", "type": "point", "xyz": [0, 0, 0]})
config["sensors"].extend([
    {"name": "tof", "site": "site1", "type": "tof", "noise": 0, "cutoff": 0},
    {"name": "camera", "site": "site1", "type": "camera", "noise": 0, "cutoff": 0, "fovy": 60},
])
config["equalities"] = [{"name": "closure", "type": "connect", "site1": "cad_frame", "site2": "closure_base"}]
sidecar = urdf.with_suffix(".allfeatures.sim.json")
sidecar.write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
output = urdf.with_suffix(".allfeatures.mjcf.xml")
convert(urdf, sidecar, output)
model = load_mjcf(output)
data = mujoco.MjData(model)
mujoco.mj_forward(model, data)
assert (model.nsite, model.nu, model.nsensor, model.ncam, model.neq) == (4, 1, 3, 1, 1)
np.testing.assert_allclose(data.site_xpos[model.site("cad_frame").id], data.site_xpos[model.site("closure_base").id], atol=1e-7)
site = model.site("site1").id
np.testing.assert_allclose(model.site_pos[site], [0, 0, 0], atol=1e-7)
np.testing.assert_allclose(model.site_quat[site], [1, 0, 0, 0], atol=1e-7)
for _ in range(1000):
    mujoco.mj_step(model, data)
assert np.isfinite(data.qpos).all() and np.isfinite(data.qvel).all() and np.isfinite(data.sensordata).all()
assert urdf.read_bytes() == before
report = {"mujoco": mujoco.__version__, "python": sys.executable, "sites": model.nsite, "actuators": model.nu,
          "sensors": model.nsensor, "cameras": model.ncam, "equalities": model.neq, "steps": 1000,
          "urdf_sha256": hashlib.sha256(before).hexdigest(), "urdf_unchanged": True, "finite_state": True,
          "closure_initial_coincident": True, "component_frame_local_pose_verified": True}
(directory / "actual-cad-mjcf-validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
