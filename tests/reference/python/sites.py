"""Resolve stable site identities to the current MJCF export names."""


def normalize_site_references(config):
    by_id, by_name = {}, {}
    for site in config.get('attachments', []):
        name, identity = site.get('name'), site.get('id')
        if name in by_name:
            raise ValueError(f'Duplicate attachment name: {name}')
        by_name[name] = site
        if identity:
            if identity in by_id:
                raise ValueError('Duplicate site identity')
            by_id[identity] = site

    def resolve(item, key):
        identity = item.get(key + '_id')
        if identity:
            if identity not in by_id:
                raise ValueError(f'Site identity was deleted or is missing: {identity}')
            item[key] = by_id[identity]['name']
        elif item.get(key) in by_name and by_name[item[key]].get('id'):
            item[key + '_id'] = by_name[item[key]]['id']

    for sensor in config.get('sensors', []):
        resolve(sensor, 'site')
    for equality in config.get('equalities', []):
        if equality.get('type') != 'joint' and equality.get('binding', 'site') == 'site':
            resolve(equality, 'site1')
            resolve(equality, 'site2')

    if not isinstance(config.get('site_forces', []), list):
        raise ValueError('Two-site forces must be a list')
    for force in config.get('site_forces', []):
        resolve(force, 'site1')
        resolve(force, 'site2')
