SW2URDF for SolidWorks 2025 - x64, independent MuJoCo tools

Save your models and close all SolidWorks instances before installing.
Setup requires administrator rights and .NET Framework 4.8 or later.
An existing installation with the same AppId is updated in place.

URDF Preview and Export uses its original layout again.
Use the MuJoCo tools toolbar or Tools > MuJoCo tools menu for local
URDF + .sim.json conversion, custom MJCF output and viewer preview.
An existing MJCF can be previewed without a URDF or assembly.
Python and recent file paths are saved per user, independently of CAD files.
MuJoCo dependencies are supplied by your local Python environment.
This package does not install Python or pin a MuJoCo version.

Validation: x64 compilation, ten backend tests, isolated settings/process
tests, and native standalone window save/preview checks passed.
The new SolidWorks menu/toolbar has not yet been tested after installation.
No existing user SolidWorks projects were used for these checks.

Details: docs\MUJOCO扩展_使用与开发.md
STL budget settings are available in the MuJoCo tools window.
STL preprocessing now runs before MuJoCo loads the copied URDF.
SW URDF/STL export no longer performs decimation. Backends: PyMeshLab,
fast-simplification, and Blender, supplied by the user's local environment.
Details: docs\STL超限减面_使用说明.md
Output: robot_mjcf/robot.xml, retained URDF/JSON, and robot_meshes/.
Original exports are unchanged; copy or simplify meshes before compilation.
Validation: all 23 backend tests and C# backend process tests passed.
This is a locally built, unsigned installer based on ROS SW2URDF.

Collision configuration is edited in the left SolidWorks PropertyManager.
Configuration is stored in the assembly Attribute for each SW Configuration.
Save the assembly file to persist. New sidecars exclude internal collisions
by default, with allowed pairs. Optional primitive geoms replace mesh collision.
Original URDF/STL and inertial values remain unchanged.
Details: docs\MuJoCo碰撞配置_使用说明.md

Collision editor update (2026-10-04): Geometry / Contact pairs tabs,
contextual scalar inputs, straight-edge length references, corner-frame boxes,
and end-face-frame cylinders with configurable extrusion axis/direction.
Wheel input over a drop-down scrolls the editor without changing its value.
Real-time preview reuses unchanged bodies and transforms pose-only changes.
Opening collision configuration does not prepare a full URDF export.
Returning to the URDF page preserves its link tree and component ownership.
Validated with newly generated CAD fixtures and 30 backend regression tests.

Collision editor update (2026-10-04): Geometry / Contact pairs tabs,
contextual scalar inputs, straight-edge length references, corner-frame boxes,
and end-face-frame cylinders with configurable extrusion axis/direction.
Wheel input over a drop-down scrolls the editor without changing its value.
Real-time preview reuses unchanged bodies and transforms pose-only changes.
Opening collision configuration does not prepare a full URDF export.
Returning to the URDF page preserves its link tree and component ownership.
Validated with newly generated CAD fixtures and 30 backend regression tests.
