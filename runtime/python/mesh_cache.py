"""Content-addressed, validated mesh cache, outside exported robot packages."""
import hashlib
from importlib.metadata import version, PackageNotFoundError
import json
import os
import re
from pathlib import Path
import shutil
import struct
import tempfile
from time import perf_counter
from contextlib import contextmanager
from simplify_stl import triangles, simplify

@contextmanager
def measured(stage):
    start = perf_counter()
    try:
        yield
    finally:
        from incremental import CURRENT
        current=CURRENT.get()
        if current is not None:
            current['seconds'][stage]=current['seconds'].get(stage,0)+perf_counter()-start
        if os.environ.get('SW2MUJOCO_PROFILE') == '1':
            print('PERF: ' + json.dumps(dict(stage=stage, seconds=perf_counter()-start)), flush=True)

def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def same_file_content(a, b):
    return a.stat().st_size == b.stat().st_size and file_hash(a) == file_hash(b)

def link_or_copy(source, destination):
    # Staging links are immutable. Writers must unlink before replacing them.
    try:
        os.link(source, destination)
    except OSError:
        shutil.copy2(source, destination)
    return str(destination)

def count_faces(data):
    if len(data) >= 84:
        count = struct.unpack_from('<I', data, 80)[0]
        if len(data) == 84 + count * 50:
            return count, None
    raw = triangles(None, data)
    return len(raw), raw

def identity(data, maximum, backend, blender):
    if backend == 'blender':
        executable = Path(blender or '').resolve()
        stat = executable.stat() if executable.is_file() else None
        backend_version = [str(executable), stat.st_size, stat.st_mtime_ns] if stat else None
    else:
        try:
            backend_version = version(backend.replace('-', '_'))
        except PackageNotFoundError:
            backend_version = None
    key = dict(source=hashlib.sha256(data).hexdigest(), maximum=maximum,
               backend=backend, version=backend_version,
               algorithm=file_hash(Path(__file__).with_name('simplify_stl.py')),
               format=1)
    return hashlib.sha256(json.dumps(key, sort_keys=True).encode()).hexdigest()

def cache_folder():
    configured = os.environ.get('SW2MUJOCO_MESH_CACHE')
    return Path(configured) if configured else Path(os.environ.get('LOCALAPPDATA', tempfile.gettempdir())) / 'SW2MuJoCo' / 'mesh-cache-v1'

def prune(folder, keep, maximum_bytes=512*1024*1024):
    entries = sorted((p for p in folder.glob('*.stl') if re.fullmatch(r'[0-9a-f]{64}\.stl', p.name) and p.with_suffix('.json').is_file()), key=lambda p: p.stat().st_mtime)
    total = sum(p.stat().st_size for p in entries)
    for path in entries:
        if total <= maximum_bytes:
            break
        if path == keep:
            continue
        size = path.stat().st_size
        try:
            path.unlink()
            path.with_suffix('.json').unlink(missing_ok=True)
            total -= size
        except OSError:
            pass

def prepare_mesh(data, destination, settings):
    destination = Path(destination)
    before, raw = count_faces(data)
    enabled = bool(settings.get('Enabled', False))
    maximum = int(settings.get('MaximumTriangles', 200000))
    backend = settings.get('Backend', 'pymeshlab')
    if enabled and backend not in ('pymeshlab', 'fast-simplification', 'blender'):
        raise ValueError('Unknown simplification backend')
    report = dict(before=before, after=before, changed=False, cache_hit=False)
    if not enabled or before <= maximum:
        destination.write_bytes(data)
        return report
    use_cache = settings.get('CacheEnabled', True)
    cached = cache_folder() / (identity(data, maximum, backend, settings.get('Blender')) + '.stl') if use_cache else None
    metadata = cached.with_suffix('.json') if cached else None
    if cached and cached.is_file() and metadata.is_file():
        try:
            info = json.loads(metadata.read_text())
            if info['sha256'] == file_hash(cached) and 0 < info['after'] <= maximum:
                with cached.open('rb') as stream:
                    header = stream.read(84)
                if len(header) == 84 and struct.unpack_from('<I', header, 80)[0] == info['after'] and cached.stat().st_size == 84 + 50*info['after']:
                    shutil.copyfile(cached, destination)
                    os.utime(cached, None)
                    report.update(after=info['after'], changed=True, cache_hit=True)
                    return report
        except (OSError, ValueError, KeyError, TypeError):
            pass
    destination.write_bytes(data)
    from incremental import count
    count('mesh_simplification')
    with measured('simplify'):
        result = simplify(destination, maximum, backend, settings.get('Blender'), raw=raw if raw is not None else triangles(None, data))
    report.update(result)
    if cached:
        try:
            cached.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix='.cache-', dir=cached.parent) as temporary:
                staged = Path(temporary) / 'mesh.stl'
                shutil.copyfile(destination, staged)
                info = dict(after=report['after'], sha256=file_hash(staged))
                staged_meta = Path(temporary) / 'mesh.json'
                staged_meta.write_text(json.dumps(info))
                os.replace(staged, cached)
                os.replace(staged_meta, metadata)
            prune(cached.parent, cached)
        except OSError:
            # Cache permissions or a concurrent eviction must not prevent export.
            pass
    return report
