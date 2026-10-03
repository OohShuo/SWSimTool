"""Convert an unchanged SW2URDF URDF and its simulation sidecar to MJCF.

All edits apply to an in-memory copy. The input URDF and meshes are never rewritten.
"""
import argparse
from contextlib import contextmanager
import hashlib
import importlib
import json
import math
from pathlib import Path
import tempfile
import xml.etree.ElementTree as ET


def load_runtime(preview=False):
    import sys
    if sys.version_info < (3, 10):
        raise RuntimeError("The configured local Python must be Python 3.10 or newer")
    try:
        runtime = importlib.import_module("mujoco")
    except ImportError as error:
        raise RuntimeError(f"Cannot import MuJoCo from the configured local Python ({sys.executable}): {error}. Select an environment with MuJoCo installed.") from error
    if not callable(getattr(getattr(runtime, "MjSpec", None), "from_string", None)) or not callable(getattr(getattr(runtime, "MjModel", None), "from_xml_string", None)):
        raise RuntimeError("The local MuJoCo lacks required MjSpec.from_string / MjModel.from_xml_string APIs")
    if preview:
        try:
            viewer = importlib.import_module("mujoco.viewer")
        except ImportError as error:
            raise RuntimeError(f"Cannot import mujoco.viewer from the configured local Python: {error}") from error
        if not callable(getattr(viewer, "launch", None)):
            raise RuntimeError("The local MuJoCo viewer lacks the launch API")
    print(f"Python: {sys.executable}")
    print(f"MuJoCo: {getattr(runtime, '__version__', 'unknown')} (local environment)")
    return runtime


def vector(value, size, label):
    if not isinstance(value, list) or len(value) != size:
        raise ValueError(f"{label} must have {size} numbers")
    result = [float(x) for x in value]
    if not all(math.isfinite(x) for x in result):
        raise ValueError(f"{label} contains non-finite values")
    return result


def number(value, label):
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{label} must be finite")
    return result


def text(values):
    return " ".join(format(x, ".17g") for x in values)


def quaternion(rpy):
    r, p, y = [v / 2 for v in rpy]
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return [cr * cp * cy + sr * sp * sy, sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy]


def unique(items, label):
    result = {}
    for item in items:
        name = item.get("name")
        if not isinstance(name, str) or not name.strip() or name in result:
            raise ValueError(f"Missing or duplicate {label} name: {name}")
        result[name] = item
    return result


def validate_urdf_tree(robot):
    links = unique([dict(link.attrib) for link in robot.findall("link")], "link")
    unique([dict(joint.attrib) for joint in robot.findall("joint")], "joint")
    errors, parents = [], {}
    for joint in robot.findall("joint"):
        name = joint.get("name")
        parent, child = joint.find("parent"), joint.find("child")
        parent_name = None if parent is None else parent.get("link")
        child_name = None if child is None else child.get("link")
        for role, link in (("parent", parent_name), ("child", child_name)):
            if link not in links:
                errors.append(f"Joint '{name}' {role} references missing link '{link}'")
        if parent_name == child_name:
            errors.append(f"Joint '{name}' connects a link to itself")
        if child_name in parents:
            errors.append(f"Link '{child_name}' has multiple incoming joints")
        parents[child_name] = parent_name
        if joint.get("type") in ("revolute", "continuous", "prismatic", "planar"):
            axis = joint.find("axis")
            raw = "1 0 0" if axis is None else axis.get("xyz", "")
            try:
                values = vector([float(value) for value in raw.split()], 3, name + ".axis")
                if not any(value != 0 for value in values):
                    raise ValueError("zero axis")
            except (ValueError, TypeError):
                errors.append(f"Joint '{name}' has an invalid or zero axis ({raw})")
    roots = set(links) - set(parents)
    if len(roots) != 1:
        errors.append(f"URDF must have one root link; found {len(roots)}")
    for link in links:
        visited, current = set(), link
        while current in parents:
            if current in visited:
                errors.append(f"URDF contains a joint cycle at link '{current}'")
                break
            visited.add(current)
            current = parents[current]
    if errors:
        raise ValueError("Invalid URDF tree:\n" + "\n".join(dict.fromkeys(errors)))


