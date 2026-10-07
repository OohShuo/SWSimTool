"""Optional, verified prepared-input cache. Never a source of project truth."""
import hashlib
import json
import os
import shutil
import tempfile
import uuid
from pathlib import Path
from time import perf_counter
from contextlib import contextmanager
from contextvars import ContextVar

CURRENT = ContextVar('export_metrics', default=None)
LAST_METRICS = {}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False)


def fingerprint(value):
    return hashlib.sha256(canonical(value).encode('utf-8')).hexdigest()


def count(name, amount=1):
    current = CURRENT.get()
    if current is not None:
        current['counts'][name] = current['counts'].get(name, 0) + amount


@contextmanager
def export_metrics():
    metrics = dict(export_id=os.environ.get('SW2MUJOCO_EXPORT_ID') or uuid.uuid4().hex,
                   counts=dict(mesh_prepare=0, mesh_simplification=0, mjcf_generation=0,
                               mujoco_validation=0, base_generation=0), seconds={})
    token = CURRENT.set(metrics)
    start = perf_counter()
    try:
        yield metrics
    finally:
        metrics['seconds']['total'] = perf_counter()-start
        LAST_METRICS.clear()
        LAST_METRICS.update(metrics)
        CURRENT.reset(token)
        if os.environ.get('SW2MUJOCO_PROFILE') == '1':
            print('Export metrics: ' + json.dumps(metrics, ensure_ascii=False), flush=True)


@contextmanager
def stage(name):
    start = perf_counter()
    try:
        yield
    finally:
        current = CURRENT.get()
        if current is not None:
            current['seconds'][name] = current['seconds'].get(name, 0) + perf_counter()-start


def cache_root():
    return Path(os.environ.get('SW2MUJOCO_CACHE') or
                str(Path(os.environ.get('LOCALAPPDATA', tempfile.gettempdir())) / 'SW2MuJoCo' / 'cache'))


def generator_identity():
    from mesh_cache import file_hash
    import mujoco
    return dict(schema=1, mujoco=mujoco.__version__, generator={
        p.name: file_hash(p) for p in Path(__file__).parent.glob('*.py') if not p.name.startswith('test_')})


def mesh_source_path(filename, urdf):
    if filename.startswith('package://'):
        _, relative = filename[len('package://'):].split('/', 1)
        path = urdf.parent.parent / relative
    elif filename.startswith('file://'):
        from urllib.parse import unquote, urlparse
        raw = unquote(urlparse(filename).path)
        if len(raw)>2 and raw[0]=='/' and raw[2]==':':
            raw=raw[1:]
        path=Path(raw)
    else:
        path=Path(filename)
        if not path.is_absolute():
            path=urdf.parent/path
    if not path.is_file():
        raise FileNotFoundError(f'Mesh not found: {path}')
    return path.resolve()


