"""Asset preparation, official compilation and preview for the C# generator.

This module never generates MJCF or loads URDF. Inputs are owned staging files.
"""
import argparse
from contextlib import contextmanager
import hashlib
from importlib.metadata import version, PackageNotFoundError
import json
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def runtime(preview=False):
    if sys.version_info < (3, 10):
        raise RuntimeError('Local Python 3.10 or newer is required')
    import mujoco
    if preview:
        import mujoco.viewer
    print(f'Python: {sys.executable}', flush=True)
    print(f'MuJoCo: {mujoco.__version__} (local environment)', flush=True)
    return mujoco


@contextmanager
def asset_options(mujoco, assets):
    if hasattr(mujoco, 'MjVfs'):
        with mujoco.MjVfs() as vfs:
            for name, data in assets.items():
                vfs[name] = data
            yield dict(vfs=vfs)
    else:
        yield dict(assets=assets)


def load_model(path):
    mujoco=runtime()
    path=Path(path).resolve()
    xml=path.read_text(encoding='utf-8-sig')
    root=ET.fromstring(xml)
    assets={}
    for mesh in root.findall('./asset/mesh'):
        name=mesh.attrib['file']
        target=(path.parent/name).resolve()
        if not target.is_relative_to(path.parent) or target.suffix.lower()!='.stl':
            raise ValueError('Mesh must be a relative STL inside the package')
        assets[name]=target.read_bytes()
    with asset_options(mujoco,assets) as options:
        return mujoco.MjModel.from_xml_string(xml,**options)


def prepare(manifest_path, settings_path=None):
    from incremental import ProcessedMesh, count, export_metrics, fingerprint
    from mesh_cache import prepare_mesh, file_hash, identity
    from simplify_stl import indexed_mesh, write_binary
    manifest=json.loads(Path(manifest_path).read_text(encoding='utf-8-sig'))
    settings=json.loads(Path(settings_path).read_text(encoding='utf-8-sig')) if settings_path else {}
    maximum=int(settings.get('MaximumTriangles',200000))
    if settings.get('Enabled',False) and not 4<=maximum<=200000:
        raise ValueError('Triangle budget must be between 4 and 200000')
    destination=Path(manifest['staging']).resolve()
    seen=set()
    results=[]
    with export_metrics() as metrics:
        for entry in manifest['meshes']:
            source=Path(entry['source']).resolve()
            relative=entry['relative']
            target=(destination/relative).resolve()
            if (not target.is_relative_to(destination) or target.suffix.lower()!='.stl'
                    or relative in seen):
                raise ValueError('Invalid or duplicate prepared asset path')
            seen.add(relative)
            data=source.read_bytes()
            if hashlib.sha256(data).hexdigest().lower()!=entry['sha256'].lower():
                raise ValueError('Source mesh changed before preparation')
            key=fingerprint(dict(schema=1,settings=settings,
                backend=identity(data,maximum,settings.get('Backend','pymeshlab'),settings.get('Blender')),
                support=file_hash(Path(__file__))))
            cached=ProcessedMesh(data,key,settings.get('CacheEnabled',True)
                and os.environ.get('SW2MUJOCO_USE_INCREMENTAL_EXPORT')!='0')
            target.parent.mkdir(parents=True,exist_ok=True)
            report=cached.restore(target)
            if report and settings.get('Enabled',False) and report['after']>maximum:
                target.unlink();report=None
            if report is None:
                count('mesh_prepare')
                report=prepare_mesh(data,target,settings)
                # MuJoCo accepts only binary STL. Normalize small ASCII meshes too.
                raw=target.read_bytes()
                from mesh_cache import count_faces
                faces,ascii_triangles=count_faces(raw)
                if ascii_triangles is not None:
                    vertices,indices=indexed_mesh(None,raw=ascii_triangles)
                    write_binary(target,vertices,indices)
                if not 1<=faces<=200000:
                    raise ValueError(f'STL has {faces} triangles; enable preprocessing before loading')
                if settings.get('Enabled',False) and faces>maximum:
                    raise ValueError('Simplifier did not meet the requested triangle budget')
                report['after']=faces
                cached.save(target,report)
            results.append(dict(id=entry['id'],relative=relative,report=report,sha256=file_hash(target)))
            print('Mesh: '+json.dumps(results[-1],ensure_ascii=False),flush=True)
        counts=dict(metrics['counts'])
    Path(manifest['result']).write_text(json.dumps(dict(assets=results,counts=counts)),encoding='utf-8')


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--prepare')
    parser.add_argument('--mesh-settings')
    parser.add_argument('--validate')
    parser.add_argument('--preview')
    args=parser.parse_args()
    if sum(bool(x) for x in (args.prepare,args.validate,args.preview))!=1:
        parser.error('Select exactly one operation')
    if args.prepare:
        prepare(args.prepare,args.mesh_settings)
    else:
        model=load_model(args.validate or args.preview)
        if args.validate:
            print(f'Validated: bodies={model.nbody}, joints={model.njnt}, sites={model.nsite}, actuators={model.nu}, sensors={model.nsensor}, equalities={model.neq}',flush=True)
        else:
            from solver import preview_model
            preview_model(model)


if __name__=='__main__':
    try:main()
    except Exception as error:
        print(f'ERROR: {error}',file=sys.stderr)
        sys.exit(1)