def validate(config, robot):
    validate_urdf_tree(robot)
    if config.get("schema_version") != 1:
        raise ValueError("Unsupported schema_version")
    if config.get("units") != "m,rad":
        raise ValueError("Expected units=m,rad")
    links = {link.attrib["name"] for link in robot.findall("link")}
    joints = {joint.attrib["name"]: joint.attrib["type"] for joint in robot.findall("joint")}
    attachments = unique(config.get("attachments", []), "attachment")
    for name, attachment in attachments.items():
        if attachment.get("link") not in links:
            raise ValueError(f"Unknown link for {name}: {attachment.get('link')}")
        vector(attachment.get("xyz"), 3, name + ".xyz")
        if attachment.get("type") == "frame":
            vector(attachment.get("rpy"), 3, name + ".rpy")
        elif attachment.get("type") != "point" or "rpy" in attachment or "quat_wxyz" in attachment:
            raise ValueError(f"Point must contain position only: {name}")
    for actuator in unique(config.get("actuators", []), "actuator").values():
        if joints.get(actuator.get("joint")) not in {"revolute", "continuous", "prismatic"}:
            raise ValueError(f"Actuator must reference a movable scalar joint: {actuator.get('joint')}")
        if actuator.get("type") not in {"motor", "position", "velocity"}:
            raise ValueError("Unsupported actuator type")
        for prefix in ("ctrl", "force"):
            lo = number(actuator[prefix + "_min"], prefix + "_min")
            hi = number(actuator[prefix + "_max"], prefix + "_max")
            if lo >= hi:
                raise ValueError(f"Invalid {prefix} range for {actuator['name']}")
        if number(actuator.get("gear", 1), "gear") == 0:
            raise ValueError("Actuator gear cannot be zero")
        if number(actuator.get("gain", 1), "gain") < 0:
            raise ValueError("Actuator gain cannot be negative")
    for sensor in unique(config.get("sensors", []), "sensor/camera").values():
        if sensor.get("type") not in {"imu", "tof", "camera"}:
            raise ValueError("Unsupported sensor type")
        if attachments.get(sensor.get("site"), {}).get("type") != "frame":
            raise ValueError(f"Sensor/camera needs an oriented frame: {sensor['name']}")
        if number(sensor.get("noise", 0), "noise") != 0:
            raise ValueError("This backend does not inject sensor noise; use noise=0 and add noise in the sampling layer")
        if number(sensor.get("cutoff", 0), "cutoff") < 0:
            raise ValueError("Sensor cutoff cannot be negative")
        if sensor["type"] == "camera" and not 0 < number(sensor.get("fovy", 45), "fovy") < 180:
            raise ValueError("Camera fovy must be between 0 and 180 degrees")
    for equality in unique(config.get("equalities", []), "equality").values():
        if equality.get("type") not in {"connect", "weld"}:
            raise ValueError("Unsupported equality type")
        a, b = equality.get("site1"), equality.get("site2")
        if a not in attachments or b not in attachments or a == b:
            raise ValueError("Equality requires two different attachment names")
        if attachments[a]["link"] == attachments[b]["link"]:
            raise ValueError("Equality sites must belong to different links")
        if equality["type"] == "weld" and any(attachments[s]["type"] != "frame" for s in (a, b)):
            raise ValueError("Weld needs two frames")
    return attachments


