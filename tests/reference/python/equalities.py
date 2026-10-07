"""Validated equality definitions, separate from physical joint parameters."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import math
import xml.etree.ElementTree as ET
from solver import finite


def vector(value, length, name):
    if not isinstance(value, list) or len(value) != length:
        raise ValueError(f'{name} needs {length} values')
    return [finite(x, name) for x in value]


def validate_equalities(config, attachments, joints, links):
    seen = set()
    for item in config.get('equalities', []):
        name = item.get('name')
        if not name or name in seen:
            raise ValueError('Unknown or duplicate equality name')
        seen.add(name)
        kind = item.get('type')
        if kind not in ('connect', 'weld', 'joint'):
            raise ValueError('Unsupported equality type')
        if not isinstance(item.get('active', True), bool):
            raise ValueError('Equality active must be boolean')
        if kind == 'joint':
            a, b = item.get('joint1'), item.get('joint2')
            if joints.get(a) not in ('revolute', 'continuous', 'prismatic'):
                raise ValueError('Equality joint1 must be a scalar movable joint')
            if b and (b == a or joints.get(b) not in ('revolute', 'continuous', 'prismatic')):
                raise ValueError('Equality joint2 must be a different scalar movable joint')
            vector(item.get('polycoef', [0, 1, 0, 0, 0]), 5, 'polycoef')
            continue
        binding = item.get('binding', 'site')
        if binding == 'site':
            a, b = item.get('site1'), item.get('site2')
            if a not in attachments or b not in attachments or a == b:
                raise ValueError('Equality requires two different attachment names')
            if attachments[a]['link'] == attachments[b]['link']:
                raise ValueError('Equality sites must belong to different links')
            if kind == 'weld' and any(attachments[s]['type'] != 'frame' for s in (a, b)):
                raise ValueError('Weld needs two frames')
        elif binding == 'body':
            a, b = item.get('body1'), item.get('body2')
            if a not in links or (b and b not in links) or a == b:
                raise ValueError('Equality requires distinct existing bodies (body2 may be world)')
            if kind == 'connect':
                vector(item.get('anchor', [0, 0, 0]), 3, 'anchor')
            elif item.get('pose_mode', 'inherit') == 'custom':
                vector(item.get('position'), 3, 'position')
                vector(item.get('orientation'), 3, 'orientation')
            elif item.get('pose_mode', 'inherit') != 'inherit':
                raise ValueError('Invalid weld pose mode')
        else:
            raise ValueError('Invalid equality binding')
        if kind == 'weld' and finite(item.get('torquescale', 1), 'torquescale') < 0:
            raise ValueError('torquescale must be nonnegative')


def quaternion(rpy):
    r, p, y = [x / 2 for x in rpy]
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return [cr*cp*cy+sr*sp*sy, sr*cp*cy-cr*sp*sy, cr*sp*cy+sr*cp*sy, cr*cp*sy-sr*sp*cy]


def text(values):
    return ' '.join(f'{float(x):.12g}' for x in values)


def apply_equalities(root, config, effective):
    if not config.get('equalities'):
        return
    group = root.find('equality')
    if group is None:
        group = ET.SubElement(root, 'equality')
    existing = {e.get('name') for e in group}
    for item in config['equalities']:
        if item['name'] in existing:
            raise ValueError(f'Equality conflicts with converted URDF: {item["name"]}')
        existing.add(item['name'])
        attrs = dict(name=item['name'], active=str(item.get('active', True)).lower(),
                     **effective(config, 'equality', item.get('solver')))
        kind = item['type']
        if kind == 'joint':
            attrs.update(joint1=item['joint1'], polycoef=text(item.get('polycoef', [0, 1, 0, 0, 0])))
            if item.get('joint2'):
                attrs['joint2'] = item['joint2']
        else:
            if item.get('binding', 'site') == 'site':
                attrs.update(site1=item['site1'], site2=item['site2'])
            else:
                attrs['body1'] = item['body1']
                if item.get('body2'):
                    attrs['body2'] = item['body2']
                if kind == 'connect':
                    attrs['anchor'] = text(item.get('anchor', [0, 0, 0]))
                elif item.get('pose_mode', 'inherit') == 'custom':
                    attrs['relpose'] = text(item['position'] + quaternion(item['orientation']))
            if kind == 'weld':
                attrs['torquescale'] = str(item.get('torquescale', 1))
        ET.SubElement(group, kind, attrs)
