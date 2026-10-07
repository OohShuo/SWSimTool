SWSimTool for SolidWorks 2025 (x64)

Complete plugin based on the upstream SolidWorks URDF Exporter.
The original license and attribution are retained.
Close all SolidWorks instances before installing or uninstalling.
SWSimTool uses a new COM identity, Setup AppId and installation directory.
The installer does not upgrade/uninstall the old product. Co-installation is supported;
use only one active editor for a document. Only the on-document
SW2MuJoCo Configuration v2 format is retained. Old preferences/cache/sidecars are not imported.

Tools > SWSimTool:
- URDF configuration
- Collision configuration
- Simulation configuration (sites, sensors, actuators, forces, solver)
- Constraint configuration (connect, weld, joint)
- Joint configuration (physical parameters)
- Export and preview (URDF, project/local MJCF, existing MJCF)

Assembly settings share one SW2MuJoCo Configuration attribute, keyed
by SolidWorks Configuration. Old settings migrate on explicit save.
Save the assembly to persist configuration edits.
URDF export keeps the original workflow; optional simulation sidecar is off by default.
MJCF export publishes only XML and meshes/*.stl; intermediate files use temporary storage.
Python, MuJoCo and simplification tools are supplied by your local environment.
Closing an export/preview window cancels its backend and waits for cleanup.
On failure or cancellation, the previous valid MJCF package is retained.
Offline illustrated guide: docs/SWSimTool_3.2.1_使用指南.html
Text guide: docs/SWSimTool_3.2.1_使用指南.md
