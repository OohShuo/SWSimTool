"""Equal and opposite constant forces along two sites, with no runtime callback."""
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
        if item.get('type', 'pull') not in ('pull', 'push'):
            raise ValueError('Two-site force type must be pull or push')
        if not isinstance(item.get('enabled', True), bool):
            raise ValueError('Two-site force enabled must be boolean')
        if finite(item.get('magnitude', 0), 'force magnitude') < 0:
            raise ValueError('Two-site force magnitude must be nonnegative N')
        first, second = item.get('site1'), item.get('site2')
        if first not in attachments or second not in attachments or first == second:
            raise ValueError('Two-site force requires two different existing sites')
        if attachments[first]['link'] == attachments[second]['link']:
            raise ValueError('Two-site force sites must belong to different links')
        if item.get('enabled', True) and names(item)[1] in actuator_names:
            raise ValueError('Two-site force actuator name conflicts with joint actuator')


def apply_site_forces(root, config):
    for item in config.get('site_forces', []):
        if not item.get('enabled', True):
            continue
        tendon_name, actuator_name = names(item)
        tendon = root.find('tendon')
        if tendon is None:
            tendon = ET.SubElement(root, 'tendon')
        actuator = root.find('actuator')
        if actuator is None:
            actuator = ET.SubElement(root, 'actuator')
        if any(t.get('name') == tendon_name for t in tendon) or any(a.get('name') == actuator_name for a in actuator):
            raise ValueError('Two-site force conflicts with an existing tendon or actuator')
        path = ET.SubElement(tendon, 'spatial', name=tendon_name, limited='false',
            stiffness='0', damping='0', frictionloss='0', width='.001')
        ET.SubElement(path, 'site', site=item['site1'])
        ET.SubElement(path, 'site', site=item['site2'])
        magnitude = finite(item.get('magnitude', 0), 'force magnitude')
        force = -magnitude if item.get('type', 'pull') == 'pull' else magnitude
        ET.SubElement(actuator, 'general', name=actuator_name, tendon=tendon_name,
            gear='1', dyntype='none', gaintype='fixed', gainprm='0',
            biastype='affine', biasprm=f'{force:.17g} 0 0',
            ctrllimited='false', forcelimited='false')


def validate_initial_force_directions(model, config):
    import mujoco
    import numpy as np
    active = [f for f in config.get('site_forces', []) if f.get('enabled', True) and f.get('magnitude', 0) > 0]
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
