# SWSimTool CLI

The CLI runs independently of SolidWorks. It uses the same model, native MJCF writer, mesh preparation, MuJoCo validation and atomic package publication as the plugin. It accepts a local URDF and an optional current SWSimTool sidecar; it does not read SLDASM files. SolidWorks remains a .NET Framework 4.8 plugin; the CLI requires .NET 8 and a local Python/MuJoCo environment for export and validation.

Build without the SolidWorks SDK:

```sh
dotnet build src/SWSimTool.Cli/SWSimTool.Cli.csproj -c Release -f net8.0
```

Run:

```sh
dotnet build/bin/SWSimTool.Cli/Release/net8.0/SWSimTool.Cli.dll --help
dotnet build/bin/SWSimTool.Cli/Release/net8.0/SWSimTool.Cli.dll inspect --urdf robot.urdf
dotnet build/bin/SWSimTool.Cli/Release/net8.0/SWSimTool.Cli.dll export --urdf robot.urdf --config robot.sim.json --output output/robot.xml --python python
dotnet build/bin/SWSimTool.Cli/Release/net8.0/SWSimTool.Cli.dll validate --mjcf output/robot_mjcf/robot.xml --python python
```

Quote paths containing spaces. `--mesh-settings settings.json` selects the existing mesh budget/backend settings for export. Omit `--config` for a plain local URDF. Export reports its final package path and publishes only XML and meshes/*.stl. Failed validation or tool startup retains the previous valid package. Ctrl+C cancels the operation and cleans up the owned tool process tree.

Exit codes: 0 success; 1 model/tool/validation failure; 2 invalid command/options; 130 cancellation. Inspect validates the domain input and reports counts, without compiling in MuJoCo. Validate compiles the existing XML using MuJoCo; it never edits the XML. No command launches a viewer or opens CAD.

After the full Windows product tests, `build.ps1 -Package` also prepares a separate `build/cli-release/` folder. The SolidWorks installer consumes only the net48 `build/runtime-release/` folder, so installing the plugin does not require .NET 8. For independent CLI publication, use `dotnet publish ... -f net8.0 -c Release -o build/cli-release` after the portable tests; framework-dependent output requires .NET 8 on the destination machine.

The net8 CLI also runs on Linux. Install the .NET 8 runtime and use the Linux Python interpreter path, for example `--python /home/user/venv/bin/python`. Build requires a .NET SDK; neither PowerShell nor the SolidWorks SDK is required for the portable projects. Python needs MuJoCo for export/validation and the selected simplifier only when reduction is needed. No graphical environment is required for these CLI commands. Case-sensitive paths are preserved. UTF-8/spaces are supported; MJCF numbers are invariant under the tested en-US, zh-CN and de-DE cultures.
