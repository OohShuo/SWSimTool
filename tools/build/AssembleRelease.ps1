param([string]$Configuration='Release',[string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& "$root/tools/build/ValidationReceipt.ps1" -Mode Verify -Configuration $Configuration
$source=Join-Path $root "build/bin/SWSimTool.SolidWorks/$Configuration/net48"
$version=([xml](Get-Content -LiteralPath (Join-Path $root 'Version.props'))).Project.PropertyGroup.SWSimToolVersion
$payload=Join-Path $root 'build/runtime-release'
if (!(Test-Path (Join-Path $source 'SWSimTool.dll'))) { throw 'Build the plugin before assembling the release' }
if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
New-Item -ItemType Directory -Path "$payload/docs","$payload/mujoco_backend","$payload/images" -Force | Out-Null
foreach($name in @('SWSimTool.dll','SWSimTool.Core.dll','SWSimTool.Application.dll','SWSimTool.Infrastructure.dll','SWSimTool.pdb','CsvHelper.dll','MathNet.Numerics.dll','log4net.dll','solidworkstools.dll','SWSimTool.png')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $payload
}
Copy-Item "$source/images/*.png" "$payload/images"
Copy-Item "$root/runtime/python/*.py","$root/runtime/python/requirements.txt" "$payload/mujoco_backend"
Copy-Item -LiteralPath "$root/LICENSE" -Destination $payload
Copy-Item -LiteralPath "$root/docs/INSTALL.md" -Destination "$payload/docs"
& $Python -B "$root/tools/docs/Build-Guide.py" --version $version
if($LASTEXITCODE -ne 0) { throw 'Guide generation failed' }
Copy-Item "$root/build/docs/SWSimTool_${version}_*.html","$root/docs/SWSimTool_${version}_*.md" "$payload/docs"
Copy-Item -LiteralPath "$root/docs/guide-images" -Destination "$payload/docs" -Recurse
Write-Host "Release assembled: $payload"
