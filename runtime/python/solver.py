"""Opt-in rigid robot solver settings and local constraint overrides."""
import math
import xml.etree.ElementTree as ET

EQUALITY = dict(timeconst=.005, dampratio=1, dmin=.99, dmax=.99, width=.001, midpoint=.5, power=2)
CONTACT = dict(EQUALITY, timeconst=.003, dmax=.995, margin=.001, condim=3)


def finite(value, name):
    if isinstance(value, bool):
        raise ValueError(f'{name} must be a finite number')
    try:
        value = float(value)
    except (TypeError, ValueError):
        raise ValueError(f'{name} must be a finite number') from None
    if not math.isfinite(value):
        raise ValueError(f'{name} must be a finite number')
    return value


def constraint_attributes(settings, contact=False):
    if not isinstance(settings, dict):
        raise ValueError('Constraint solver settings must be an object')
    values = dict(CONTACT if contact else EQUALITY, **settings)
    keys = ['timeconst', 'dampratio', 'dmin', 'dmax', 'width', 'midpoint', 'power']
    for key in keys:
        values[key] = finite(values[key], key)
    if not (values['timeconst'] > 0 and values['dampratio'] > 0 and
            0 < values['dmin'] <= values['dmax'] < 1 and values['width'] > 0 and
            0 < values['midpoint'] < 1 and values['power'] >= 1):
        raise ValueError('Invalid solref/solimp range')
    attrs = dict(solref=f"{values['timeconst']:.12g} {values['dampratio']:.12g}",
                 solimp=' '.join(f'{values[k]:.12g}' for k in keys[2:]))
    if contact:
        margin = finite(values['margin'], 'margin')
        condim = finite(values['condim'], 'condim')
        if margin < 0 or condim not in (1, 3, 4, 6):
            raise ValueError('Invalid contact margin or condim')
        attrs.update(margin=f'{margin:.12g}', condim=str(int(condim)))
    return attrs


def validate_solver(config):
    settings = config.get('solver')
    if settings is not None:
        if not isinstance(settings, dict) or not isinstance(settings.get('enabled', False), bool):
            raise ValueError('Invalid solver settings')
        if settings.get('enabled'):
            for key, default in [('timestep', .001), ('tolerance', 1e-9), ('impratio', 10)]:
                v = finite(settings.get(key, default), key)
                if v < 0 or (key != 'tolerance' and v == 0):
                    raise ValueError(f'Invalid {key}')
            for key, default, minimum in [('iterations', 100, 1), ('noslip_iterations', 0, 0)]:
                v = finite(settings.get(key, default), key)
                if v < minimum or v != int(v):
                    raise ValueError(f'Invalid {key}')
            constraint_attributes(settings.get('equality', EQUALITY))
            constraint_attributes(settings.get('contact', CONTACT), True)
    for equality in config.get('equalities', []):
        if equality.get('solver') is not None:
            constraint_attributes(equality['solver'])
    for pair in (config.get('collision') or {}).get('allowed_pairs', []):
        if pair.get('solver') is not None:
            constraint_attributes(pair['solver'], True)


def effective(config, kind, override=None):
    if override is not None:
        return constraint_attributes(override, kind == 'contact')
    settings = config.get('solver') or {}
    if settings.get('enabled'):
        return constraint_attributes(settings.get(kind, CONTACT if kind == 'contact' else EQUALITY), kind == 'contact')
    return {}


def apply_options(root, config):
    settings = config.get('solver') or {}
    overrides = [e['solver'] for e in config.get('equalities', []) if e.get('solver') is not None]
    overrides += [p['solver'] for p in (config.get('collision') or {}).get('allowed_pairs', []) if p.get('solver') is not None]
    overrides += [j[key] for j in config.get('joints') or [] for key in ('limit_solver', 'friction_solver') if j.get(key) is not None]
    active = settings.get('enabled', False)
    if not active and not overrides:
        return
    option = root.find('option')
    if option is None:
        option = ET.SubElement(root, 'option')
    flag = option.find('flag')
    if flag is None:
        flag = ET.SubElement(option, 'flag')
    flag.set('refsafe', 'enable')
    if active:
        option.set('solver', 'Newton')
        option.set('cone', 'elliptic')
        for key, default in [('timestep', .001), ('iterations', 100), ('tolerance', 1e-9),
                             ('noslip_iterations', 0), ('impratio', 10)]:
            option.set(key, f'{finite(settings.get(key, default), key):.12g}')
        for geom in root.findall('.//geom'):
            if int(geom.get('contype', '1')) or int(geom.get('conaffinity', '1')):
                geom.attrib.update(effective(config, 'contact'))
        overrides += [settings.get(kind) or default for kind, default in [('equality', EQUALITY), ('contact', CONTACT)]]
    dt = float(option.get('timestep', '.002'))
    if any(float(v.get('timeconst', .005)) < 2 * dt for v in overrides):
        print('WARNING: constraint timeconst < 2*timestep; refsafe clamps the effective value')


def diagnostic_sample(model, data):
    """Distances now; solver statistics describe the last completed solve."""
    import mujoco
    import numpy as np
    separation = 0.0
    for i in range(model.neq):
        # Tuple membership calls the enum's reverse equality, which differs
        # between platform bindings for NumPy scalars. Compare integer values.
        if data.eq_active[i] and int(model.eq_type[i]) in (int(mujoco.mjtEq.mjEQ_CONNECT), int(mujoco.mjtEq.mjEQ_WELD)) and int(model.eq_objtype[i]) == int(mujoco.mjtObj.mjOBJ_SITE):
            a, b = int(model.eq_obj1id[i]), int(model.eq_obj2id[i])
            separation = max(separation, float(np.linalg.norm(data.site_xpos[a] - data.site_xpos[b])))
    penetration = max((max(0.0, -float(data.contact[i].dist)) for i in range(data.ncon)), default=0.0)
    niter = max((int(v) for v in data.solver_niter), default=0)
    # MjSolverStatList uses a fixed per-island stride.
    stride = len(data.solver) // len(data.solver_niter)
    gradients = [float(data.solver[i * stride + min(int(n), stride) - 1].gradient)
                 for i, n in enumerate(data.solver_niter) if n > 0]
    return dict(site_separation_m=separation, penetration_m=penetration, contacts=int(data.ncon),
                solver_iterations=niter, solver_gradient=max(gradients, default=0.0),
                iteration_limit_reached=niter >= model.opt.iterations,
                finite=bool(np.isfinite(data.qpos).all() and np.isfinite(data.qvel).all()))


def preview_model(model):
    """Launch the standard viewer without periodic logging or control callbacks."""
    import mujoco.viewer
    mujoco.viewer.launch(model)