class PreparedInputs:
    def __init__(self, urdf, robot, original, settings, output_root, config):
        from mesh_cache import file_hash, identity
        self.enabled = settings.get('CacheEnabled', True) and os.environ.get('SW2MUJOCO_USE_INCREMENTAL_EXPORT', '1') != '0'
        with stage('fingerprint'):
            sources={}
            mesh_fingerprints={}
            for mesh in robot.findall('.//mesh'):
                path=mesh_source_path(mesh.get('filename'), urdf)
                if path.is_relative_to(output_root):
                    raise ValueError(f'Output package must not contain an original input mesh: {path}')
                sources.setdefault(str(path),file_hash(path))
                mesh_fingerprints[mesh.get('filename')]=sources[str(path)]
            self.sources=sources
            self.fingerprints=dict(mesh_source=mesh_fingerprints, source=fingerprint(dict(urdf=hashlib.sha256(original).hexdigest(), meshes=list(sources.values()))),
                mesh_process=fingerprint(dict(settings=settings, generator=generator_identity(),
                    backend=self.backend_identity(settings))),
                mjcf_config=fingerprint(config))
            # Configuration deliberately excluded: prepared assets and base XML contain no simulation overlays.
            self.key=fingerprint({k:v for k,v in self.fingerprints.items() if k in ('source','mesh_process')})
        self.maximum=min(int(settings.get("MaximumTriangles",200000)),200000) if settings.get("Enabled",False) else 200000
        self.folder=cache_root()/'projects'/self.key

    @staticmethod
    def backend_identity(settings):
        from importlib.metadata import version, PackageNotFoundError
        backend=settings.get('Backend','pymeshlab')
        if backend=='blender':
            path=Path(settings.get('Blender') or '')
            if not path.is_file(): return None
            info=path.stat()
            return dict(path=str(path.resolve()),size=info.st_size,mtime=info.st_mtime_ns)
        try:
            return version(backend.replace('-','_'))
        except PackageNotFoundError:
            return None

    def restore(self, workspace):
        if not self.enabled:
            return None
        from mesh_cache import file_hash, link_or_copy
        try:
            info=json.loads((self.folder/'state.json').read_text())
            if not isinstance(info,dict) or not isinstance(info.get('files'),dict) or not isinstance(info.get('reports'),list) or info.get('key')!=self.key or info.get('schema')!=1:
                return None
            for relative, digest in info['files'].items():
                path=self.folder/relative
                if Path(relative).is_absolute() or '..' in Path(relative).parts or not path.resolve().is_relative_to(self.folder.resolve()) or file_hash(path)!=digest:
                    return None
            if not {'.internal/source.urdf','.internal/base.xml'}.issubset(info['files']):
                return None
            import struct
            for relative in info['files']:
                if relative.startswith('meshes/'):
                    path=self.folder/relative
                    with path.open('rb') as stream: header=stream.read(84)
                    if len(header)!=84: return None
                    faces=struct.unpack_from('<I',header,80)[0]
                    if not 1<=faces<=self.maximum or path.stat().st_size!=84+50*faces: return None
            for relative in info['files']:
                destination=workspace/relative
                destination.parent.mkdir(parents=True,exist_ok=True)
                link_or_copy(self.folder/relative,destination)
            return info
        except (OSError,ValueError,KeyError,TypeError):
            # Remove partial restores, but only files in this invocation's temporary workspace.
            for path in list(workspace.iterdir()):
                if path.is_dir(): shutil.rmtree(path)
                else: path.unlink()
            return None

    def verify_sources(self):
        from mesh_cache import file_hash
        if any(file_hash(Path(path))!=digest for path,digest in self.sources.items()):
            raise ValueError('Source mesh changed during export; retry from a consistent snapshot')

    def save(self, workspace, reports):
        if not self.enabled:
            return
        from mesh_cache import file_hash, link_or_copy
        try:
            self.folder.parent.mkdir(parents=True,exist_ok=True)
            with tempfile.TemporaryDirectory(prefix='.prepared-',dir=self.folder.parent) as temporary:
                staged=Path(temporary)/'entry'
                staged.mkdir()
                files={}
                for source in [workspace/'.internal'/'source.urdf',workspace/'.internal'/'base.xml']+list((workspace/'meshes').glob('*.stl')):
                    relative=source.relative_to(workspace).as_posix()
                    destination=staged/relative
                    destination.parent.mkdir(parents=True,exist_ok=True)
                    link_or_copy(source,destination)
                    files[relative]=file_hash(destination)
                (staged/'state.json').write_text(json.dumps(dict(schema=1,key=self.key,files=files,reports=reports,
                    fingerprints=self.fingerprints)),encoding='utf-8')
                # Entries are replaceable cache data, never published user packages.
                if self.folder.resolve().parent != self.folder.parent.resolve() or self.folder.is_symlink():
                    return
                if self.folder.exists():
                    shutil.rmtree(self.folder)
                os.replace(staged,self.folder)
        except OSError:
            pass


class ProcessedMesh:
    """Content cache for all successfully compiled STL assets, including small meshes."""
    def __init__(self, data, process_fingerprint, enabled):
        self.enabled=enabled
        self.key=fingerprint(dict(source=hashlib.sha256(data).hexdigest(),process=process_fingerprint))
        self.path=cache_root()/'processed-mesh'/(self.key+'.stl')
        self.metadata=self.path.with_suffix('.json')

    def restore(self, destination):
        if not self.enabled: return None
        from mesh_cache import file_hash, link_or_copy
        try:
            info=json.loads(self.metadata.read_text())
            if not isinstance(info,dict) or not isinstance(info.get('report'),dict) or info['sha256']!=file_hash(self.path): return None
            import struct
            with self.path.open('rb') as stream: header=stream.read(84)
            if len(header)!=84: return None
            faces=struct.unpack_from('<I',header,80)[0]
            if not 1<=faces<=200000 or info['report'].get('after')!=faces or self.path.stat().st_size!=84+faces*50: return None
            link_or_copy(self.path,destination)
            return dict(info['report'],cache_hit=True,prepared_cache_hit=True)
        except (OSError,ValueError,KeyError,TypeError):
            return None

    def save(self, source, report):
        if not self.enabled: return
        from mesh_cache import file_hash, link_or_copy
        try:
            self.path.parent.mkdir(parents=True,exist_ok=True)
            with tempfile.TemporaryDirectory(prefix='.mesh-',dir=self.path.parent) as temporary:
                staged=Path(temporary)/'mesh.stl'
                link_or_copy(source,staged)
                metadata=Path(temporary)/'mesh.json'
                metadata.write_text(json.dumps(dict(sha256=file_hash(staged),report=report)))
                os.replace(staged,self.path)
                os.replace(metadata,self.metadata)
        except OSError:
            pass
