SW2URDF for SolidWorks 2025 - x64

This package contains the exact DLL tested with SolidWorks 2025 SP1.2.
It fixes the Part export window disappearing immediately after opening.
Build: 1.6.9767.21140 (882169e-sw2025-diagnostics).

Before installing, SAVE YOUR MODELS and close ALL SolidWorks instances.
Setup requires administrator rights and .NET Framework 4.8 or later.
SolidWorks 2025 must already be installed; this package does not install it.

Setup installs the exporter and dependencies and registers the 64-bit COM add-in.
The standard legacy exporter location is retained for menu image compatibility.
An existing SW2URDF installation with the same installer AppId is updated.

After installation, start SolidWorks 2025. In Tools > Add-Ins, enable SW2URDF.
Open a part or assembly, then use Tools > Export as URDF.
In a Chinese SolidWorks UI, this may appear under Tools > Tools > Export as URDF.
If elevation used a different administrator account, enable Load at Startup
in the add-in settings for your normal Windows user.

Uninstall from Windows Installed Apps. Close SolidWorks before uninstalling.
Models and exported URDF packages are not installed or removed by this package.
Detailed diagnosis and validation: docs/history/SW2025_SW2URDF_FIX.md (source repository historical documentation).

This is a locally built, unsigned installer based on the ROS SW2URDF project.
