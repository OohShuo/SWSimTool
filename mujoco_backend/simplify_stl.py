"""Simplify only oversized STL files, committing a verified binary STL atomically.

The source coordinate system and units are preserved. No CAD or inertia data is read.
"""
import argparse
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile


def triangles(path):
    import numpy as np
    data = Path(path).read_bytes()
    if len(data) >= 84:
        count = struct.unpack_from('<I', data, 80)[0]
        if len(data) == 84 + 50 * count:
            dtype = np.dtype([('normal', '<f4', (3,)), ('vertices', '<f4', (3, 3)), ('attribute', '<u2')])
            return np.frombuffer(data, dtype=dtype, count=count, offset=84)['vertices'].astype(np.float64)
    vertices = []
    for line in data.decode('ascii').splitlines():
        fields = line.split()
        if fields and fields[0].lower() == 'vertex':
            if len(fields) != 4:
                raise ValueError('Malformed ASCII STL vertex')
            vertices.append([float(value) for value in fields[1:]])
    if not vertices or len(vertices) % 3:
        raise ValueError('Invalid or empty STL')
    return np.asarray(vertices, dtype=np.float64).reshape(-1, 3, 3)


def indexed_mesh(path):
    import numpy as np
    faces = triangles(path)
    if not len(faces) or not np.isfinite(faces).all():
        raise ValueError('STL contains no triangles or non-finite coordinates')
    # STL repeats vertex positions per triangle; exact welding is essential for edge collapse.
    vertices, indices = np.unique(faces.reshape(-1, 3), axis=0, return_inverse=True)
    return vertices, indices.reshape(-1, 3)


def write_binary(path, vertices, faces):
    import numpy as np
    points = np.asarray(vertices)[np.asarray(faces, dtype=np.int64)]
    if not len(points) or not np.isfinite(points).all():
        raise ValueError('Simplification produced empty or non-finite geometry')
    normals = np.cross(points[:, 1] - points[:, 0], points[:, 2] - points[:, 0])
    lengths = np.linalg.norm(normals, axis=1)
    if np.any(lengths == 0):
        raise ValueError('Simplification produced degenerate triangles')
    normals /= lengths[:, None]
    dtype = np.dtype([('normal', '<f4', (3,)), ('vertices', '<f4', (3, 3)), ('attribute', '<u2')])
    records = np.zeros(len(points), dtype=dtype)
    records['normal'] = normals
    records['vertices'] = points
    Path(path).write_bytes(bytes(80) + struct.pack('<I', len(points)) + records.tobytes())


def reduce_python(vertices, faces, target, backend):
    if backend == 'fast-simplification':
        import fast_simplification
        return fast_simplification.simplify(vertices, faces, target_count=target)
    import pymeshlab
    meshes = pymeshlab.MeshSet()
    meshes.add_mesh(pymeshlab.Mesh(vertex_matrix=vertices, face_matrix=faces))
    meshes.meshing_decimation_quadric_edge_collapse(targetfacenum=target, targetperc=0,
        preserveboundary=True, preservenormal=True, preservetopology=True, autoclean=True)
    mesh = meshes.current_mesh()
    return mesh.vertex_matrix(), mesh.face_matrix()


def blender_worker(source, destination, target):
    import bpy
    import numpy as np
    vertices, faces = indexed_mesh(source)
    mesh = bpy.data.meshes.new('SW2URDF_STL')
    mesh.from_pydata(vertices.tolist(), [], faces.tolist())
    mesh.update()
    obj = bpy.data.objects.new('SW2URDF_STL', mesh)
    bpy.context.collection.objects.link(obj)
    modifier = obj.modifiers.new('Triangle budget', 'DECIMATE')
    modifier.decimate_type = 'COLLAPSE'
    modifier.use_collapse_triangulate = True
    modifier.ratio = max(0.0, min(1.0, target / len(faces)))
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    result = evaluated.to_mesh()
    try:
        result.calc_loop_triangles()
        new_vertices = np.asarray([v.co[:] for v in result.vertices], dtype=np.float64)
        new_faces = np.asarray([t.vertices[:] for t in result.loop_triangles], dtype=np.int64)
        write_binary(destination, new_vertices, new_faces)
    finally:
        evaluated.to_mesh_clear()


def simplify(source, maximum, backend, blender=None, output=None):
    if maximum < 4:
        raise ValueError('Maximum triangle count must be at least 4')
    if backend not in ('pymeshlab', 'fast-simplification', 'blender'):
        raise ValueError('Unknown simplification backend')
    source = Path(source).resolve()
    output = source if output is None else Path(output).resolve()
    raw = triangles(source)
    count = len(raw)
    if count == 0:
        raise ValueError('Empty STL')
    if count <= maximum:
        # Never rewrite an accepted input, even if it has a different STL header.
        if output != source:
            raise ValueError('Under-limit files are left untouched; no output copy was created')
        print(f'SKIP: {source.name}: {count} <= {maximum}', flush=True)
        return dict(file=str(source), backend=backend, before=count, after=count, changed=False)
    print(f'SIMPLIFY: {source.name}: {count} -> <= {maximum} ({backend})', flush=True)
    vertices, faces = indexed_mesh(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    # Temporary files are on the destination volume so replacement is atomic.
    with tempfile.TemporaryDirectory(prefix='.sw2urdf-mesh-', dir=output.parent) as directory:
        staged = Path(directory) / 'simplified.stl'
        target = maximum
        for attempt in range(8):
            if backend == 'blender':
                if not blender or not Path(blender).is_file():
                    raise FileNotFoundError('Select a valid blender.exe path')
                command = [blender, '--background', '--factory-startup', '--python-exit-code', '1',
                    '--python', str(Path(__file__).resolve()), '--', '--blender-worker', '--input', str(source),
                    '--output', str(staged), '--max-triangles', str(target)]
                subprocess.run(command, check=True, timeout=300)
            else:
                new_vertices, new_faces = reduce_python(vertices, faces, target, backend)
                write_binary(staged, new_vertices, new_faces)
            result = triangles(staged)
            after = len(result)
            print(f'Attempt {attempt + 1}: {after} triangles', flush=True)
            if 0 < after <= maximum:
                # Validate the serialized float32 result, not just the in-memory algorithm output.
                checked_vertices, checked_faces = indexed_mesh(staged)
                validation = Path(directory) / 'checked.stl'
                write_binary(validation, checked_vertices, checked_faces)
                os.replace(staged, output)
                report = dict(file=str(output), backend=backend, before=count, after=after, changed=True)
                print('RESULT: ' + json.dumps(report, ensure_ascii=False), flush=True)
                return report
            next_target = max(4, min(target - 1, int(target * maximum / max(after, 1) * .85)))
            if target == 4:
                break
            target = next_target
        raise ValueError(f'Unable to meet triangle budget {maximum}; original STL retained')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output')
    parser.add_argument('--max-triangles', type=int, required=True)
    parser.add_argument('--backend', choices=['pymeshlab', 'fast-simplification', 'blender'])
    parser.add_argument('--blender')
    parser.add_argument('--blender-worker', action='store_true')
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    args = parser.parse_args(argv)
    if args.blender_worker:
        blender_worker(args.input, args.output, args.max_triangles)
    else:
        if not args.backend:
            parser.error('--backend is required')
        simplify(args.input, args.max_triangles, args.backend, args.blender, args.output)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'ERROR: {error}', file=sys.stderr, flush=True)
        sys.exit(1)
