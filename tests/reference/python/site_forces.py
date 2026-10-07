"""Constant forces or passive linear springs between two sites, without callbacks."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import math
import xml.etree.ElementTree as ET
from solver import finite


def names(item):
    return 'sw2mujoco_tendon_' + item['name'], 'sw2mujoco_force_' + item['name']


def validate_site_forces(config, attachments):
    seen = set()
    actuator_names = {a['name'] for a in config.get('actuators', [])}
    for item in config.get('site_forces', []):
        name = item.get('name')
        if not isinstance(name, str) or not name.strip() or name in seen:
            raise ValueError('Two-site force names must be nonempty and unique')
        seen.add(name)
        if item.get('type', 'pull') not in ('pull', 'push', 'spring'):
            raise ValueError('Two-site force type must be pull or push or spring')
        if not isinstance(item.get('enabled', True), bool):
            raise ValueError('Two-site force enabled must be boolean')
        if item.get('type', 'pull') == 'spring':
            for key in ('stiffness', 'damping'):
                if finite(item.get(key, 0), key) < 0:
                    raise ValueError(f'Spring {key} must be nonnegative')
            if item.get('length_mode', 'initial') not in ('initial', 'custom'):
                raise ValueError('Spring length mode must be initial or custom')
            if item.get('length_mode') == 'custom' and finite(item.get('rest_length', .1), 'rest length') < 0:
                raise ValueError('Spring rest length must be nonnegative m')
        elif finite(item.get('magnitude', 0), 'force magnitude') < 0:
            raise ValueError('Two-site force magnitude must be nonnegative N')
        first, second = item.get('site1'), item.get('site2')
        if first not in attachments or second not in attachments or first == second:
            raise ValueError('Two-site force requires two different existing sites')
        if attachments[first]['link'] == attachments[second]['link']:
            raise ValueError('Two-site force sites must belong to different links')
        if item.get('enabled', True) and item.get('type') != 'spring' and names(item)[1] in actuator_names:
            raise ValueError('Two-site force actuator name conflicts with joint actuator')


def apply_site_forces(root, config):
    for item in config.get('site_forces', []):
        if not item.get('enabled', True):
            continue
        tendon_name, actuator_name = names(item)
        tendon = root.find('tendon')
        if tendon is None:
            tendon = ET.SubElement(root, 'tendon')
        spring = item.get('type') == 'spring'
        actuator = root.find('actuator')
        if any(t.get('name') == tendon_name for t in tendon) or (not spring and actuator is not None and any(a.get('name') == actuator_name for a in actuator)):
            raise ValueError('Two-site force conflicts with an existing tendon or actuator')
        path = ET.SubElement(tendon, 'spatial', name=tendon_name, limited='false',
            stiffness='0', damping='0', frictionloss='0', width='.001')
        ET.SubElement(path, 'site', site=item['site1'])
        ET.SubElement(path, 'site', site=item['site2'])
        if spring:
            path.set('stiffness', str(item.get('stiffness', 0)))
            path.set('damping', str(item.get('damping', 0)))
            path.set('springlength', str(item.get('rest_length', .1)) if item.get('length_mode') == 'custom' else '-1')
            continue
        if actuator is None:
            actuator = ET.SubElement(root, 'actuator')
        magnitude = finite(item.get('magnitude', 0), 'force magnitude')
        force = -magnitude if item.get('type', 'pull') == 'pull' else magnitude
        ET.SubElement(actuator, 'general', name=actuator_name, tendon=tendon_name,
            gear='1', dyntype='none', gaintype='fixed', gainprm='0',
            biastype='affine', biasprm=f'{force:.17g} 0 0',
            ctrllimited='false', forcelimited='false')


def validate_initial_force_directions(model, config):
    import mujoco
    import numpy as np
    active = [f for f in config.get('site_forces', []) if f.get('enabled', True) and ((f.get('stiffness', 0) > 0 or f.get('damping', 0) > 0) if f.get('type') == 'spring' else f.get('magnitude', 0) > 0)]
    if not active:
        return
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    for item in active:
        a = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, item['site1'])
        b = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, item['site2'])
        gap = np.linalg.norm(data.site_xpos[b] - data.site_xpos[a])
        if not math.isfinite(gap) or gap <= 1e-9:
            raise ValueError(f"Two-site force '{item['name']}' endpoints coincide initially; force direction is undefined")


def resolve_initial_spring_lengths(root, model, config):
    """Use qpos0, irrespective of the model's separate joint spring reference pose."""
    import mujoco
    import numpy as np
    initial = [f for f in config.get('site_forces', []) if f.get('enabled', True) and f.get('type') == 'spring' and f.get('length_mode', 'initial') == 'initial']
    if not initial:
        return
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    for item in initial:
        name = names(item)[0]
        index = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_TENDON, name)
        first = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, item['site1'])
        second = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, item['site2'])
        length = float(np.linalg.norm(data.site_xpos[second] - data.site_xpos[first]))
        if not math.isfinite(length):
            raise ValueError('Spring initial length must be finite')
        for path in root.findall('tendon/spatial'):
            if path.get('name') == name:
                path.set('springlength', format(length, '.17g'))
                break
        model.tendon_lengthspring[index, :] = length