def resolve_meshes(robot, urdf):
    package_root = urdf.parent.parent
    files = {}
    for mesh in robot.findall(".//mesh"):
        filename = mesh.attrib["filename"]
        if filename.startswith("package://"):
            _, relative = filename[len("package://"):].split("/", 1)
            path = package_root / relative
        elif filename.startswith("file://"):
            from urllib.parse import unquote, urlparse
            raw = unquote(urlparse(filename).path)
            if len(raw) > 2 and raw[0] == "/" and raw[2] == ":":
                raw = raw[1:]
            path = Path(raw)
        else:
            path = Path(filename)
            if not path.is_absolute():
                path = urdf.parent / path
        if not path.is_file():
            raise FileNotFoundError(f"Mesh not found: {path}")
        path = path.resolve()
        files.setdefault(path, path.read_bytes())
        mesh.set("filename", str(path))
    # Native Windows file I/O can reject Unicode paths. Load bytes in Python,
    # use ASCII VFS names, and emit a portable copy alongside the MJCF.
    digest = hashlib.sha256()
    for data in files.values():
        digest.update(len(data).to_bytes(8, "little"))
        digest.update(data)
    folder = "sw2urdf_assets_" + digest.hexdigest()[:20]
    aliases = {path: f"{folder}/mesh_{index:04d}{path.suffix.lower()}" for index, path in enumerate(files)}
    for mesh in robot.findall(".//mesh"):
        mesh.set("filename", aliases[Path(mesh.attrib["filename"])])
    return {aliases[path]: data for path, data in files.items()}


@contextmanager
def asset_options(mujoco, assets):
    if not assets:
        yield {}
    elif hasattr(mujoco, "MjVfs"):
        with mujoco.MjVfs() as vfs:
            for name, data in assets.items():
                vfs[name] = data
            yield {"vfs": vfs}
    else:
        yield {"assets": assets}


def load_mjcf(path):
    """Load a generated MJCF even when its directory contains Unicode characters."""
    mujoco = load_runtime()
    path = Path(path)
    xml = path.read_text(encoding="utf-8-sig")
    assets = {mesh.attrib["file"]: (path.parent / mesh.attrib["file"]).read_bytes()
              for mesh in ET.fromstring(xml).findall("./asset/mesh")}
    with asset_options(mujoco, assets) as options:
        return mujoco.MjModel.from_xml_string(xml, **options)


