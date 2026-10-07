"""Apply optional link collision proxies and an internal collision allow-list."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import itertools
import math
import xml.etree.ElementTree as ET


def validate_collision(config, links):
    rules = config.get('collision')
    if rules is None:
        return
    if not isinstance(rules, dict) or not isinstance(rules.get('disable_internal', True), bool):
        raise ValueError('Invalid collision policy')
    modes = rules.get('link_modes', {})
    if not isinstance(modes, dict) or any(link not in links or mode not in ('mesh', 'primitive', 'none') for link, mode in modes.items()):
        raise ValueError('Unknown collision link or mode')
    names, counts = set(), {}
    for geom in rules.get('geometries', []):
        name, link, kind = geom.get('name'), geom.get('link'), geom.get('type')
        if not isinstance(name, str) or not name.strip() or name in names or link not in links:
            raise ValueError('Missing/duplicate collision geometry name or unknown link')
        names.add(name)
        counts[link] = counts.get(link, 0) + 1
        for key in ('xyz', 'rpy'):
            values = geom.get(key)
            if not isinstance(values, list) or len(values) != 3 or not all(math.isfinite(float(v)) for v in values):
                raise ValueError(f'Invalid {name}.{key}')
        size = geom.get('size')
        expected = {'box': 3, 'sphere': 1, 'cylinder': 2, 'capsule': 2}.get(kind)
        if expected is None or not isinstance(size, list) or len(size) != expected:
            raise ValueError(f'Invalid collision type/size for {name}')
        if not all(math.isfinite(float(v)) and float(v) > 0 for v in size):
            raise ValueError(f'Collision dimensions must be positive: {name}')
    if any(mode == 'primitive' and not counts.get(link) for link, mode in modes.items()):
        raise ValueError('Primitive collision mode requires at least one geometry')
    pairs = set()
    for pair in rules.get('allowed_pairs', []):
        a, b = pair.get('link1'), pair.get('link2')
        key = tuple(sorted((str(a), str(b))))
        if a not in links or b not in links or a == b or key in pairs:
            raise ValueError('Invalid or duplicate collision link pair')
        if modes.get(a, 'mesh') == 'none' or modes.get(b, 'mesh') == 'none':
            raise ValueError('Allowed collision pair references a link with collision disabled')
        pairs.add(key)


def apply_collision(mjcf, bodies, config, quaternion, text):
    rules = config.get('collision')
    if rules is None:
        return  # Older sidecars retain their original collision behavior.
    from solver import effective
    collision_geoms = {}
    existing_names = {g.get('name') for g in mjcf.findall('.//geom') if g.get('name')}
    for index, (link, body) in enumerate(bodies.items()):
        mode = rules.get('link_modes', {}).get(link, 'mesh')
        active = []
        for j, geom in enumerate(body.findall('geom')):
            if not geom.get('name'):
                name = f'sw2urdf_original_{index}_{j}'
                while name in existing_names:
                    name += '_'
                geom.set('name', name)
                existing_names.add(name)
            is_collision = int(geom.get('contype', '1')) != 0 or int(geom.get('conaffinity', '1')) != 0
            if mode != 'mesh':
                geom.set('contype', '0')
                geom.set('conaffinity', '0')
            elif is_collision:
                geom.attrib.update(effective(config, 'contact'))
                active.append(geom.get('name'))
        collision_geoms[link] = active
    for geom in rules.get('geometries', []):
        link = geom['link']
        if rules.get('link_modes', {}).get(link, 'mesh') != 'primitive':
            continue
        name = geom['name']
        if name in existing_names:
            raise ValueError(f'Collision geom name already exists: {name}')
        existing_names.add(name)
        size = list(map(float, geom['size']))
        # JSON stores full box dimensions and full cylinder/central capsule lengths.
        if geom['type'] == 'box':
            size = [v / 2 for v in size]
        elif geom['type'] in ('cylinder', 'capsule'):
            size[1] /= 2
        ET.SubElement(bodies[link], 'geom', dict(name=name, type=geom['type'], size=text(size),
            pos=text(geom['xyz']), quat=text(quaternion(geom['rpy'])), group='3',
            contype='1', conaffinity='1', mass='0', rgba='0.2 0.8 1 0.35'))
        bodies[link].findall('geom')[-1].attrib.update(effective(config, 'contact'))
        collision_geoms[link].append(name)
    contact = mjcf.find('contact')
    if contact is None:
        contact = ET.SubElement(mjcf, 'contact')
    allowed = {tuple(sorted((p['link1'], p['link2']))) for p in rules.get('allowed_pairs', [])}
    if rules.get('disable_internal', True):
        for a, b in itertools.combinations(sorted(bodies), 2):
            if (a, b) not in allowed:
                ET.SubElement(contact, 'exclude', body1=a, body2=b)
    overrides = {tuple(sorted((p['link1'], p['link2']))): p.get('solver') for p in rules.get('allowed_pairs', [])}
    for a, b in sorted(allowed):
        if not collision_geoms[a] or not collision_geoms[b]:
            raise ValueError(f'Allowed pair has no collision geoms: {a}, {b}')
        for ga, gb in itertools.product(collision_geoms[a], collision_geoms[b]):
            ET.SubElement(contact, 'pair', dict(geom1=ga, geom2=gb, **effective(config, 'contact', overrides[(a, b)])))
    print(f'Collision proxies={sum(len(v) for k,v in collision_geoms.items() if rules.get("link_modes", {}).get(k) == "primitive")}; allowed link pairs={len(allowed)}')
