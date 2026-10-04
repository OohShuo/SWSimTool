"""Joint overrides are independent of URDF source data and actuator settings."""
import math
import xml.etree.ElementTree as ET
from solver import finite, constraint_attributes


def validate_joints(config, robot):
    source = {j.get('name'): j for j in robot.findall('joint')}
    values = config.get('joints') or []
    if not isinstance(values, list):
        raise ValueError('Joint settings must be a list')
    seen = set()
    for value in values:
        if not isinstance(value, dict):
            raise ValueError('Each joint setting must be an object')
        name = value.get('joint')
        if name not in source or name in seen:
            raise ValueError(f'Unknown or duplicate joint configuration: {name}')
        seen.add(name)
        original = source[name].get('type')
        if original not in ('revolute', 'continuous', 'prismatic'):
            raise ValueError(f'Joint configuration requires a scalar movable joint: {name}')
        if value.get('type', 'inherit') not in ('inherit', 'hinge', 'slide'):
            raise ValueError(f'Unsupported joint override type: {name}')
        if value.get('type', 'inherit') != 'inherit' and source[name].find('mimic') is not None:
            raise ValueError(f'Cannot change the type of a mimic joint: {name}')
        for key in ('damping', 'frictionloss', 'armature', 'stiffness'):
            if value.get(key) is not None and finite(value[key], key) < 0:
                raise ValueError(f'Joint {key} must be nonnegative: {name}')
        for key in ('ref', 'springref'):
            if value.get(key) is not None:
                finite(value[key], key)
        for key in ('pos', 'axis'):
            vector = value.get(key)
            if vector is not None:
                if not isinstance(vector, list) or len(vector) != 3:
                    raise ValueError(f'Joint {key} requires three values: {name}')
                vector = [finite(x, key) for x in vector]
                if key == 'axis' and math.hypot(*vector) < 1e-12:
                    raise ValueError(f'Joint axis cannot be zero: {name}')
        mode = value.get('limit_mode', 'inherit')
        if mode not in ('inherit', 'none', 'custom'):
            raise ValueError(f'Invalid joint limit mode: {name}')
        if mode == 'custom':
            lower, upper = finite(value.get('lower'), 'lower'), finite(value.get('upper'), 'upper')
            if lower >= upper:
                raise ValueError(f'Invalid joint limit range: {name}')
        if value.get('margin') is not None and finite(value['margin'], 'margin') < 0:
            raise ValueError(f'Joint margin must be nonnegative: {name}')
        for key in ('limit_solver', 'friction_solver'):
            if value.get(key) is not None:
                constraint_attributes(value[key])
    if config.get('base_mode', 'inherit') not in ('inherit', 'fixed', 'floating'):
        raise ValueError('Invalid base mode')
    limits = config.get('joint_force_limits') or []
    if not isinstance(limits, list):
        raise ValueError('Joint force limits must be a list')
    seen.clear()
    for value in limits:
        if not isinstance(value, dict):
            raise ValueError('Each joint force limit must be an object')
        name = value.get('joint')
        if name not in source or name in seen or source[name].get('type') not in ('revolute', 'continuous', 'prismatic'):
            raise ValueError(f'Unknown, duplicate or non-scalar joint force limit: {name}')
        seen.add(name)
        if finite(value.get('lower'), 'force lower') >= finite(value.get('upper'), 'force upper'):
            raise ValueError(f'Invalid joint total force range: {name}')


def apply_joints(root, config):
    joints = {j.get('name'): j for j in root.findall('.//body/joint')}
    compiler = root.find('compiler')
    # MjSpec serialization uses radians. Be explicit for all new angle fields.
    if compiler is None:
        compiler = ET.SubElement(root, 'compiler')
    compiler.set('angle', 'radian')
    for value in config.get('joints') or []:
        name = value['joint']
        if name not in joints:
            raise ValueError(f'Joint lost during URDF conversion: {name}')
        joint = joints[name]
        kind = value.get('type', 'inherit')
        if kind != 'inherit' and kind != joint.get('type', 'hinge'):
            # Angular URDF limits cannot be silently reused as linear limits.
            if value.get('limit_mode', 'inherit') == 'inherit':
                raise ValueError(f'Changing hinge/slide requires explicit limits or no limits: {name}')
            if joint.get('stiffness', '0') != '0' and value.get('stiffness') is None:
                raise ValueError(f'Changing type requires an explicit spring setting: {name}')
            joint.set('type', kind)
        for key in ('damping', 'frictionloss', 'armature', 'stiffness', 'springref', 'ref', 'margin'):
            if value.get(key) is not None:
                joint.set(key, f'{float(value[key]):.12g}')
        for key in ('pos', 'axis'):
            if value.get(key) is not None:
                joint.set(key, ' '.join(f'{float(x):.12g}' for x in value[key]))
        mode = value.get('limit_mode', 'inherit')
        if mode == 'none':
            joint.set('limited', 'false')
            joint.attrib.pop('range', None)
        elif mode == 'custom':
            joint.set('limited', 'true')
            joint.set('range', f"{float(value['lower']):.12g} {float(value['upper']):.12g}")
        for key, suffix in [('limit_solver', 'limit'), ('friction_solver', 'friction')]:
            if value.get(key) is not None:
                attrs = constraint_attributes(value[key])
                joint.set('solref' + suffix, attrs['solref'])
                joint.set('solimp' + suffix, attrs['solimp'])
    for value in config.get('joint_force_limits') or []:
        if value['joint'] not in joints:
            raise ValueError(f'Force-limited joint lost during conversion: {value["joint"]}')
        joint = joints[value['joint']]
        joint.set('actuatorfrclimited', 'true')
        joint.set('actuatorfrcrange', f"{float(value['lower']):.12g} {float(value['upper']):.12g}")


def apply_base(root, config, root_names, bodies):
    mode = config.get('base_mode', 'inherit')
    if mode == 'inherit':
        return
    world = root.find('worldbody')
    if len(root_names) != 1:
        raise ValueError('Base mode requires a single URDF root')
    name = next(iter(root_names))
    body = bodies[name]
    if body is world:
        body = ET.Element('body', name=name)
        for element in list(world):
            world.remove(element)
            body.append(element)
        world.append(body)
        bodies[name] = body
    if body not in list(world):
        raise ValueError('Floating base must be a child of worldbody')
    for joint in list(body.findall('joint')) + list(body.findall('freejoint')):
        if joint.tag == 'freejoint' or joint.get('type') == 'free':
            body.remove(joint)
        elif mode == 'floating':
            raise ValueError('Floating base cannot coexist with other root joints')
    if mode == 'floating':
        reserved = '__sw2mujoco_base_free'
        if root.find(f".//*[@name='{reserved}']") is not None:
            raise ValueError('Reserved floating-base joint name already exists')
        ET.SubElement(body, 'freejoint', name=reserved)