def convert(urdf, config_path, output):
    mujoco = load_runtime()
    urdf, config_path, output = Path(urdf).resolve(), Path(config_path).resolve(), Path(output).resolve()
    if output in (urdf, config_path):
        raise ValueError("Output must differ from input files")
    original = urdf.read_bytes()
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    if config.get("urdf") != urdf.name:
        raise ValueError("Sidecar belongs to a different URDF filename")
    if config.get("urdf_sha256") and config["urdf_sha256"].lower() != hashlib.sha256(original).hexdigest():
        raise ValueError("URDF changed since sidecar export; re-export the pair")
    robot = ET.fromstring(original)
    attachments = validate(config, robot)
    assets = resolve_meshes(robot, urdf)
    extension = robot.find("mujoco")
    if extension is None:
        extension = ET.SubElement(robot, "mujoco")
    compiler = extension.find("compiler")
    if compiler is None:
        compiler = ET.SubElement(extension, "compiler")
    compiler.set("fusestatic", "false")
    compiler.set("discardvisual", "false")
    compiler.set("strippath", "false")
    with asset_options(mujoco, assets) as options:
        spec = mujoco.MjSpec.from_string(ET.tostring(robot, encoding="unicode"), **options)
        if not callable(getattr(spec, "compile", None)) or not callable(getattr(spec, "to_xml", None)):
            raise RuntimeError("The local MuJoCo lacks required MjSpec.compile / to_xml APIs")
        spec.compile(**({"vfs": options["vfs"]} if "vfs" in options else {}))
        # to_xml() recompiles internally without accepting a VFS argument.
        # Keep the byte assets on the spec for that serialization pass.
        if assets:
            spec.assets = assets
        compiled_xml = spec.to_xml()
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        mjcf = ET.fromstring(compiled_xml)
        bodies = {body.attrib["name"]: body for body in mjcf.findall(".//body") if "name" in body.attrib}
        # MuJoCo may place the fixed URDF root directly in worldbody.
        child_names = {joint.find("child").attrib["link"] for joint in robot.findall("joint")}
        roots = {link.attrib["name"] for link in robot.findall("link")} - child_names
        for root_name in roots:
            bodies.setdefault(root_name, mjcf.find("worldbody"))
        for name, attachment in attachments.items():
            if attachment["link"] not in bodies:
                raise ValueError(f"URDF body was lost during conversion: {attachment['link']}")
            attributes = {"name": name, "pos": text(attachment["xyz"]), "size": "0.003", "rgba": "1 0.3 0.1 1"}
            if attachment["type"] == "frame":
                attributes["quat"] = text(quaternion(attachment["rpy"]))
            ET.SubElement(bodies[attachment["link"]], "site", attributes)
        if config.get("actuators"):
            group = ET.SubElement(mjcf, "actuator")
            for actuator in config["actuators"]:
                attributes = {"name": actuator["name"], "joint": actuator["joint"], "gear": str(actuator.get("gear", 1)),
                              "ctrllimited": "true", "ctrlrange": text([actuator["ctrl_min"], actuator["ctrl_max"]]),
                              "forcelimited": "true", "forcerange": text([actuator["force_min"], actuator["force_max"]])}
                if actuator["type"] != "motor":
                    attributes["kp" if actuator["type"] == "position" else "kv"] = str(actuator.get("gain", 1))
                ET.SubElement(group, actuator["type"], attributes)
        sensor_group = ET.SubElement(mjcf, "sensor") if any(s["type"] != "camera" for s in config.get("sensors", [])) else None
        for sensor in config.get("sensors", []):
            if sensor["type"] == "camera":
                frame = attachments[sensor["site"]]
                ET.SubElement(bodies[frame["link"]], "camera", {"name": sensor["name"], "pos": text(frame["xyz"]),
                    "quat": text(quaternion(frame["rpy"])), "fovy": str(sensor.get("fovy", 45))})
            else:
                for kind in (["accelerometer", "gyro"] if sensor["type"] == "imu" else ["rangefinder"]):
                    ET.SubElement(sensor_group, kind, {"name": sensor["name"] + "_" + kind, "site": sensor["site"], "cutoff": str(sensor.get("cutoff", 0))})
        if config.get("equalities"):
            group = ET.SubElement(mjcf, "equality")
            for equality in config["equalities"]:
                ET.SubElement(group, equality["type"], {"name": equality["name"], "site1": equality["site1"], "site2": equality["site2"]})
        xml = ET.tostring(mjcf, encoding="unicode")
        with asset_options(mujoco, assets) as options:
            final_model = mujoco.MjModel.from_xml_string(xml, **options)
        # Compile first; failed conversions leave the previous output intact.
        for name, data in assets.items():
            asset_path = output.parent / name
            asset_path.parent.mkdir(parents=True, exist_ok=True)
            asset_path.write_bytes(data)
        staged = Path(temporary) / "final.xml"
        ET.indent(mjcf)
        staged.write_bytes(ET.tostring(mjcf, encoding="utf-8", xml_declaration=True))
        staged.replace(output)
    print(f"MJCF saved: {output}")
    print(f"Sites={final_model.nsite}, actuators={final_model.nu}, sensors={final_model.nsensor}, equalities={final_model.neq}")
    return final_model


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--urdf")
    parser.add_argument("--config")
    parser.add_argument("--output")
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--check-environment", action="store_true")
    args = parser.parse_args()
    if args.check_environment:
        load_runtime(preview=args.preview)
        return
    if not all((args.urdf, args.config, args.output)):
        parser.error("--urdf, --config and --output are required for conversion")
    if args.preview:
        load_runtime(preview=True)
    model = convert(args.urdf, args.config, args.output)
    if args.preview:
        import mujoco.viewer
        mujoco.viewer.launch(model)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        import sys
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
