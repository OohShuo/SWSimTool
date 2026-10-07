"""Compare compiled semantics by object identity, not XML/array ordering."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
import numpy as np
import mujoco


def objects(model, kind, count):
    return {mujoco.mj_id2name(model, kind, i) or ('world' if i == 0 and kind == mujoco.mjtObj.mjOBJ_BODY else f'unnamed:{i}'): i for i in range(count)}


def close(a, b, label, atol=1e-9, rtol=1e-9):
    np.testing.assert_allclose(a, b, atol=atol, rtol=rtol, err_msg=label)


def rotation(q):
    result = np.zeros(9)
    mujoco.mju_quat2Mat(result, q)
    return result.reshape(3, 3)


def inertia_in_body(model, body):
    r = rotation(model.body_iquat[body])
    return r @ np.diag(model.body_inertia[body]) @ r.T


def mapping(a, b, kind, count):
    left, right = objects(a, kind, getattr(a, count)), objects(b, kind, getattr(b, count))
    assert left.keys() == right.keys(), (count, left.keys(), right.keys())
    return [(left[name], right[name], name) for name in left]


def dof_mapping(a, b):
    pairs = []
    for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_JOINT, 'njnt'):
        assert a.jnt_type[i] == b.jnt_type[j], name
        width = {0: 6, 1: 3, 2: 1, 3: 1}[int(a.jnt_type[i])]
        pairs.extend((int(a.jnt_dofadr[i]) + k, int(b.jnt_dofadr[j]) + k) for k in range(width))
    return np.array(pairs, dtype=int).reshape(-1, 2)


def geom_mapping(a, b):
    # URDF serializer may not name geoms. Their stable fixture identity is owning body + ordinal.
    body_pairs = mapping(a, b, mujoco.mjtObj.mjOBJ_BODY, 'nbody')
    result = []
    for i, j, name in body_pairs:
        ai, bj = np.flatnonzero(a.geom_bodyid == i), np.flatnonzero(b.geom_bodyid == j)
        assert len(ai) == len(bj), ('geoms', name)
        result.extend((int(x), int(y), f'{name}/geom/{k}') for k, (x, y) in enumerate(zip(ai, bj)))
    return result


def mesh_world_vertices(model, data, geom):
    mesh = model.geom_dataid[geom]
    vertices = model.mesh_vert[model.mesh_vertadr[mesh]:model.mesh_vertadr[mesh] + model.mesh_vertnum[mesh]]
    # mesh_vert is already compiler-normalized; geom_xmat/xpos include compiler compensation.
    return vertices @ data.geom_xmat[geom].reshape(3, 3).T + data.geom_xpos[geom]


def nearest_distances(source, target):
    """Exact point-set comparison without allocating a full CAD-sized N x M array."""
    try:
        from scipy.spatial import cKDTree
    except ImportError:
        result = np.full(len(source), np.inf)
        for start in range(0, len(source), 256):
            block = source[start:start + 256]
            best = np.full(len(block), np.inf)
            for offset in range(0, len(target), 1024):
                delta = block[:, None, :] - target[None, offset:offset + 1024, :]
                best = np.minimum(best, np.linalg.norm(delta, axis=2).min(axis=1))
            result[start:start + len(block)] = best
        return result
    return cKDTree(target).query(source, k=1, eps=0)[0]


def compiled_semantics(a, b, precision=1e-9):
    compare = lambda x, y, label, **kwargs: close(x, y, label, **{**dict(atol=precision, rtol=precision), **kwargs})
    for count in ('nbody', 'njnt', 'ngeom', 'nsite', 'nu', 'nsensor', 'neq', 'ntendon', 'nq', 'nv', 'ncam'):
        assert getattr(a, count) == getattr(b, count), count
    ad, bd = mujoco.MjData(a), mujoco.MjData(b)
    mujoco.mj_forward(a, ad); mujoco.mj_forward(b, bd)
    ab, bb = objects(a, mujoco.mjtObj.mjOBJ_BODY, a.nbody), objects(b, mujoco.mjtObj.mjOBJ_BODY, b.nbody)
    for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_BODY, 'nbody'):
        assert next(k for k, v in ab.items() if v == a.body_parentid[i]) == next(k for k, v in bb.items() if v == b.body_parentid[j])
        for key in ('body_pos', 'body_ipos', 'body_mass'):
            compare(getattr(a, key)[i], getattr(b, key)[j], name + '/' + key)
        compare(rotation(a.body_quat[i]), rotation(b.body_quat[j]), name + '/rotation')
        compare(inertia_in_body(a, i), inertia_in_body(b, j), name + '/inertia')
        compare(ad.xpos[i], bd.xpos[j], name + '/world_position')
    for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_JOINT, 'njnt'):
        for key in ('jnt_type', 'jnt_pos', 'jnt_axis', 'jnt_limited', 'jnt_range', 'jnt_margin', 'jnt_solref', 'jnt_solimp', 'jnt_stiffness', 'jnt_actfrclimited', 'jnt_actfrcrange'):
            compare(getattr(a, key)[i], getattr(b, key)[j], name + '/' + key)
        width = {0: 7, 1: 4, 2: 1, 3: 1}[int(a.jnt_type[i])]
        for key in ('qpos0', 'qpos_spring'):
            compare(getattr(a, key)[a.jnt_qposadr[i]:a.jnt_qposadr[i] + width], getattr(b, key)[b.jnt_qposadr[j]:b.jnt_qposadr[j] + width], name + '/' + key)
    dofs = dof_mapping(a, b)
    for key in ('dof_damping', 'dof_frictionloss', 'dof_armature', 'dof_solref', 'dof_solimp'):
        compare(getattr(a, key)[dofs[:, 0]], getattr(b, key)[dofs[:, 1]], key)
    for i, j, name in geom_mapping(a, b):
        for key in ('geom_type', 'geom_size', 'geom_rgba', 'geom_group', 'geom_contype', 'geom_conaffinity', 'geom_friction', 'geom_solref', 'geom_solimp', 'geom_margin', 'geom_condim'):
            compare(getattr(a, key)[i], getattr(b, key)[j], name + '/' + key)
        if a.geom_type[i] == mujoco.mjtGeom.mjGEOM_MESH:
            av, bv = mesh_world_vertices(a, ad, i), mesh_world_vertices(b, bd, j)
            assert len(av) == len(bv)
            # Vertex indices and principal-axis signs need not agree. Compare point sets.
            compare(nearest_distances(bv, av), np.zeros(len(bv)), name + '/mesh', atol=max(2e-7, precision))
            compare(nearest_distances(av, bv), np.zeros(len(av)), name + '/mesh', atol=max(2e-7, precision))
        else:
            compare(ad.geom_xpos[i], bd.geom_xpos[j], name + '/world_pose')
            compare(ad.geom_xmat[i], bd.geom_xmat[j], name + '/world_rotation')
    for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_SITE, 'nsite'):
        compare(ad.site_xpos[i], bd.site_xpos[j], name + '/world_position')
        compare(ad.site_xmat[i], bd.site_xmat[j], name + '/world_rotation')
    for kind, count, keys in [
        (mujoco.mjtObj.mjOBJ_ACTUATOR, 'nu', ('actuator_trntype', 'actuator_dyntype', 'actuator_gaintype', 'actuator_biastype', 'actuator_gear', 'actuator_gainprm', 'actuator_biasprm', 'actuator_ctrllimited', 'actuator_ctrlrange', 'actuator_forcelimited', 'actuator_forcerange')),
        (mujoco.mjtObj.mjOBJ_SENSOR, 'nsensor', ('sensor_type', 'sensor_dim', 'sensor_objtype', 'sensor_cutoff')),
        (mujoco.mjtObj.mjOBJ_EQUALITY, 'neq', ('eq_type', 'eq_objtype', 'eq_active0', 'eq_data', 'eq_solref', 'eq_solimp')),
        (mujoco.mjtObj.mjOBJ_TENDON, 'ntendon', ('tendon_stiffness', 'tendon_damping', 'tendon_lengthspring', 'tendon_frictionloss')),
        (mujoco.mjtObj.mjOBJ_CAMERA, 'ncam', ('cam_pos', 'cam_fovy')),
    ]:
        for i, j, name in mapping(a, b, kind, count):
            for key in keys: compare(getattr(a, key)[i], getattr(b, key)[j], name + '/' + key)
            if kind == mujoco.mjtObj.mjOBJ_ACTUATOR:
                target_kind = mujoco.mjtObj.mjOBJ_TENDON if a.actuator_trntype[i] == mujoco.mjtTrn.mjTRN_TENDON else mujoco.mjtObj.mjOBJ_JOINT
                assert mujoco.mj_id2name(a, target_kind, a.actuator_trnid[i, 0]) == mujoco.mj_id2name(b, target_kind, b.actuator_trnid[j, 0])
            if kind == mujoco.mjtObj.mjOBJ_SENSOR:
                assert mujoco.mj_id2name(a, int(a.sensor_objtype[i]), a.sensor_objid[i]) == mujoco.mj_id2name(b, int(b.sensor_objtype[j]), b.sensor_objid[j])
            if kind == mujoco.mjtObj.mjOBJ_EQUALITY:
                for key in ('eq_obj1id', 'eq_obj2id'):
                    ai, bj = getattr(a, key)[i], getattr(b, key)[j]
                    assert (ai < 0 and bj < 0) or mujoco.mj_id2name(a, int(a.eq_objtype[i]), ai) == mujoco.mj_id2name(b, int(b.eq_objtype[j]), bj)
            if kind == mujoco.mjtObj.mjOBJ_CAMERA:
                compare(ad.cam_xpos[i], bd.cam_xpos[j], name + '/camera_world_position')
                compare(ad.cam_xmat[i], bd.cam_xmat[j], name + '/camera_world_rotation')
    for key in ('timestep', 'solver', 'integrator', 'iterations', 'tolerance', 'noslip_iterations', 'impratio', 'gravity', 'cone', 'enableflags', 'disableflags'):
        compare(getattr(a.opt, key), getattr(b.opt, key), 'option/' + key)
    def exclusions(model):
        return {tuple(sorted((mujoco.mj_id2name(model, mujoco.mjtObj.mjOBJ_BODY, int(signature) >> 16), mujoco.mj_id2name(model, mujoco.mjtObj.mjOBJ_BODY, int(signature) & 65535)))) for signature in model.exclude_signature}
    assert exclusions(a) == exclusions(b), 'contact exclusions'
    geoms = geom_mapping(a, b)
    ga, gb = {i: name for i, _, name in geoms}, {j: name for _, j, name in geoms}
    def pairs(model, identities):
        return {tuple(sorted((identities[int(model.pair_geom1[i])], identities[int(model.pair_geom2[i])]))): i for i in range(model.npair)}
    ap, bp = pairs(a, ga), pairs(b, gb)
    assert ap.keys() == bp.keys(), 'explicit contact pairs'
    for name in ap:
        for key in ('pair_dim', 'pair_solref', 'pair_solreffriction', 'pair_solimp', 'pair_margin', 'pair_gap', 'pair_friction'):
            compare(getattr(a, key)[ap[name]], getattr(b, key)[bp[name]], 'contact/' + str(name) + '/' + key)
    return dict(dofs=dofs, geoms=geom_mapping(a, b))


def mass_matrix(model, data):
    matrix = np.zeros((model.nv, model.nv))
    try: mujoco.mj_fullM(model, data, matrix)
    except TypeError: mujoco.mj_fullM(model, matrix, data.qM)
    return matrix


def dynamics(a, b, steps=(1, 10, 100), precision=2e-7):
    dofs = dof_mapping(a, b)
    left, right = dofs[:, 0], dofs[:, 1]
    for perturb in (0., .03, -.05):
        ad, bd = mujoco.MjData(a), mujoco.MjData(b)
        # Canonical integration inputs mapped independently; no raw state blob is copied.
        for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_JOINT, 'njnt'):
            if a.jnt_type[i] in (mujoco.mjtJoint.mjJNT_HINGE, mujoco.mjtJoint.mjJNT_SLIDE):
                ad.qpos[a.jnt_qposadr[i]] += perturb; bd.qpos[b.jnt_qposadr[j]] += perturb
        ad.qvel[left] = .02; bd.qvel[right] = .02
        ad.qacc_warmstart[left] = .001; bd.qacc_warmstart[right] = .001
        ad.qfrc_applied[left] = .01; bd.qfrc_applied[right] = .01
        for i, j, _ in mapping(a, b, mujoco.mjtObj.mjOBJ_ACTUATOR, 'nu'):
            ad.ctrl[i] = bd.ctrl[j] = .1
        for i, j, _ in mapping(a, b, mujoco.mjtObj.mjOBJ_BODY, 'nbody'):
            ad.xfrc_applied[i] = bd.xfrc_applied[j] = [0, .01, 0, 0, 0, .002]
        for i, j, _ in mapping(a, b, mujoco.mjtObj.mjOBJ_EQUALITY, 'neq'):
            ad.eq_active[i] = bd.eq_active[j] = a.eq_active0[i]
        mujoco.mj_forward(a, ad); mujoco.mj_forward(b, bd)
        close(mass_matrix(a, ad)[np.ix_(left, left)], mass_matrix(b, bd)[np.ix_(right, right)], 'mass_matrix', atol=precision, rtol=precision)
        for step in range(max(steps) + 1):
            if step in (0, *steps):
                for key in ('qvel', 'qacc', 'qfrc_bias', 'qfrc_passive', 'qfrc_actuator', 'qfrc_constraint'):
                    close(getattr(ad, key)[left], getattr(bd, key)[right], f'{step}/{key}', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_JOINT, 'njnt'):
                    width = {0: 7, 1: 4, 2: 1, 3: 1}[int(a.jnt_type[i])]
                    qa, qb = ad.qpos[a.jnt_qposadr[i]:a.jnt_qposadr[i]+width], bd.qpos[b.jnt_qposadr[j]:b.jnt_qposadr[j]+width]
                    if width == 7:
                        close(qa[:3], qb[:3], f'{step}/{name}/translation', atol=precision, rtol=precision)
                    if width in (4, 7):
                        close(rotation(qa[-4:]), rotation(qb[-4:]), f'{step}/{name}/rotation', atol=precision, rtol=precision)
                    else: close(qa, qb, f'{step}/{name}/qpos', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_BODY, 'nbody'):
                    close(ad.xpos[i], bd.xpos[j], f'{step}/{name}/world', atol=precision, rtol=precision)
                    close(ad.xmat[i], bd.xmat[j], f'{step}/{name}/world_rotation', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_SITE, 'nsite'):
                    close(ad.site_xpos[i], bd.site_xpos[j], f'{step}/{name}/world', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_ACTUATOR, 'nu'):
                    close(ad.actuator_force[i], bd.actuator_force[j], f'{step}/{name}/force', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_SENSOR, 'nsensor'):
                    close(ad.sensordata[a.sensor_adr[i]:a.sensor_adr[i] + a.sensor_dim[i]], bd.sensordata[b.sensor_adr[j]:b.sensor_adr[j] + b.sensor_dim[j]], f'{step}/{name}', atol=precision, rtol=precision)
                for i, j, name in mapping(a, b, mujoco.mjtObj.mjOBJ_TENDON, 'ntendon'):
                    close(ad.ten_length[i], bd.ten_length[j], f'{step}/{name}/length', atol=precision, rtol=precision)
                assert np.isfinite(ad.qpos).all() and np.isfinite(bd.qpos).all()
            if step < max(steps): mujoco.mj_step(a, ad); mujoco.mj_step(b, bd)
