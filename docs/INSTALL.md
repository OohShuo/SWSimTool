SW2MuJoCo for SolidWorks 2025 (x64)

Complete plugin based on the upstream SolidWorks URDF Exporter.
The original license and attribution are retained.
Close all SolidWorks instances before installing or uninstalling.
Existing COM identity and Setup AppId are retained for upgrades.

Tools > SW2MuJoCo:
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
Offline illustrated guide: docs/SW2MuJoCo_3.2.1_使用指南.html
Text guide: docs/SW2MuJoCo_3.2.1_使用指南.md
